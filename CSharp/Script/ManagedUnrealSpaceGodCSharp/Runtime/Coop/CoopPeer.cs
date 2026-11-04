// US-T39: cooperative multiplayer for the C# track (new file; docs/MIGRATION.md "Co-op"). The one addition to an otherwise
// faithful port: the original's own Session layer (Core/Session) run in lockstep across a listen server and its clients.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using Unity.SpaceGod.Core;
using UnrealSharp.Log;

namespace Unity.SpaceGod.Coop
{
    /// <summary>One end of a player's connection: the host's link to a client, or a client's link to the host.</summary>
    public interface ICoopLink
    {
        /// <summary>Reliable and ordered (a reliable RPC on the player's link actor).</summary>
        void Send(string message);

        /// <summary>Best effort (an unreliable RPC): presence, which the next one replaces.</summary>
        void SendUnreliable(string message);
    }

    /// <summary>What every peer knows of a player: their name, what they last selected, where they are looking.</summary>
    public sealed class CoopPlayer
    {
        public int Seat;
        public string Name;
        /// <summary>The selection's key (<see cref="UnitySpaceApp.CoopTargetKey"/>): "n" nothing, "b 3" body 3, "s 12" ship 12.</summary>
        public string Selection = "n";
        /// <summary>Where they are: a body of the active system and an offset from it in true km (-1: not known yet).</summary>
        public int Body = -1;
        public DVec3 OffsetKm;
        public bool Left;
        public PlayerId Id => new PlayerId(Seat);
    }

    /// <summary>
    /// The co-op session of this process (US-T39): host-authoritative lockstep over the original's Session layer.
    ///
    /// The host (a listen server, player 1) runs Unity.SpaceGod's whole app as single-player does and, for every frame its
    /// Update runs, writes a <see cref="FrameRecord"/>: the real seconds of the frame, its clock just before and after
    /// advancing, the players' commands it applied first (CommandCodec lines, the original's own format) and the little the
    /// frame did that a client must repeat. Clients run the same app on the same Core and replay those frames in order, so
    /// the deterministic Core (seed + date + commands; the Bench proves it) produces the same world on every machine; only
    /// the commands and the clock cross the wire. A join, a load on the host or a detected desync sends the host's save
    /// (Core/Persistence) to every client and every peer, the host included, loads those same bytes; a digest of the Core's
    /// state, compared every <see cref="DigestEvery"/> frames, detects a drift.
    /// </summary>
    public sealed class CoopPeer
    {
        public const string LogCategory = "LogSpaceGod";
        public const int MaxPlayers = 4;
        public const int DigestEvery = 60;
        const int PresenceEvery = 10;
        const int ChunkWindow = 4;
        const int MaxResyncs = 3;
        /// <summary>Frames a client may fall behind (its pause menu open, a slow machine) before it asks to be resynced instead.</summary>
        public const int MaxBacklog = 900;

        /// <summary>This process's co-op session, or null: single-player, the original's path unchanged.</summary>
        public static CoopPeer Current { get; private set; }

        public static bool Active => Current != null;

        /// <summary>A client goes straight into the game (the host's save replaces it), as does the smoke run's host.</summary>
        public static bool SkipsMenu => (Current != null && Current.IsClient) || CoopSmoke.Active;

        public bool IsHost { get; }
        public bool IsClient => !IsHost;

        /// <summary>The seat this machine plays (0 the host; a client's once the host has welcomed it).</summary>
        public int LocalSeat { get; private set; }

        public string LocalName { get; }

        /// <summary>Every player by seat (null for a free seat).</summary>
        public readonly CoopPlayer[] Players = new CoopPlayer[MaxPlayers];

        /// <summary>True while a peer applies a synced save itself (its own Activate and load hooks stay quiet).</summary>
        public bool Applying { get; private set; }

        /// <summary>The host reloads the save it sends (default), so every peer starts from the same loaded bytes.</summary>
        public bool HostReloads { get; set; } = true;

        public static void Log(string message)
        {
            UnrealLogger.Log(LogCategory, "SpaceGodCoop: " + message);
            ToCoopLog("Display", message);
        }

        public static void Warn(string message)
        {
            UnrealLogger.Log(LogCategory, "SpaceGodCoop: " + message, ELogVerbosity.Warning);
            ToCoopLog("Warning", message);
        }

        // US-T67 (the C++ track's US-T64): -SpaceGodCoopLog=<file> writes every co-op line to a file of its own as well, in the
        // log's own line format ([time][frame]LogSpaceGod: <verbosity>: SpaceGodCoop: ...). A Shipping build logs nothing
        // (UE_LOG, which UnrealLogger calls, is compiled out), so this is how tools/play.ps1 -Mode coop-smoke -PackageDir reads
        // a packaged host's and client's lines.
        static readonly object coopLogLock = new object();
        static System.IO.StreamWriter coopLog;
        static bool coopLogOpened;

