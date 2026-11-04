using System;
using System.Collections.Generic;
using Unity.SpaceGod.Cameras;
using Unity.SpaceGod.Core;
using Unity.SpaceGod.Input;
using Unity.SpaceGod.Rendering;
using Unity.SpaceGod.UI;
using UnityEngine;
using Unity.SpaceGod.Views;

namespace Unity.SpaceGod
{
    /// <summary>
    /// Owns the simulation and drives every frame:
    /// input → clock → every star system (ephemeris, fleet, markets, wars) → scale layout of the
    /// active system → ships → camera → camera-relative views.
    ///
    /// Every star system in the <see cref="Galaxy"/> is simulated on the shared clock all the time;
    /// only the active one is drawn. Switching system tears its views down and builds the other's
    /// (the camera flies between them through the interstellar view, see <see cref="StarTravel"/>).
    ///
    /// Floating origin: the camera never moves from the world origin. All positions are kept
    /// in double-precision scene units and every renderer is placed at (scene - camera) each frame,
    /// so a 3 km ship next to the camera renders cleanly while Neptune sits 4.5 million units away.
    /// (In Unreal the same camera-relative positions become Large World Coordinates doubles at the one
    /// conversion, <see cref="Bridge.UnrealSpace"/>.)
    ///
    /// Port of Unity.SpaceGod's Runtime/UnitySpaceApp.cs, still a MonoBehaviour, on the shared Unity facade
    /// (namespace UnityEngine; UnityBridge runs its Update each Tick of the game mode, ASpaceGameMode). The original's
    /// lines on piloting (TogglePiloting .. UnitNearestCrosshair) are in UnitySpaceApp.Helm.cs; the original's own
    /// partials keep their names.
    /// </summary>
    [SettingsSource]
    public sealed partial class UnitySpaceApp : MonoBehaviour, ICameraWorld
    {
        /// <summary>Raised when the galaxy is rebuilt from scratch (a backwards time reset): anything holding
        /// references into the old one (a running tour, a capture) must stop rather than run on stale data.</summary>
        public event Action GalaxyRebuilt;

        public Galaxy Galaxy { get; private set; }

        /// <summary>The star system being looked at; the properties below are its.</summary>
        public StarSystem ActiveSystem { get; private set; }

        public BodyCatalog Catalog => ActiveSystem.Catalog;
        public Ephemeris Ephemeris => ActiveSystem.Ephemeris;
        public World World => ActiveSystem.World;
        public SimClock Clock { get; private set; }
        public ScaleModel Scale { get; private set; }
        public Fleet Fleet => World.Fleet;
        public Economy Economy => World.Economy;
        public SpaceCamera SpaceCamera { get; private set; }
        public Camera UnityCamera { get; private set; }
        public StarTravel Travel { get; private set; }
        public InterstellarView Interstellar { get; private set; }

        /// <summary>Yards, factories, habitats, spacecraft and docking rings of the active system.</summary>
        public StructureViews Structures { get; private set; }

        /// <summary>The real main belt and Jupiter Trojans (JPL SBDB), drawn on the GPU (Sol only).</summary>
        public AsteroidBelt AsteroidBelt { get; private set; }

        /// <summary>The skybox behind the system in view, kept per system (U.SPC-T595).</summary>
        public SkyView Sky { get; private set; }
        public InputState Input { get; } = new InputState();


        public Target Selected { get; private set; } = Target.None;
        public Target Hovered { get; private set; } = Target.None;

        /// <summary>The ship under direct player control, if any (ship-only, or <see cref="Target.None"/>).</summary>
        public Target Piloted { get; private set; } = Target.None;
        public bool IsPiloting => !Piloted.IsNone && CoopHelmHere; // US-T43: in co-op the helm is everyone's in the shared steps, the holder's elsewhere

        /// <summary>Where the piloted ship really is (its body frame and true km offset) and how fast it is
        /// flying: its schedule shows only the spot it was pinned at when the helm was taken.</summary>
        public int PilotFrameBody => pilotFrameBody;
        public DVec3 PilotOffsetKm => pilotOffsetKm;
        public double PilotSpeedKmS => pilotVelKmS.Length;

        /// <summary>The piloted hull's velocity in its body's frame (ecliptic axes, km/s), nose and roof:
        /// what the HUD's velocity vector and prograde/retrograde markers are drawn from.</summary>
        public DVec3 PilotVelocityKmS => pilotVelKmS;
        public DVec3 PilotForward => pilot.Forward;
        public DVec3 PilotUp => pilot.Up;

        /// <summary>Whether the flight computer is flying the hull to the stick (on each time the helm is
        /// taken; F toggles it) or the hull is drifting on raw thrust. See <see cref="PilotFlight"/>.</summary>
        public bool PilotFlightAssist => !pilotFlightAssistOff;
        bool pilotFlightAssistOff;

        /// <summary>Where a piloted capital ship's engine telegraph stands (U.SPC-T499, <see cref="CapitalFlightModel"/>);
        /// null for any other hull.</summary>
        public EngineTelegraph? PilotTelegraph { get; private set; }

        int pilotFrameBody;

        /// <summary>What the helm flies (U.SPC-T449): the piloted hull's motion and the world's part in this frame,
        /// flown by <see cref="ShipHelm.Control"/>; kept here across frames (and across hulls, a gate handing the
        /// same session to the next).</summary>
        readonly HelmFlight helmFlight = new HelmFlight();

        /// <summary>The helm being flown, holding <see cref="helmFlight"/>; remade when the hull changes.</summary>
        ShipHelm pilotHelm;

        /// <summary>The piloted hull's whole motion (position, velocity, attitude, spin, cruise ramp),
        /// integrated by its helm every frame; the properties below are its parts.</summary>
        ref PilotState pilot => ref helmFlight.State;

        DVec3 pilotOffsetKm { get => pilot.Pos; set => pilot.Pos = value; }
        DVec3 pilotVelKmS { get => pilot.Vel; set => pilot.Vel = value; }
        DVec3 pilotForward => pilot.Forward;

        /// <summary>Where the stick points the nose (ecliptic, unit): mouse movement swings it about the
        /// hull's own roof and starboard axes, so steering stays screen-relative however the hull has
        /// rolled, and it can point anywhere -- straight up, down or behind -- with no gimbal clamp.</summary>
        DVec3 pilotAim = DVec3.UnitX;
        static float PilotLookSensitivity => float.IsNaN(CoopLookSensitivity) ? SpaceCamera.LookSensitivity : CoopLookSensitivity; // US-T43: the helm holder's in co-op

        /// <summary>The free-aim view's roof (see <see cref="PilotAim.Look"/>): it turns with the crosshair and
        /// follows the hull's roll, so the mouse stays screen-relative.</summary>
        DVec3 pilotViewUp;

        /// <summary>At a capital ship's helm the mouse looks around the hull rather than steering it: how far
        /// the view is swung from dead astern, degrees.</summary>
        float helmLookYaw, helmLookPitch;

        /// <summary>How the flown hull is steered (see <see cref="PilotControl"/>).</summary>
        public PilotControl PilotControlMode { get; private set; }

        /// <summary>Where the stick points the hull (ecliptic, unit): the crosshair in free aim.</summary>
        public DVec3 PilotAimDirection => pilotAim;

        /// <summary>The stick straight ahead again, and the view behind the hull: on taking the helm and
        /// whenever the hull is put on a new heading by something other than the stick.</summary>
        void ResetPilotAim()
        {
            pilotAim = pilot.Forward;
            pilotViewUp = pilot.Up;
            helmLookYaw = helmLookPitch = 0;
        }

        /// <summary>What the pilot camera leans toward tracking, independent of the ship's own facing.</summary>
        Target pilotLockTarget = Target.None;

        /// <summary>Base field of view everywhere outside an active boost (matches <see cref="SetupCamera"/>'s
        /// initial value) and the widened view boost eases toward -- a classic sense-of-speed cue
        /// (Freelancer, Elite, Star Citizen all do this) that costs nothing to add since the ship's
        /// actual boosted velocity is already real.</summary>
        const float BaseFov = 60f, BoostFov = 74f, CruiseFov = 86f, FovResponseHz = 6f;
        float cameraFov = BaseFov;
        bool pilotBoosting;

        /// <summary>The cruise drive's own ramp state (see <see cref="PilotFlight.Step"/>) -- persists
        /// across frames like <see cref="pilotVelKmS"/> does, and only while piloting.</summary>
        PilotCruiseState pilotCruise { get => pilot.Cruise; set => pilot.Cruise = value; }

        /// <summary>Real seconds until the emergency tanker the pilot called reaches the ship, or below 0 for none on its way.</summary>
        public float TankerDueS { get; private set; } = -1;

        /// <summary>The propellant warning last shown (0 none, 1 low, 2 dry), so each is toasted once as it is crossed.</summary>
        int propellantWarning;

        /// <summary>Real seconds a tanker the pilot calls takes to come alongside.</summary>
        public const float TankerCallS = 20f;

        /// <summary>Seconds until the next check for a hostile worth proactively starting a fight over
        /// (see <see cref="War.TryStartPlayerBattle"/>) -- a few times a second is plenty responsive
        /// and cheap enough not to bother running every single frame.</summary>
        float pilotBattleCheckTimer;
        const float PilotBattleCheckIntervalS = 0.5f;

        /// <summary>The surrendered hull (or pirate cargo demand) the player was last prompted about, so the
        /// choice toast shows once per hail rather than every frame. See <see cref="UpdateSurrenderPrompts"/>.</summary>
        BattleUnit termsPrompted;
        bool demandPrompted;

        /// <summary>The jump gate the piloted ship is close enough to activate, or null
        /// (see <see cref="UpdateGateActivation"/>); drives that gate's glow and the HUD prompt.</summary>
        JumpGate nearGate;

        /// <summary>The hulk the piloted ship is latched on to (see <see cref="UpdateSalvage"/>), or null.</summary>
        public SalvageSession Salvage { get; private set; }

        /// <summary>The nearest derelict or wreck within <see cref="SalvageApproachKm"/> while piloting and not
        /// latched: a line for the HUD saying how far off it is and how fast the gap is closing.</summary>
        public string SalvageApproach { get; private set; }

        /// <summary>How near a hulk must be for its approach readout to show, km.</summary>
        const double SalvageApproachKm = 60;

        /// <summary>The hulk last offered "1 latch on", and how far into the session's log the toasts have reached.</summary>
        object salvagePrompted;
        int salvageLogShown;

        /// <summary>The piloted ship's offset from the hulk it is latched to, held fixed while the clamps are on.</summary>
        DVec3 salvageLatchOffsetKm;

        public bool ShowOrbits { get; set; } = true;
        public bool ShowLabels { get; set; } = true;
        /// <summary>The Help screen (H / F1, or Help in the Esc menu, U.SPC-T494): every key and control over the whole view; closed at the start.</summary>
        public bool ShowHelp { get; set; }

        /// <summary>U: the lighter toggle; off, the HUD's panels and readouts go while the names and markers on the view stay.</summary>
        public bool ShowHud { get; set; } = true;

        /// <summary>
        /// F11 (U.SPC-T496): the screen cleared of all its UI, in the strategy view and at the helm alike -- HUD, navigator,
        /// help, panels, banners, toasts, names and markers -- all but the reticle in use (the helm's aim, a seat's sight).
        /// F11 again brings it all back as it was; opening a panel or a screen meanwhile brings it back too, so nothing
        /// opens unseen. The pause menu still shows over it.
        /// </summary>
        public bool ScreenCleared { get; set; }

        public bool ShowMarket { get; set; }
        public bool ShowCargo { get; set; }

        /// <summary>The shipyard's outfitting panel (F2): fit modules into the piloted hull's slots at a yard, hand over prisoners.</summary>
        public bool ShowOutfit { get; set; }

        /// <summary>The bounty board (F4): the outlaws (and the player, when wanted) the port in view has bounties on.</summary>
        public bool ShowBounties { get; set; }

        /// <summary>The contracts board (F7) of the port in view: jobs to take, and the player's own to see through or give up.</summary>
        public bool ShowContracts { get; set; }

        /// <summary>The player's fleet and property (F10): buy hulls and property, hire captains, give standing orders, read the books.</summary>
        public bool ShowFleet { get; set; }

        /// <summary>The trade desk (backslash, U.SPC-T426): the trader's rank, runs, brokers, trade news, charters, insurance, warehouses, the back door.</summary>
        public bool ShowTrade { get; set; }

        /// <summary>The galaxy map ([, U.SPC-T103): a full screen of every system, its holder, the gate traffic, fronts and raiders.</summary>
        public bool ShowGalaxyMap { get; set; }

