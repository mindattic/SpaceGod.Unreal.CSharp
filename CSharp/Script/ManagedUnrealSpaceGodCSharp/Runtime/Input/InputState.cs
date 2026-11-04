using UnityEngine;

namespace Unity.SpaceGod.Input
{
    /// <summary>
    /// One frame of player input, polled from the keyboard and mouse (<see cref="IInputDevice"/>: Unreal's player
    /// controller in the game, a script in the smoke run). Keeping all key reads in one place makes the bindings
    /// easy to find and keeps the camera and app logic device-free. Port of Unity.SpaceGod's Runtime/Input/InputState.cs:
    /// the same fields, the same bindings, the same edge rules.
    /// </summary>
    public sealed class InputState
    {
        /// <summary>Where the keys come from; the host sets it (Unreal's player controller, or a script headless).</summary>
        public IInputDevice Device { get; set; } = NoInputDevice.Instance;

        /// <summary>x = strafe, y = rise, z = forward; each in [-1, 1].</summary>
        public Vector3 Move;
        public Vector2 LookDelta;
        public bool LookHeld;

        /// <summary>Raw mouse delta, unconditional (unlike <see cref="LookDelta"/>, which only reports while
        /// the right button is held for camera orbit) — piloting reads this for always-on heading control.</summary>
        public Vector2 MouseDelta;
        public float Scroll;
        public bool Boost;
        public bool Slow;

        /// <summary>Held while piloting: engages the cruise drive (Elite-style) for fast open-space
        /// travel. See <see cref="Unity.SpaceGod.Core.PilotCruiseState"/>.</summary>
        public bool Cruise;

        /// <summary>While piloting a capital ship (U.SPC-T499): W rings its engine telegraph one step ahead, S one step astern (0: neither).</summary>
        public int ThrottleStep;

        /// <summary>While piloting: roll, -1 (Z, anticlockwise) .. 1 (X, clockwise), seen from behind.</summary>
        public float Roll;

        /// <summary>While piloting: switches flight assist on or off (shares F with <see cref="Focus"/>,
        /// which is gated off while piloting, so the two never fire together).</summary>
        public bool ToggleFlightAssist;

        /// <summary>Held, not click-edged: while piloting, fires the ship's forward-mount weapons.
        /// Left click still selects normally outside piloting, via the click-edged <see cref="SelectClicked"/>
        /// below (gated off while piloting, so holding the trigger down and releasing it never also selects).</summary>
        public bool Fire;

        /// <summary>While piloting, in a fight (U.SPC-T271): the EMP trigger (middle mouse, held), and a mine dropped
        /// astern (Ctrl, on the press).</summary>
        public bool FireEmp, DropMine;

        public bool SelectClicked;
        public bool SelectDoubleClicked;
        public Vector2 MousePosition;

        public bool Focus;
        public bool Escape;
        public bool ToggleScale;
        public bool TimeFaster;
        public bool TimeSlower;
        public bool TogglePause;
        public bool ResetTime;
        public bool ToggleOrbits;
        public bool ToggleLabels;
        public bool ToggleHelp;
        /// <summary>U: the lighter toggle, the HUD's panels and readouts off while the names and markers on the view stay.</summary>
        public bool ToggleHud;

        /// <summary>F11 (U.SPC-T496): clears the screen of all its UI but the reticle in use, and brings it all back.</summary>
        public bool ClearScreen;
        public bool ToggleMarket;
        public bool ToggleCargo;
        public bool ToggleOutfit;

        /// <summary>While piloting: switches silent running on or off (F3; see <see cref="Unity.SpaceGod.Core.Ship.SilentRunning"/>).</summary>
        public bool ToggleSilent;

        /// <summary>The bounty board of the port in view (F4): who is wanted there, for how much, and where last seen.</summary>
        public bool ToggleBounties;

        /// <summary>The contracts board of the port in view (F7): jobs to take, and the player's own (U.SPC-T101).</summary>
        public bool ToggleContracts;

        /// <summary>The player's own fleet and property (F10; F9 is the model viewer's): owned ships, captains, standing orders, the books.</summary>
        public bool ToggleFleet;

        /// <summary>The trade desk (backslash, U.SPC-T426): runs, brokers, trade news, charters, insurance, warehouses, the back door.</summary>
        public bool ToggleTrade;