        static void ToCoopLog(string verbosity, string message)
        {
            lock (coopLogLock)
            {
                if (!coopLogOpened)
                {
                    coopLogOpened = true;
                    string path = Bootstrap.CommandLineValue("-SpaceGodCoopLog");
                    if (!string.IsNullOrEmpty(path))
                    {
                        try
                        {
                            var dir = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path));
                            if (!string.IsNullOrEmpty(dir)) System.IO.Directory.CreateDirectory(dir);
                            var stream = new System.IO.FileStream(path, System.IO.FileMode.Create, System.IO.FileAccess.Write, System.IO.FileShare.ReadWrite);
                            coopLog = new System.IO.StreamWriter(stream, new System.Text.UTF8Encoding(false)) { AutoFlush = true, NewLine = "\r\n" };
                        }
                        catch (Exception e)
                        {
                            UnrealLogger.Log(LogCategory, "SpaceGodCoop: could not open -SpaceGodCoopLog=" + path + ": " + e.Message, ELogVerbosity.Warning);
                        }
                    }
                }
                if (coopLog == null) return;
                int frame = 0;
                try { frame = UnityEngine.Time.frameCount % 1000; } catch { }
                coopLog.WriteLine("[" + DateTime.Now.ToString("yyyy.MM.dd-HH.mm.ss:fff", System.Globalization.CultureInfo.InvariantCulture) + "][" +
                    frame.ToString(System.Globalization.CultureInfo.InvariantCulture).PadLeft(3) + "]LogSpaceGod: " + verbosity + ": SpaceGodCoop: " + message);
            }
        }

        CoopPeer(bool host, string name)
        {
            IsHost = host;
            LocalName = string.IsNullOrWhiteSpace(name) ? (host ? "Host" : "Player") : name.Replace('\n', ' ').Trim();
            if (host) Players[0] = new CoopPlayer { Seat = 0, Name = LocalName };
        }

        /// <summary>
        /// Core/Persistence names the objects reachable from the Core's static fields once, the first time anything is saved or
        /// loaded in a process, and a save refers to those by name. Its walk takes in what static caches hold at that moment
        /// (FlightDecks' table of live battle units, among others), so two processes that first saved at different moments
        /// name different objects and cannot read each other's saves ("a static object this build does not have"). Every co-op
        /// peer therefore takes that snapshot at start, before any galaxy exists: the same design tables on every machine.
        /// </summary>
        static void PrimeSaveStatics()
        {
            // US-T41: the same root cause as the original's weak-table bug (FlightDecks.decks, ArmourAngling.Tables), fixed for
            // every process in CSharp/Core/SaveCompat/SaveStatics.cs; InitSaves builds it at start too, so this is a no-op there.
            try { UnrealSpaceGod.SaveCompat.SaveStatics.EnsureFixed(); }
            catch (Exception e) { Warn("could not prime the save statics: " + e.Message); }
        }

        public static CoopPeer StartHost(string name)
        {
            PrimeSaveStatics();
            CoopSettings.Hook(); // US-T43
            Current = new CoopPeer(true, name) { HostReloads = !Bootstrap.HasCommandLineFlag("-CoopNoHostReload") };
            Log("hosting as " + Current.Players[0].Name + " (player 1), up to " + MaxPlayers + " players; host reloads its sync saves: " + Current.HostReloads);
            return Current;
        }

        public static CoopPeer StartClient(ICoopLink host, string name)
        {
            PrimeSaveStatics();
            CoopSettings.Hook(); // US-T43
            Current = new CoopPeer(false, name) { hostLink = host };
            return Current;
        }

        public static void Stop() => Current = null;

        /// <summary>The name a player goes by: -CoopName=..., else the machine's user.</summary>
        public static string NameFromCommandLine(string fallback) => Bootstrap.CommandLineValue("-CoopName") ?? fallback ?? Environment.UserName;

        /// <summary>
        /// The session the app holds its seats through: the original's single-player <see cref="LocalSession"/>, or in co-op
        /// the same LocalSession with every seat of the game and this machine's player local. Every peer applies the same
        /// commands to its own, in the same order, so they hold the same state (seat ids included: the authority hands them
        /// out in the order seats are first named, which the commands fix).
        /// </summary>
        public static LocalSession NewSession() =>
            Current == null ? LocalSession.Create(seed: 0) : LocalSession.Create(seed: 0, players: MaxPlayers, local: Current.LocalSeat);

        public string NameOf(int seat) => seat >= 0 && seat < MaxPlayers && Players[seat] != null ? Players[seat].Name : "player " + (seat + 1);

        // ================================================================ commands, from either end

        /// <summary>
        /// A command from this machine's player into the stream: a host queues it for its next frame; a client sends it to the
        /// host, which stamps it into a frame every peer applies. Entries: "s &lt;CommandCodec line&gt;[ @seat key]".
        /// </summary>
        public void Submit(string entry)
        {
            if (IsHost) pending.Add(entry);
            else hostLink?.Send("cmd " + entry);
        }

        /// <summary>A change of the shared clock asked for at this machine ("warp 5", "pause 1").</summary>
        public void RequestClock(string request)
        {
            if (IsClient) hostLink?.Send(request);
        }

        // ---------------------------------------------------------------- US-T43: requests, the helm's hands, sync state

        /// <summary>The host's next frame carries <paramref name="entry"/> (a shared setting changed here, an app entry).</summary>
        public void QueueEntry(string entry)
        {
            if (IsHost) pending.Add(entry);
        }

        /// <summary>A client asks the host to set a shared setting (<see cref="CoopSettings"/>).</summary>
        public void RequestSetting(string key, string value)
        {
            if (IsClient) hostLink?.Send("setreq " + key + " " + value);
        }

        /// <summary>A client asks the host to take the party to another star (the host's view makes the trip).</summary>
        public void RequestTravel(string system)
        {
            if (IsClient) hostLink?.Send("travel " + system);
        }

        /// <summary>A client asks the host to load one of the host's save slots (every peer then loads the host's save).</summary>
        public void RequestLoad(string slot)
        {
            if (IsClient) hostLink?.Send("load " + slot);
        }

        /// <summary>A client asks for a resync now (the smoke run's deliberate one; a slow client's is "resync &lt;frames&gt;").</summary>
        public void RequestResync(string why)
        {
            if (IsClient) hostLink?.Send("resync " + why);
        }

        /// <summary>The engine tick a client is in (several host frames can replay in one): edges are sent once a tick.</summary>
        public static long EngineTick;

        /// <summary>Whether a second Update in one engine tick sees the same edges again (Unreal's player controller: yes; the
        /// smoke run's scripted device clears them after every Update, so no).</summary>
        public static bool EdgesPerTick = true;

        /// <summary>Whether the edges this Update read are new ones (false for a repeat within the tick, see <see cref="EdgesPerTick"/>).</summary>
        public static bool FreshEdges(ref long lastTick)
        {
            if (!EdgesPerTick) return true;
            bool fresh = lastTick != EngineTick;
            lastTick = EngineTick;
            return fresh;
        }

        long pilotSentTick = -1;

        /// <summary>The helm holder (a client) sends its hands for the frame it just replayed; a second send in the same engine
        /// tick carries only what is held (the device's edges and mouse delta belong to the tick, not to each Update).</summary>
        public void SendPilot(PilotFrame frame)
        {
            if (!IsClient) return;
            if (!FreshEdges(ref pilotSentTick)) frame = frame.HeldOnly();
            hostLink?.Send("pin " + frame.Write());
            PilotSent++;
        }

        public int PilotSent { get; private set; }

        readonly Dictionary<int, List<PilotFrame>> pilotIn = new Dictionary<int, List<PilotFrame>>();
        readonly Dictionary<int, PilotFrame> pilotLast = new Dictionary<int, PilotFrame>();

        /// <summary>The host's frame for a client's helm: what came from it since the last frame (<see cref="PilotFrame.Compose"/>).</summary>
        public PilotFrame HostPilotFor(int seat)
        {
            pilotIn.TryGetValue(seat, out var since);
            pilotLast.TryGetValue(seat, out var last);
            var frame = PilotFrame.Compose(last, since ?? new List<PilotFrame>());
            if (since != null && since.Count > 0) pilotLast[seat] = since[since.Count - 1];
            since?.Clear();
            return frame;
        }

        /// <summary>Forgets a seat's hands (the helm changed hands): the next holder starts from idle.</summary>
        public void ForgetPilot(int seat)
        {
            pilotIn.Remove(seat);
            pilotLast.Remove(seat);
        }

        /// <summary>The state a sync's save does not hold, sent with it: who flies the helm, each player's seat (by key, to be
        /// taken again after the load, in seat order), and the host's shared settings.</summary>
        public string PendingSyncState { get; private set; }

        /// <summary>The sync state that came with the client's current sync (null until it has).</summary>
        public string ClientSyncState { get; private set; }

        public int Retakes { get; set; }

        /// <summary>This machine's player selected something: everyone sees it (applied with the frames, as commands are).</summary>
        public void Selected(string key)
        {
            if (IsHost) pending.Add("a 0 select " + key);
            else hostLink?.Send("select " + key);
        }

        /// <summary>Applies a frame's roster entries ("a", "j", "l"); session entries are the app's.</summary>
        public void ApplyRosterEntry(string entry)
        {
            var f = entry.Split(new[] { ' ' }, 4);
            if (f.Length < 2) return;
            if (!int.TryParse(f[1], out int seat) || seat < 0 || seat >= MaxPlayers) return;
            switch (f[0])
            {
                case "j":
                    Players[seat] = new CoopPlayer { Seat = seat, Name = f.Length > 2 ? string.Join(" ", f, 2, f.Length - 2) : "player " + (seat + 1) };
                    break;
                case "l":
                    if (Players[seat] != null) Players[seat].Left = true;
                    if (!IsHost || seat != 0) Players[seat] = null;
                    break;
                case "a":
                    if (f.Length >= 3 && f[2] == "select" && Players[seat] != null)
                    {
                        Players[seat].Selection = f.Length > 3 ? f[3] : "n";
                        Log(NameOf(seat) + " (player " + (seat + 1) + ") selected '" + Players[seat].Selection + "' at frame " + Frame);
                    }
                    else if (f.Length >= 3) Log(NameOf(seat) + " (player " + (seat + 1) + ") asked: " + string.Join(" ", f, 2, f.Length - 2) + " (applied at frame " + Frame + ")");
                    break;
            }
        }

        // ================================================================ host

        long frameNo;
        FrameRecord open;
        readonly List<string> pending = new List<string>();
        readonly List<(int Seat, string Request)> clockRequests = new List<(int, string)>();
        readonly List<Remote> remotes = new List<Remote>();
        readonly Dictionary<long, string> digests = new Dictionary<long, string>();
        bool syncWanted;
        string syncReason;
        int syncId;
        List<string> syncChunks;
        long syncFrame = -1;
        int presenceTick;
        public long Frame => IsHost ? frameNo : ClientRecord?.Frame ?? lastFrame;
        public int RemoteCount => remotes.Count;
        public int DigestMatches { get; private set; }
        public int Desyncs { get; private set; }

        enum RemoteState { Joining, Welcomed, Syncing, Running }

        sealed class Remote
        {
            public ICoopLink Link;
            public int Seat = -1;
            public RemoteState State;
            public int SyncId;
            public int NextChunk;
            public int Acked = -1;
            public int Resyncs;
            public bool Bye;
        }

        /// <summary>The record of the frame the host's Update is running, once its clock has been reached (null outside).</summary>
        public FrameRecord Open => open;

        /// <summary>Whether the host's current frame has set anything clients replay.</summary>
        public bool InFrame => IsHost ? open != null : ClientRecord != null;

        /// <summary>At the app's clock step: opens this frame's record, with every command queued since the last one.</summary>
        public FrameRecord HostOpenFrame()
        {
            if (open != null) return open;
            open = new FrameRecord { Frame = ++frameNo, Dt = UnityEngine.Time.unscaledDeltaTime };
            open.Entries.AddRange(pending);
            pending.Clear();
            return open;
        }

        /// <summary>The clients' clock requests since the last frame, for the host's clock (it is the authority over time).</summary>
        public List<(int Seat, string Request)> TakeClockRequests()
        {
            var list = new List<(int, string)>(clockRequests);
            clockRequests.Clear();
            return list;
        }

        /// <summary>After each Update on the host: the frame goes out, digests and presence, a pending sync, the save's chunks.</summary>
        public void HostEndUpdate(UnitySpaceApp app)
        {
            if (open != null)
            {
                var rec = open;
                open = null;
                string msg = rec.Write();
                foreach (var r in remotes)
                    if (r.State == RemoteState.Syncing || r.State == RemoteState.Running) r.Link.Send(msg);
                if (app != null && rec.Frame % DigestEvery == 0 && (remotes.Count > 0 || CoopSmoke.Active))
                {
                    string d = app.CoopDigest();
                    digests[rec.Frame] = d;
                    digests.Remove(rec.Frame - DigestEvery * 64);
                    Log("digest frame " + rec.Frame + " = " + d + " (" + LocalName + ", player 1)");
                }
                if (app != null && ++presenceTick % PresenceEvery == 0) HostPresence(app);
                CoopSmoke.HostFrame(this, app, rec);
            }
            if (syncWanted && app != null) TryBeginSync(app);
            PumpChunks();
        }

        void HostPresence(UnitySpaceApp app)
        {
            var me = Players[0];
            if (me != null) (me.Body, me.OffsetKm) = app.CoopPresence();
            if (remotes.Count == 0) return;
            var sb = new System.Text.StringBuilder("roster");
            foreach (var p in Players)
                if (p != null) sb.Append('\n').Append(p.Seat).Append(' ').Append(p.Body).Append(' ').Append(Wire.D(p.OffsetKm.X)).Append(' ')
                    .Append(Wire.D(p.OffsetKm.Y)).Append(' ').Append(Wire.D(p.OffsetKm.Z));
            string msg = sb.ToString();
            foreach (var r in remotes)
                if (r.State == RemoteState.Running) r.Link.SendUnreliable(msg);
        }

        /// <summary>A save for every client at the next frame boundary (a join, a load on the host, a desync).</summary>
        /// <summary>US-T43: the sync wanted is for a load (every view starts again, as a load does); else a resync, which leaves
        /// each player's view as it was.</summary>
        bool syncLoad;

        public void RequestSync(string reason, bool load)
        {
            syncLoad |= load;
            RequestSync(reason);
        }

        public void RequestSync(string reason)
        {
            if (!IsHost) return;
            if (!syncWanted) Log("sync wanted: " + reason);
            syncWanted = true;
            syncReason = reason;
        }

        void TryBeginSync(UnitySpaceApp app)
        {
            string blocked = app.CoopSaveBlocker();
            if (blocked != null) return; // tried again next frame
            syncWanted = false;
            var watch = Stopwatch.StartNew();
            // US-T43: what the save does not hold (who flies the helm, every player's seat, the shared settings), taken
            // before it, so every peer (this one too) takes the seats again after loading the same bytes.
            string state = app.CoopSyncState(!syncLoad) + "\n" + CoopSettings.Snapshot();
            syncLoad = false;
            PendingSyncState = state;
            byte[] save = app.CoopCaptureSave();
            long captured = watch.ElapsedMilliseconds;
            if (HostReloads)
            {
                Applying = true;
                try { app.CoopApplySave(save, state); }
                finally { Applying = false; }
            }
            long reloaded = watch.ElapsedMilliseconds;
            syncChunks = Wire.Pack(save);
            syncId++;
            syncFrame = frameNo;
            string digest = app.CoopDigest();
            digests[frameNo] = digest;
            int sent = 0;
            foreach (var r in remotes)
            {
                if (r.State == RemoteState.Joining) continue;
                r.State = RemoteState.Syncing;
                r.SyncId = syncId;
                r.NextChunk = 0;
                r.Acked = -1;
                r.Link.Send("sync " + syncId + " " + syncFrame + " " + syncChunks.Count);
                r.Link.Send("syncstate " + syncId + "\n" + state); // US-T43
                sent++;
            }
            Log("sync #" + syncId + " at frame " + syncFrame + " (" + syncReason + "): save " + save.Length / 1024 + " KB in " + syncChunks.Count + " chunks to " + sent + " player(s); captured in " + captured + " ms" +
                (HostReloads ? ", host reloaded it in " + (reloaded - captured) + " ms" : "") + "; digest frame " + syncFrame + " = " + digest + " (" + LocalName + ", player 1)");
            CaptureEvents.CoopResync(syncId, syncFrame, syncReason); // US-T72
        }

        void PumpChunks()
        {
            if (syncChunks == null) return;
            foreach (var r in remotes)
            {
                if (r.State != RemoteState.Syncing || r.SyncId != syncId) continue;
                while (r.NextChunk < syncChunks.Count && r.NextChunk - (r.Acked + 1) < ChunkWindow)
                {
                    r.Link.Send("chunk " + syncId + " " + r.NextChunk + " " + syncChunks[r.NextChunk]);
                    r.NextChunk++;
                }
            }
        }

        /// <summary>A client's link actor reached the host (its player controller logged in).</summary>
        public void HostLogin(ICoopLink link)
        {
            remotes.Add(new Remote { Link = link, State = RemoteState.Joining });
        }

        /// <summary>A client logged out: its seat goes back to its AI on every peer, and it leaves the roster.</summary>
        public void HostLogout(ICoopLink link)
        {
            var r = remotes.Find(x => x.Link == link);
            if (r == null) return;
            remotes.Remove(r);
            if (r.Seat > 0)
            {
                pending.Add("s " + CommandCodec.Write(new ReleaseSeatCommand(new PlayerId(r.Seat))));
                pending.Add("h " + r.Seat + " release"); // US-T43: a helm they flew goes back to its crew too
                pending.Add("l " + r.Seat);
                ForgetPilot(r.Seat);
                Log(NameOf(r.Seat) + " (player " + (r.Seat + 1) + ") left");
                CaptureEvents.CoopLeft(NameOf(r.Seat), r.Seat + 1); // US-T72
            }
        }

        /// <summary>A message from a client.</summary>
        public void HostReceive(ICoopLink link, string msg)
        {
            var r = remotes.Find(x => x.Link == link);
            if (r == null || string.IsNullOrEmpty(msg)) return;
            int sp = msg.IndexOf(' ');
            string kind = sp < 0 ? msg : msg.Substring(0, sp);
            string rest = sp < 0 ? "" : msg.Substring(sp + 1);
            try
            {
                switch (kind)
                {
                    case "hello": Hello(r, rest); break;
                    case "cmd": Command(r, rest); break;
                    case "warp":
                    case "pause":
                        clockRequests.Add((r.Seat, msg));
                        pending.Add("a " + r.Seat + " " + msg);
                        break;
                    case "select": pending.Add("a " + r.Seat + " select " + rest); break;
                    case "ack":
                    {
                        var f = rest.Split(' ');
                        if (Wire.ParseI(f[0]) == r.SyncId) r.Acked = Math.Max(r.Acked, Wire.ParseI(f[1]));
                        break;
                    }
                    case "synced":
                        if (Wire.ParseI(rest) == r.SyncId && r.State == RemoteState.Syncing)
                        {
                            r.State = RemoteState.Running;
                            Log(NameOf(r.Seat) + " (player " + (r.Seat + 1) + ") is in sync #" + r.SyncId + " from frame " + syncFrame);
                        }
                        break;
                    case "digest": Digest(r, rest); break;
                    case "pres":
                    {
                        var f = rest.Split(' ');
                        var p = r.Seat >= 0 ? Players[r.Seat] : null;
                        if (p != null && f.Length >= 4)
                        {
                            p.Body = Wire.ParseI(f[0]);
                            p.OffsetKm = new DVec3(Wire.ParseD(f[1]), Wire.ParseD(f[2]), Wire.ParseD(f[3]));
                        }
                        break;
                    }
                    case "resync":
                        RequestSync(rest.Length > 0 && char.IsDigit(rest[0]) ? NameOf(r.Seat) + " fell " + rest + " frames behind" : NameOf(r.Seat) + " asked: " + rest);
                        break;
                    // US-T43: the helm's hands, and what a client asks of the host.
                    case "pin":
                    {
                        if (r.Seat < 0) break;
                        if (!pilotIn.TryGetValue(r.Seat, out var list)) pilotIn[r.Seat] = list = new List<PilotFrame>();
                        list.Add(PilotFrame.Read(rest));
                        break;
                    }
                    case "setreq":
                    {
                        int sp2 = rest.IndexOf(' ');
                        string key = sp2 > 0 ? rest.Substring(0, sp2) : rest;
                        if (sp2 < 0 || !CoopSettings.IsSharedKey(key)) { Warn("player " + (r.Seat + 1) + " asked to set '" + key + "', not a shared setting"); break; }
                        pending.Add("a " + r.Seat + " set " + rest);
                        pending.Add("set " + rest);
                        break;
                    }
                    case "travel": pending.Add("t " + r.Seat + " " + rest); break;
                    case "load": pending.Add("a " + r.Seat + " load " + rest); break;
                    case "bye":
                        r.Bye = true;
                        Log(NameOf(r.Seat) + " (player " + (r.Seat + 1) + ") says bye");
                        break;
                    default: Warn("unknown message '" + kind + "' from player " + (r.Seat + 1)); break;
                }
            }
            catch (Exception e)
            {
                Warn("bad message from player " + (r.Seat + 1) + " ('" + (msg.Length > 80 ? msg.Substring(0, 80) : msg) + "'): " + e.Message);
            }
        }

        public bool AnyBye => remotes.Exists(r => r.Bye);

        void Hello(Remote r, string name)
        {
            if (r.Seat >= 0) return;
            int seat = -1;
            for (int i = 1; i < MaxPlayers; i++)
                if (Players[i] == null) { seat = i; break; }
            if (seat < 0)
            {
                r.Link.Send("full");
                Warn("refused " + name + ": all " + MaxPlayers + " seats are taken");
                return;
            }
            name = string.IsNullOrWhiteSpace(name) ? "player " + (seat + 1) : name.Replace('\n', ' ').Trim();
            r.Seat = seat;
            r.State = RemoteState.Welcomed;
            // The roster as it is; joins after this come in the frames (the "j" entry below is one).
            var sb = new System.Text.StringBuilder("welcome " + seat + " " + MaxPlayers);
            foreach (var p in Players)
                if (p != null) sb.Append('\n').Append(p.Seat).Append(' ').Append(p.Name);
            r.Link.Send(sb.ToString());
            pending.Add("j " + seat + " " + name);
            Players[seat] = new CoopPlayer { Seat = seat, Name = name };
            Log(name + " joined as player " + (seat + 1));
            CaptureEvents.CoopJoined(name, seat + 1); // US-T72
            RequestSync(name + " joined");
        }

        void Command(Remote r, string entry)
        {
            // US-T43: the app's own entries ("h" the helm, "k" a cockpit key, "x" an action that writes the Core), each
            // "<kind> <seat> ...": a client speaks only for its own seat.
            if (entry.Length > 2 && (entry[0] == 'h' || entry[0] == 'k' || entry[0] == 'x') && entry[1] == ' ')
            {
                var f = entry.Split(new[] { ' ' }, 3);
                if (f.Length < 3 || !int.TryParse(f[1], out int seat) || seat != r.Seat) { Warn("player " + (r.Seat + 1) + " sent '" + entry + "' for another seat: dropped"); return; }
                pending.Add(entry);
                return;
            }
            if (!entry.StartsWith("s ", StringComparison.Ordinal)) { Warn("unknown command entry '" + entry + "'"); return; }
            string codec = entry.Substring(2);
            int at = codec.IndexOf(" @", StringComparison.Ordinal);
            var cmd = CommandCodec.Read(at < 0 ? codec : codec.Substring(0, at));
            // A client speaks only for its own seat.
            if (cmd.Player.Seat != r.Seat) { Warn("player " + (r.Seat + 1) + " sent a command for " + cmd.Player + ": dropped"); return; }
            pending.Add(entry);
        }

        void Digest(Remote r, string rest)
        {
            var f = rest.Split(' ');
            long frame = Wire.ParseL(f[0]);
            string theirs = f[1];
            if (!digests.TryGetValue(frame, out var ours)) { Warn("player " + (r.Seat + 1) + "'s digest for frame " + frame + " has nothing to compare with"); return; }
            if (ours == theirs)
            {
                DigestMatches++;
                Log("frame " + frame + " digests match: host " + ours + " = " + NameOf(r.Seat) + " (player " + (r.Seat + 1) + ") " + theirs);
                return;
            }
            Desyncs++;
            Warn("DESYNC at frame " + frame + ": host " + ours + ", " + NameOf(r.Seat) + " (player " + (r.Seat + 1) + ") " + theirs);
            if (r.State != RemoteState.Running) return; // a sync is already on its way
            if (++r.Resyncs > MaxResyncs) { Warn("player " + (r.Seat + 1) + " has desynced " + r.Resyncs + " times: not resyncing again"); return; }
            RequestSync("desync at frame " + frame + " with player " + (r.Seat + 1));
        }

        // ================================================================ client

        ICoopLink hostLink;
        readonly Queue<FrameRecord> queue = new Queue<FrameRecord>();
        string[] chunks;
        int chunksGot, clientSyncId;
        long clientSyncFrame = -1, lastFrame;
        public long FramesSinceSync { get; private set; }

        /// <summary>The client has the host's world loaded and replays its frames.</summary>
        public bool Running { get; private set; }

        public bool Welcomed { get; private set; }
        public bool Refused { get; private set; }

        /// <summary>The frame the client's Update is replaying (null outside one).</summary>
        public FrameRecord ClientRecord { get; set; }

        /// <summary>Set by the app's clock step when it replayed <see cref="ClientRecord"/> (an Update that stopped at the
        /// pause menu did not, and the frame waits).</summary>
        public bool Consumed { get; set; }

        public int Backlog => queue.Count;
        public int ClockCorrections { get; set; }

        /// <summary>The app is up: asks the host for a seat.</summary>
        public void ClientHello() => hostLink?.Send("hello " + LocalName);

        public FrameRecord NextFrame() => queue.Count > 0 ? queue.Peek() : null;

        /// <summary>The client finished replaying <paramref name="rec"/> (and its post steps): digest, presence, the smoke run.</summary>
        public void ClientFrameDone(UnitySpaceApp app, FrameRecord rec)
        {
            queue.Dequeue();
            lastFrame = rec.Frame;
            FramesSinceSync++;
            if (rec.Frame % DigestEvery == 0) SendDigest(app, rec.Frame);
            if (rec.Frame % PresenceEvery == 0)
            {
                var (body, off) = app.CoopPresence();
                if (Players[LocalSeat] != null) (Players[LocalSeat].Body, Players[LocalSeat].OffsetKm) = (body, off);
                hostLink?.SendUnreliable("pres " + body + " " + Wire.D(off.X) + " " + Wire.D(off.Y) + " " + Wire.D(off.Z));
            }
            if (queue.Count > MaxBacklog && Running)
            {
                Warn(queue.Count + " frames behind the host: asking for a resync");
                hostLink?.Send("resync " + queue.Count);
                queue.Clear();
            }
        }

        void SendDigest(UnitySpaceApp app, long frame)
        {
            string d = app.CoopDigest();
            Log("digest frame " + frame + " = " + d + " (" + LocalName + ", player " + (LocalSeat + 1) + ")");
            hostLink?.Send("digest " + frame + " " + d);
        }

        public void SayBye() => hostLink?.Send("bye");

        /// <summary>A message from the host.</summary>
        public void ClientReceive(string msg)
        {
            if (string.IsNullOrEmpty(msg)) return;
            int sp = msg.IndexOfAny(new[] { ' ', '\n' });
            string kind = sp < 0 ? msg : msg.Substring(0, sp);
            try
            {
                switch (kind)
                {
                    case "frame":
                        if (chunks != null || Running) queue.Enqueue(FrameRecord.Read(msg));
                        break;
                    case "welcome":
                    {
                        var lines = msg.Split('\n');
                        var f = lines[0].Split(' ');
                        LocalSeat = Wire.ParseI(f[1]);
                        for (int i = 1; i < lines.Length; i++)
                        {
                            var p = lines[i].Split(new[] { ' ' }, 2);
                            int seat = Wire.ParseI(p[0]);
                            Players[seat] = new CoopPlayer { Seat = seat, Name = p.Length > 1 ? p[1] : "player " + (seat + 1) };
                        }
                        Players[LocalSeat] = new CoopPlayer { Seat = LocalSeat, Name = LocalName };
                        Welcomed = true;
                        Log("welcomed by " + NameOf(0) + " as player " + (LocalSeat + 1) + " (" + LocalName + ")");
                        break;
                    }
                    case "full":
                        Refused = true;
                        Warn("the host's seats are all taken");
                        break;
                    case "sync":
                    {
                        var f = msg.Split(' ');
                        clientSyncId = Wire.ParseI(f[1]);
                        clientSyncFrame = Wire.ParseL(f[2]);
                        chunks = new string[Wire.ParseI(f[3])];
                        chunksGot = 0;
                        ClientSyncState = null;
                        Running = false;
                        // Everything queued is from before the save; what comes now is after it.
                        queue.Clear();
                        break;
                    }
                    case "syncstate":
                    {
                        // US-T43: "syncstate <id>\n<the state>", sent just after its "sync".
                        int nl = msg.IndexOf('\n');
                        if (nl > 0 && Wire.ParseI(msg.Substring(10, nl - 10)) == clientSyncId) ClientSyncState = msg.Substring(nl + 1);
                        break;
                    }
                    case "chunk":
                    {
                        // "chunk <id> <i> <data>"
                        int a = msg.IndexOf(' ', 6), b = msg.IndexOf(' ', a + 1);
                        int id = Wire.ParseI(msg.Substring(6, a - 6)), i = Wire.ParseI(msg.Substring(a + 1, b - a - 1));
                        if (id != clientSyncId || chunks == null || i < 0 || i >= chunks.Length) break;
                        if (chunks[i] == null) chunksGot++;
                        chunks[i] = msg.Substring(b + 1);
                        hostLink?.Send("ack " + id + " " + i);
                        break;
                    }
                    case "roster":
                    {
                        var lines = msg.Split('\n');
                        for (int i = 1; i < lines.Length; i++)
                        {
                            var f = lines[i].Split(' ');
                            if (f.Length < 5) continue;
                            int seat = Wire.ParseI(f[0]);
                            if (seat == LocalSeat || seat < 0 || seat >= MaxPlayers || Players[seat] == null) continue;
                            Players[seat].Body = Wire.ParseI(f[1]);
                            Players[seat].OffsetKm = new DVec3(Wire.ParseD(f[2]), Wire.ParseD(f[3]), Wire.ParseD(f[4]));
                        }
                        break;
                    }
                    default: Warn("unknown message '" + kind + "' from the host"); break;
                }
            }
            catch (Exception e)
            {
                Warn("bad message from the host ('" + (msg.Length > 80 ? msg.Substring(0, 80) : msg) + "'): " + e.Message);
            }
        }

        /// <summary>Once every chunk of a sync is in: the save is loaded, and the frames after it replay.</summary>
        public bool ClientTryLoad(UnitySpaceApp app)
        {
            if (chunks == null || chunksGot < chunks.Length || app == null || ClientSyncState == null) return false;
            var watch = Stopwatch.StartNew();
            byte[] save = Wire.Unpack(chunks);
            chunks = null;
            Applying = true;
            try { app.CoopApplySave(save, ClientSyncState); }
            catch (Exception e)
            {
                Warn("could not load the host's save (sync #" + clientSyncId + "): " + e.Message);
                Refused = true;
                return false;
            }
            finally { Applying = false; }
            lastFrame = clientSyncFrame;
            FramesSinceSync = 0;
            Running = true;
            hostLink?.Send("synced " + clientSyncId);
            Log("loaded sync #" + clientSyncId + " at frame " + clientSyncFrame + ": save " + save.Length / 1024 + " KB in " + watch.ElapsedMilliseconds + " ms, " +
                queue.Count + " frames already waiting");
            CaptureEvents.CoopResync(clientSyncId, clientSyncFrame, ""); // US-T72
            SendDigest(app, clientSyncFrame);
            return true;
        }
    }
}