        /// <summary>The polity the galaxy map's envoy screen opens on next (U.SPC-T427), set by a hail; the map takes it and clears it.</summary>
        public Faction EnvoyTo { get; set; }

        /// <summary>
        /// F12 (U.SPC-T427): hails the polity of the ship locked at the helm, the one selected, or the patrol hailing you, in the
        /// name of the polity you speak for (the one commanded from the map, else your own ship's), shows its answer and opens
        /// the galaxy map's DIPLOMACY panel on it to treat.
        /// </summary>
        void HailPolity()
        {
            var statecraft = Galaxy.Polities.Statecraft;
            Faction to = pilotLockTarget.IsShip ? pilotLockTarget.Ship.Faction : null;
            if (to == null && Selected.IsShip) to = Selected.Ship.Faction;
            if (to == null) to = World.Law?.Hail?.By;
            var from = statecraft.Player ?? Piloted.Ship?.Faction;
            if (to == null || to == from)
            {
                ShowToast("Nobody to hail: lock or select a ship of another polity first (F12 hails it)");
                return;
            }
            CaptureEvents.Hail(from?.Name, to.Name); // US-T72: on the peer whose player hailed, before co-op routing
            if (UI.GalaxyMapScreen.CoopSendsHail(this, from, to)) return; // US-T47: the hail at its frame on every peer, the envoy opened here
            ShowToast(statecraft.Hail(from, to, Clock.Jd));
            if (!Statecraft.Party(to)) return;
            EnvoyTo = to;
            ShowGalaxyMap = true;
        }

        /// <summary>The news feed panel in place of the four-line ticker (X).</summary>
        public bool ShowNews { get; set; }
        public bool ShowSpawn { get; set; }

        /// <summary>A screen the player clicks through is open (market, cargo hold, spawn grid): the cursor is
        /// freed for it, so the mouse stops steering (and firing) the piloted hull meanwhile.</summary>
        bool ClickPanelOpen => coopPanelOverride ?? // US-T43: the helm holder's, while its frame is flown in co-op
            !ScreenCleared && (ShowSpawn || ShowSaves || ShowGalaxyMap || ShowHelp || (ShowHud && (ShowMarket || ShowCargo || ShowNews || ShowOutfit || ShowBounties || ShowContracts || ShowFleet || ShowTrade || ShowPicker))); // a cleared screen (F11) shows none of them; market, cargo, outfitting, bounties, contracts and news are HUD panels, hidden with it
        public bool ShowLanes { get; set; } = true;

        /// <summary>Satellite constellations (thousands of points round Earth); off by default, K toggles.</summary>
        public bool ShowSatellites { get; set; }

        /// <summary>False while the camera is between the stars (the system's views are hidden).</summary>
        public bool SystemVisible { get; private set; } = true;

        public bool IsSol => ActiveSystem.Catalog.Root.Name == "Sun";

        /// <summary>Every body's scene position this frame.</summary>
        public DVec3[] BodyScenes => bodyScene;

        /// <summary>1 = gameplay, 0 = realistic; the value the scale is animating toward.</summary>
        public double ScaleGoal { get; private set; } = 1;
        public bool ScaleAnimating => scaleT < 1f;

        public string Toast { get; private set; }
        public float ToastAge { get; private set; } = 99f;

        /// <summary>GUI-space rects drawn by the HUD last frame; clicks inside them do not reach the world.</summary>
        public readonly List<Rect> UiRects = new List<Rect>();

        /// <summary>Where the HUD drew each ship's name last repaint (U.SPC-T376): clicking a name picks its ship, like its icon.</summary>
        public readonly List<(Rect rect, Ship ship)> ShipNameRects = new List<(Rect, Ship)>();

        /// <summary>Ships the HUD folded into another's mark last repaint (U.SPC-T415): clicking the mark picks the ship that stands for it, not one hidden in it.</summary>
        public readonly HashSet<Ship> FoldedShips = new HashSet<Ship>();

        DVec3[] bodyScene;
        double[] bodyRadius;
        BodyView[] bodyViews;
        LineStrip[] orbitLines;
        DVec3[] orbitSamples;
        Vector3[] orbitPoints;
        float orbitRefreshTimer;

        ShipView[] shipViews;
        DVec3[] shipScene;
        Quaternion[] shipRotation;
        double[] shipLength;
        ShipState[] shipStates;
        bool[] shipRotationInitialised;

        // Moving into and out of battle space is eased over a couple of seconds, from wherever the
        // ship was drawn (kept relative to its body, which keeps moving).
        DVec3[] shipBlendFrom;
        float[] shipBlendT;
        Battle[] shipLastBattle;

        // Wrecks burn out where the ship died, in its battle's frame.
        float[] wreckStart;
        BattleFx battleFx;
        DebrisFields debrisFields;
        PodBeacons podBeacons;
        DiscView discView;
        FighterViews fighterViews;
        LaneView laneView;
        GateView gateView;
        EmbeddedPods embeddedPods;
        SatelliteSwarm swarm;
        Faction[] viewFaction = new Faction[0];
        readonly Dictionary<ShipCombat, GameObject> platformViews = new Dictionary<ShipCombat, GameObject>();
        readonly List<(Transform t, Material m)> entryGlows = new List<(Transform, Material)>();
        Battle battleTimeFor;

        /// <summary>Scene units per km inside a battle: the ship scale, so a fight reads at ship size in both views.</summary>
        public double BattleKmScale => Scale.ShipLength(1.0);

        /// <summary>Scene position of a point in a battle (km from its anchor).</summary>
        public DVec3 BattleScene(Battle b, DVec3 localKm) => BattleAnchorScene(b) + localKm.SwapYZ * BattleKmScale;

        /// <summary>Where a battle is drawn: in orbit of its body, or beside the drawn hyperlane for an ambush.</summary>
        DVec3 BattleAnchorScene(Battle b) => AnchorScene(b.Site, b.Body, b.Anchor);

        /// <summary>Scene position of a lasting wreck: where its battle was drawn, at the spot its unit died.</summary>
        public DVec3 WreckScene(Wreck w) => AnchorScene(w.Site, w.Body, w.Anchor) + w.Local.SwapYZ * BattleKmScale;

        /// <summary>Scene position of a drifting escape pod cluster: where its hull's battle was drawn, drifted away from the hull.</summary>
        public DVec3 PodScene(EscapePod p) => AnchorScene(p.Site, p.Body, p.Anchor) + p.LocalAt(Clock.Jd).SwapYZ * BattleKmScale;

        DVec3 AnchorScene(LaneSite site, int body, DVec3 anchor)
        {
            if (site == null) return bodyScene[body] + Scale.LocalOffset(Catalog[body], anchor);
            var hop = site.Hop;
            var aKm = Ephemeris.Helio(hop.From) + site.FromOffset;
            var cKm = Ephemeris.Helio(hop.To) + site.ToOffset;
            var onLane = Scale.LaneCurveScene(Ephemeris, bodyScene, Fleet.Lanes, hop.Lane, hop.From, aKm, hop.To, cKm, site.Progress, out _);
            return onLane + SideScene(aKm, site.Side);
        }

        /// <summary>A sideways offset (true km) beside a lane, in scene units at that distance from the system's root.</summary>
        DVec3 SideScene(DVec3 nearHelio, DVec3 sideKm) =>
            sideKm.SwapYZ * Scale.LocalDistanceDerivative(Catalog.Root, Math.Max(nearHelio.Length, 1e6));

        /// <summary>A ship lurking beside a lane, or drifting after being pulled out of one.</summary>
        DVec3 LaneHoldScene(Ship ship, out DVec3 along)
        {
            var on = Scale.LaneScene(Ephemeris, bodyScene, Fleet.Lanes, ship, ship.LaneHold, out along);
            double ease = Astro.SmoothStep(0, 0.08, Clock.Jd - ship.PhaseStartJd);
            return on + SideScene(Fleet.LanePoint(ship, Clock.Jd, ship.LaneHold), ship.LaneHoldSide) * ease;
        }

        /// <summary>The model a battle unit is drawn with (a ship's view, or a platform), for parenting things to its hull.</summary>
        public Transform UnitTransform(BattleUnit u)
        {
            if (u.Ship != null) return u.Ship.Id < shipViews.Length && shipViews[u.Ship.Id] != null ? shipViews[u.Ship.Id].Root.transform : null;
            if (u.Kind == UnitKind.Platform && platformViews.TryGetValue(u.Combat, out var go)) return go.transform;
            return null;
        }
        LineStrip pathLine;
        Vector3[] pathPoints = new Vector3[64];

        Transform worldRoot;
        Transform systemRoot;
        Hud hud;

        /// <summary>Esc's pause menu (Resume, Settings > Display, Quit): while it is open the frame stops after input.</summary>
        readonly PauseMenu pauseMenu = new PauseMenu();
        double scaleFrom = 1;
        float scaleT = 1f;
        const float ScaleSeconds = 2.4f;
        int lastNumberKey = -1;
        float lastNumberTime = -10f;

        /// <summary>The scene root everything of the game is drawn under (the Rendering port backs it in Unreal).</summary>
        public Transform WorldRoot => worldRoot;

        /// <summary>US-T45: a fixed start for the next game's clock (the screenshot runs, so both tracks show the same
        /// moment); null starts at now, as the original.</summary>
        public static double? StartJd { get; set; }

        /// <summary>Whether Awake has run (Bootstrap runs it on AddComponent, as Unity does, unless the facade already has).</summary>
        internal bool Awoken { get; private set; }

        // ---------------------------------------------------------------- lifecycle

        void Awake()
        {
            if (Awoken) return;
            Awoken = true;
            GameSettingsHost.Bind(); // every declared setting, from the active profile (U.SPC-T431)
            DisplayOptions.ApplySaved(); // the resolution, window mode and VSync last kept (VSync on until then)
            pauseMenu.ShowSaves = () => ShowSaves = true;
            pauseMenu.ShowHelp = () => ShowHelp = true;
            pauseMenu.ToMainMenu = () => pendingMainMenu = true;

            Clock = StartJd is double startJd ? new SimClock(startJd) : SimClock.StartingNow(); // US-T45: was SimClock.StartingNow()
            Clock.SetWarp(SimClock.StartingWarp.Index);
            Scale = new ScaleModel { Blend = 1 };
            Galaxy = Galaxy.CreateDefault(Clock.Jd);

            SetupCamera();
            worldRoot = new GameObject("World").transform;
            var starfield = Starfield.Create(worldRoot);
            // The skybox behind every system (U.SPC-T595): the player's own panoramas join the catalogue first.
            SkyView.DiscoverDropIns();
            Sky = new SkyView(worldRoot, starfield);
            orbitSamples = new DVec3[257];
            orbitPoints = new Vector3[orbitSamples.Length];

            hud = new Hud(this);
            SpaceCamera = new SpaceCamera(this);
            Travel = new StarTravel(this);
            Interstellar = new InterstellarView(Galaxy, worldRoot);
            SubscribeWorlds();

            Activate(Galaxy.Systems[0]);
            AsteroidBelt = AsteroidBelt.Create(this);

            var earth = Catalog["Earth"].Index;
            var start = bodyScene[earth] + new DVec3(-0.6, 0.35, -1).Normalized * (Scale.DisplayRadius(Catalog[earth]) * 9);
            SpaceCamera.PlaceFree(start, 0, 0);
            var look = (bodyScene[earth] - start).Normalized;
            SpaceCamera.PlaceFree(start, (float)(Math.Atan2(look.X, look.Z) * Mathf.Rad2Deg), (float)(-Math.Asin(look.Y) * Mathf.Rad2Deg));
            Select(Target.OfBody(earth));
            InitSaves();
            CaptureEvents.Attach(this); // US-T72: the capture events' subscriptions and polling (docs/CAPTURE.md)
        }

        /// <summary>The polled capture events, after every frame's update (US-T72, docs/CAPTURE.md section 3).</summary>
        void LateUpdate() => CaptureEvents.Tick(this);

        /// <summary>Battle toasts from whichever system is being watched, in every system there is and any a new gate opens (U.SPC-T97).</summary>
        void SubscribeWorlds()
        {
            foreach (var system in Galaxy.Systems) SubscribeWorld(system);
            Galaxy.SystemAdded += (s, jd) => SubscribeWorld(s);
            Galaxy.SystemOpened += (s, by, jd) => ShowToast(s.Name.ToUpperInvariant() + " IS OPEN: " + by.Name + " has finished the first gate in");
        }

        void SubscribeWorld(StarSystem s)
        {
            s.World.War.BattleStarted += b =>
            {
                if (s == ActiveSystem) ShowToast(b.Name.ToUpperInvariant() + " has begun");
            };
        }