        /// <summary>[ (left bracket): the galaxy map (U.SPC-T103; on F11 until U.SPC-T496 gave that to the clear screen).</summary>
        public bool ToggleGalaxyMap;

        /// <summary>F12 (U.SPC-T427): hails the polity of the ship locked, selected or hailing you, and opens the envoy screen to treat with it.</summary>
        public bool HailPolity;

        /// <summary>The news feed: the full log of what has happened, filterable (X).</summary>
        public bool ToggleNews;

        /// <summary>Dev/sandbox panel: spawn a ship of any class at the camera and pilot it.</summary>
        public bool ToggleSpawn;

        /// <summary>U.SPC-T601: Shift+V puts the practice fighter at the camera and the player at its helm.</summary>
        public bool SpawnPractice;
        public bool WatchBattle;
        public bool ToggleLanes;
        public bool ToggleSatellites;

        /// <summary>U.SPC-T595: Shift+K or numpad * moves the system in view on to its next skybox (+1); Shift+Alt+K or
        /// numpad / back to the one before (-1).</summary>
        public int CycleSky;
        public bool ToggleReorient;
        public bool CycleShips;
        public bool CycleBackward;
        public bool Home;
        public int NumberKey = -1;

        /// <summary>Takes or releases direct control of the selected/focused ship.</summary>
        public bool Pilot;

        /// <summary>Take charge (U.SPC-T422): ' takes the next seat in the fight in view (a ship's helm or a launched
        /// fighter's cockpit), ; the one before (0: neither).</summary>
        public int CycleSeat;

        /// <summary>Take charge (U.SPC-T422): / mans the next gun on the hull flown or selected, and past its last gun
        /// goes back up to the helm.</summary>
        public bool ManGun;

        /// <summary>At a gun mount (U.SPC-T445): Backspace moves up from the gun to the helm of the hull it is mounted on
        /// (in the strategy view Backspace is Home).</summary>
        public bool MoveToHost;

        /// <summary>F9 (U.SPC-T446): the take-charge picker, every seat on the thing in hand and around the camera to click into.</summary>
        public bool TogglePicker;

        /// <summary>While piloting in a fight (U.SPC-T488): Insert retunes the hull's phaser arrays, a narrow lance for hulls or a wide sweep for fighters and missiles.</summary>
        public bool TunePhasers;

        /// <summary>B at the controls (in the strategy view B watches a battle): the boarding command panel, for a fight aboard the piloted hull or one its marines are boarding (U.SPC-T468).</summary>
        public bool ToggleBoarding;
        /// <summary>At a big ship's helm (U.SPC-T491): Shift+Insert orders the crew to run hot, keeping the guns and drive at full however
        /// hot the hull gets, or to throttle down again.</summary>
        public bool ToggleRunHot;

        /// <summary>While piloting: locks (or clears) the camera's tracking onto the hovered/selected
        /// ship. Shares its key with <see cref="ResetTime"/>, which is gated off while piloting so the
        /// two never fire together.</summary>
        public bool LockTarget;

        /// <summary>While piloting, in range of one of this system's jump gates: activates it.</summary>
        public bool ActivateGate;

        /// <summary>While piloting a hull with its own FTL drive (U.SPC-T417): spools the drive for what the nose is on (Enter), or calls a charge off.</summary>
        public bool EngageFtl;

        /// <summary>While piloting a hull with its own FTL drive (U.SPC-T420): End engages the FTL autopilot for the plotted
        /// course (plotting one for the star the nose is on if there is none), or lets go of it.</summary>
        public bool ToggleFtlAutopilot;

        /// <summary>While piloting a hull with its own FTL drive (U.SPC-T420): Page Down plots a course to the next star or
        /// beacon out, nearest first, Page Up the one before (0: neither).</summary>
        public int CycleFtlTarget;

        /// <summary>While piloting (U.SPC-T420): Delete clears the plotted FTL course.</summary>
        public bool ClearFtlCourse;

        /// <summary>At a capital ship's helm (U.SPC-T502): Shift+Delete orders the helm assist to roll the hull's weak shield
        /// patches away from the heaviest fire, or lets the roll go back to the helmsman.</summary>
        public bool ToggleRollPatches;

