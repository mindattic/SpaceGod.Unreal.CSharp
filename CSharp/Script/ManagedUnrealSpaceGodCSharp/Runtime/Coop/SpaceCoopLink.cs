// US-T39: a player's network link in co-op (new file; docs/MIGRATION.md "Co-op").
using System;
using System.Diagnostics;
using Unity.Shared.Core;
using Unity.SpaceGod.Bridge;
using Unity.SpaceGod.Input;
using UnrealSharp.Attributes;
using UnrealSharp.Engine;

namespace Unity.SpaceGod.Coop
{
    /// <summary>
    /// One remote player's link (US-T39): the host spawns one per client that logs in, owned by that client's player
    /// controller and relevant only to it, so the client may call its server RPCs and the host its client RPCs. Everything
    /// co-op says goes through four RPCs: a reliable and an unreliable message each way (<see cref="CoopPeer"/> reads them).
    ///
    /// On the client the link is also what runs the game: there is no game mode on a client, so its Tick does what
    /// ASpaceGameMode's does on the host (US-T26's player loop), once for each host frame waiting, up to
    /// <see cref="MaxFramesPerTick"/> a tick (a client that fell behind catches up).
    /// </summary>
    [UClass]
    public partial class ASpaceCoopLink : AActor, ICoopLink
    {
        const int MaxFramesPerTick = 8;

        // ---------------------------------------------------------------- the four RPCs

        [UFunction(FunctionFlags.RunOnClient | FunctionFlags.Reliable)]
        public partial void ClientMessage(string message);
        public partial void ClientMessage_Implementation(string message)
        {
            var peer = CoopPeer.Current;
            if (peer != null && peer.IsClient) peer.ClientReceive(message);
        }

        [UFunction(FunctionFlags.RunOnClient)]
        public partial void ClientPresence(string message);
        public partial void ClientPresence_Implementation(string message)
        {
            var peer = CoopPeer.Current;
            if (peer != null && peer.IsClient) peer.ClientReceive(message);
        }

        [UFunction(FunctionFlags.RunOnServer | FunctionFlags.Reliable)]
        public partial void ServerMessage(string message);
        public partial void ServerMessage_Implementation(string message)
        {
            var peer = CoopPeer.Current;
            if (peer != null && peer.IsHost) peer.HostReceive(this, message);
        }

        [UFunction(FunctionFlags.RunOnServer)]
        public partial void ServerPresence(string message);
        public partial void ServerPresence_Implementation(string message)
        {
            var peer = CoopPeer.Current;
            if (peer != null && peer.IsHost) peer.HostReceive(this, message);
        }

        public void Send(string message)
        {
            if (HasAuthority()) ClientMessage(message);
            else ServerMessage(message);
        }

        public void SendUnreliable(string message)
        {
            if (HasAuthority()) ClientPresence(message);
            else ServerPresence(message);
        }

        // ---------------------------------------------------------------- the host's side

        /// <summary>The host's link to <paramref name="player"/>, a client that just logged in.</summary>
        public static ASpaceCoopLink SpawnFor(APlayerController player)
        {
            var link = SpawnActor<ASpaceCoopLink>(new UnrealSharp.TSubclassOf<ASpaceCoopLink>(), new UnrealSharp.CoreUObject.FTransform(),
                ESpawnActorCollisionHandlingMethod.AlwaysSpawn, null, player);
            if (link == null) return null;
            // An editor default (an init accessor in UnrealSharp's binding): set once, as the actor is spawned, before it replicates.
            typeof(AActor).GetProperty(nameof(OnlyRelevantToOwner))?.SetValue(link, true);
            link.Replicates = true;
            return link;
        }

        // ---------------------------------------------------------------- the client's side: the player loop

        UnrealHost host;
        bool started, helloSent;

