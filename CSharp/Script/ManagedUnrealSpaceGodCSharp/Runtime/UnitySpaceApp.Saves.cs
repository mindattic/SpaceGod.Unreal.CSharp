// Port (US-T26) of Unity.SpaceGod's Runtime/UnitySpaceApp.Saves.cs: the original source, unchanged but for the lines marked US-T26, on
// the shared Unity facade (namespace UnityEngine, ManagedUnrealSpaceGodCSharp/UnityCompat).
using System;
using Unity.SpaceGod.Core;
using Unity.SpaceGod.Rendering;
using Unity.SpaceGod.Views;
using UnityEngine;

namespace Unity.SpaceGod
{
    /// <summary>
    /// Save and load (U.SPC-T67): the whole simulation (the clock, every system's world and the traffic
    /// between them) and the player's session at the helm go to a save slot, and come back from one.
    /// F5 quicksaves, F8 loads the quicksave, F6 opens the slots; an autosave is taken every few minutes
    /// and when the app closes, and <c>-continue</c> on the command line resumes the latest save.
    /// </summary>
    public sealed partial class UnitySpaceApp
    {
        /// <summary>The save slots, in the player's data folder.</summary>
        public SaveSlots Saves { get; private set; }

        /// <summary>The save and load panel (F6).</summary>
        public bool ShowSaves { get; set; }

        /// <summary>Real seconds between autosaves.</summary>
        public const float AutosaveIntervalS = 300f;

        float autosaveDueS = AutosaveIntervalS;

        /// <summary>Off for the scripted tour and captures, so a demo run never rotates the player's autosaves out.</summary>
        bool autosaveEnabled = true;

        void InitSaves()
        {
            // US-T41: the saves' static-object names built now, before any galaxy, without the original's weak-table caches
            // (CSharp/Core/SaveCompat/SaveStatics.cs), so every process names the same objects and reads every other's saves.
            UnrealSpaceGod.SaveCompat.SaveStatics.EnsureFixed();
            Saves = new SaveSlots(ActiveProfile.Current.SavesFolder);
            autosaveEnabled = !Bootstrap.HasCommandLineFlag("-demo") && !Bootstrap.HasCommandLineFlag("-capture");
            // Started from the main menu: the save it picked (or, for a new game, none).
            string picked = GameLaunch.TakeLoadSlot();
            if (picked != null)
            {
                LoadFrom(picked);
                return;
            }
            if (GameLaunch.FromMenu) return;
            var latest = Saves.Latest();
            if (latest == null) return;
            if (Bootstrap.HasCommandLineFlag("-continue")) LoadFrom(latest.Slot);
            else ShowToast("Saved game found (" + DescribeSave(latest) + "): F6 to load it, F8 for the quicksave");
        }

        void UpdateSaves(float dt)
        {
            if (!autosaveEnabled) return;
            autosaveDueS -= dt;
            // Between the stars there is no system to come back to (nor, latched to a hulk, a session to keep): try again once that is over.
            if (autosaveDueS > 0 || SaveBlocker() != null) return;
            autosaveDueS = AutosaveIntervalS;
            Autosave(false);
        }

        void OnApplicationQuit()
        {
            if (autosaveEnabled && Galaxy != null && SaveBlocker() == null) Autosave(true);
        }

        /// <summary>Back to the main menu, taking an autosave on the way as quitting does.</summary>
        public void ReturnToMainMenu()
        {
            if (autosaveEnabled && Galaxy != null && SaveBlocker() == null) Autosave(true);
            GameLaunch.ToMainMenu();
        }

        string pendingSave, pendingSaveLabel, pendingLoad;

        /// <summary>Main Menu was picked in the pause menu: the app leaves at the start of the next frame, not from inside its draw.</summary>
        bool pendingMainMenu;

        /// <summary>Saves to <paramref name="slot"/> at the start of the next frame (the HUD asks from inside its draw, where views must not be rebuilt).</summary>
        public void RequestSave(string slot, string label = null)
        {
            pendingSave = slot;
            pendingSaveLabel = label;
        }

        /// <summary>Loads <paramref name="slot"/> at the start of the next frame.</summary>
        public void RequestLoad(string slot) => pendingLoad = slot;

        void HandleSaveCommands()
        {
            if (pendingMainMenu)
            {
                pendingMainMenu = false;
                ReturnToMainMenu();
                return;
            }
            if (pendingSave != null)
            {
                SaveTo(pendingSave, pendingSaveLabel);
                pendingSave = pendingSaveLabel = null;
            }
            if (pendingLoad != null)
            {
                LoadFrom(pendingLoad);
                pendingLoad = null;
            }
            if (Input.QuickSave) SaveTo(SaveSlots.Quick, "Quicksave");
            if (Input.QuickLoad)
            {
                if (Saves.Exists(SaveSlots.Quick)) LoadFrom(SaveSlots.Quick);
                else ShowToast("No quicksave yet (F5 to quicksave)");
            }
            if (Input.ToggleSaves) ShowSaves = !ShowSaves;
        }