        /// <summary>
        /// Makes <paramref name="system"/> the one drawn: its bodies, orbits, ships, lanes, battles,
        /// structures and rings get views (the previous system's are torn down). The camera keeps its
        /// scene position; callers place it.
        /// </summary>
        public void Activate(StarSystem system)
        {
            // Let go of anything in the old system first: its ships, structures and indices are about to go.
            SpaceCamera.ExitFocus();
            if (systemRoot != null)
            {
                Materials.DestroyOwned(systemRoot);
                Destroy(systemRoot.gameObject);
            }
            ActiveSystem = system;
            // The system the player is in runs at full detail, the rest at coarse (U.SPC-T104): this one is brought
            // up to the moment from its last coarse step before anything of it is drawn.
            Galaxy.Promote(system, Clock.Jd);
            // The galaxy is charted and built about wherever the player is (U.SPC-T413).
            Galaxy.Viewpoint = system.Origin;
            systemRoot = new GameObject(system.Name).transform;
            systemRoot.SetParent(worldRoot, false);
            SystemVisible = true;

            int n = Catalog.Count;
            bodyScene = new DVec3[n];
            bodyRadius = new double[n];
            bodyViews = new BodyView[n];
            orbitLines = new LineStrip[n];
            for (int i = 0; i < n; i++)
            {
                bodyViews[i] = new BodyView(Catalog[i], systemRoot);
                if (Catalog[i].ParentIndex >= 0) orbitLines[i] = new LineStrip("Orbit " + Catalog[i].Name, systemRoot);
            }
            shipViews = null;
            BuildShipViews();
            pathLine = new LineStrip("Flight path", systemRoot) { Visible = false };
            battleFx = new BattleFx(this, systemRoot);
            debrisFields = new DebrisFields(this, systemRoot);
            podBeacons = new PodBeacons(this, systemRoot);
            discView = new DiscView(this, systemRoot);
            fighterViews = new FighterViews(this, systemRoot);
            laneView = new LaneView(this, systemRoot);
            gateView = new GateView(this, systemRoot);
            embeddedPods = new EmbeddedPods(this, systemRoot);
            platformViews.Clear();
            entryGlows.Clear();
            Structures = new StructureViews(this, systemRoot);
            swarm = new SatelliteSwarm(this, systemRoot);
            if (AsteroidBelt != null) AsteroidBelt.enabled = IsSol;

            viewFaction = new Faction[0];
            lastBattleScene = new DVec3[0];
            lastDrawnValid = new bool[0];
            battleTimeFor = null;
            Selected = Target.None;
            Hovered = Target.None;
            Scale.Layout(Ephemeris, bodyScene);
            UpdateRadii();
            UpdateShips(0);
            Structures.Layout();
            // A new system starts level with the ecliptic again (auto-reorient then swings to its own plane).
            SpaceCamera.SetUpFrame(Quaternion.identity);
            SpaceCamera.SetPose(SpaceCamera.ScenePosition, SpaceCamera.Rotation);
            // Each system keeps its own sky (U.SPC-T595); the last one stays up until this one has loaded.
            Sky?.Show(system.Name);
            CoopActivated(system); // US-T39: the host's frame tells its clients
        }

        /// <summary>Hides the active system's views (between the stars) or shows them again.</summary>
        public void SetSystemVisible(bool visible)
        {
            SystemVisible = visible;
            if (systemRoot != null) systemRoot.gameObject.SetActive(visible);
        }

        /// <summary>Flies the camera to another star system.</summary>
        public void TravelTo(StarSystem system)
        {
            if (system == null || system == ActiveSystem || Travel.Active) return;
            if (CoopRefusesTravel(system)) return; // US-T39/US-T43: in co-op the party travels together, or not while another player is at the controls
            // Already on the way somewhere under the hull's own drive (U.SPC-T417).
            if (InFtlCruise) { ShowToast("Under FTL for " + FtlCruise.ToName + ": the view goes where the hull goes"); return; }
            // The pilot session belongs to this system's ship and world: the gate is the way across while flying.
            if (IsSeated) ReturnToCommand();
            // At the helm the trip is a plotted course, flown gate by gate by the pilot (U.SPC-T516).
            if (IsPiloting) { PlotCourse(system); return; }
            // A ship followed in the god view is sent on the course and the view goes with it (U.SPC-T516); no ship, the view's own trip.
            if (TrySendFollowedShip(system)) return;
            if (CoopPartyTrip(system)) return; // US-T43: the trip goes into the frames; the host's view makes it, every peer arrives
            Select(Target.None);
            Travel.Begin(system);
        }

        void UpdateRadii()
        {
            // The barycentre is drawn as nothing but still needs a size to frame the system around it.
            for (int i = 0; i < Catalog.Count; i++) bodyRadius[i] = Catalog[i].IsVisible ? Scale.DisplayRadius(Catalog[i]) : 1.0;
        }

        void SetupCamera()
        {
            UnityCamera = Camera.main;
            if (UnityCamera == null)
            {
                var go = new GameObject("Main Camera") { tag = "MainCamera" };
                UnityCamera = go.AddComponent<Camera>();
                Camera.main = UnityCamera; // Unity finds the MainCamera tag; the facade is told
            }
            UnityCamera.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            UnityCamera.clearFlags = CameraClearFlags.SolidColor;
            UnityCamera.backgroundColor = Color.black;
            UnityCamera.fieldOfView = 60f;
            UnityCamera.nearClipPlane = 0.1f;
            UnityCamera.farClipPlane = 5e8f;
            // Whatever the pipeline needs of its camera (HDR off, its per-camera data) and no post-processing yet.
            var backend = RenderBackend.Active;
            backend.SetupCamera(UnityCamera, CameraRole.Main);
            backend.SetMsaa(UnityCamera, EngineSettings.Msaa.Value);
            backend.SetPostProcessing(UnityCamera, PostProcessLook.None);
        }

        /// <summary>Adds views (and per-ship arrays) for hulls a yard has launched since the last frame.
        /// Returns whether a resize actually happened, so a caller reached off the normal per-frame
        /// path (<see cref="TargetScene"/>) knows whether it must also force a fresh <see cref="UpdateShips"/>
        /// before trusting the newcomer's <see cref="shipScene"/> entry.</summary>
        bool SyncShipViews()
        {
            var ships = Fleet.Ships;
            int have = shipViews.Length;
            if (ships.Count == have) return false;
            Array.Resize(ref shipViews, ships.Count);
            Array.Resize(ref shipScene, ships.Count);
            Array.Resize(ref shipRotation, ships.Count);
            Array.Resize(ref shipLength, ships.Count);
            Array.Resize(ref shipStates, ships.Count);
            Array.Resize(ref shipRotationInitialised, ships.Count);
            Array.Resize(ref shipBlendFrom, ships.Count);
            Array.Resize(ref shipBlendT, ships.Count);
            Array.Resize(ref shipLastBattle, ships.Count);
            Array.Resize(ref wreckStart, ships.Count);
            // Their models are built when they are wanted (U.SPC-T414, see UpdateStreaming), not here.
            for (int i = have; i < ships.Count; i++)
            {
                shipBlendT[i] = 1;
                wreckStart[i] = -1;
            }
            return true;
        }

        void BuildShipViews()
        {
            if (shipViews != null)
                foreach (var v in shipViews)
                {
                    if (v == null) continue;
                    Materials.DestroyOwned(v.Root.transform);
                    Destroy(v.Root);
                }
            var ships = Fleet.Ships;
            shipViews = new ShipView[ships.Count];
            shipScene = new DVec3[ships.Count];
            shipRotation = new Quaternion[ships.Count];
            shipLength = new double[ships.Count];
            shipStates = new ShipState[ships.Count];
            shipRotationInitialised = new bool[ships.Count];
            shipBlendFrom = new DVec3[ships.Count];
            shipBlendT = new float[ships.Count];
            shipLastBattle = new Battle[ships.Count];
            wreckStart = new float[ships.Count];
            // No model is built on entering a system (U.SPC-T414): each is streamed in by UpdateStreaming as it is
            // wanted, a few a frame, and drawn as its icon until then.
            for (int i = 0; i < ships.Count; i++)
            {
                shipBlendT[i] = 1;
                wreckStart[i] = ships[i].Destroyed ? float.MaxValue : -1;
            }
        }

        /// <summary>The game scene is unloading (back to the main menu): its materials and view meshes go with it,
        /// and so do the HUD's and the pause menu's own DontSave textures (U.SPC-T366) -- otherwise every session
        /// (and every galaxy map one opened) would leave its textures behind past this scene.</summary>
        void OnDestroy()
        {
            Sky?.Dispose();
            Materials.DestroyAllOwned();
            hud?.DestroyOwnedTextures();
            pauseMenu.DestroyOwnedTextures();
        }

        /// <summary>One frame (MonoBehaviour.Update, run by UnityBridge.Tick). The host has fed Time, Screen and
        /// <see cref="Input"/>'s device first, and applies Cursor and the camera after.</summary>
        void Update()
        {
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            bool overUi = IsPointerOverUi();
            Input.Poll(overUi, ModelViewer.CapturesInput);
            CaptureEvents.Hotkeys(Input); // US-T72: Alt+F9/F10/F11 while capture runs, on this machine only
            // The galaxy map turns with its own drag: the camera behind it does not look about meanwhile.
            if ((ShowGalaxyMap || ShowHelp) && !ScreenCleared) Input.ReleaseLook();
            // The pause menu has the frame: nothing else moves, and the stick and trigger do nothing, until it closes.
            if (pauseMenu.IsOpen)
            {
                pauseMenu.Update(Input.Escape, Time.unscaledDeltaTime);
                FreeCursorForMenu();
                return;
            }
            int screensOpen = OpenScreens();
            HandleCommands();
            // A panel or screen opened while the screen is cleared (F11) brings the UI back, rather than opening unseen.
            if (ScreenCleared && (OpenScreens() & ~screensOpen) != 0) ScreenCleared = false;
            if (pauseMenu.IsOpen)
            {
                FreeCursorForMenu();
                return;
            }

            // The hull flies in real seconds while the battle it is in steps in sim seconds (U.SPC-T555): under warp the
            // AI units would outrun it and the swept collision test (which assumes real-dt motion) would miss them, so
            // time is held at real speed for as long as the piloted hull is in a battle ('.' is refused at the helm).
            if (!Piloted.IsNone && Piloted.Ship?.Battle != null && Clock.WarpIndex > 0) // US-T43: was IsPiloting (any player's helm holds the shared clock)
            {
                Clock.SetWarp(0);
                ShowToast("Time warp is held at real time while the helm is in a battle");
            }

            long raceSim = Race.RaceProbe.Now(); // US-T46: the race's simulation time (docs/RACE.md)
            CoopAdvance(dt); // US-T39: Clock.Advance(dt), in lockstep with the host in co-op
            Galaxy.Update(Clock.Jd);
            Race.RaceProbe.SimMs += Race.RaceProbe.Since(raceSim); // US-T46
            RefreshCharts();
            UpdateSaves(dt);
            SyncShipViews();

            AnimateScale(dt);
            Scale.Layout(Ephemeris, bodyScene);
            UpdateRadii();
            CoopUpdatePiloting(dt); // US-T43: UpdatePiloting(dt), on the helm holder's frame of input on every peer in co-op
            UpdateSeat(dt);
            ShowLawNotices();
            UpdateShips(dt);
            Structures.Layout();
            raceSim = Race.RaceProbe.Now(); // US-T46
            CoopUpdateAdventures(!Travel.Active && !InFtlCruise); // US-T39: was `if (that) World.UpdateAdventures(Clock.Jd, PlayerHelioKm());`, the host's call in co-op
            Race.RaceProbe.SimMs += Race.RaceProbe.Since(raceSim); // US-T46

            CoopShared(DropLostTargets); // US-T43: DropLostTargets(); a helm lost is lost on every peer
            BattleTime();
            CoopShared(UpdateCourseShip); // US-T43: UpdateCourseShip(); the course is the shared helm's on every peer
            if (Travel.Active) Travel.Update(dt);
            // Between the stars under the hull's own drive (U.SPC-T417): the view goes with it.
            else if (InFtlCruise) UpdateFtlCruiseView(dt);
            else
            {
                KeepReoriented();
                SpaceCamera.Update(dt, Input);
            }
            UpdateClipPlanes(dt);
            UpdateStreaming(dt);
            Sky.Update(dt);
            UpdateViews(dt);
            UpdateHover();
            HandleSelectionClicks();

            ToastAge += dt;
            bool lockCursor = (Input.LookHeld || (AtControls && !ClickPanelOpen)) && !ModelViewer.CapturesInput;
            Cursor.lockState = lockCursor ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !lockCursor;
        }

