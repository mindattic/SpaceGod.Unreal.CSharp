using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using MindAttic.Capture;
using MindAttic.Capture.Unity;
using Unity.SpaceGod.Core;
using Unity.SpaceGod.Input;
using UnityEngine;

namespace Unity.SpaceGod
{
    /// <summary>
    /// The game's events for MindAttic.Capture (US-T72, docs/CAPTURE.md): each one is raised through
    /// <see cref="CaptureUnity.Raise"/> for the capture.json rules and written to the log as one
    /// <c>[capture] event name k=v ...</c> line. Only an observer: nothing here changes the game, and all of it is a no-op
    /// unless capture is running (only <c>-capture</c> on the command line, in a capture-enabled build, starts it).
    /// Polled events compare state once a frame, after the frame (<see cref="Tick"/>, from LateUpdate).
    /// </summary>
    public static class CaptureEvents
    {
        /// <summary>The shipped default rules, in StreamingAssets (docs/CAPTURE.md section 4).</summary>
        public const string ConfigFile = "SpaceGod.capture.json";

        /// <summary>Whether capture is running: false in a plain launch and in builds without capture.</summary>
        public static bool Running => CaptureUnity.Engine.Enabled;

        // ---------------------------------------------------------------- defaults (section 4)

        /// <summary>
        /// Under <c>-capture</c> without <c>-capture-config</c>: the game's shipped capture.json goes into the options
        /// before the engine starts (its runner starts on the first frame). The replay ring stays armed only when the
        /// session records video, so <c>-capture screen N</c> alone never starts a tape.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        internal static void ApplyDefaults() // internal: the facade runs no load hooks, Bootstrap.EnsureApp calls it (US-T72)
        {
            var args = CaptureUnity.Args;
            var options = CaptureUnity.Options;
            if (args == null || !args.Any || options == null || args.ConfigPath != null) return;
            var path = Path.Combine(Application.streamingAssetsPath, ConfigFile);
            try
            {
                if (File.Exists(path)) options.ApplyConfigFile(path);
                else Debug.LogWarning("[capture] no " + ConfigFile + " in StreamingAssets: no default rules");
            }
            catch (Exception e)
            {
                Debug.LogError("[capture] " + ConfigFile + " not applied: " + e.Message);
            }
            if (!options.SessionRecording) options.ReplayArmed = false;
        }

        // ---------------------------------------------------------------- raising

        /// <summary>Raises <paramref name="name"/> with its fields in catalogue order (name, value pairs).</summary>
        public static void Raise(string name, params object[] fields)
        {
            if (!Running) return;
            var data = new CaptureData();
            var line = new StringBuilder("[capture] event ").Append(name);
            for (int i = 0; i + 1 < fields.Length; i += 2)
            {
                string key = (string)fields[i];
                object value = fields[i + 1];
                data[key] = value;
                line.Append(' ').Append(key).Append('=').Append(Format(value));
            }
            Debug.Log(line.ToString());
            CaptureUnity.Raise(name, data);
        }

        /// <summary>A value as the log line writes it: invariant culture, and quoted (inner quotes doubled) when it holds
        /// a space or a quote, or is empty.</summary>
        static string Format(object value)
        {
            string s = value is IFormattable f ? f.ToString(null, CultureInfo.InvariantCulture) : value?.ToString() ?? "";
            if (s.Length == 0 || s.IndexOf(' ') >= 0 || s.IndexOf('"') >= 0) s = "\"" + s.Replace("\"", "\"\"") + "\"";
            return s;
        }

        // ---------------------------------------------------------------- direct events

        public static void Trade(string side, string good, int crates, string market) =>
            Raise("trade", "side", side, "good", good, "crates", crates, "market", market);

        public static void Hail(string from, string to) => Raise("hail", "from", from ?? "", "to", to ?? "");

        public static void GateDeparture(string ship, string gate, string system, string to) =>
            Raise("gate-departure", "ship", ship ?? "", "gate", gate ?? "", "system", system ?? "", "to", to ?? "");

        public static void GameSaved(string slot) => Raise("game-saved", "slot", slot);

        public static void GameLoaded(string slot) => Raise("game-loaded", "slot", slot);

        public static void CoopJoined(string player, int seat) => Raise("coop-joined", "player", player, "seat", seat);

        public static void CoopLeft(string player, int seat) => Raise("coop-left", "player", player, "seat", seat);

        public static void CoopResync(long sync, long frame, string reason) =>
            Raise("coop-resync", "sync", sync, "frame", frame, "reason", reason ?? "");

        // ---------------------------------------------------------------- hotkeys (section 5)

        /// <summary>Alt+F9 / Alt+F10 / Alt+F11, read by <see cref="InputState.Poll"/>: run locally, never sent to co-op peers.</summary>
        public static void Hotkeys(InputState input)
        {
            if (!Running) return;
            if (input.CaptureScreenshot) Command("screenshot");
            if (input.CaptureToggleRecording) Command("toggle-recording");
            if (input.CaptureSaveReplay) Command("save-replay");
        }

        static void Command(string command)
        {
            Debug.Log("[capture] hotkey " + command);
            _ = CaptureUnity.Engine.ExecuteCommandAsync(command);
        }

        // ---------------------------------------------------------------- subscriptions

        static UnitySpaceApp app;
        static Galaxy subscribed;