        /// <summary>Why the game cannot be saved or loaded right now, or null when it can.</summary>
        public string SaveBlocker() =>
            Travel.Active ? "Not between the stars: wait until the trip is over"
            // Under the hull's own FTL drive (U.SPC-T417): the charge, the run and the crossing are the helm's until it drops out.
            : InFtlCruise || Ftl.Stage != FtlStage.Idle ? "Not under FTL: wait until the drive drops out"
            // The FTL autopilot's run out is flown on its own clock (U.SPC-T420), not the helm's.
            : FtlAutopilot.Active ? "Not while the FTL autopilot flies: take the helm back first (End)"
            // What the clamps have brought out (survivors, crates, a fight aboard) is the session's until they come off.
            : Salvage != null ? "Not while latched to a hulk: cast off first (4)"
            : null;

        void Autosave(bool quiet)
        {
            try
            {
                var h = Saves.Autosave(CaptureSave());
                if (!quiet) ShowToast("Autosaved  (" + DescribeSave(h) + ")");
            }
            catch (Exception e)
            {
                Debug.LogError("[Unity.SpaceGod] Autosave failed: " + e);
                if (!quiet) ShowToast("Autosave failed: " + e.Message);
            }
        }

        /// <summary>Saves the whole simulation and the session into <paramref name="slot"/>.</summary>
        public bool SaveTo(string slot, string label = null)
        {
            string blocked = SaveBlocker();
            if (blocked != null)
            {
                ShowToast(blocked);
                return false;
            }
            try
            {
                var h = Saves.Save(slot, CaptureSave(), label ?? SlotName(slot));
                ShowToast("Saved to " + SlotName(slot) + "  (" + DescribeSave(h) + ")");
                CaptureEvents.GameSaved(SlotName(slot)); // US-T72
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError("[Unity.SpaceGod] Save to " + slot + " failed: " + e);
                ShowToast("Save failed: " + e.Message);
                return false;
            }
        }

        /// <summary>Replaces the running simulation with the one saved in <paramref name="slot"/>.</summary>
        public bool LoadFrom(string slot)
        {
            if (CoopRoutesLoad(slot)) return false; // US-T43: a client's load is a request to the host, whose save every peer loads
            // (Loading drops whatever the session held, a hulk latched to included: only a trip between the stars stops it.)
            string blocked = Travel.Active || InFtlCruise || Ftl.Stage != FtlStage.Idle ? SaveBlocker() : null;
            if (blocked != null)
            {
                ShowToast(blocked);
                return false;
            }
            SaveData data;
            SaveHeader header;
            try
            {
                data = Saves.Load(slot, out header);
            }
            catch (Exception e)
            {
                Debug.LogError("[Unity.SpaceGod] Loading " + slot + " failed: " + e);
                ShowToast("Could not load " + SlotName(slot) + ": " + e.Message);
                return false;
            }
            CoopLoadingHere(() => ApplyLoaded(data)); // US-T43: ApplyLoaded(data); the loaded helm is everyone's in co-op
            ShowSaves = false;
            ShowToast("Loaded " + SlotName(slot) + "  (" + DescribeSave(header) + ")");
            CaptureEvents.GameLoaded(SlotName(slot)); // US-T72
            return true;
        }

        SaveData CaptureSave()
        {
            var data = new SaveData { Clock = Clock, Galaxy = Galaxy };
            var s = data.Session;
            s.ActiveSystem = ActiveSystem.Name;
            s.ScaleGoal = ScaleGoal;
            if (!Piloted.IsNone) // US-T43: was IsPiloting (in co-op whoever flies it: the host's save is everyone's)
            {
                s.Piloted = Piloted.Ship;
                s.PilotFrameBody = pilotFrameBody;
                s.Pilot = pilot;
                s.PilotAim = pilotAim;
                s.FlightAssistOff = pilotFlightAssistOff;
            }
            return data;
        }