        /// <summary>The pause menu is clicked through, whether or not the helm had the cursor locked.</summary>
        static void FreeCursorForMenu()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        /// <summary>A ship destroyed while selected or followed: let go of it.</summary>
        void DropLostTargets()
        {
            if (Selected.IsShip && Selected.Ship.Destroyed) Select(Target.None);
            if (Hovered.IsShip && Hovered.Ship.Destroyed) Hovered = Target.None;
            if (IsPiloting && Piloted.Ship.Destroyed) ExitPiloting();
            else if (IsPiloting && World.PilotedShip != Piloted.Ship) LoseHelm();
            else if (SpaceCamera.FocusTarget.IsShip && SpaceCamera.FocusTarget.Ship.Destroyed) SpaceCamera.ExitFocus();
        }

        /// <summary>Following a ship into battle slows the clock to battle time once, so the fight can be watched.</summary>
        void BattleTime()
        {
            var t = SpaceCamera.FocusTarget;
            var battle = t.IsShip ? t.Ship.Battle : null;
            if (battle == null || battle == battleTimeFor) return;
            EnterBattleTime(battle);
        }

        /// <summary>Real time, so a capital ship engagement plays out at the pace it is fought.</summary>
        public void EnterBattleTime(Battle battle)
        {
            battleTimeFor = battle;
            if (Clock.WarpIndex > 0)
            {
                Clock.SetWarp(0);
                ShowToast("BATTLE TIME: " + battle.Name + ", " + Clock.Warp.Label + "  (. to speed up)");
            }
        }

        /// <summary>The largest battle under way, or null.</summary>
        public Battle BiggestBattle()
        {
            Battle best = null;
            int bestCount = 0;
            foreach (var b in World.War.Battles)
            {
                int n = 0;
                foreach (var u in b.Units)
                    if (u.Present) n++;
                if (n > bestCount)
                {
                    bestCount = n;
                    best = b;
                }
            }
            return best;
        }

        /// <summary>Flies the camera to a battle's flagship and drops to battle time.</summary>
        public void WatchBattle(Battle battle)
        {
            Ship flag = null;
            foreach (var u in battle.Units)
                if (u.Ship != null && u.Present && (flag == null || u.Combat.Spec.Hull > flag.Combat.Spec.Hull)) flag = u.Ship;
            if (flag == null) return;
            var t = Target.OfShip(flag);
            Select(t);
            SpaceCamera.FocusOn(t, 30, 25f);
            EnterBattleTime(battle);
        }

        /// <summary>The HUD and pause menu's repaint (MonoBehaviour.OnGUI): the UI port draws them in Unreal.</summary>
        void OnGUI()
        {
            // The model viewer has the screen: the game's HUD would paint over the model and its buttons
            // (travel, Now, Focus...) would still act on the hidden game.
            if (ModelViewer.CapturesInput) return;
            // Under the pause menu the HUD is shown but cannot be clicked.
            bool menu = pauseMenu.IsOpen;
            if (menu) GUI.enabled = false;
            hud.Draw();
            GUI.enabled = true;
            if (menu) pauseMenu.Draw();
        }

        bool IsPointerOverUi()
        {
            var device = Input.Device;
            if (device == null || !device.Present) return false;
            var p = device.MousePosition;
            var gui = new Vector2(p.x, Screen.height - p.y);
            foreach (var r in UiRects)
                if (r.Contains(gui)) return true;
            return false;
        }

        // ---------------------------------------------------------------- commands

        /// <summary>The panels and full screens open now, one bit each: what a cleared screen (F11) watches for newly opened.</summary>
        int OpenScreens() =>
            (ShowSpawn ? 1 : 0) | (ShowSaves ? 2 : 0) | (ShowGalaxyMap ? 4 : 0) | (ShowMarket ? 8 : 0) | (ShowCargo ? 16 : 0) | (ShowNews ? 32 : 0) |
            (ShowOutfit ? 64 : 0) | (ShowBounties ? 128 : 0) | (ShowContracts ? 256 : 0) | (ShowFleet ? 512 : 0) | (ShowTrade ? 1024 : 0) | (ShowHelp ? 2048 : 0) | (ShowPicker ? 4096 : 0);

        void HandleCommands()
        {
            var i = Input;
            HandleSaveCommands();
            // F11 clears the screen anywhere, between the stars too (U.SPC-T496).
            if (i.ClearScreen) ScreenCleared = !ScreenCleared;
            if (Travel.Active || InFtlCruise)
            {
                // Between the stars only time and the escape hatch work (and under the hull's own drive, U.SPC-T417, not
                // even that: there is nowhere to come out but the far star).
                if (i.Escape && Travel.Active) Travel.Finish();
                else if (i.Escape) ShowToast("Under FTL between the stars: the drive drops out at the far star");
                if (i.TimeFaster) Clock.Faster();
                if (i.TimeSlower) Clock.Slower();
                if (i.TogglePause) TogglePause();
                return;
            }
            if (i.ToggleScale) ToggleScale();
            // Being run down, time stays at the pace the chase is flown (U.SPC-T273).
            if (i.TimeFaster) TimeFaster();
            if (i.TimeSlower) Clock.Slower();
            if (i.TogglePause) TogglePause();
            if (i.ResetTime && !AtControls) ResetToNow();
            if (i.ToggleOrbits) ShowOrbits = !ShowOrbits;
            if (i.ToggleLabels) ShowLabels = !ShowLabels;
            if (i.ToggleHelp) ShowHelp = !ShowHelp;
            if (i.ToggleHud) ShowHud = !ShowHud;
            if (i.ToggleMarket) ShowMarket = !ShowMarket;
            if (i.ToggleCargo) ShowCargo = !ShowCargo;
            if (i.ToggleOutfit) ShowOutfit = !ShowOutfit;
            if (i.ToggleBounties) ShowBounties = !ShowBounties;
            if (i.ToggleContracts) ShowContracts = !ShowContracts;
            if (i.ToggleFleet) ShowFleet = !ShowFleet;
            if (i.ToggleTrade) ShowTrade = !ShowTrade;
            if (i.ToggleGalaxyMap) ShowGalaxyMap = !ShowGalaxyMap;
            if (i.HailPolity) HailPolity();
            // At the helm X rolls the hull (Z/X): the news feed would free the cursor and take the stick and guns.
            if (i.ToggleNews && !AtControls) ShowNews = !ShowNews;
            if (i.ToggleSpawn) ShowSpawn = !ShowSpawn;
            // Shift+V: the practice fighter at the camera, the player at its helm (U.SPC-T601).
            if (i.SpawnPractice) SpawnPracticeFighter();
            if (i.ToggleLanes) ShowLanes = !ShowLanes;
            if (i.ToggleReorient) ToggleAutoReorient();
            if (i.ToggleSatellites)
            {
                ShowSatellites = !ShowSatellites;
                ShowToast(ShowSatellites ? "SATELLITES ON: constellations drawn" : "SATELLITES OFF  (K to show)");
            }
            // Shift+K / numpad * cycles the sky of the system in view, which keeps it (U.SPC-T595).
            if (i.CycleSky != 0 && ActiveSystem != null)
            {
                var sky = Sky.Cycle(i.CycleSky);
                ShowToast("SKY: " + sky.Name + "  (" + ActiveSystem.Name + " keeps it; Shift+K for the next)");
            }
            // The chase camera belongs to the pilot: nothing re-aims it at something else mid-flight.
            if (i.WatchBattle && !AtControls)
            {
                var b = BiggestBattle();
                if (b != null) WatchBattle(b);
                else ShowToast("No battles under way");
            }
            if (i.Home && !AtControls) FocusSystemOverview();
            if (i.Focus && !AtControls) FocusSelected();
            if (i.Escape) BackOut();
            if (i.CycleShips && !AtControls) CycleShips(i.CycleBackward ? -1 : 1);
            if (i.NumberKey >= 0 && !AtControls) NumberHotkey(i.NumberKey);
            if (i.Pilot) TogglePiloting();
            // Take charge of anything, any time (U.SPC-T422): the next seat in the fight in view, or the next gun on the hull.
            if (i.CycleSeat != 0) CycleSeat(i.CycleSeat);
            if (i.ManGun) ManNextGun();
            // At a gun mount (U.SPC-T445): up to the helm of the hull it is on.
            if (i.MoveToHost && IsSeated) MoveSeatToHost();
            if (i.TogglePicker) TogglePicker();
        }

        /// <summary>Speeds time up a step (the key and the top bar's button alike), unless the ship at the helm is being
        /// run down: then time stays at the pace the chase is flown (U.SPC-T273).</summary>
        public void TimeFaster()
        {
            if (IsPiloting && World.Pursuit != null && World.Pursuit.Active) ShowToast("Being hunted: time stays at real speed until you shake them off or fight");
            else Clock.Faster();
        }

        /// <summary>
        /// Esc (U.SPC-T276): closes the panels being clicked through, else leaves the camera's focus, else clears
        /// the selection, and with nothing left to back out of opens the pause menu. At the helm it goes from the
        /// panels straight to the menu: P, not Esc, hands the ship back. See <see cref="EscapeKey"/>.
        /// </summary>
        void BackOut()
        {
            switch (EscapeKey.Route(ClickPanelOpen, AtControls, SpaceCamera.Mode != SpaceCamera.CameraMode.Free, !Selected.IsNone))
            {
                case EscapeAction.ClosePanels:
                    ShowSpawn = ShowSaves = ShowMarket = ShowCargo = ShowNews = ShowOutfit = ShowBounties = ShowContracts = ShowFleet = ShowTrade = ShowGalaxyMap = ShowHelp = ShowPicker = false;
                    break;
                case EscapeAction.LeaveFocus:
                    SpaceCamera.ExitFocus();
                    break;
                case EscapeAction.Deselect:
                    Select(Target.None);
                    break;
                default:
                    pauseMenu.Open();
                    break;
            }
        }

        static readonly string[] SolNumberBodies = { "Sun", "Mercury", "Venus", "Earth", "Mars", "Jupiter", "Saturn", "Uranus", "Neptune", "Pluto" };

        static readonly string[] CentauriNumberBodies =
        {
            "Alpha Centauri A", "Alpha Centauri B", "Proxima Centauri", "Proxima b", "Proxima d", "Proxima c",
            "Alpha Centauri Ab", "Rigil Gate", "Toliman Forge", "Blackhollow",
        };

        /// <summary>The bodies the number keys pick: Sol's and Alpha Centauri's chosen lists, elsewhere the visible bodies in catalogue order.</summary>
        string[] NumberBodies()
        {
            if (IsSol) return SolNumberBodies;
            if (ActiveSystem.Name == "Alpha Centauri") return CentauriNumberBodies;
            var names = new List<string>();
            foreach (var b in Catalog.Bodies)
                if (b.IsVisible && names.Count < 10) names.Add(b.Name);
            return names.ToArray();
        }

        void NumberHotkey(int key)
        {
            var names = NumberBodies();
            if (key >= names.Length || !Catalog.TryGet(names[key], out var body)) return;
            var target = Target.OfBody(body.Index);
            bool repeat = key == lastNumberKey && Time.unscaledTime - lastNumberTime < 0.45f;
            Select(target);
            if (repeat || SpaceCamera.Mode != SpaceCamera.CameraMode.Free) SpaceCamera.FocusOn(target);
            lastNumberKey = key;
            lastNumberTime = Time.unscaledTime;
        }

        void CycleShips(int direction)
        {
            var candidates = new List<Ship>();
            foreach (var s in Fleet.Ships)
                if (!s.IsEscort && !s.Destroyed && s.Phase != ShipPhase.Landed) candidates.Add(s);
            if (candidates.Count == 0) return;
            int current = Selected.IsShip ? candidates.IndexOf(Selected.Ship.Controller) : -1;
            // With nothing selected, forward starts at the first ship and backward at the last.
            int next = current < 0 ? (direction > 0 ? 0 : candidates.Count - 1)
                : ((current + direction) % candidates.Count + candidates.Count) % candidates.Count;
            Select(Target.OfShip(candidates[next]));
            if (SpaceCamera.Mode != SpaceCamera.CameraMode.Free) SpaceCamera.FocusOn(Selected);
        }

        public void Select(Target target)
        {
            Selected = target;
        }