        /// <summary>Hooks the galaxy's own events (star discoveries, every world's log) and starts the polling afresh:
        /// called once the game is up and again after every rebuild (a load or a co-op sync).</summary>
        public static void Attach(UnitySpaceApp host)
        {
            if (app != host)
            {
                app = host;
                host.GalaxyRebuilt += () => Attach(host);
            }
            primed = false;
            followed = null;
            deadSeen.Clear();
            var galaxy = host.Galaxy;
            if (galaxy == null || galaxy == subscribed) return;
            subscribed = galaxy;
            galaxy.StarDiscovered += e => { if (galaxy == app.Galaxy) Raise("star-discovered", "star", e.Name ?? "", "by", e.By ?? ""); };
            foreach (var system in galaxy.Systems) WatchLog(galaxy, system);
            galaxy.SystemAdded += (s, jd) => WatchLog(galaxy, s);
        }

        static void WatchLog(Galaxy galaxy, StarSystem system)
        {
            var world = system.World;
            world.Logged += e =>
            {
                if (galaxy != app.Galaxy || app.ActiveSystem?.World != world || e.Kind != WorldEventKind.Diplomacy) return;
                if (e.Text.IndexOf(" declares war on ", StringComparison.Ordinal) >= 0) Raise("war-declared", "text", e.Text);
                else if (e.Text.IndexOf("make peace", StringComparison.Ordinal) >= 0 || e.Text.IndexOf("makes peace", StringComparison.Ordinal) >= 0)
                    Raise("peace", "text", e.Text);
            };
        }

        // ---------------------------------------------------------------- polled events

        static bool primed;
        static StarSystem lastSystem;
        static int lastWarp;
        static Ship lastHelm;
        static bool lastDestroyed;
        static ShipPhase lastPhase;
        static int lastBody;
        static Battle followed;
        static int followedSide;
        static StarSystem followedIn;
        static string followedShip = "";
        static readonly HashSet<BattleUnit> deadSeen = new HashSet<BattleUnit>();

        /// <summary>
        /// Compares this frame's state with the last one's, in a fixed order (docs/CAPTURE.md section 3): system-arrival,
        /// time-warp, player-ship-destroyed, orbit-arrival / orbit-departure, battle-started, ship-destroyed, the battle's end.
        /// </summary>
        public static void Tick(UnitySpaceApp host)
        {
            if (!Running || host.Galaxy == null || host.ActiveSystem == null)
            {
                primed = false;
                return;
            }
            var system = host.ActiveSystem;
            var helm = system.World.PilotedShip;
            if (!primed)
            {
                Remember(host, system, helm);
                primed = true;
                return;
            }

            if (system != lastSystem)
                Raise("system-arrival", "system", system.Name, "from", lastSystem?.Name ?? "", "ship", helm?.Name ?? "");
            if (host.Clock.WarpIndex != lastWarp)
                Raise("time-warp", "index", host.Clock.WarpIndex, "label", host.Clock.Warp.Label);
            if (lastHelm != null && !lastDestroyed && lastHelm.Destroyed)
                Raise("player-ship-destroyed", "ship", lastHelm.Name, "class", lastHelm.Spec.Class.ToString(), "system", lastSystem?.Name ?? "");
            if (helm != null && helm == lastHelm && !helm.Destroyed)
            {
                bool parked = helm.Phase == ShipPhase.Parked, wasParked = lastPhase == ShipPhase.Parked;
                if (parked && !wasParked) Raise("orbit-arrival", "ship", helm.Name, "body", BodyName(host, helm.Orbit?.Body ?? -1));
                else if (!parked && wasParked) Raise("orbit-departure", "ship", helm.Name, "body", BodyName(host, lastBody));
            }

            // A battle followed is let go, unannounced, when the system it is in is left.
            if (followed != null && followedIn != system)
            {
                followed = null;
                deadSeen.Clear();
            }
            if (followed == null && helm != null && !helm.Destroyed && helm.Battle != null && !helm.Battle.Over)
            {
                followed = helm.Battle;
                followedIn = system;
                followedShip = helm.Name;
                followedSide = -1;
                foreach (var u in followed.Units)
                {
                    if (u.Ship == helm) followedSide = u.FoughtOn;
                    if (u.Dead) deadSeen.Add(u);
                }
                Raise("battle-started", "battle", followed.Name, "system", system.Name, "ship", helm.Name);
            }
            if (followed != null)
            {
                foreach (var u in followed.Units)
                    if (u.Dead && deadSeen.Add(u))
                        Raise("ship-destroyed", "name", u.Name, "kind", u.Kind.ToString(),
                            "side", followedSide >= 0 && u.FoughtOn == followedSide ? "ours" : "theirs", "battle", followed.Name);
                if (followed.Over)
                {
                    string end = followed.Winner < 0 ? "battle-drawn" : followed.Winner == followedSide ? "battle-won" : "battle-lost";
                    Raise(end, "battle", followed.Name, "system", system.Name, "ship", followedShip);
                    followed = null;
                    deadSeen.Clear();
                }
            }
            Remember(host, system, helm);
        }

        static void Remember(UnitySpaceApp host, StarSystem system, Ship helm)
        {
            lastSystem = system;
            lastWarp = host.Clock.WarpIndex;
            lastHelm = helm;
            lastDestroyed = helm != null && helm.Destroyed;
            lastPhase = helm != null ? helm.Phase : ShipPhase.Parked;
            lastBody = helm?.Orbit?.Body ?? -1;
        }

        static string BodyName(UnitySpaceApp host, int body) =>
            body >= 0 && body < host.Catalog.Count ? host.Catalog[body].Name : "";
    }
}