        public override void BeginPlay()
        {
            base.BeginPlay();
            if (HasAuthority()) return;
            CoopPeer.StartClient(this, CoopPeer.NameFromCommandLine(CoopSmoke.Active ? "Client" : null));
            CoopPeer.Log("connected: link to the host is up");
        }

        public override void Tick(float deltaSeconds)
        {
            base.Tick(deltaSeconds);
            if (HasAuthority()) return;
            var peer = CoopPeer.Current;
            if (peer == null || !peer.IsClient) return;
            CoopPeer.EngineTick++; // US-T43: the device's edges belong to the tick, however many host frames it replays
            CoopSmoke.ClientTick(peer);
            if (!started)
            {
                if (UGameplayStatics.GetPlayerController(0) == null) return;
                started = true;
                StartApp();
                return;
            }
            var app = Bootstrap.App;
            if (app == null || peer.Refused) return;
            if (!helloSent)
            {
                helloSent = true;
                peer.ClientHello();
            }
            if (!peer.Running && !peer.ClientTryLoad(app)) return; // waiting for the host's save
            int ran = 0;
            for (int k = 0; k < MaxFramesPerTick; k++)
            {
                var rec = peer.NextFrame();
                if (rec == null) break;
                peer.ClientRecord = rec;
                peer.Consumed = false;
                host.BeforeUpdate(app);
                UnityEngine.UnityBridge.Tick(rec.Dt);
                host.AfterUpdate();
                CoopSmoke.Script?.EndFrame();
                ran++;
                if (!peer.Consumed)
                {
                    // The Update stopped before the clock (the pause menu is open here): the frame waits for the next one.
                    peer.ClientRecord = null;
                    break;
                }
                app.CoopApplyPost(rec);
                peer.ClientFrameDone(app, rec);
                CoopSmoke.ClientFrame(peer, app);
                peer.ClientRecord = null;
                app = Bootstrap.App;
                if (app == null || !peer.Running) break;
            }
            if (ran > 0) UnityEngine.UnityBridge.Sync();
        }

        void StartApp()
        {
            Bootstrap.MatchSettingsOrder(); // US-T70: the settings pages in the original's order (a client has no game mode)
            // A client's autosaves are of the host's world: a profile of their own, never the player's single-player saves.
            var root = System.IO.Path.Combine(SystemLibrary.ProjectSavedDirectory, CoopSmoke.Active ? "SmokeProfiles" : "SpaceGod", "CoopClient");
            System.IO.Directory.CreateDirectory(root);
            Profiles.UseRoot(root);
            var watch = Stopwatch.StartNew();
            UnityEngine.UnityBridge.Begin(this);
            host = new UnrealHost(this);
            host.Begin();
            if (CoopSmoke.Active) host.Override = CoopSmoke.Script = new ScriptedInputDevice();
            if (CoopSmoke.Active) CoopPeer.EdgesPerTick = false; // US-T43: the script clears its edges after every Update
            UGameplayStatics.GetPlayerController(0)?.ClientSetHUD(typeof(UI.ASpaceHud));
            WireHud();
            Bootstrap.EnsureApp();
            if (CoopSmoke.Active) UnityEngine.Application.targetFrameRate = 60;
            CoopPeer.Log("client app up in " + watch.ElapsedMilliseconds + " ms; asking " + "the host for a seat as " + CoopPeer.Current.LocalName);
        }

        public override void EndPlay(EEndPlayReason endPlayReason)
        {
            if (!HasAuthority() && started)
            {
                Bootstrap.TearDown();
                CoopPeer.Stop();
            }
            base.EndPlay(endPlayReason);
        }

        static bool hudWired;

        /// <summary>Every app made while co-op runs (a host's New Game too) gets the co-op HUD.</summary>
        public static void WireHud()
        {
            if (hudWired) return;
            hudWired = true;
            Bootstrap.AppChanged += app =>
            {
                if (app != null && CoopPeer.Active && app.GetComponent<CoopHud>() == null) app.gameObject.AddComponent<CoopHud>();
            };
        }
    }
}