        public void FocusSelected()
        {
            // The chase camera belongs to the pilot (the HUD's Focus button lands here too), or whoever holds a seat.
            if (AtControls) return;
            if (Selected.IsNone)
            {
                if (SpaceCamera.Mode != SpaceCamera.CameraMode.Free) SpaceCamera.ExitFocus();
                return;
            }
            bool refocus = SpaceCamera.Mode == SpaceCamera.CameraMode.Focus && SpaceCamera.FocusTarget == Selected;
            SpaceCamera.FocusOn(Selected, refocus ? SpaceCamera.DefaultFraming(Selected) : double.NaN);
        }

        // ---------------------------------------------------------------- piloting: see UnitySpaceApp.Helm.cs

        // ---------------------------------------------------------------- system overview, plane, time, scale

        /// <summary>How much of the system the overview frames: the inner planets in Sol, the A-B pair in Alpha Centauri, the planets elsewhere.</summary>
        double OverviewSpanKm => ActiveSystem.OverviewSpanKm;

        /// <summary>The overview's camera distance from the system's root, scene units.</summary>
        public double OverviewDistance() => Scale.LocalDistance(Catalog.Root, OverviewSpanKm) * 1.9;

        public void FocusSystemOverview()
        {
            var root = Target.OfBody(0);
            Select(Catalog.Root.IsVisible ? root : Target.None);
            SpaceCamera.FocusOn(root, OverviewDistance() / Math.Max(bodyRadius[0], 1e-9), OverviewPitch);
        }

        /// <summary>
        /// The plane the active system is laid out in, as its north-side normal (Unity axes): the ecliptic in Sol, the
        /// orbit of the first star round the barycentre for a binary (Alpha Centauri's A-B plane, steeply tilted to the
        /// ecliptic), and for any other star the plane its own planets sweep, weighted by their masses.
        /// </summary>
        public Vector3 SystemPlaneNormal() => PlaneNormal(ActiveSystem);

        /// <summary>The plane <paramref name="system"/> is laid out in (see <see cref="SystemPlaneNormal"/>), active or not.</summary>
        public static Vector3 PlaneNormal(StarSystem system)
        {
            var catalog = system.Catalog;
            var eph = system.Ephemeris;
            if (catalog.Root.Name == "Sun" || catalog.Count < 2) return Vector3.up;
            DVec3 n;
            if (!catalog.Root.IsStar) n = DVec3.Cross(eph.Relative(1), eph.RelativeVelocityKmS(1));
            else
            {
                n = DVec3.Zero;
                for (int i = 0; i < catalog.Count; i++)
                    if (catalog[i].ParentIndex == catalog.Root.Index)
                        n += DVec3.Cross(eph.Relative(i), eph.RelativeVelocityKmS(i)) * Math.Max(catalog[i].Gm, 1e-9);
            }
            if (!(n.LengthSquared > 0)) return Vector3.up;
            n = n.Normalized.SwapYZ;
            return new Vector3((float)n.X, (float)n.Y, (float)n.Z);
        }

        /// <summary>
        /// How far out <paramref name="system"/>'s Oort cloud reaches, light-years: the Sun's about 100,000 AU, other
        /// systems' scaled by the cube root of their stars' mass, as a star's gravitational reach is.
        /// </summary>
        public static double OortCloudLy(StarSystem system)
        {
            double gm = 0;
            foreach (var b in system.Catalog.Bodies)
                if (b.IsStar) gm += b.Gm;
            double mass = gm > 0 ? gm / Core.StarSystemBuilder.SunGm : 1;
            return SunOortCloudAu * Math.Pow(mass, 1.0 / 3.0) / AuPerLy;
        }

        const double SunOortCloudAu = 100000, AuPerLy = 63241.077;

        public string SystemPlaneName =>
            IsSol ? "the ecliptic" : !Catalog.Root.IsStar ? "the A–B orbital plane" : Catalog.Root.Name + "'s planetary plane";

        /// <summary>Whether the view keeps the active system's plane level by itself (R or the on-screen toggle; remembered between sessions).</summary>
        public bool AutoReorient => AutoReorientSetting.Value;

        /// <summary>Controls (U.SPC-T431), under the key the R toggle has always been remembered by.</summary>
        public static readonly ToggleSetting AutoReorientSetting = new ToggleSetting(GameSettings.AutoReorient, SettingGroups.Controls,
            "Auto-reorient view", true, "Keep the active system's plane level by itself when the view moves to a new system (R in play).",
            "camera level plane horizon");

        public void ToggleAutoReorient()
        {
            AutoReorientSetting.Set(!AutoReorient); // remembered in the profile by the settings registry
            ShowToast(AutoReorient ? "AUTO-REORIENT ON: keeping " + SystemPlaneName + " level" : "AUTO-REORIENT OFF: the view stays as you leave it");
        }

        /// <summary>Swings the view smoothly round until the system's plane is level.</summary>
        public void Reorient()
        {
            // The chase camera follows the hull the mouse is flying: there is no view of its own to level,
            // and the levelling ease would compound on the chase's own easing every frame.
            if (Travel.Active || IsPiloting) return;
            SpaceCamera.Level(SystemPlaneNormal());
        }

        /// <summary>With auto-reorient on, swings the view level again whenever the system's plane is not (a new system, the helm let go).</summary>
        void KeepReoriented()
        {
            if (!AutoReorient || IsPiloting || SpaceCamera.Levelling) return;
            var normal = SystemPlaneNormal();
            if (Vector3.Angle(SpaceCamera.UpFrame * Vector3.up, normal) > 0.5f) SpaceCamera.Level(normal);
        }

        /// <summary>Space: the whole simulation (every system) eases to a stop, or back up to speed.</summary>
        public void TogglePause()
        {
            Clock.ToggleEasedPause();
            ShowToast(Clock.PauseHeld ? "PAUSING: every system easing to a stop  (Space to resume)" : "RESUMING: time picking back up to " + Clock.Warp.Label);
        }

        public void ToggleScale()
        {
            scaleFrom = Scale.Blend;
            ScaleGoal = ScaleGoal > 0.5 ? 0 : 1;
            scaleT = 0;
            ShowToast(ScaleGoal > 0.5
                ? "GAMEPLAY SCALE: sizes and distances compressed to be navigable"
                : "REALISTIC SCALE: true proportions, 1 unit = 1,000 km");
        }

        void AnimateScale(float dt)
        {
            if (scaleT >= 1f) return;
            scaleT = Mathf.Min(1f, scaleT + dt / ScaleSeconds);
            float e = scaleT * scaleT * scaleT * (scaleT * (scaleT * 6 - 15) + 10);
            Scale.Blend = scaleFrom + (ScaleGoal - scaleFrom) * e;
        }

        public void ShowToast(string message)
        {
            if (coopMuteToasts > 0) return; // US-T43: another player's action, replayed here
            Toast = message;
            ToastAge = 0;
        }

        public void ResetToNow()
        {
            // A reset can rebuild the galaxy out from under the piloted ship (the HUD's Now button lands here too).
            if (CoopAtControls()) { ShowToast("Release the helm or seat (P) before resetting the time"); return; } // US-T43: was AtControls (in co-op: anyone's)
            // Nor under the hull's own FTL drive: the crossing holds the old galaxy's systems (the Now button reaches here too).
            if (InFtlCruise) { ShowToast("Not under FTL: wait until the drive drops out before resetting the time"); return; }
            double now = CoopNow ?? Astro.JulianDate(DateTime.UtcNow); // US-T43: in co-op the asker's now, the same on every peer
            if (CoopSends("reset", Coop.Wire.D(now))) return; // US-T43: into the frames
            bool backwards = now < Clock.Jd;
            // Rebuilding the systems mid-trip would leave the trip pointing at the old ones: land first.
            if (backwards && Travel.Active) Travel.Finish();
            Clock.SetJd(now);
            Clock.SetWarp(SimClock.DefaultWarpIndex);
            if (backwards)
            {
                // Schedules only run forward; rebuild every system at the new date.
                int index = Galaxy.Systems.IndexOf(ActiveSystem);
                var cameraAt = SpaceCamera.ScenePosition;
                var cameraRotation = SpaceCamera.Rotation;
                Galaxy = Galaxy.CreateDefault(now);
                Materials.DestroyOwned(Interstellar.Root.transform);
                Destroy(Interstellar.Root);
                Interstellar = new InterstellarView(Galaxy, worldRoot);
                SubscribeWorlds();
                Activate(Galaxy.Systems[Math.Max(0, index)]);
                SpaceCamera.SetPose(cameraAt, cameraRotation);
                GalaxyRebuilt?.Invoke();
            }
            ShowToast("Time reset to now");
        }

        /// <summary>The galaxy's <see cref="Galaxy.ChartVersion"/> the view between the stars was last built from.</summary>
        int chartsShown;

        /// <summary>
        /// Rebuilds the view between the stars when exploring has charted new stars or built new systems (U.SPC-T413),
        /// never in the middle of a trip through it.
        /// </summary>
        void RefreshCharts()
        {
            if (Galaxy.ChartVersion == chartsShown || Travel.Active) return;
            chartsShown = Galaxy.ChartVersion;
            Materials.DestroyOwned(Interstellar.Root.transform);
            Destroy(Interstellar.Root);
            Interstellar = new InterstellarView(Galaxy, worldRoot);
        }

        // ---------------------------------------------------------------- ships