        /// <summary>F5 saves to the quicksave slot, F8 loads it, F6 opens the save slots.</summary>
        public bool QuickSave, QuickLoad, ToggleSaves;

        /// <summary>While capture runs (US-T72, docs/CAPTURE.md section 5): Alt+F9 a screenshot, Alt+F10 toggles an
        /// on-demand recording, Alt+F11 saves the replay. Local to this machine: never sent to co-op peers, so they are
        /// properties, which PilotFrame (it carries every public field) leaves out.</summary>
        public bool CaptureScreenshot { get; private set; }
        public bool CaptureToggleRecording { get; private set; }
        public bool CaptureSaveReplay { get; private set; }

        Vector2 pressPosition;
        float lastClickTime = -10f;
        Vector2 lastClickPosition;
        bool rightDragged;
        Vector2 rightPressPosition;

        /// <summary>True on the frame the right button was released without dragging (a "right click").</summary>
        public bool RightClicked;

        /// <summary>Reads this frame's input; with <paramref name="suspended"/> (another screen has the
        /// keyboard and mouse, such as the model viewer) everything reads as idle instead.</summary>
        public void Poll(bool pointerOverUi, bool suspended = false)
        {
            var kb = Device;
            var mouse = Device;
            Reset();
            if (suspended || kb == null || !kb.Present) return;

            Move = new Vector3(
                Axis(kb, Key.D, Key.A),
                Axis(kb, Key.E, Key.Q) + Axis(kb, Key.RightBracket, Key.C),
                Axis(kb, Key.W, Key.S));
            Move.y = Mathf.Clamp(Move.y, -1, 1);
            Boost = kb.IsPressed(Key.LeftShift) || kb.IsPressed(Key.RightShift);
            Slow = kb.IsPressed(Key.LeftCtrl) || kb.IsPressed(Key.RightCtrl);
            Cruise = kb.IsPressed(Key.J);
            ThrottleStep = kb.WasPressedThisFrame(Key.W) ? 1 : kb.WasPressedThisFrame(Key.S) ? -1 : 0;
            Roll = Axis(kb, Key.X, Key.Z);

            MousePosition = mouse.MousePosition;
            MouseDelta = mouse.MouseDelta;
            Fire = mouse.IsPressed(MouseButton.Left) && !pointerOverUi;
            FireEmp = mouse.IsPressed(MouseButton.Middle) && !pointerOverUi;
            DropMine = kb.WasPressedThisFrame(Key.LeftCtrl) || kb.WasPressedThisFrame(Key.RightCtrl);
            LookHeld = mouse.IsPressed(MouseButton.Right);
            LookDelta = LookHeld ? MouseDelta : Vector2.zero;
            if (mouse.WasPressedThisFrame(MouseButton.Right))
            {
                rightPressPosition = MousePosition;
                rightDragged = false;
            }
            if (LookHeld && (MousePosition - rightPressPosition).sqrMagnitude > 16) rightDragged = true;
            if (mouse.IsPressed(MouseButton.Right) && mouse.MouseDelta.sqrMagnitude > 4) rightDragged = true;
            RightClicked = mouse.WasReleasedThisFrame(MouseButton.Right) && !rightDragged;

            if (!pointerOverUi)
            {
                float wheel = mouse.ScrollY;
                if (Mathf.Abs(wheel) > 0.01f)
                {
                    // Standard mice report multiples of 120 per notch; trackpads and high-resolution wheels
                    // already report small per-notch values directly, so scale each convention on its own
                    // rather than dividing both by 120 and snapping the underflowed result to a full notch.
                    float normalized = Mathf.Abs(wheel) < 2f ? wheel : wheel / 120f;
                    Scroll = Mathf.Clamp(normalized, -3f, 3f);
                }
                else
                {
                    Scroll = 0f;
                }
            }

            if (mouse.WasPressedThisFrame(MouseButton.Left)) pressPosition = MousePosition;
            if (mouse.WasReleasedThisFrame(MouseButton.Left) && !pointerOverUi && (MousePosition - pressPosition).sqrMagnitude < 36)
            {
                SelectClicked = true;
                float now = Time.unscaledTime;
                SelectDoubleClicked = now - lastClickTime < 0.35f && (MousePosition - lastClickPosition).sqrMagnitude < 100;
                lastClickTime = SelectDoubleClicked ? -10f : now;
                lastClickPosition = MousePosition;
            }

            Focus = kb.WasPressedThisFrame(Key.F);
            ToggleFlightAssist = kb.WasPressedThisFrame(Key.F);
            Escape = kb.WasPressedThisFrame(Key.Escape);
            ToggleScale = kb.WasPressedThisFrame(Key.G) || kb.WasPressedThisFrame(Key.Backquote);
            TimeFaster = kb.WasPressedThisFrame(Key.Period) || kb.WasPressedThisFrame(Key.Equals) || kb.WasPressedThisFrame(Key.NumpadPlus);
            TimeSlower = kb.WasPressedThisFrame(Key.Comma) || kb.WasPressedThisFrame(Key.Minus) || kb.WasPressedThisFrame(Key.NumpadMinus);
            TogglePause = kb.WasPressedThisFrame(Key.Space) || kb.WasPressedThisFrame(Key.Pause);
            ResetTime = kb.WasPressedThisFrame(Key.T);
            LockTarget = kb.WasPressedThisFrame(Key.T);
            ToggleOrbits = kb.WasPressedThisFrame(Key.O);
            ToggleLabels = kb.WasPressedThisFrame(Key.L);
            ToggleHelp = kb.WasPressedThisFrame(Key.H) || kb.WasPressedThisFrame(Key.F1);
            ToggleHud = kb.WasPressedThisFrame(Key.U);
            ToggleMarket = kb.WasPressedThisFrame(Key.M);
            ToggleCargo = kb.WasPressedThisFrame(Key.I);
            ToggleOutfit = kb.WasPressedThisFrame(Key.F2);
            ToggleSilent = kb.WasPressedThisFrame(Key.F3);
            ToggleBounties = kb.WasPressedThisFrame(Key.F4);
            ToggleContracts = kb.WasPressedThisFrame(Key.F7);
            ToggleFleet = kb.WasPressedThisFrame(Key.F10);
            ToggleTrade = kb.WasPressedThisFrame(Key.Backslash);
            ToggleGalaxyMap = kb.WasPressedThisFrame(Key.LeftBracket);
            ClearScreen = kb.WasPressedThisFrame(Key.F11);
            HailPolity = kb.WasPressedThisFrame(Key.F12);
            ToggleNews = kb.WasPressedThisFrame(Key.X);
            ToggleSpawn = kb.WasPressedThisFrame(Key.V) && !Boost;
            SpawnPractice = kb.WasPressedThisFrame(Key.V) && Boost;
            WatchBattle = kb.WasPressedThisFrame(Key.B);
            ToggleLanes = kb.WasPressedThisFrame(Key.N);
            bool skyShift = kb.IsPressed(Key.LeftShift) || kb.IsPressed(Key.RightShift);
            bool skyAlt = kb.IsPressed(Key.LeftAlt) || kb.IsPressed(Key.RightAlt);
            ToggleSatellites = kb.WasPressedThisFrame(Key.K) && !skyShift;
            CycleSky = (kb.WasPressedThisFrame(Key.K) && skyShift && skyAlt) || kb.WasPressedThisFrame(Key.NumpadDivide) ? -1
                : (kb.WasPressedThisFrame(Key.K) && skyShift) || kb.WasPressedThisFrame(Key.NumpadMultiply) ? 1 : 0;
            ToggleReorient = kb.WasPressedThisFrame(Key.R);
            CycleShips = kb.WasPressedThisFrame(Key.Tab);
            CycleBackward = CycleShips && Boost;
            Home = kb.WasPressedThisFrame(Key.Home) || kb.WasPressedThisFrame(Key.Backspace);
            Pilot = kb.WasPressedThisFrame(Key.P);
            CycleSeat = kb.WasPressedThisFrame(Key.Quote) ? 1 : kb.WasPressedThisFrame(Key.Semicolon) ? -1 : 0;
            ManGun = kb.WasPressedThisFrame(Key.Slash);
            MoveToHost = kb.WasPressedThisFrame(Key.Backspace);
            TogglePicker = kb.WasPressedThisFrame(Key.F9);
            // US-T72 (docs/CAPTURE.md section 5): while capture runs, Alt+F9 / Alt+F10 / Alt+F11 are its screenshot, toggle
            // recording and save replay instead of the picker, fleet and clear screen. Without capture Alt changes nothing.
            if (CaptureEvents.Running && (kb.IsPressed(Key.LeftAlt) || kb.IsPressed(Key.RightAlt)))
            {
                CaptureScreenshot = TogglePicker;
                CaptureToggleRecording = ToggleFleet;
                CaptureSaveReplay = ClearScreen;
                TogglePicker = ToggleFleet = ClearScreen = false;
            }
            bool shifted = kb.IsPressed(Key.LeftShift) || kb.IsPressed(Key.RightShift);
            TunePhasers = kb.WasPressedThisFrame(Key.Insert) && !shifted;
            ToggleBoarding = kb.WasPressedThisFrame(Key.B);
            ToggleRunHot = kb.WasPressedThisFrame(Key.Insert) && shifted;
            ActivateGate = kb.WasPressedThisFrame(Key.Y);
            EngageFtl = kb.WasPressedThisFrame(Key.Enter) || kb.WasPressedThisFrame(Key.NumpadEnter);
            ToggleFtlAutopilot = kb.WasPressedThisFrame(Key.End);
            CycleFtlTarget = kb.WasPressedThisFrame(Key.PageDown) ? 1 : kb.WasPressedThisFrame(Key.PageUp) ? -1 : 0;
            ClearFtlCourse = kb.WasPressedThisFrame(Key.Delete) && !shifted;
            ToggleRollPatches = kb.WasPressedThisFrame(Key.Delete) && shifted;
            QuickSave = kb.WasPressedThisFrame(Key.F5);
            QuickLoad = kb.WasPressedThisFrame(Key.F8);
            ToggleSaves = kb.WasPressedThisFrame(Key.F6);

            Key[] digits = { Key.Digit0, Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4, Key.Digit5, Key.Digit6, Key.Digit7, Key.Digit8, Key.Digit9 };
            for (int i = 0; i < digits.Length; i++)
                if (kb.WasPressedThisFrame(digits[i])) NumberKey = i;
        }