        /// <summary>
        /// Swaps the loaded galaxy and clock in, rebuilds every view around the system that was being watched,
        /// and takes the helm again of the ship that was being flown, exactly where and how it was moving.
        /// </summary>
        void ApplyLoaded(SaveData data)
        {
            // US-T41: a save from the C++ Core carries no delegates: wire them as construction did (CSharp/Core/SaveCompat/SaveWiring.cs).
            UnrealSpaceGod.SaveCompat.SaveWiring.RewireIfNeeded(data);
            DropSession();
            Clock = data.Clock;
            Galaxy = data.Galaxy;
            Materials.DestroyOwned(Interstellar.Root.transform);
            Destroy(Interstellar.Root);
            Interstellar = new InterstellarView(Galaxy, worldRoot);
            SubscribeWorlds();

            var session = data.Session ?? new SaveSession();
            Activate(Galaxy.Find(session.ActiveSystem) ?? Galaxy.Systems[0]);
            ScaleGoal = session.ScaleGoal;
            scaleFrom = ScaleGoal;
            scaleT = 1f;
            Scale.Blend = ScaleGoal;
            Scale.Layout(Ephemeris, bodyScene);
            UpdateRadii();
            GalaxyRebuilt?.Invoke();

            var ship = session.Piloted;
            if (ship != null && !ship.Destroyed && ship.Id < Fleet.Ships.Count && Fleet.Ships[ship.Id] == ship)
            {
                EnterPiloting(Target.OfShip(ship));
                // Its motion as it was, not the spot its schedule was pinned at when the helm was first taken.
                pilotFrameBody = session.PilotFrameBody;
                pilot = session.Pilot;
                pilotViewUp = pilot.Up; // the view's roof follows the hull as saved, not as freshly seeded (U.SPC-T386)
                pilotAim = session.PilotAim;
                pilotFlightAssistOff = session.FlightAssistOff;
                // A ride under way goes on from where it was (EnterPiloting has taken it up again).
                if (ship.Battle == null && ship.Phase != ShipPhase.Lane) PinWherePiloted(ship);
            }
            else
            {
                World.PilotedShip = null;
                FocusSystemOverview();
            }
            autosaveDueS = AutosaveIntervalS;
            CoopLoaded(); // US-T39
        }

        /// <summary>Lets go of everything the session held in the old galaxy, without touching it (it is about to be dropped).</summary>
        void DropSession()
        {
            // A fighter or gun held in the world being left goes back to its AI there (U.SPC-T422); the loaded game starts in command.
            ReleaseSeat();
            session = Coop.CoopPeer.NewSession(); // US-T39: LocalSession.Create(seed: 0) single-player
            commandWarp = -1;
            commandFocus = Target.None;
            Piloted = Target.None;
            pilotLockTarget = Target.None;
            pilotLockUnit = null;
            pilotBoosting = false;
            pilotEmpSeconds = 0;
            pilot = default;
            rideWarpBefore = -1;
            // The FTL autopilot's run out (and the course it flies) belong to the old galaxy's hull: a load made mid-run must
            // not fly the loaded helm on it, nor leave it "flying" with no helm at all (which blocks every save after).
            FtlAutopilot.Reset();
            PlottedCourse = null;
            RideOnward.Clear();
            RideChoice = -1;
            RideThreat = null;
            warnedOf = null;
            laneRejoinable = false;
            SpaceCamera.PilotLockTarget = Target.None;
            Salvage = null;
            SalvageApproach = null;
            salvagePrompted = null;
            salvageLogShown = 0;
            TankerDueS = -1;
            propellantWarning = 0;
            termsPrompted = null;
            demandPrompted = false;
            havenPrompted = null;
            nearGate = null;
            helmTriggerWas = false;
            SpaceCamera.ExitFocus();
        }

        public static string SlotName(string slot)
        {
            if (slot == SaveSlots.Quick) return "Quicksave";
            if (SaveSlots.IsManual(slot)) return "Save " + slot.Substring(5);
            if (slot.StartsWith("autosave-", StringComparison.Ordinal)) return "Autosave " + slot.Substring(9);
            return slot;
        }

        /// <summary>What a list calls the save in <paramref name="slot"/>: the name the player's own save was given, else the slot's.</summary>
        public static string SaveTitle(string slot, SaveHeader h) =>
            SaveSlots.IsManual(slot) && !string.IsNullOrEmpty(h?.Label) ? h.Label : SlotName(slot);

        /// <summary>One line for a save: where and when in the simulation, who was flying and their purse.</summary>
        public static string DescribeSave(SaveHeader h)
        {
            if (h == null) return "empty";
            string text = h.ActiveSystem + ", " + h.SimUtc.ToString("yyyy-MM-dd HH:mm") + " UTC";
            if (h.PilotedShip != null) text += ", flying " + h.PilotedShip + " (" + h.Credits.ToString("N0", System.Globalization.CultureInfo.InvariantCulture) + " cr)";
            return text;
        }
    }
}