        void UpdateShips(float dt)
        {
            var ships = Fleet.Ships;
            double jd = Clock.Jd;
            float response = 1f - Mathf.Exp(-dt * 3f);

            for (int pass = 0; pass < 2; pass++)
            {
                for (int i = 0; i < ships.Count; i++)
                {
                    var ship = ships[i];
                    if (ship.IsEscort != (pass == 1)) continue;
                    shipLength[i] = Scale.ShipLength(ship.LengthKm);

                    if (ship == World.PilotedShip && ship.Phase == ShipPhase.Lane)
                    {
                        // Riding a lane at the helm: drawn on the lane as every rider is, nose along it.
                        var ride = Fleet.StateOf(ship, jd);
                        shipScene[i] = Scale.LaneScene(Ephemeris, bodyScene, Fleet.Lanes, ship, ride.Progress, out var along)
                            + SideScene(Fleet.LanePoint(ship, jd, ride.Progress), ship.LaneHoldSide) * Fleet.LaneRejoin(ship, jd);
                        var af = new Vector3((float)along.X, (float)along.Y, (float)along.Z);
                        var roof = pilot.Up.SwapYZ;
                        var ru = new Vector3((float)roof.X, (float)roof.Y, (float)roof.Z);
                        if (af.sqrMagnitude < 1e-12f) af = Vector3.forward;
                        if (ru.sqrMagnitude < 1e-12f || Mathf.Abs(Vector3.Dot(af.normalized, ru.normalized)) > 0.98f) ru = Mathf.Abs(Vector3.Dot(af.normalized, Vector3.up)) > 0.98f ? Vector3.right : Vector3.up;
                        shipRotation[i] = Quaternion.LookRotation(af, ru);
                        shipRotationInitialised[i] = true;
                        shipStates[i] = ride;
                        continue;
                    }
                    if (ship == World.PilotedShip)
                    {
                        shipScene[i] = bodyScene[pilotFrameBody] + Scale.LocalOffset(Catalog[pilotFrameBody], pilotOffsetKm);
                        // Nose and roof both: the hull rolls for real (U.SPC-T59).
                        var pilotFacing = pilotForward.SwapYZ;
                        var pilotRoof = pilot.Up.SwapYZ;
                        var pf = new Vector3((float)pilotFacing.X, (float)pilotFacing.Y, (float)pilotFacing.Z);
                        var pup = new Vector3((float)pilotRoof.X, (float)pilotRoof.Y, (float)pilotRoof.Z);
                        if (pf.sqrMagnitude < 1e-12f) pf = Vector3.forward;
                        if (pup.sqrMagnitude < 1e-12f) pup = Mathf.Abs(Vector3.Dot(pf.normalized, Vector3.up)) > 0.98f ? Vector3.right : Vector3.up;
                        shipRotation[i] = Quaternion.LookRotation(pf, pup);
                        shipRotationInitialised[i] = true;
                        // Its own state, not whatever the schedule last drew (an entry glow, a drive flame).
                        shipStates[i] = new ShipState(Ephemeris.Helio(pilotFrameBody) + pilotOffsetKm, pilotForward, pilotVelKmS.Length, false, 0);
                        continue;
                    }
                    // Running the player down (U.SPC-T273): where the chase has it, nose on its way, drive lit.
                    var chase = World.Pursuit != null && World.Pursuit.Active ? World.Pursuit.Of(ship) : null;
                    if (chase != null)
                    {
                        int frame = World.Pursuit.Body;
                        var off = Pursuit.OffsetOf(chase, ship);
                        var heading = chase.Vel.LengthSquared > 1e-12 ? chase.Vel.Normalized : (pilotOffsetKm - off).Normalized;
                        shipScene[i] = bodyScene[frame] + Scale.LocalOffset(Catalog[frame], off);
                        shipRotation[i] = ToRotation(heading, DVec3.UnitZ);
                        shipRotationInitialised[i] = true;
                        shipStates[i] = new ShipState(Ephemeris.Helio(frame) + off, heading, chase.Vel.Length, true, 0);
                        continue;
                    }

                    var st = Fleet.StateOf(ship, jd);
                    shipStates[i] = st;

                    if (ship.IsEscort)
                    {
                        int leader = ship.Leader.Id;
                        // Human pilots: each escort wanders slowly about its slot instead of sitting welded to it.
                        float t = Time.unscaledTime * 0.25f + ship.Id * 1.7f;
                        var slot = ship.FormationSlot + new DVec3(Mathf.Sin(t * 1.3f) * 0.12f, Mathf.Sin(t * 0.9f + 1.1f) * 0.08f, Mathf.Sin(t * 0.7f + 2.3f) * 0.15f);
                        var offset = shipRotation[leader] * new Vector3((float)slot.X, (float)slot.Y, (float)slot.Z) * (float)shipLength[leader];
                        shipScene[i] = shipScene[leader] + new DVec3(offset.x, offset.y, offset.z);
                        shipRotation[i] = shipRotation[leader];
                        shipRotationInitialised[i] = true;
                        continue;
                    }

                    DVec3 facing;
                    if (ship.Phase == ShipPhase.Parked)
                    {
                        var orbit = ship.Orbit;
                        shipScene[i] = bodyScene[orbit.Body] + Scale.LocalOffset(Catalog[orbit.Body], orbit.OffsetAt(jd));
                        facing = st.Facing.SwapYZ;
                    }
                    else if (ship.Phase == ShipPhase.Queued || ship.Planetside)
                    {
                        // At a docking ring: in the ringed world's own (turning) neighbourhood.
                        int body = ship.Ring.Body;
                        shipScene[i] = bodyScene[body] + Scale.LocalOffset(Catalog[body], st.HelioKm - Ephemeris.Helio(body));
                        facing = st.Facing.SwapYZ;
                    }
                    else if (ship.Phase == ShipPhase.Lane)
                    {
                        shipScene[i] = Scale.LaneScene(Ephemeris, bodyScene, Fleet.Lanes, ship, st.Progress, out var travel)
                            + SideScene(Fleet.LanePoint(ship, jd, st.Progress), ship.LaneHoldSide) * Fleet.LaneRejoin(ship, jd);
                        facing = travel;
                    }
                    else if (ship.Phase == ShipPhase.Lurk || ship.Phase == ShipPhase.Adrift)
                    {
                        shipScene[i] = LaneHoldScene(ship, out var along);
                        facing = along;
                    }
                    else
                    {
                        shipScene[i] = TransitScene(ship, st.Progress, out var travel);
                        facing = travel;
                    }

                    // Stepping aside to keep clear of another ship: the sidestep (true km) in scene units here.
                    var evasion = Fleet.EvasionOf(ship);
                    if (evasion.LengthSquared > 0) shipScene[i] += evasion.SwapYZ * KmToSceneNear(ship, st.HelioKm);

                    var f = new Vector3((float)facing.X, (float)facing.Y, (float)facing.Z);
                    if (f.sqrMagnitude < 1e-12f) f = Vector3.forward;
                    var up = Mathf.Abs(Vector3.Dot(f.normalized, Vector3.up)) > 0.98f ? Vector3.right : Vector3.up;
                    var goal = Quaternion.LookRotation(f, up);
                    shipRotation[i] = shipRotationInitialised[i] ? Quaternion.Slerp(shipRotation[i], goal, response) : goal;
                    shipRotationInitialised[i] = true;
                }
            }
            PlaceInBattles(dt);
        }

        /// <summary>Scene units per km around a ship (sideways), from the mapping of the world it is nearest, or the star's out in open space.</summary>
        double KmToSceneNear(Ship ship, DVec3 helio)
        {
            var c = ship.Controller;
            int b = c.Ring != null && (c.Phase == ShipPhase.Queued || c.Planetside) ? c.Ring.Body : c.Orbit?.Body ?? 0;
            double d = (helio - Ephemeris.Helio(b)).Length;
            var body = Catalog[b];
            if (d > ScaleModel.NeighbourhoodKm(body))
            {
                body = Catalog.Root;
                d = (helio - Ephemeris.Helio(0)).Length;
            }
            d = Math.Max(d, Math.Max(body.RadiusKm, 1));
            return Scale.LocalDistance(body, d) / d;
        }

        /// <summary>
        /// Ships fighting are drawn where the battle has them; ships entering or leaving a fight ease
        /// between that and their orbit over two seconds.
        /// </summary>
        void PlaceInBattles(float dt)
        {
            var ships = Fleet.Ships;
            if (lastBattleScene.Length != ships.Count)
            {
                Array.Resize(ref lastBattleScene, ships.Count);
                Array.Resize(ref lastDrawnValid, ships.Count);
            }
            for (int i = 0; i < ships.Count; i++)
            {
                var ship = ships[i];
                if (ship.Destroyed) continue;
                var battle = ship.Battle;
                int body = battle != null ? battle.Body : ship.Controller.Orbit.Body;
                if (battle != shipLastBattle[i])
                {
                    var current = DrawnScene(i);
                    shipBlendFrom[i] = current - bodyScene[body];
                    shipBlendT[i] = 0;
                    shipLastBattle[i] = battle;
                }
                if (battle != null)
                {
                    var u = battle.UnitOf(ship);
                    if (u != null)
                    {
                        shipScene[i] = BattleScene(battle, u.Pos);
                        var f = u.Forward.SwapYZ;
                        var fv = new Vector3((float)f.X, (float)f.Y, (float)f.Z);
                        // The flown hull keeps the roll its pilot gave it (set with its pose in UpdateShips); the
                        // battle's units roll as their captains roll them, to keep weak patches out of the fire (U.SPC-T502).
                        if (ship == World.PilotedShip) shipRotation[i] = ToRotation(pilotForward, pilot.Up);
                        else if (fv.sqrMagnitude > 1e-12f)
                        {
                            u.Basis(out _, out var unitUp);
                            shipRotation[i] = ToRotation(u.Forward, unitUp);
                        }
                    }
                }
                if (shipBlendT[i] < 1)
                {
                    shipBlendT[i] = Mathf.Min(1, shipBlendT[i] + dt / 2f);
                    float e = shipBlendT[i] * shipBlendT[i] * (3 - 2 * shipBlendT[i]);
                    shipScene[i] = DVec3.Lerp(bodyScene[body] + shipBlendFrom[i], shipScene[i], e);
                }
                lastBattleScene[i] = shipScene[i];
                lastDrawnValid[i] = true;
            }
        }

        DVec3[] lastBattleScene = new DVec3[0];

        bool[] lastDrawnValid = new bool[0];

        // Where the ship was last drawn (after any blend), so a blend interrupted or restarted mid-way starts from what
        // the player saw rather than the raw schedule or battle position (U.SPC-T568).
        DVec3 DrawnScene(int i) => lastDrawnValid.Length > i && lastDrawnValid[i] ? lastBattleScene[i] : shipScene[i];

        /// <summary>Scene position of a transiting ship at a fraction of its path (see <see cref="ScaleModel.TransitScene"/>).</summary>
        public DVec3 TransitScene(Ship ship, double progress, out DVec3 travelDirection) =>
            Scale.TransitScene(Ephemeris, bodyScene, ship, progress, out travelDirection);

        // A ship added since this frame's sync (spawned mid-frame by a tour coroutine, say) gets its views first.
        void EnsureShipSynced(Ship ship)
        {
            if (ship.Id >= shipScene.Length && SyncShipViews()) UpdateShips(0f);
        }

        public DVec3 ShipScene(Ship ship) { EnsureShipSynced(ship); return shipScene[ship.Id]; }
        public double ShipDisplayLength(Ship ship) { EnsureShipSynced(ship); return shipLength[ship.Id] * ShipLod.ExtentFactor(ship); }
        public ShipState ShipStateOf(Ship ship) { EnsureShipSynced(ship); return shipStates[ship.Id]; }

        // ---------------------------------------------------------------- ICameraWorld

        public DVec3 BodyScene(int body) => bodyScene[body];
        public double BodyDisplayRadius(int body) => bodyRadius[body];

        public DVec3 TargetScene(Target target)
        {
            // A ship spawned this same frame (the scripted tour's skirmishes, the Spawn & Pilot grid,
            // a jump-gate arrival) may not have been sized into shipScene yet -- the per-ship arrays
            // only grow once a frame, in Update, but this is also reached from a coroutine step or an
            // OnGUI click that can land before or after that. A resize alone leaves the newcomer's
            // entry zeroed, so also force one fresh UpdateShips to actually place it before reading it.
            if (target.IsShip && SyncShipViews()) UpdateShips(0f);
            if (target.IsBody) return bodyScene[target.Body];
            if (target.IsShip) return shipScene[target.Ship.Id];
            if (target.IsStructure && Structures.Scenes.TryGetValue(target.Structure, out var s)) return s;
            if (target.IsRing && Structures.RingScenes.TryGetValue(target.Ring, out var r)) return r;
            if (target.IsUnit) return BattleScene(target.Battle, target.Unit.Pos);
            return DVec3.Zero;
        }

        public double TargetRadius(Target target)
        {
            if (target.IsBody) return bodyRadius[target.Body];
            if (target.IsShip) return shipLength[target.Ship.Id] * 0.5;
            if (target.IsStructure && Structures.Sizes.TryGetValue(target.Structure, out var s)) return s * 0.5;
            if (target.IsRing && Structures.Sizes.TryGetValue(target.Ring, out var r)) return r * 0.5;
            if (target.IsUnit) return Scale.ShipLength(target.Unit.LengthKm) * 0.5;
            return 1;
        }

        public Quaternion TargetRotation(Target target)
        {
            if (target.IsShip) return shipRotation[target.Ship.Id];
            if (target.IsUnit)
            {
                var u = target.Unit;
                u.Basis(out _, out var up);
                return Quaternion.LookRotation(ToVector(u.Forward.SwapYZ), ToVector(up.SwapYZ));
            }
            return Quaternion.identity;
        }

        static Vector3 ToVector(DVec3 v) => new Vector3((float)v.X, (float)v.Y, (float)v.Z);

        public double NearestSurface(DVec3 p)
        {
            double best = double.MaxValue;
            for (int i = 0; i < bodyScene.Length; i++)
                best = Math.Min(best, (p - bodyScene[i]).Length - bodyRadius[i]);
            var ships = Fleet.Ships;
            for (int i = 0; i < ships.Count; i++)
            {
                if (ships[i].Destroyed || ships[i].Controller.Phase == ShipPhase.Landed) continue;
                best = Math.Min(best, (p - shipScene[i]).Length - shipLength[i] * 0.5);
            }
            foreach (var kv in Structures.Scenes)
                best = Math.Min(best, (p - kv.Value).Length - Structures.Sizes[kv.Key] * 0.5);
            foreach (var kv in Structures.RingScenes)
                best = Math.Min(best, (p - kv.Value).Length - Structures.Sizes[kv.Key] * 0.5);
            return Math.Max(best, 1e-7);
        }

        // ---------------------------------------------------------------- views

        void UpdateClipPlanes(float dt)
        {
            // Going through a gate, the ring passes close by, nearer than anything else.
            double nearest = Math.Min(NearestSurface(SpaceCamera.ScenePosition), Travel.GateClearance);
            UnityCamera.nearClipPlane = (float)Math.Max(1e-6, Math.Min(nearest * 0.25, 20.0));
            UnityCamera.farClipPlane = 5e8f;
            UnityCamera.transform.SetPositionAndRotation(Vector3.zero, SpaceCamera.Rotation);

            float targetFov = RidingLane ? BaseFov + (LaneFov - BaseFov) * LaneRideSpeed
                : IsPiloting && pilotCruise.Active ? CruiseFov : IsPiloting && pilotBoosting ? BoostFov : BaseFov;
            cameraFov = Mathf.Lerp(cameraFov, targetFov, 1f - Mathf.Exp(-dt * FovResponseHz));
            // A ring gate kicks the view wide for a moment and flashes as the hull goes through (U.SPC-T41).
            fovKick *= Mathf.Exp(-dt * 3.5f);
            GateFlash = Mathf.Max(0, GateFlash - dt / GateFlashS);
            LaneExitFade = Mathf.Max(0, LaneExitFade - dt / LaneExitFadeS);
            UnityCamera.fieldOfView = Mathf.Clamp(cameraFov + fovKick + Travel.FovKick - HelmFoldDeg * HelmGateFold, 10f, 150f);
        }