        /// <summary>Drops this frame's right-button look (a full screen of its own has the mouse): no look, no right-click.</summary>
        public void ReleaseLook()
        {
            LookHeld = false;
            LookDelta = Vector2.zero;
            RightClicked = false;
        }

        void Reset()
        {
            Move = Vector3.zero;
            LookDelta = Vector2.zero;
            LookHeld = false;
            MouseDelta = Vector2.zero;
            Scroll = 0;
            Roll = 0;
            ToggleFlightAssist = false;
            Boost = Slow = Fire = Cruise = FireEmp = DropMine = false;
            SelectClicked = SelectDoubleClicked = RightClicked = false;
            Focus = Escape = ToggleScale = TimeFaster = TimeSlower = TogglePause = ResetTime = false;
            ToggleOrbits = ToggleLabels = ToggleHelp = ToggleHud = ClearScreen = ToggleMarket = ToggleCargo = ToggleOutfit = ToggleSilent = ToggleBounties = ToggleContracts = ToggleFleet = ToggleTrade = ToggleGalaxyMap = ToggleNews = ToggleSpawn = WatchBattle = ToggleLanes = ToggleSatellites = ToggleReorient = CycleShips = CycleBackward = Home = Pilot = LockTarget = ActivateGate = EngageFtl = false;
            QuickSave = QuickLoad = ToggleSaves = false;
            SpawnPractice = false;
            ToggleFtlAutopilot = ClearFtlCourse = false;
            CycleFtlTarget = 0;
            CycleSeat = 0;
            CycleSky = 0;
            ThrottleStep = 0;
            ManGun = false;
            MoveToHost = false;
            TogglePicker = false;
            TunePhasers = false;
            ToggleBoarding = false;
            ToggleRunHot = false;
            ToggleRollPatches = false;
            NumberKey = -1;
            HailPolity = false;
            CaptureScreenshot = false;
            CaptureToggleRecording = false;
            CaptureSaveReplay = false;
        }

        static float Axis(IInputDevice kb, Key positive, Key negative) =>
            (kb.IsPressed(positive) ? 1f : 0f) - (kb.IsPressed(negative) ? 1f : 0f);
    }
}