        public Vector3 Relative(DVec3 scene)
        {
            var d = scene - SpaceCamera.ScenePosition;
            return new Vector3((float)d.X, (float)d.Y, (float)d.Z);
        }

        /// <summary>The camera-relative offset in doubles (what Unreal's LWC placement takes, see <see cref="Bridge.UnrealSpace"/>).</summary>
        public DVec3 RelativeD(DVec3 scene) => scene - SpaceCamera.ScenePosition;

        /// <summary>
        /// Lights the scene with the two stars that shine brightest where the camera is (luminosity
        /// over distance squared): the Sun alone in Sol; A with B beside it in Alpha Centauri, or
        /// Proxima out by Proxima. Light colours follow each star's temperature, the Sun's being white.
        /// </summary>
        void UpdateLights()
        {
            int first = -1, second = -1;
            double best = 0, next = 0;
            for (int i = 0; i < Catalog.Count; i++)
            {
                var b = Catalog[i];
                if (!b.IsStar) continue;
                double d = Math.Max((SpaceCamera.ScenePosition - bodyScene[i]).Length, bodyRadius[i]);
                double flux = b.LuminositySolar / (d * d);
                if (flux > best)
                {
                    second = first;
                    next = best;
                    first = i;
                    best = flux;
                }
                else if (flux > next)
                {
                    second = i;
                    next = flux;
                }
            }
            if (first < 0) first = 0;
            var light = new StarLighting { SunPosition = Relative(bodyScene[first]), SunColor = LightColor(Catalog[first], 1) };
            if (second >= 0)
            {
                // The square root keeps a companion's light a visible fill; the true ratio would hide it.
                float strength = Mathf.Clamp01((float)Math.Sqrt(next / best)) * 0.9f;
                light.Sun2Position = Relative(bodyScene[second]);
                light.Sun2Color = LightColor(Catalog[second], strength);
            }
            else
            {
                light.Sun2Position = Relative(bodyScene[first]);
                light.Sun2Color = Vector4.zero;
            }
            RenderBackend.Active.Lighting = light;
            UpdateAtmosphere();
        }

        bool atmosphereSet;

        /// <summary>
        /// Tells a pipeline that draws a physically based sky (HDRP) which planet's air the camera is in (U.SPC-T430),
        /// scoped to docking-ring atmospheric entry only (U.SPC-T452): the sky draws while the ship the camera rides
        /// (flown, or followed) is coming down through a ring, landed at its city or climbing back, for that ringed world
        /// alone, and is cleared everywhere else, where the shaders' own atmosphere rims draw every planet's air. A
        /// pipeline without such a sky is not asked.
        /// </summary>
        void UpdateAtmosphere()
        {
            var backend = RenderBackend.Active;
            if (!backend.Profile.Offers(PipelineFeatures.PhysicalSky)) return;
            var ridden = IsPiloting ? Piloted.Ship : SpaceCamera.FocusTarget.IsShip ? SpaceCamera.FocusTarget.Ship : null;
            int body = PlanetAtmospheres.EntryBody(ridden);
            if (body < 0 || body >= Catalog.Count || !Catalog[body].IsVisible)
            {
                ClearAtmosphere();
                return;
            }
            var b = Catalog[body];
            double distance = (SpaceCamera.ScenePosition - bodyScene[body]).Length;
            if (double.IsPositiveInfinity(PlanetAtmospheres.Closeness(distance, bodyRadius[body], b.AtmosphereStrength, b.IsStar)))
            {
                // An airless world, or the camera pulled far back from the descent: the rims carry it.
                ClearAtmosphere();
                return;
            }
            var shell = PlanetAtmospheres.Describe(body, bodyRadius[body], distance, b.AtmosphereColor.R, b.AtmosphereColor.G,
                b.AtmosphereColor.B, b.AtmosphereStrength);
            backend.SetAtmosphere(UnityCamera, shell, Relative(bodyScene[body]));
            atmosphereSet = true;
        }

        void ClearAtmosphere()
        {
            if (!atmosphereSet) return;
            atmosphereSet = false;
            RenderBackend.Active.SetAtmosphere(UnityCamera, AtmosphereShell.None, Vector3.zero);
        }

        static readonly Rgb SunLight = AlphaCentauri.StarColor(5772);

        /// <summary>A star's light relative to sunlight (so the Sun lights white), scaled to its brightest channel.</summary>
        public static Vector4 LightColor(BodyDefinition star, float strength)
        {
            var c = AlphaCentauri.StarColor(star.TeffK);
            var v = new Vector3(c.R / Mathf.Max(SunLight.R, 1e-3f), c.G / Mathf.Max(SunLight.G, 1e-3f), c.B / Mathf.Max(SunLight.B, 1e-3f));
            float max = Mathf.Max(v.x, Mathf.Max(v.y, v.z));
            v /= Mathf.Max(max, 1e-3f);
            return new Vector4(v.x * strength, v.y * strength, v.z * strength, strength);
        }

        void UpdateViews(float dt)
        {
            if (!SystemVisible)
            {
                ClearAtmosphere();
                return;
            }
            UpdateLights();
            var forward = SpaceCamera.Rotation * Vector3.forward;
            double pixelsPerUnit = PixelsPerUnitAtUnitDistance();
            double warp = Clock.Paused ? 0 : Clock.Warp.SimSecondsPerSecond * Clock.Speed;

            for (int i = 0; i < Catalog.Count; i++)
            {
                // Body-fixed frame: IAU prime meridians, or 0° toward the parent for synchronous moons.
                var rel = Ephemeris.Relative(i);
                var orbitNormal = DVec3.Cross(rel, Ephemeris.RelativeVelocityKmS(i));
                BodyOrientation.Frame(Catalog[i], Clock.Jd, -rel, orbitNormal, out var north, out var meridian);
                var target = Target.OfBody(i);
                bodyViews[i].Apply(Relative(bodyScene[i]), (float)bodyRadius[i], north, meridian,
                    target == Selected || target == Hovered, forward);
                bodyViews[i].UpdateWeather(Clock.Jd, warp);
            }
            if (IsSol) AsteroidBelt?.Render(Clock.Jd, Scale, Relative(bodyScene[0]));

            int glowIndex = 0;
            for (int i = 0; i < shipViews.Length; i++)
            {
                var ship = Fleet.Ships[i];
                // Not streamed in (U.SPC-T414): out of sight, or drawn as its icon until its model is built.
                if (shipViews[i] == null) continue;
                if (ship.Destroyed)
                {
                    // A jump gate retires the origin hull the same way a kill does (so its id stays put
                    // for every ship after it -- see GateTransit) but it flew off, not blew up.
                    if (ship.GateDeparted) { shipViews[i].SetVisible(false); continue; }
                    DrawWreck(i, ship);
                    continue;
                }
                if (viewFaction.Length != shipViews.Length) Array.Resize(ref viewFaction, shipViews.Length);
                if (viewFaction[i] == null) viewFaction[i] = ship.Faction;
                if (viewFaction[i] != ship.Faction || shipViews[i].BuiltFor != ship.Combat)
                {
                    // Taken as a prize: repaint in the captor's livery (freeing the old view's own meshes and materials).
                    // Refitted at a yard: rebuild its hardpoints from the new loadout.
                    Materials.DestroyOwned(shipViews[i].Root.transform);
                    Destroy(shipViews[i].Root);
                    shipViews[i] = new ShipView(ship, systemRoot);
                    viewFaction[i] = ship.Faction;
                }
                var rel = Relative(shipScene[i]);
                var controller = ship.Controller;
                if (controller.Phase == ShipPhase.Landed)
                {
                    shipViews[i].SetVisible(false);
                    continue;
                }
                float heat = shipStates[i].Heat;
                if (heat > 0.01f) PlaceEntryGlow(glowIndex++, i, rel, heat, pixelsPerUnit);
                // An ore tug with its load on is drawn by the length of both (U.SPC-T498).
                double px = ShipLod.ApparentPx(shipLength[i] * ShipLod.ExtentFactor(ship), rel.magnitude, pixelsPerUnit);
                // Too small to read: hide the model, the HUD draws its named faction icon instead (U.SPC-T376), folded
                // into one mark with its neighbours where they crowd; too small for its hardpoints, the hull alone (U.SPC-T415).
                var tier = RenderLod.Tier(RenderLod.Ship, px);
                bool visible = RenderLod.DrawsModel(tier);
                shipViews[i].SetVisible(visible);
                if (!visible) continue;
                shipViews[i].SetDetail(tier == LodTier.Full && !ship.Derelict);
                if (ship.Derelict)
                {
                    shipViews[i].Derelict(rel, shipRotation[i], (float)shipLength[i], Time.unscaledTime + ship.Id);
                    continue;
                }
                var target = Target.OfShip(ship);
                var unit = ship.Battle?.UnitOf(ship);
                bool thrusting = shipStates[i].Burning || (unit != null && unit.Vel.LengthSquared > 1e-4);
                shipViews[i].Apply(rel, shipRotation[i], (float)shipLength[i], shipStates[i].Burning,
                    target == Selected || target == Hovered);
                shipViews[i].ApplyCombat(ship.Combat, ship.Battle != null, thrusting, Time.unscaledTime, dt);
            }
            for (int g = glowIndex; g < entryGlows.Count; g++) entryGlows[g].t.gameObject.SetActive(false);

            UpdatePlatforms();
            laneView.Update(ShowLanes);
            gateView.Update(true, nearGate);
            fighterViews.Update();
            embeddedPods.Update();
            battleFx.Update();
            debrisFields.Update();
            podBeacons.Update();
            Structures.Update(true);
            swarm.Update(ShowSatellites);
            UpdateOrbitLines(dt);
            discView.Update(ShowOrbits);
            UpdatePathLine();
        }

        static Mesh glowQuad;

        /// <summary>The plasma sheath round a ship coming down through an atmosphere: a hot glow at its nose.</summary>
        void PlaceEntryGlow(int index, int ship, Vector3 rel, float heat, double pixelsPerUnit)
        {
            if (index >= entryGlows.Count)
            {
                glowQuad ??= MeshFactory.Quad();
                var go = new GameObject("Entry glow");
                go.transform.SetParent(systemRoot, false);
                go.AddComponent<MeshFilter>().sharedMesh = glowQuad;
                var mr = go.AddComponent<MeshRenderer>();
                var m = Materials.Create(Materials.Glow, "Entry glow");
                m.SetFloat("_Falloff", 2.0f);
                mr.sharedMaterial = m;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                entryGlows.Add((go.transform, m));
            }
            var (t, material) = entryGlows[index];
            t.gameObject.SetActive(true);
            float length = (float)shipLength[ship];
            // Ships come down tail-first: the sheath forms on the face meeting the air, below the drive.
            var nose = shipRotation[ship] * Vector3.forward;
            var p = rel - nose * length * 0.55f;
            float dist = Mathf.Max(p.magnitude, 1e-6f);
            // At least a few pixels, so a descent shows as a spark from orbit.
            float size = Mathf.Max(length * (0.45f + 0.75f * heat), dist * 5f / (float)pixelsPerUnit * heat);
            t.localPosition = p;
            t.localRotation = Quaternion.LookRotation(p / dist, Vector3.up);
            t.localScale = Vector3.one * size;
            material.SetColor("_Color", Color.Lerp(new Color(1f, 0.55f, 0.25f), new Color(1f, 0.4f, 0.75f), heat));
            material.SetFloat("_Intensity", 0.35f + 0.9f * heat);
        }

        /// <summary>
        /// A ship lost in battle breaks up and burns out where it died, then lingers as a slowly
        /// tumbling wreck (<see cref="World.War"/>'s persistent <see cref="Wreck"/>, not just the
        /// transient burn-out) until its own configurable lifetime runs out and the Core clears it away.
        /// </summary>
        void DrawWreck(int i, Ship ship)
        {
            var wreck = World.War.Wrecks.Of(ship);
            if (wreck == null)
            {
                shipViews[i].SetVisible(false);
                return;
            }
            if (wreckStart[i] < 0) wreckStart[i] = Time.unscaledTime;
            var rel = Relative(WreckScene(wreck));
            float length = (float)Scale.ShipLength(ship.LengthKm);
            // A wreck has no icon: below a pixel it is not drawn at all (U.SPC-T415).
            if (RenderLod.Tier(RenderLod.Wreck, RenderLod.ApparentPx(length, rel.magnitude, PixelsPerUnitAtUnitDistance())) == LodTier.Culled)
            {
                shipViews[i].SetVisible(false);
                return;
            }
            shipViews[i].SetDetail(true);
            bool stillBurning = wreckStart[i] != float.MaxValue
                && shipViews[i].Wreck(rel, shipRotation[i], length, Time.unscaledTime - wreckStart[i], Time.unscaledTime);
            if (!stillBurning)
            {
                wreckStart[i] = float.MaxValue;
                // Still in two halves, its guns gone: a burnt-out wreck, not a whole hull adrift.
                shipViews[i].BurntOut(rel, shipRotation[i], length, Time.unscaledTime + ship.Id);
            }
        }

        /// <summary>Defence platforms are drawn while they fight.</summary>
        void UpdatePlatforms()
        {
            double ppu = PixelsPerUnitAtUnitDistance();
            var live = new HashSet<ShipCombat>();
            foreach (var b in World.War.Battles)
            {
                foreach (var u in b.Units)
                {
                    if (u.Kind != UnitKind.Platform || !u.Present) continue;
                    live.Add(u.Combat);
                    if (!platformViews.TryGetValue(u.Combat, out var go))
                    {
                        go = new GameObject(u.Name);
                        go.transform.SetParent(systemRoot, false);
                        go.AddComponent<MeshFilter>().sharedMesh = ShipMeshFactory.GetPlatform(u.Faction);
                        var mr = go.AddComponent<MeshRenderer>();
                        mr.sharedMaterial = ShipModels.ProceduralMaterial;
                        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                        platformViews[u.Combat] = go;
                    }
                    var rel = Relative(BattleScene(b, u.Pos));
                    float length = (float)Scale.ShipLength(CombatSpec.PlatformLengthKm);
                    // Below a pixel a platform is not drawn at all (U.SPC-T415); its battle still shows.
                    bool shown = RenderLod.DrawsModel(RenderLod.Tier(RenderLod.Platform, RenderLod.ApparentPx(length, rel.magnitude, ppu)));
                    if (go.activeSelf != shown) go.SetActive(shown);
                    if (!shown) continue;
                    go.transform.localPosition = rel;
                    go.transform.localRotation = Quaternion.Euler(20, (float)(b.Seconds * 0.5 % 360), 0);
                    go.transform.localScale = Vector3.one * length;
                }
            }
            var gone = new List<ShipCombat>();
            foreach (var kv in platformViews)
                if (!live.Contains(kv.Key))
                {
                    Destroy(kv.Value);
                    gone.Add(kv.Key);
                }
            foreach (var k in gone) platformViews.Remove(k);
        }

        void UpdateOrbitLines(float dt)
        {
            orbitRefreshTimer -= dt;
            bool refresh = orbitRefreshTimer <= 0 || ScaleAnimating;
            if (refresh) orbitRefreshTimer = 0.2f;

            for (int i = 0; i < Catalog.Count; i++)
            {
                var line = orbitLines[i];
                if (line == null) continue;
                var body = Catalog[i];
                var parent = Catalog[body.ParentIndex];
                line.Visible = ShowOrbits;
                if (!ShowOrbits) continue;

                double orbitScene = Scale.LocalDistance(parent, body.Elements.AKm);
                double cameraDistance = (SpaceCamera.ScenePosition - bodyScene[parent.Index]).Length;
                float alpha;
                Color color;
                bool selected = Selected == Target.OfBody(i);
                if (body.Kind == BodyKind.Moon)
                {
                    alpha = Mathf.Clamp01(1.6f - (float)(cameraDistance / (orbitScene * 14.0)));
                    color = new Color(0.55f, 0.65f, 0.78f, 0.45f);
                }
                else
                {
                    alpha = 1f;
                    color = body.Kind == BodyKind.DwarfPlanet || body.Kind == BodyKind.Asteroid ? new Color(0.7f, 0.62f, 0.5f, 0.32f)
                        : body.Kind == BodyKind.Station ? new Color(0.9f, 0.75f, 0.45f, 0.3f)
                        : body.IsStar ? new Color(1f, 0.85f, 0.55f, 0.35f)
                        : body.Status != BodyStatus.Confirmed ? new Color(0.6f, 0.6f, 0.7f, 0.25f)
                        : new Color(0.45f, 0.62f, 0.9f, 0.42f);
                }
                if (selected) color = new Color(0.55f, 0.9f, 1f, 0.85f);
                line.Visible = alpha > 0.01f;
                if (!line.Visible) continue;

                if (refresh || line.Capacity == 0)
                {
                    Ephemeris.SampleOrbitTrail(i, Clock.Jd, orbitSamples);
                    for (int k = 0; k < orbitSamples.Length; k++)
                    {
                        var o = Scale.LocalOffset(parent, orbitSamples[k]);
                        orbitPoints[k] = new Vector3((float)o.X, (float)o.Y, (float)o.Z);
                    }
                    line.SetPoints(orbitPoints, orbitPoints.Length, color, 1f, 0.12f);
                }
                line.SetTint(new Color(1, 1, 1, alpha));
                line.Root.transform.localPosition = Relative(bodyScene[parent.Index]);
                line.Root.transform.localRotation = Quaternion.identity;
            }
        }

        void UpdatePathLine()
        {
            var t = Selected.IsShip ? Selected : Hovered.IsShip ? Hovered : Target.None;
            var ship = t.IsShip ? t.Ship.Controller : null;
            if (ship == null || ship.Destroyed || ship.Derelict || ship.Battle != null || (ship.Phase != ShipPhase.Transit && ship.Phase != ShipPhase.Lane))
            {
                pathLine.Visible = false;
                return;
            }
            double progress = shipStates[ship.Id].Progress;
            for (int k = 0; k < pathPoints.Length; k++)
            {
                double u = progress + (1 - progress) * k / (pathPoints.Length - 1);
                pathPoints[k] = Relative(ship.Phase == ShipPhase.Lane ? Scale.LaneScene(Ephemeris, bodyScene, Fleet.Lanes, ship, u, out _) : TransitScene(ship, u, out _));
            }
            // A company hull's path is drawn in its company's livery.
            var c = ship.Company?.Livery ?? ship.Faction.Color;
            pathLine.SetPoints(pathPoints, pathPoints.Length, new Color(c.R, c.G, c.B, 0.9f), 1f, 0.35f);
            pathLine.SetTint(Color.white);
            pathLine.Root.transform.localPosition = Vector3.zero;
            pathLine.Visible = true;
        }

        public double PixelsPerUnitAtUnitDistance() =>
            Screen.height / (2.0 * Math.Tan(UnityCamera.fieldOfView * 0.5 * Mathf.Deg2Rad));

        /// <summary>Where another star system shines in this one's sky (a Unity-axes unit vector).</summary>
        public Vector3 SkyDirection(StarSystem other)
        {
            var d = (other.PositionLy - ActiveSystem.PositionLy).Normalized.SwapYZ;
            return new Vector3((float)d.X, (float)d.Y, (float)d.Z);
        }

        // ---------------------------------------------------------------- picking

        /// <summary>Projects a scene point to GUI coordinates (origin top-left). False if behind the camera.</summary>
        public bool ToGui(DVec3 scene, out Vector2 gui, out float depth)
        {
            var rel = Relative(scene);
            var sp = UnityCamera.WorldToScreenPoint(rel);
            depth = sp.z;
            gui = new Vector2(sp.x, Screen.height - sp.y);
            return sp.z > 0;
        }

        /// <summary>Projects a camera-relative point (or direction far away) to GUI coordinates.</summary>
        public bool RelativeToGui(Vector3 rel, out Vector2 gui)
        {
            var sp = UnityCamera.WorldToScreenPoint(rel);
            gui = new Vector2(sp.x, Screen.height - sp.y);
            return sp.z > 0;
        }

        /// <summary>True if a sphere of radius <paramref name="ignoreRadius"/> at scene point p is hidden behind any body.</summary>
        public bool Occluded(DVec3 p, int ignoreBody = -1)
        {
            var dir = p - SpaceCamera.ScenePosition;
            double len = dir.Length;
            if (len <= 0) return false;
            dir /= len;
            for (int i = 0; i < bodyScene.Length; i++)
            {
                if (i == ignoreBody || !Catalog[i].IsVisible) continue;
                var oc = SpaceCamera.ScenePosition - bodyScene[i];
                double b = DVec3.Dot(oc, dir);
                double c = oc.LengthSquared - bodyRadius[i] * bodyRadius[i];
                if (c < 0) continue;
                double disc = b * b - c;
                if (disc <= 0) continue;
                double hit = -b - Math.Sqrt(disc);
                if (hit > 0 && hit < len - bodyRadius[i] * 1e-3) return true;
            }
            return false;
        }

        void UpdateHover()
        {
            if (Input.LookHeld || IsPointerOverUi() || Travel.Active)
            {
                Hovered = Target.None;
                return;
            }
            var mouse = new Vector2(Input.MousePosition.x, Screen.height - Input.MousePosition.y);
            Hovered = Pick(mouse);
        }

        void HandleSelectionClicks()
        {
            // The fire trigger shares the left button; a locked, centred cursor while piloting would
            // otherwise register every release as an in-place click and reselect something underfoot.
            if (!Input.SelectClicked || Travel.Active || AtControls) return;
            Select(Hovered);
            if (Input.SelectDoubleClicked && !Hovered.IsNone) SpaceCamera.FocusOn(Hovered);
        }

        public Target Pick(Vector2 gui)
        {
            double ppu = PixelsPerUnitAtUnitDistance();
            var best = Target.None;
            double bestScore = double.MaxValue;

            for (int i = 0; i < Catalog.Count; i++)
            {
                if (!Catalog[i].IsVisible) continue;
                if (!ToGui(bodyScene[i], out var p, out var depth)) continue;
                double pxRadius = bodyRadius[i] / depth * ppu;
                double pickRadius = Math.Max(pxRadius, Icons.PixelSize(Catalog[i]) * 0.5 + 5);
                double d = (p - gui).magnitude;
                if (d > pickRadius) continue;
                if (pxRadius < 3 && Occluded(bodyScene[i], i)) continue;
                double score = d / pickRadius + 0.5;
                if (score < bestScore)
                {
                    bestScore = score;
                    best = Target.OfBody(i);
                }
            }

            var ships = Fleet.Ships;
            // At the helm only what the scanners hold can be picked (U.SPC-T66), unless it is close enough to see.
            var picture = IsPiloting && Piloted.Ship != null ? World.Sensors?.Player : null;
            for (int i = 0; i < ships.Count; i++)
            {
                if (ships[i].Destroyed || ships[i].Controller.Phase == ShipPhase.Landed) continue;
                if (!ToGui(shipScene[i], out var p, out var depth)) continue;
                double px = ShipLod.ApparentPx(shipLength[i] * ShipLod.ExtentFactor(ships[i]), depth, ppu);
                if (picture != null && ships[i] != Piloted.Ship && !ShipLod.DrawsModel(px) && picture.Of(ships[i]) == null) continue;
                if (FoldedShips.Contains(ships[i]) && !ShipLod.DrawsModel(px)) continue;
                double pickRadius = Math.Max(px * 0.5, Icons.PixelSize(ships[i].Spec.Class) * 0.5 + 4);
                double d = (p - gui).magnitude;
                if (d > pickRadius) continue;
                if (Occluded(shipScene[i])) continue;
                // Ships win ties against the planet they orbit.
                double score = d / pickRadius;
                if (score < bestScore)
                {
                    bestScore = score;
                    best = Target.OfShip(ships[i]);
                }
            }

            // A ship's name beside its icon is as good as the icon itself.
            foreach (var (rect, ship) in ShipNameRects)
            {
                if (ship.Destroyed || !rect.Contains(gui)) continue;
                const double nameScore = 0.15;
                if (nameScore < bestScore)
                {
                    bestScore = nameScore;
                    best = Target.OfShip(ship);
                }
            }

            // Structures and rings, only once they are drawn large enough to be things.
            void Consider(object key, DVec3 scene, Target t)
            {
                if (!ToGui(scene, out var p, out var depth)) return;
                double px = Structures.Sizes[key] / depth * ppu;
                if (px < 4) return;
                double pickRadius = Math.Max(px * 0.5, 8);
                double d = (p - gui).magnitude;
                if (d > pickRadius || Occluded(scene)) return;
                double score = d / pickRadius + 0.2;
                if (score < bestScore)
                {
                    bestScore = score;
                    best = t;
                }
            }
            foreach (var kv in Structures.Scenes) Consider(kv.Key, kv.Value, Target.OfStructure(kv.Key));
            foreach (var kv in Structures.RingScenes) Consider(kv.Key, kv.Value, Target.OfRing(kv.Key));
            return best;
        }
    }
}
