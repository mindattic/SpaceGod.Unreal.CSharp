using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using Unity.SpaceGod.Cameras;
using Unity.SpaceGod.Core;
using Unity.SpaceGod.Rendering;
using UnityEngine;

namespace Unity.SpaceGod.UI
{
    /// <summary>
    /// Immediate-mode HUD. World overlay: billboard icons for anything below a few pixels, labels,
    /// selection brackets and an off-screen pointer. Panels: time and scale bar, navigator tree,
    /// selection details, camera status and the controls card.
    /// </summary>
    public sealed partial class Hud
    {
        readonly UnitySpaceApp app;
        bool stylesReady;
        GUIStyle panel, title, text, small, minorLabel, label, labelShadow, button, buttonActive, row, rowSelected, toast;
        Texture2D panelTexture, rowHoverTexture, rowSelectedTexture, buttonTexture, buttonActiveTexture;

        Vector2 navigatorScroll;
        bool navigatorOpen = false;
        float lastRowClick = -10f;
        Target lastRowTarget = Target.None;

        /// <summary>The crate currently picked up by a source click in the cargo panel, awaiting a
        /// destination click; null means nothing is picked up.</summary>
        CargoItem cargoSelected;
        CargoHold cargoSelectedHold; // the hold the picked-up crate is in: a crate never carries over to another ship's hold

        /// <summary>Crates per manual Buy/Sell click in the market board, shared by every good's row.</summary>
        int tradeCrates = 1;

        /// <summary>Dev/sandbox spawn panel: which faction the next spawned ship joins.</summary>
        bool spawnHostile;

        /// <summary>Collapse state for panels that can sit over the 3D view and get in the way --
        /// same "[-]/[+] TITLE" bar <see cref="DrawNavigator"/> already uses, so Market/Cargo/Help can
        /// be shrunk to just their title bar without closing them outright.</summary>
        bool marketOpen = true, cargoOpen = true;

        /// <summary>The outfitting panel's chosen slot (-1: none) and how far its slot list is scrolled.</summary>
        int outfitSlot = -1;
        Vector2 outfitScroll;

        /// <summary>How far the selection card's lines are scrolled when they are too many for the screen, and for which target.</summary>
        Vector2 selectionScroll;
        Target selectionScrollFor = Target.None;

        internal static readonly Color Accent = new Color(0.45f, 0.85f, 1f);
        internal static readonly Color Dim = new Color(0.62f, 0.7f, 0.78f);

        /// <summary>An unidentified contact: no faction colour until the scanners know whose it is.</summary>
        static readonly Color ContactGrey = new Color(0.78f, 0.8f, 0.82f);

        /// <summary>The piloted ship's own sensor picture, or null when only watching (then everything is shown).</summary>
        ContactBook Picture => app.IsPiloting && app.Piloted.Ship != null ? app.World.Sensors?.Player : null;

        /// <summary>Where each contact was last drawn while the scanners held it, so a lost one stays marked there.</summary>
        readonly Dictionary<Ship, DVec3> lastSeenScene = new Dictionary<Ship, DVec3>();
        Fleet lastSeenFleet;

        static readonly Color LostEnemy = new Color(1f, 0.35f, 0.3f, 0.8f);
        static readonly Color LostAlly = new Color(0.35f, 0.65f, 1f, 0.8f);
        static readonly Color LostNeutral = new Color(ContactGrey.r, ContactGrey.g, ContactGrey.b, 0.7f);

        /// <summary>A lost contact's question mark: red if hostile to the hull flown, blue if of its own flag, grey if
        /// neutral -- or if the scanners never made out whose it was.</summary>
        Color LostColor(Contact contact)
        {
            var me = app.Piloted.Ship?.Faction;
            var them = contact.Ship?.Faction;
            if (contact.Level != ContactLevel.Identified || me == null || them == null) return LostNeutral;
            if (them == me) return LostAlly;
            return app.World.Diplomacy.Hostile(me, them) ? LostEnemy : LostNeutral;
        }

        /// <summary>Where a ship is drawn: where it is, unless it is a contact the scanners have lost -- then where they last
        /// had it (false if they never had it here).</summary>
        bool ContactScene(Ship ship, Contact contact, out DVec3 scene)
        {
            if (contact == null || !contact.Cold)
            {
                scene = app.ShipScene(ship);
                if (contact != null) lastSeenScene[ship] = scene;
                return true;
            }
            return lastSeenScene.TryGetValue(ship, out scene);
        }

        public Hud(UnitySpaceApp app) => this.app = app;

        /// <summary>The galaxy map screen ([), made the first time it is opened; and whether it was up last frame.</summary>
        GalaxyMapScreen galaxyMap;
        bool galaxyMapOpen;

        /// <summary>The Help screen (H / F1, or Help in the Esc menu), made the first time it is opened.</summary>
        HelpScreen helpScreen;

        public void Draw()
        {
            EnsureStyles();
            if (Event.current.type == EventType.Layout)
            {
                app.UiRects.Clear();
                app.ShipNameRects.Clear();
            }

            // F11 (U.SPC-T496): a clear screen, nothing on it but the reticle in use.
            if (app.ScreenCleared)
            {
                DrawReticleOnly();
                return;
            }

            // Spawn & Pilot is its own full screen, not an overlay panel -- everything else waits.
            if (app.ShowSpawn) { DrawSpawn(); return; }

            // So is Help (U.SPC-T494): every key and control, by group, over the whole view.
            if (app.ShowHelp)
            {
                helpScreen ??= new HelpScreen();
                var whole = new Rect(0, 0, Screen.width, Screen.height);
                if (!app.UiRects.Contains(whole)) app.UiRects.Add(whole); // the world under it takes no clicks
                if (helpScreen.Draw("Esc, H or F1 to close")) app.ShowHelp = false;
                DrawToast();
                return;
            }

            // So is the galaxy map (U.SPC-T103), between the stars aside: a trip under way has the screen.
            if (app.ShowGalaxyMap && !app.Travel.Active)
            {
                galaxyMap ??= new GalaxyMapScreen(app);
                if (!galaxyMapOpen) galaxyMap.Opened();
                galaxyMapOpen = true;
                galaxyMap.Draw();
                DrawToast();
                return;
            }
            galaxyMapOpen = false;

            if (app.Travel.Active) DrawTravel();
            else if (app.InFtlCruise) DrawFtlCruise();
            else
            {
                DrawWorldOverlay();
                // A plotted course over the gates: the way and the next leg (U.SPC-T516).
                var courseLine = app.CourseLine();
                if (courseLine != null) DrawBanner(courseLine);
                if (app.CourseAutopilotOffered)
                {
                    var autopilot = new Rect((Screen.width - 240f) * 0.5f, Mathf.Max(Screen.height * 0.26f, 170f) + 34f, 240f, 26f);
                    if (GUI.Button(autopilot, "AUTOPILOT: CREW FLIES THE COURSE", button)) app.EngageCourseAutopilot();
                }
            }
            DrawLaneEffects();
            if (!app.ShowHud)
            {
                if (app.ShowSaves) DrawSaves();
                return;
            }
            DrawFlightMarkers();
            DrawCombatHud();
            DrawHunters();
            DrawTopBar();
            if (app.ShowNews) DrawNews();
            else DrawTicker();
            DrawNavigator();
            DrawSelection();
            DrawStatus();
            DrawSalvage();
            DrawBoarding();
            DrawHail();
            DrawLaneRide();
            DrawFtl();
            DrawSeat();
            DrawCourseMarker();
            DrawInterdiction();
            if (!app.ShowMarket) DrawOpportunityBoard();
            if (app.ShowMarket) DrawMarket();
            if (app.ShowCargo) DrawCargo();
            if (app.ShowOutfit) DrawOutfit();
            if (app.ShowBounties) DrawBounties();
            if (app.ShowContracts) DrawContracts();
            if (app.ShowFleet) DrawFleet();
            if (app.ShowTrade) DrawTradeDesk();
            if (app.ShowPicker) DrawPicker();
            if (app.ShowSaves) DrawSaves();
            DrawToast();
        }

        // ---------------------------------------------------------------- save and load (U.SPC-T67)

        string[] saveNames;
        string nextManualSlot;
        SaveHeader[] saveHeaders;
        float savesReadAt = -10f;
        Vector2 savesScroll;

        /// <summary>The save the player has asked to delete, waiting for the second click that confirms it.</summary>
        string deleteArmed;

        /// <summary>
        /// The saves (F6, or Save / Load in the pause menu), of the profile in use: New Save makes another of the
        /// player's own, and each row shows where and when it was saved, who was flying and their purse, with
        /// Save to overwrite it (the quicksave and the player's saves), Load, and Delete (asked twice). Save and
        /// Load act at the next frame.
        /// </summary>
        void DrawSaves()
        {
            var slots = app.Saves;
            if (slots == null) return;
            if (saveNames == null || Time.unscaledTime - savesReadAt > 1f)
            {
                saveNames = new System.Collections.Generic.List<string>(slots.AllSlots()).ToArray();
                saveHeaders = new SaveHeader[saveNames.Length];
                for (int i = 0; i < saveNames.Length; i++) saveHeaders[i] = slots.Header(saveNames[i]);
                nextManualSlot = slots.NewManual();
                savesReadAt = Time.unscaledTime;
            }
            const float w = 760, rowH = 24;
            float listH = Mathf.Min(saveNames.Length, 14) * rowH;
            float h = 116 + listH;
            var r = Panel(new Rect((Screen.width - w) * 0.5f, Mathf.Min(170, Screen.height - h - 20), w, h));
            GUI.Label(new Rect(r.x + 12, r.y + 6, w - 60, 22), "SAVE & LOAD  ·  " + ActiveProfile.Current.Name.ToUpperInvariant(), title);
            if (GUI.Button(new Rect(r.xMax - 34, r.y + 6, 24, 22), "X", button)) app.ShowSaves = false;
            var old = GUI.contentColor;
            GUI.contentColor = Dim;
            string blocked = app.SaveBlocker();
            GUI.Label(new Rect(r.x + 12, r.y + 30, w - 24, 18), blocked ??
                "F5 quicksave   F8 quickload   F6 close   ·   autosaves every " + (UnitySpaceApp.AutosaveIntervalS / 60f).ToString("0", CultureInfo.InvariantCulture) + " minutes and on quit", small);
            GUI.contentColor = old;

            bool enabled = GUI.enabled;
            GUI.enabled = enabled && blocked == null;
            string next = nextManualSlot;
            if (GUI.Button(new Rect(r.x + 8, r.y + 54, 160, 26), "New Save", buttonActive))
            {
                app.RequestSave(next, UnitySpaceApp.SlotName(next));
                savesReadAt = -10f;
                deleteArmed = null;
            }
            GUI.enabled = enabled;

            var view = new Rect(r.x + 8, r.y + 88, w - 16, listH + 4);
            var content = new Rect(0, 0, view.width - (saveNames.Length > 14 ? 18 : 0), saveNames.Length * rowH);
            savesScroll = GUI.BeginScrollView(view, savesScroll, content);
            for (int i = 0; i < saveNames.Length; i++)
            {
                string slot = saveNames[i];
                var hdr = saveHeaders[i];
                var row0 = new Rect(0, i * rowH, content.width, rowH - 2);
                GUI.Box(row0, GUIContent.none, row);
                GUI.Label(new Rect(row0.x + 6, row0.y + 2, 150, 18), UnitySpaceApp.SaveTitle(slot, hdr), text);
                GUI.contentColor = hdr == null ? Dim : Color.white;
                string line = hdr == null ? "empty" : UnitySpaceApp.DescribeSave(hdr) + "   ·   saved " + hdr.SavedUtc.ToLocalTime().ToString("MMM d HH:mm", CultureInfo.InvariantCulture);
                GUI.Label(new Rect(row0.x + 160, row0.y + 3, row0.width - 400, 18), line, small);
                GUI.contentColor = old;
                if (blocked != null) continue;
                if (!SaveSlots.IsAutosave(slot) && GUI.Button(new Rect(row0.xMax - 228, row0.y + 1, 70, 20), "Save", button))
                {
                    app.RequestSave(slot, hdr?.Label);
                    savesReadAt = -10f;
                    deleteArmed = null;
                }
                if (hdr != null && GUI.Button(new Rect(row0.xMax - 152, row0.y + 1, 70, 20), "Load", button))
                {
                    app.RequestLoad(slot);
                    deleteArmed = null;
                }
                if (hdr != null && GUI.Button(new Rect(row0.xMax - 76, row0.y + 1, 72, 20), deleteArmed == slot ? "Sure?" : "Delete", deleteArmed == slot ? buttonActive : button))
                {
                    if (deleteArmed == slot)
                    {
                        slots.Delete(slot);
                        deleteArmed = null;
                        savesReadAt = -10f;
                    }
                    else deleteArmed = slot;
                }
            }
            GUI.EndScrollView();
        }

        // ---------------------------------------------------------------- world overlay

        /// <summary>Between the stars: each star named, the two systems highlighted, and what the camera is doing.</summary>
        void DrawTravel()
        {
            var travel = app.Travel;
            if (Event.current.type == EventType.Repaint)
            {
                DrawGateLight(travel.GateGlow, GateGlowColor, 0.3f);
                DrawWarp();
                DrawGateLight(travel.GateFlash * travel.GateFlash, GateFlashColor, 0.9f);
            }
            DrawInterstellarLabels();
            if (string.IsNullOrEmpty(travel.Banner)) return;
            DrawBanner(travel.Banner);
        }

        /// <summary>
        /// Between the stars under the hull's own FTL drive (U.SPC-T417): the stars' names, warp streaks pouring from the
        /// star ahead, and one caption: where it is bound, how far is left, and how long at this pace.
        /// </summary>
        void DrawFtlCruise()
        {
            var c = app.FtlCruise;
            if (c == null) return;
            double jd = app.Clock.Jd;
            if (Event.current.type == EventType.Repaint)
            {
                double peak = c.Drive != null ? c.Drive.LyPerDay : 1;
                float strength = Mathf.Clamp01((float)(c.Profile.SpeedAt(jd - c.LegJd) / Math.Max(peak, 1e-12)));
                var centre = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
                if (DirectionToGui(c.Direction, out var ahead)) centre = ahead;
                DrawStreaks(centre, 0.25f + 0.75f * strength, WarpLineCount, WarpColor, 500, 320f);
            }
            DrawInterstellarLabels();
            double ly = c.LyToGo(jd);
            double speedC = c.SpeedKmS(jd) / 299792.458;
            if (c.Crossing)
            {
                // Pulled out of FTL by a star's well or a gravity projector (U.SPC-T418), crossing it under the ordinary drive.
                DrawBanner("PULLED OUT OF FTL BY " + (c.InterdictedBy ?? "A GRAVITY WELL").ToUpperInvariant() + "   ·   CROSSING UNDER NORMAL DRIVE   ·   BOUND FOR " +
                           c.ToName.ToUpperInvariant() + ", " + ly.ToString("0.00", CultureInfo.InvariantCulture) + " LY TO GO");
                return;
            }
            // The trip compressed on the sim clock (U.SPC-T420): when it drops out, by the calendar and in real seconds at this pace.
            DrawBanner("FTL  ·  BOUND FOR " + c.ToName.ToUpperInvariant() + "   ·   " + ly.ToString("0.00", CultureInfo.InvariantCulture) + " LY TO GO   ·   " +
                       speedC.ToString("N0", CultureInfo.InvariantCulture) + " C   ·   ETA " + Astro.ToDateTime(c.ArriveJd).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) +
                       "   ·   DROPPING OUT IN " + Mathf.CeilToInt((float)app.FtlSecondsLeft) + " S AT " + app.Clock.Warp.Label.ToUpperInvariant() + "  (, .)");
        }

        /// <summary>
        /// The helm's FTL drive (U.SPC-T417): the star the nose is on and the key to spool for it (or, inside the system's
        /// gravity wells, how far out the rim is: canon, U.SPC-T455, no jump inside them), and the charge as it builds.
        /// </summary>
        void DrawFtl()
        {
            if (!app.IsPiloting || app.RidingLane) return;
            var ftl = app.Ftl;
            var ap = app.FtlAutopilot;
            var course = app.PlottedCourse;
            double jd = app.Clock.Jd;
            var inv = CultureInfo.InvariantCulture;
            string head, mid = null, tail;
            if (ap.Active && ap.Course != null)
            {
                // The FTL autopilot at the helm (U.SPC-T420): where it has got to in flying the course.
                var c = ap.Course;
                string name = c.Target.Name.ToUpperInvariant();
                switch (ap.Stage)
                {
                    case FtlAutopilotStage.OutRun:
                        head = "FTL AUTOPILOT  ·  " + name + "  ·  OUT TO THE RIM  ·  " + Percent(ap.RunFraction(jd)) + "  ·  " +
                               SpeedText(ap.VelocityKmS(jd).Length) + "  ·  beyond it in " + Mathf.CeilToInt((float)app.FtlSecondsLeft) + " s";
                        break;
                    case FtlAutopilotStage.Aligning:
                        head = "FTL AUTOPILOT  ·  " + name + "  ·  TURNING ONTO IT";
                        break;
                    case FtlAutopilotStage.Cooling:
                        head = "FTL AUTOPILOT  ·  " + name + "  ·  DRIVE COOLING  ·  spools in " + Mathf.CeilToInt((float)app.FtlSecondsLeft) + " s";
                        break;
                    default:
                        head = "FTL AUTOPILOT  ·  " + name + "  ·  SPOOLING  ·  " + Percent(ftl.SpoolFraction(jd)) + "  ·  engages in " +
                               Mathf.CeilToInt((float)app.FtlSecondsLeft) + " s";
                        break;
                }
                mid = CourseLine(c);
                tail = "time runs at " + app.Clock.Warp.Label + "  (, . to change)    End takes the helm back    a fight or hunters let go of it";
            }
            else if (ftl.Stage == FtlStage.Spooling)
            {
                head = "FTL DRIVE SPOOLING  ·  " + ftl.Target.Name.ToUpperInvariant() + "  ·  " + app.Distance(ftl.Target) + "  ·  " +
                       Percent(ftl.SpoolFraction(jd)) + "  ·  engages in " + Mathf.CeilToInt((float)app.FtlSecondsLeft) + " s";
                tail = "hold the nose on it, beyond the rim and out of any fight, or the charge is lost    Enter calls it off";
            }
            else if (course != null)
            {
                // A plotted course (U.SPC-T420): the way there, the ETA and the fuel, and how to fly it.
                head = "COURSE  ·  " + course.Target.KindName.ToUpperInvariant() + "  " + course.Target.Name.ToUpperInvariant() + "  ·  " + app.Distance(course.Target);
                mid = course.Flyable ? CourseLine(course) : course.Refusal;
                tail = (course.Flyable ? "End  autopilot: out to the rim, onto it, spool and engage    " : "") + "PgUp/PgDn  another star or beacon    Del  clear    [  galaxy map";
            }
            else
            {
                var t = app.FtlPointed;
                if (t == null) return;
                head = "FTL  ·  " + t.KindName.ToUpperInvariant() + "  " + t.Name.ToUpperInvariant() + "  ·  " + app.Distance(t);
                tail = app.FtlBeyondRim
                    ? app.FtlBlocked ?? "Enter  spool the drive for it (out through open space to the edge of its well)    End  autopilot"
                    : "inside the gravity wells: no jump until beyond the outer rim, " + app.FtlRimAu.ToString("0.##", inv) + " AU out (" +
                      app.PilotFromStarAu.ToString("0.##", inv) + " AU now)    End  autopilot flies you out and on";
            }
            var a = new GUIContent(head);
            var m = new GUIContent(mid ?? "");
            var b = new GUIContent(tail);
            float w = Mathf.Max(text.CalcSize(a).x, Mathf.Max(small.CalcSize(m).x, small.CalcSize(b).x)) + 28;
            float h = mid == null ? 50 : 68;
            var r = Panel(new Rect((Screen.width - w) * 0.5f, Screen.height - 222 - h, w, h));
            GUI.contentColor = StreakColor;
            GUI.Label(new Rect(r.x + 14, r.y + 5, w, 20), a, text);
            float y = r.y + 27;
            if (mid != null)
            {
                GUI.contentColor = course != null && !course.Flyable && !ap.Active ? RetrogradeColor : Color.white;
                GUI.Label(new Rect(r.x + 14, y, w, 18), m, small);
                y += 18;
            }
            GUI.contentColor = Dim;
            GUI.Label(new Rect(r.x + 14, y, w, 18), b, small);
            GUI.contentColor = Color.white;
        }

        /// <summary>A plotted course's numbers in a line: the run out, the jump, any interdictions, the ETA and the fuel (U.SPC-T420).</summary>
        static string CourseLine(FtlCourse c)
        {
            var inv = CultureInfo.InvariantCulture;
            string run = c.NeedsOutRun ? "out to the rim " + (c.OutRunKm / Astro.AuKm).ToString("0.##", inv) + " AU, " + FtlNavigation.Describe(c.OutRunDays) + "   ·   " : "";
            string cool = c.CoolDays > 0 ? "cooling " + FtlNavigation.Describe(c.CoolDays) + "   ·   " : "";
            string jump = c.Ly.ToString("0.00", inv) + " ly under FTL, " + FtlNavigation.Describe(c.SpoolDays + c.FtlDays);
            string pulled = c.Interdictions.Count == 0 ? "" : "   ·   pulled out by " + string.Join(", ", c.Interdictions) + " (" + FtlNavigation.Describe(c.CrossingDays) + " under normal drive)";
            return run + cool + jump + pulled + "   ·   ETA " + Astro.ToDateTime(c.EtaJd).ToString("yyyy-MM-dd HH:mm", inv) + " (" + FtlNavigation.Describe(c.TotalDays) + ")" +
                   "   ·   fuel " + c.FuelTonnes.ToString("N0", inv) + " of " + c.TanksTonnes.ToString("N0", inv) + " t";
        }

        /// <summary>The plotted course's star or beacon marked where it lies in the view, named, with how far (U.SPC-T420).</summary>
        void DrawCourseMarker()
        {
            if (Event.current.type != EventType.Repaint || !app.PlottedBearing(out var dir, out double km)) return;
            if (!DirectionToGui(dir, out var p)) return;
            var c = new Color(StreakColor.r, StreakColor.g, StreakColor.b, 0.95f);
            const float r = 11;
            DrawLine(p + new Vector2(0, -r), p + new Vector2(r, 0), c, 1.5f);
            DrawLine(p + new Vector2(r, 0), p + new Vector2(0, r), c, 1.5f);
            DrawLine(p + new Vector2(0, r), p + new Vector2(-r, 0), c, 1.5f);
            DrawLabel(app.PlottedCourse.Target.Name.ToUpperInvariant() + "  " + app.Distance(app.PlottedCourse.Target), p + new Vector2(r + 5, -8), c, false);
            DrawLine(p + new Vector2(-r, 0), p + new Vector2(0, -r), c, 1.5f);
        }

        /// <summary>The names of the stars in the view between them (with who holds a system's).</summary>
        void DrawInterstellarLabels()
        {
            if (Event.current.type == EventType.Repaint && app.Interstellar.Visible && app.ShowLabels)
            {
                foreach (var s in app.Interstellar.Stars)
                {
                    if (!app.RelativeToGui(s.Relative, out var p)) continue;
                    var c = Color.Lerp(s.Color, Color.white, 0.4f);
                    if (s.Label == null) continue;
                    bool system = s.System;
                    string name = s.Label;
                    // A simulated system's first star names who holds it (U.SPC-T91).
                    var holder = s.Of?.Holder;
                    if (holder != null) name += "  ·  " + holder.Name;
                    double ly = s.PositionLy.Length;
                    DrawLabel(system ? name.ToUpperInvariant() : name + (ly > 0.01 ? "  " + ly.ToString("0.0", CultureInfo.InvariantCulture) + " ly" : ""),
                        p + new Vector2(10, -8), system ? Accent : new Color(c.r, c.g, c.b, 0.75f), !system);
                }
            }
        }

        /// <summary>One quiet caption for the whole trip, over the view itself: no panel or progress bar to read as a loading screen.</summary>
        void DrawBanner(string banner)
        {
            var content = new GUIContent(banner);
            var size = toast.CalcSize(content);
            // Below the clock and news feed at the top of the screen.
            var r = new Rect((Screen.width - size.x) * 0.5f, Mathf.Max(Screen.height * 0.26f, 170f), size.x, size.y);
            var old = GUI.contentColor;
            GUI.contentColor = new Color(0, 0, 0, 0.7f);
            GUI.Label(new Rect(r.x + 1, r.y + 1, r.width, r.height), content, toast);
            GUI.contentColor = old;
            GUI.Label(r, content, toast);
        }

        void DrawWorldOverlay()
        {
            if (Event.current.type != EventType.Repaint) return;
            var catalog = app.Catalog;
            double ppu = app.PixelsPerUnitAtUnitDistance();
            DrawSkyMarkers();
            DrawStructureLabels(ppu);

            for (int i = 0; i < catalog.Count; i++)
            {
                var body = catalog[i];
                if (!body.IsVisible) continue;
                var scene = app.BodyScene(i);
                if (!app.ToGui(scene, out var p, out var depth)) continue;
                float pxRadius = (float)(app.BodyDisplayRadius(i) / depth * ppu);

                if (body.Kind == BodyKind.Moon)
                {
                    // Moons hug their planet on screen until you are close; skip them until they separate.
                    if (app.ToGui(app.BodyScene(body.ParentIndex), out var pp, out _) && (pp - p).magnitude < 14f) continue;
                }
                bool occluded = pxRadius < 3 && app.Occluded(scene, i);
                if (occluded) continue;

                var tint = BodyTint(body);
                float iconSize = Icons.PixelSize(body);
                if (pxRadius < iconSize * 0.3f)
                    DrawIcon(Icons.ForBody(body), p, iconSize, tint);

                if (app.ShowLabels && (body.Kind != BodyKind.Moon || pxRadius > 1.5f || ParentSeparation(body, p) > 50))
                    DrawLabel(body.Name + StatusMark(body) + HavenMark(body), p + new Vector2(Mathf.Max(pxRadius, iconSize * 0.5f) + 5, -8), tint, body.Kind == BodyKind.Moon);
            }

            var ships = app.Fleet.Ships;
            app.FoldedShips.Clear();
            iconMarks.Clear();
            iconDraws.Clear();
            // At the helm the player sees only what the ship's own scanners hold (U.SPC-T66): an unknown blip, a hull
            // class, or -- identified -- whose it is; a track gone cold is drawn faded where it was last seen.
            var picture = Picture;
            if (picture == null || app.Fleet != lastSeenFleet)
            {
                lastSeenScene.Clear();
                lastSeenFleet = app.Fleet;
            }
            // A destroyed ship is skipped below before it ever reaches ContactScene again, so its last-seen
            // entry would otherwise sit here for the rest of a long piloting session in this system, never
            // cleared by the whole-dictionary resets above: dropped here instead (same shape as the
            // contactToastAt leak fixed in UnitySpaceApp.Collisions.cs).
            if (lastSeenScene.Count > 0)
            {
                List<Ship> gone = null;
                foreach (var kv in lastSeenScene)
                    if (kv.Key.Destroyed) (gone ??= new List<Ship>()).Add(kv.Key);
                if (gone != null) foreach (var s in gone) lastSeenScene.Remove(s);
            }
            for (int i = 0; i < ships.Count; i++)
            {
                var ship = ships[i];
                if (ship.Destroyed || ship.Controller.Phase == ShipPhase.Landed) continue;
                var contact = picture != null && ship != app.Piloted.Ship ? picture.Of(ship) : null;
                if (picture != null && ship != app.Piloted.Ship && contact == null) continue;
                if (!ContactScene(ship, contact, out var scene)) continue;
                if (!app.ToGui(scene, out var p, out var depth)) continue;
                if (contact != null && contact.Cold)
                {
                    // Lost: only a question mark where the scanners last had it, never where it really is now.
                    DrawIcon(Icons.Lost, p, 14, LostColor(contact));
                    continue;
                }
                double length = app.ShipDisplayLength(ship);
                float px = (float)ShipLod.ApparentPx(length, depth, ppu);
                // Drawn as its model (and the model built: one still streaming in shows as its icon meanwhile).
                if (app.ShipModelShown(ship, px)) continue;
                if (app.Occluded(scene)) continue;
                if (ship.IsEscort && app.ToGui(app.ShipScene(ship.Leader), out var lp, out _) && (lp - p).magnitude < 7f)
                {
                    app.FoldedShips.Add(ship);
                    continue;
                }
                var c = ship.Faction.Color;
                const float alpha = 0.95f;
                Texture2D icon;
                float size;
                Color tint;
                int group;
                if (contact == null || contact.Level == ContactLevel.Identified)
                {
                    icon = Icons.ForShip(ship.Spec.Class);
                    size = Icons.PixelSize(ship.Spec.Class);
                    tint = new Color(c.R, c.G, c.B, alpha);
                    group = GroupOf(ship.Faction);
                }
                else if (contact.Level == ContactLevel.Blip)
                {
                    icon = Icons.Blip;
                    size = 12;
                    tint = new Color(ContactGrey.r, ContactGrey.g, ContactGrey.b, alpha);
                    group = -1;
                }
                else
                {
                    icon = Icons.ForShip(ship.Spec.Class);
                    size = Icons.PixelSize(ship.Spec.Class);
                    tint = new Color(ContactGrey.r, ContactGrey.g, ContactGrey.b, alpha);
                    group = -2;
                }
                var t = Target.OfShip(ship);
                bool pinned = t == app.Hovered || t == app.Selected || app.SpaceCamera.FocusTarget == t || ship == app.Piloted.Ship;
                iconMarks.Add(new LodMark(p.x, p.y, group, length, pinned));
                iconDraws.Add((ship, icon, size, tint, p));
            }

            // Icons of one side that crowd on screen fold into one mark with a count (U.SPC-T415): a fleet, or the
            // traffic round a port, is drawn once, its biggest hull standing for the rest.
            foreach (var mark in RenderLod.Aggregate(iconMarks, RenderLod.AggregatePx.Value))
            {
                var (ship, icon, size, tint, p) = iconDraws[mark.Representative];
                DrawIcon(icon, p, size, tint);
                if (mark.Count > 1)
                {
                    for (int k = 1; k < mark.Members.Count; k++) app.FoldedShips.Add(iconDraws[mark.Members[k]].ship);
                    DrawLabel(RenderLod.CountLabel(mark.Count), p + new Vector2(size * 0.5f, size * 0.5f - 6), tint, true);
                }
            }

            DrawTargetMarker(app.Hovered, false);
            DrawTargetMarker(app.Selected, true);
            DrawOpportunityMarkers();

            app.ShipNameRects.Clear();
            if (app.ShowLabels)
            {
                // Names that must show (hovered, selected, followed, a flagship on sight) go down first; a far-off ship's
                // icon name (U.SPC-T376) only where it does not run into one already drawn, so a crowded port stays legible.
                shipLabelRects.Clear();
                iconNamed.Clear();
                foreach (var ship in ships)
                {
                    if (ship.Destroyed) continue;
                    var contact = picture != null && ship != app.Piloted.Ship ? picture.Of(ship) : null;
                    if (picture != null && ship != app.Piloted.Ship && contact == null) continue;
                    var t = Target.OfShip(ship);
                    bool named = t == app.Hovered || t == app.Selected || app.SpaceCamera.FocusTarget == t;
                    bool identified = contact == null || contact.Level == ContactLevel.Identified;
                    // A flagship is named on sight only once the scanners have identified it.
                    if (!named && !ship.IsEscort && ship.Spec.IsMilitary && identified)
                    {
                        // Name fleet flagships once they are big enough on screen to be a thing, not a dot.
                        if (app.ToGui(app.ShipScene(ship), out _, out var d))
                            named = app.ShipDisplayLength(ship) / d * ppu > 12;
                    }
                    if (!named)
                    {
                        // Drawn as its icon: named beside it if the settings say so and the scanners know whose it is.
                        // Folded into another's mark (U.SPC-T415), the mark's own ship is named for it.
                        if (identified && (contact == null || !contact.Cold) && !app.FoldedShips.Contains(ship) && ShipLod.NamesIcon(ship.Spec.IsMilitary, ship.IsEscort)) iconNamed.Add((ship, contact));
                        continue;
                    }
                    DrawShipName(ship, contact, ppu, false);
                }
                foreach (var (ship, contact) in iconNamed)
                    DrawShipName(ship, contact, ppu, true);
            }
        }

        /// <summary>The far-off ships' icons this repaint, before they are folded into marks (U.SPC-T415).</summary>
        readonly List<LodMark> iconMarks = new List<LodMark>();
        readonly List<(Ship ship, Texture2D icon, float size, Color tint, Vector2 p)> iconDraws = new List<(Ship, Texture2D, float, Color, Vector2)>();
        readonly Dictionary<Faction, int> factionGroups = new Dictionary<Faction, int>();

        /// <summary>Which side an icon folds with: one group per faction.</summary>
        int GroupOf(Faction f)
        {
            if (f == null) return 0;
            if (!factionGroups.TryGetValue(f, out int g)) factionGroups[f] = g = factionGroups.Count + 1;
            return g;
        }

        /// <summary>Ship name labels drawn this repaint, so an icon's name can stay clear of them.</summary>
        readonly List<Rect> shipLabelRects = new List<Rect>();

        /// <summary>Ships drawn as icons whose names are wanted this repaint, drawn after the names that must show.</summary>
        readonly List<(Ship ship, Contact contact)> iconNamed = new List<(Ship, Contact)>();

        /// <summary>
        /// A ship's name beside it. <paramref name="iconName"/>: an icon's name (U.SPC-T376), drawn only while the ship is
        /// an icon and only where it overlaps no name already drawn. Every name is clickable, like the icon.
        /// </summary>
        void DrawShipName(Ship ship, Contact contact, double ppu, bool iconName)
        {
            if (!ContactScene(ship, contact, out var at) || !app.ToGui(at, out var p, out var depth) || app.Occluded(at)) return;
            float px = (float)ShipLod.ApparentPx(app.ShipDisplayLength(ship), depth, ppu);
            if (iconName && app.ShipModelShown(ship, px)) return;
            var c = ship.Faction.Color;
            var at2 = p + new Vector2(Mathf.Max(px * 0.5f, iconName ? Icons.PixelSize(ship.Spec.Class) * 0.5f : 10) + 4, -8);
            string text;
            Color tint;
            bool minor;
            if (contact == null)
            {
                text = ship.Name;
                tint = new Color(c.R, c.G, c.B);
                minor = iconName;
            }
            else
            {
                // A contact is named by what the scanners make of it: unknown, its class, or its name.
                text = contact.Label + (contact.Cold ? "  (contact lost)" : contact.LagSeconds >= 0.1 ? "  (" + LightSpeed.Describe(contact.LagSeconds / Astro.SecondsPerDay) + " ago, light lag)" : "");
                tint = contact.Level == ContactLevel.Identified ? new Color(c.R, c.G, c.B) : ContactGrey;
                if (contact.Cold) tint.a = 0.5f;
                minor = contact.Level != ContactLevel.Identified;
            }
            var size = (minor ? minorLabel : label).CalcSize(new GUIContent(text));
            var r = new Rect(at2.x, at2.y, size.x, size.y);
            if (iconName)
                foreach (var other in shipLabelRects)
                    if (other.Overlaps(r)) return;
            shipLabelRects.Add(r);
            app.ShipNameRects.Add((r, ship));
            DrawLabel(text, at2, tint, minor);
        }

        /// <summary>Unconfirmed worlds are marked as such wherever they are named.</summary>
        static string StatusMark(BodyDefinition b) =>
            b.Status == BodyStatus.Candidate ? "  (candidate)" : b.Status == BodyStatus.Disputed ? "  (disputed)" : "";

        /// <summary>The other simulated systems shine in this one's sky: Alpha Centauri from Sol, the Sun in Cassiopeia from Alpha Centauri.</summary>
        void DrawSkyMarkers()
        {
            if (!app.ShowLabels) return;
            foreach (var other in app.Galaxy.Systems)
            {
                if (other == app.ActiveSystem) continue;
                var dir = app.SkyDirection(other) * 1e6f;
                if (!app.RelativeToGui(dir, out var p)) continue;
                if (p.x < 0 || p.y < 0 || p.x > Screen.width || p.y > Screen.height) continue;
                var c = new Color(1f, 0.95f, 0.8f, 0.9f);
                // A binary's root is the invisible barycentre, so its first star stands for it; a Void Base has no
                // star at all (U.SPC-T372), just its one body.
                var mark = other.Catalog.Root.IsStar || other.Catalog.Count < 2 ? other.Catalog.Root : other.Catalog[1];
                DrawIcon(Icons.ForBody(mark), p, 14, c);
                string name = other.Catalog.Root.Name == "Sun" ? "Sun  (Sol System)" : other.Name;
                double ly = Galaxy.DistanceLy(app.ActiveSystem, other);
                // Every ship in another system is folded into its one mark (U.SPC-T415), which says how many.
                string ships = RenderLod.SystemShipsLabel(RenderLod.LiveShips(other.World?.Fleet));
                DrawLabel(name + "  " + ly.ToString("0.00", CultureInfo.InvariantCulture) + " ly" + (ships.Length > 0 ? "  · " + ships : ""), p + new Vector2(10, -8), c, true);
            }
        }

        /// <summary>Yards, factories, spacecraft and docking rings are named once they are drawn big enough to see.</summary>
        void DrawStructureLabels(double ppu)
        {
            var views = app.Structures;
            if (views == null || !app.ShowLabels) return;
            foreach (var kv in views.Scenes)
            {
                if (!app.ToGui(kv.Value, out var p, out var depth)) continue;
                float px = (float)(views.Sizes[kv.Key] / depth * ppu);
                var t = Target.OfStructure(kv.Key);
                if (px < 6 && t != app.Selected && t != app.Hovered) continue;
                if (app.Occluded(kv.Value)) continue;
                var c = kv.Key.Owner != null ? new Color(kv.Key.Owner.Color.R, kv.Key.Owner.Color.G, kv.Key.Owner.Color.B) : new Color(0.85f, 0.88f, 0.92f);
                DrawLabel(kv.Key.Name, p + new Vector2(Mathf.Max(px * 0.5f, 6) + 4, -8), c, true);
            }
            foreach (var kv in views.RingScenes)
            {
                if (!app.ToGui(kv.Value, out var p, out var depth)) continue;
                float px = (float)(views.Sizes[kv.Key] / depth * ppu);
                var t = Target.OfRing(kv.Key);
                if (px < 2.5f && t != app.Selected && t != app.Hovered) continue;
                if (app.Occluded(kv.Value)) continue;
                int queued = 0;
                foreach (var s in app.Fleet.Ships)
                    if (!s.Destroyed && s.Phase == ShipPhase.Queued && s.Ring == kv.Key) queued++;
                DrawLabel(kv.Key.Name + (queued > 0 ? "  · " + queued + " queued" : ""), p + new Vector2(Mathf.Max(px * 0.5f, 6) + 4, -8), new Color(0.6f, 0.9f, 1f), true);
            }
        }

        float ParentSeparation(BodyDefinition moon, Vector2 p)
        {
            return app.ToGui(app.BodyScene(moon.ParentIndex), out var pp, out _) ? (pp - p).magnitude : 0;
        }

        // ---------------------------------------------------------------- flight markers (U.SPC-T59)

        static readonly Color ProgradeColor = new Color(0.55f, 1f, 0.6f, 0.95f);
        static readonly Color RetrogradeColor = new Color(1f, 0.62f, 0.35f, 0.95f);

        /// <summary>
        /// While piloting, the flight instruments a Newtonian hull needs: a boresight where the nose
        /// points, the prograde marker where the hull is actually going (its velocity relative to the
        /// body it flies near) and the retrograde marker opposite it, joined to the boresight by the
        /// velocity vector so a slide (flight assist off, or a hull still killing its drift) reads at a
        /// glance, and a readout of flight assist, speed and how far the track is off the nose.
        /// </summary>
        void DrawFlightMarkers()
        {
            if (!app.IsPiloting || app.Travel.Active || Event.current.type != EventType.Repaint) return;
            var vel = app.PilotVelocityKmS;
            double speed = vel.Length;
            bool noseOn = DrawAimReticle(out var nose);

            double driftDeg = 0;
            if (speed > 1e-6)
            {
                var dir = vel / speed;
                driftDeg = Math.Acos(Math.Max(-1, Math.Min(1, DVec3.Dot(dir, app.PilotForward)))) * 180 / Math.PI;
                if (DirectionToGui(dir, out var pro))
                {
                    if (noseOn && (pro - nose).sqrMagnitude > 400) DrawLine(nose, pro, new Color(ProgradeColor.r, ProgradeColor.g, ProgradeColor.b, 0.45f), 1.5f);
                    DrawIcon(Icons.Prograde, pro, 28, ProgradeColor);
                    DrawLabel(SpeedText(speed), pro + new Vector2(17, -8), ProgradeColor, true);
                }
                if (DirectionToGui(-dir, out var retro)) DrawIcon(Icons.Retrograde, retro, 28, RetrogradeColor);
            }

            // A hauler's stop point (U.SPC-T443): where full retro brings it to rest with what it carries, so the braking
            // can be planned; marked toward it on the track.
            var stop = app.PilotStop;
            string stopText = "";
            if (stop.HasValue && speed > 1e-6)
            {
                var st = stop.Value;
                stopText = st.Reachable
                    ? "   STOP " + RangeText(st.DistanceKm) + " in " + Mathf.CeilToInt((float)st.Seconds) + "s"
                    : "   STOP none (no retro)";
                if (st.Reachable && st.DistanceKm > 1e-9 && DirectionToGui(st.OffsetKm / st.DistanceKm, out var sp))
                {
                    DrawRing(sp, 9, RetrogradeColor);
                    DrawLabel("STOP " + RangeText(st.DistanceKm), sp + new Vector2(12, 6), RetrogradeColor, true);
                }
            }

            bool assist = app.PilotFlightAssist;
            // A capital's engine telegraph (U.SPC-T499): the step its engines are rung to, held until rung again.
            var telegraph = app.PilotTelegraph;
            string readout = (assist ? "FLIGHT ASSIST ON" : "FLIGHT ASSIST OFF") +
                             (telegraph.HasValue ? "   ENGINES " + TelegraphText(telegraph.Value) + " (W/S)" : "") +
                             (app.PilotDocking ? "   DOCKING" : app.PilotStop.HasValue && app.PilotInPortApproach ? "   hold Ctrl to dock" : "") + "   " + SpeedText(speed) + stopText +
                             (speed > 1e-6 ? "   track " + driftDeg.ToString("0", CultureInfo.InvariantCulture) + "° off the nose" : "") + "   (F)";
            var content = new GUIContent(readout);
            var size = small.CalcSize(content);
            var p = new Vector2((Screen.width - size.x) * 0.5f, Screen.height - 66);
            DrawLabel(readout, p, assist ? Dim : RetrogradeColor, true);
        }

        /// <summary>
        /// The helm's aim reticle: the boresight where the nose points and, where the stick points the hull elsewhere
        /// (U.SPC-T269: far ahead of the nose in free aim, a little ahead of it on a hull steered by the nose; a capital
        /// ship's helm is its heading, the nose itself), the crosshair. Returns whether the nose is on screen, and where.
        /// </summary>
        bool DrawAimReticle(out Vector2 nose)
        {
            bool noseOn = DirectionToGui(app.PilotForward, out nose);
            if (noseOn) DrawBoresight(nose, new Color(1, 1, 1, 0.8f));
            if (app.PilotControlMode != PilotControl.Helm && DirectionToGui(app.PilotAimDirection, out var aim) && (!noseOn || (aim - nose).sqrMagnitude > 4))
                DrawCrosshair(aim, CrosshairColor);
            return noseOn;
        }

        /// <summary>
        /// The cleared screen (F11, U.SPC-T496): only the reticle in use -- a seat's sight when one is held (U.SPC-T422),
        /// else the helm's aim reticle while piloting -- and nothing at all in the strategy view or between the stars.
        /// </summary>
        void DrawReticleOnly()
        {
            if (Event.current.type != EventType.Repaint || app.Travel.Active) return;
            if (app.SeatLine() != null) DrawSeatSight();
            else if (app.IsPiloting) DrawAimReticle(out _);
        }

        // ---------------------------------------------------------------- the chase (U.SPC-T273)

        /// <summary>
        /// Hostiles running the ship down: each group's name, range and closing speed, and an EMP charging with the seconds
        /// left and how far off it reaches, top right under the readouts.
        /// </summary>
        void DrawHunters()
        {
            if (!app.IsPiloting || Event.current.type != EventType.Repaint) return;
            var pursuit = app.World.Pursuit;
            if (pursuit == null || !pursuit.Active) return;
            var me = app.PilotOffsetKm;
            var myVel = app.PilotVelocityKmS;
            float y = 118;
            DrawLabel("HUNTED", new Vector2(SafeRight - 380, y), HostileColor, false);
            y += 22;
            foreach (var g in pursuit.Groups)
            {
                var rel = g.Pos - me;
                double d = rel.Length;
                double closing = d > 1e-9 ? -DVec3.Dot(g.Vel - myVel, rel / d) : 0;
                string line = g.Faction.Name + " " + g.Leader.Name + (g.Ships.Count > 1 ? " +" + (g.Ships.Count - 1) : "") + "   " + RangeText(d) +
                              "   closing " + closing.ToString("0.0", CultureInfo.InvariantCulture) + " km/s";
                if (g.EmpCharge >= 0)
                    line += "   EMP " + Math.Max(0, Pursuit.EmpChargeSeconds - g.EmpCharge).ToString("0.0", CultureInfo.InvariantCulture) + " s (clear " +
                            Mathf.RoundToInt((float)(Pursuit.EmpRangeKm * 1.25)) + " km)";
                DrawLabel(line, new Vector2(SafeRight - 380, y), g.EmpCharge >= 0 ? LockColor : Dim, true);
                y += 18;
            }
        }

        // ---------------------------------------------------------------- aimed combat (U.SPC-T270)

        static readonly Color HostileColor = new Color(1f, 0.4f, 0.35f, 0.95f);
        static readonly Color LockColor = new Color(1f, 0.85f, 0.3f, 0.95f);

        /// <summary>
        /// In a fight at the helm: the locked hull's box with the lock building on it, the lead pip where the
        /// guns' rounds will meet it, and the gauges a pilot fights by -- heat, boost, shields and hull, missiles.
        /// </summary>
        void DrawCombatHud()
        {
            if (Event.current.type != EventType.Repaint) return;
            var me = app.PilotUnit;
            if (me == null || app.Travel.Active) return;
            var battle = app.Piloted.Ship.Battle;
            var c = me.Combat;

            var locked = me.PlayerLockTarget;
            if (locked != null && locked.Present && app.ToGui(app.BattleScene(battle, locked.Pos), out var lp, out _))
            {
                bool made = me.LockProgress >= 1;
                // The box closes in on the hull as the lock builds.
                float half = Mathf.Lerp(46f, 18f, (float)me.LockProgress);
                DrawBrackets(new Rect(lp.x - half, lp.y - half, half * 2, half * 2), made ? LockColor : HostileColor, made ? 2.5f : 1.5f);
                string state = made ? "LOCKED" : "LOCKING " + Mathf.RoundToInt((float)me.LockProgress * 100) + "%";
                DrawLabel(state + "  " + RangeText((locked.Pos - me.Pos).Length), lp + new Vector2(half + 6, -8), made ? LockColor : HostileColor, true);
            }

            // The lead pip for the guns' rounds, on the locked hull or else the one nearest the crosshair.
            double speed = RoundSpeed(c);
            var pipOn = locked != null && locked.Present ? locked : NearestHostileToAim(battle, me);
            if (speed > 0 && pipOn != null)
            {
                var lead = Gunnery.LeadPoint(me.Pos, me.Vel, pipOn.Pos, pipOn.Vel, speed);
                if (app.ToGui(app.BattleScene(battle, lead), out var pip, out _))
                {
                    DrawRing(pip, 7, HostileColor);
                    if (app.ToGui(app.BattleScene(battle, pipOn.Pos), out var at, out _) && (at - pip).sqrMagnitude > 64)
                        DrawLine(at, pip, new Color(HostileColor.r, HostileColor.g, HostileColor.b, 0.35f), 1f);
                }
            }

            // The gauges, in a row above the flight readout.
            float w = 120, x0 = Screen.width * 0.5f - 2 * (w + 14) + 7, y = Screen.height - 98;
            DrawGauge(new Rect(x0, y, w, 6), (float)me.Heat, me.Overheated ? HostileColor : new Color(1f, 0.7f, 0.3f), me.Overheated ? "OVERHEATED" : "HEAT");
            DrawGauge(new Rect(x0 + (w + 14), y, w, 6), (float)c.BoostFraction, Accent, "BOOST");
            double maxShield = Math.Max(1e-9, c.Spec.Shield);
            DrawGauge(new Rect(x0 + 2 * (w + 14), y, w, 6), (float)(c.Subsystems.Sum(s => s.Shield) / maxShield), new Color(0.45f, 0.7f, 1f), "SHIELDS");
            DrawGauge(new Rect(x0 + 3 * (w + 14), y, w, 6), (float)(c.Hull / Math.Max(1e-9, c.Spec.Hull)), ProgradeColor, "HULL");
            int missiles = c.Subsystems.Where(s => !s.Destroyed && s.Def.Weapon != null && s.Def.Weapon.Missile && s.Kind != SubsystemKind.Torpedo).Sum(s => s.Ammo);
            var line = new List<string>();
            if (missiles > 0) line.Add("MISSILES " + missiles + (me.LockProgress >= 1 ? " (right mouse)" : " (T lock, hold the crosshair on it)"));
            // The rest of the arsenal (U.SPC-T271): rounds left, and whether the mount is ready.
            AddMount(line, c, SubsystemKind.EmpLauncher, "EMP", "middle mouse");
            AddMount(line, c, SubsystemKind.MineLayer, "MINES", "Ctrl");
            if (line.Count > 0) DrawLabel(string.Join("    ", line), new Vector2(x0, y + 12), Dim, true);
            if (app.PilotControlMode == PilotControl.Helm) DrawBroadside(battle, me, x0, y + 30);
        }

        /// <summary>
        /// At a capital ship's helm (U.SPC-T272): how many guns bear where the player is looking (their fire cones take that
        /// line in, U.SPC-T533) and how many of them are loaded, the volley building while the trigger is held, and the hull
        /// it will be fired at.
        /// </summary>
        void DrawBroadside(Battle battle, BattleUnit me, float x, float y)
        {
            var look = app.PilotLookDirection;
            int loaded = 0, bearing = 0;
            foreach (var s in me.Combat.Subsystems)
            {
                var w = s.Def.Weapon;
                if (s.Destroyed || w == null || w.Missile || w.AntiSmall || w.Supporting || !me.BearsAlong(s.Def, look)) continue;
                bearing++;
                if (s.Cooldown <= 0) loaded++;
            }
            float built = (float)(me.VolleyCharge / Battle.VolleyBuildSeconds);
            string text = (bearing == 0 ? "NO GUN BEARS HERE" : "BATTERY BEARING  " + loaded + "/" + bearing + " loaded") + (me.PlayerFireHeld ? "  ·  VOLLEY " + Mathf.RoundToInt(built * 100) + "%  (let go to fire)" : "  ·  hold left mouse to build a volley");
            DrawLabel(text, new Vector2(x, y), me.PlayerFireHeld ? LockColor : Dim, true);

            // The hull the volley will go at: the locked one near the line, else the hostile nearest it.
            BattleUnit target = null;
            double best = Math.Cos(Battle.VolleyConeDeg * Math.PI / 180);
            foreach (var o in battle.Units)
            {
                if (!battle.Hostile(me, o)) continue;
                var to = o.Pos - me.Pos;
                if (to.LengthSquared < 1e-12) continue;
                double cos = DVec3.Dot(look, to.Normalized);
                if (o == me.PlayerLockTarget && cos > Math.Cos(Battle.VolleyConeDeg * Math.PI / 180)) { target = o; break; }
                if (cos > best) { best = cos; target = o; }
            }
            if (target != null && app.ToGui(app.BattleScene(battle, target.Pos), out var tp, out _))
                DrawRing(tp, 14 + 6 * built, me.PlayerFireHeld ? LockColor : HostileColor);
            DrawCaptain(battle, me, x, y);
        }

        /// <summary>
        /// The captain's orders at a capital ship's helm (U.SPC-T425), above the gauges: the order of the guns bearing where the
        /// player is looking (U.SPC-T533), whether the gunners focus a weakened shield patch
        /// (U.SPC-T501), the brace, damage control and the line's formation, the turning circle at this speed, and a hostile
        /// broadside building on this hull, which is the moment to brace.
        /// </summary>
        void DrawCaptain(Battle battle, BattleUnit me, float x, float y)
        {
            var cmd = me.Broadside;
            if (cmd == null) return;
            var (order, mounts) = Broadsides.OrderAlong(me, app.PilotLookDirection);
            string battery = mounts == 0 ? "none bear here" : order == null ? "mixed" : order == BatteryOrder.HoldFire ? "HOLD" : order == BatteryOrder.Volley ? "VOLLEY" : "AT WILL";
            string brace = cmd.Braced ? "BRACED" : cmd.BraceCooldown > 0 ? "brace in " + Mathf.CeilToInt((float)cmd.BraceCooldown) + "s" : "3 brace";
            string orders = "1 BATTERY " + battery + (mounts > 0 ? " (" + mounts + ")" : "")
                + "    2 GUNNERS " + (cmd.SpreadFire ? "spread" : "focus")
                + "    " + brace
                + "    4 DAMAGE CONTROL " + Broadsides.Label(cmd.Focus) + " (" + Broadsides.Teams(me) + " teams)"
                + (EnergyWeapons.HasPhasers(me.Combat) ? "    INS PHASERS " + EnergyWeapons.Label(me.Combat.PhaserTuning) : "")
                + HeatText(me);
            DrawLabel(orders, new Vector2(x, y - 62), cmd.Braced ? LockColor : Dim, true);
            double turn = me.Ship != null ? PilotTuning.Of(me.Ship.Spec.Class).TurnRateDegS : me.Combat.Spec.TurnRateDegS;
            double about = Broadsides.ComeAboutSeconds(turn);
            // At the helm a capital turns by its own model's steerage at the speed it is making (U.SPC-T499).
            if (me.Ship != null && FlightModelRegistry.Default.For(me.Ship.Spec.Class) is CapitalFlightModel capital)
            {
                var hull = HullDynamics.Of(me.Combat.Spec);
                var tuning = PilotTuning.Of(me.Ship.Spec.Class);
                turn = capital.TurnRateAt(me.Vel.Length, hull, tuning);
                about = capital.ComeAboutSecondsAt(me.Vel.Length, hull, tuning);
            }
            double radius = Broadsides.TurningRadiusKm(me.Vel.Length, turn);
            string line = "5-9 LINE " + Broadsides.Label(cmd.Formation) + "  ·  0 all at will"
                + "    TURN CIRCLE " + (double.IsInfinity(radius) ? "-" : RangeText(radius)) + "  ·  about in " + (double.IsInfinity(about) ? "-" : Mathf.RoundToInt((float)about) + "s");
            DrawLabel(line, new Vector2(x, y - 44), Dim, true);
            DrawPatches(battle, me, cmd, x, y - 80);

            // The enemy's broadside building on us: the Black Flag tell.
            var incoming = Broadsides.IncomingBroadside(battle, me);
            if (incoming == null)
                foreach (var o in battle.Units)
                    if (battle.Hostile(me, o) && o.Broadside != null && o.Broadside.Readying && o.Broadside.ReadyingOn == me) { incoming = o; break; }
            if (incoming != null)
            {
                var r = incoming.Broadside;
                int loaded = r != null ? Mathf.RoundToInt((float)r.ReadyingLoaded * 100) : 100;
                DrawLabel("INCOMING BROADSIDE  ·  " + incoming.Name + " VOLLEY " + loaded + "%  ·  3 BRACE",
                    new Vector2(Screen.width * 0.5f - 160, Screen.height * 0.28f), HostileColor, false);
            }
        }

        /// <summary>
        /// The hull's shield patches at the helm (U.SPC-T502, see <see cref="WeakPatches"/>): which are weak (holed, or their
        /// field how far down), which one faces the heaviest fire as the hull lies now, and the order to roll them away.
        /// Red while a weak patch is the one taking the fire.
        /// </summary>
        void DrawPatches(Battle battle, BattleUnit me, BroadsideCommand cmd, float x, float y)
        {
            var patches = WeakPatches.Readout(battle, me);
            if (patches.Count == 0) return;
            var weak = new List<string>();
            HullPatch? under = null;
            foreach (var p in patches)
            {
                if (p.Weak) weak.Add(p.Name + (p.Holed ? " HOLED" : " " + Mathf.RoundToInt((float)p.Field * 100) + "%"));
                if (p.Fire > 0 && (under == null || p.Fire > under.Value.Fire)) under = p;
            }
            string text = "PATCHES " + (weak.Count == 0 ? "all holding" : "WEAK: " + string.Join(", ", weak))
                + (under != null ? "  ·  UNDER FIRE: " + under.Value.Name + (under.Value.Weak ? " (weak)" : "") : "")
                + "    SHIFT+DEL roll them away: " + (cmd.RollWeakPatches ? "ON" : "off");
            bool danger = under != null && under.Value.Weak;
            DrawLabel(text, new Vector2(x, y), danger ? HostileColor : weak.Count > 0 ? LockColor : Dim, true);
        }

        /// <summary>A big ship's heat at the helm (U.SPC-T491): how full its sinks are, its radiators standing, and whether
        /// the crew is throttling for it or running hot. Empty for a hull with none to manage.</summary>
        static string HeatText(BattleUnit me)
        {
            if (!Radiators.Carries(me.Combat)) return "";
            int standing = 0, panels = 0;
            foreach (var s in me.Combat.Subsystems)
                if (s.Kind == SubsystemKind.Radiator)
                {
                    panels++;
                    if (!s.Destroyed) standing++;
                }
            double throttle = Radiators.Throttle(me);
            string state = Radiators.RunsHot(me) ? (me.HullHeat > 1 ? "RUNNING HOT, COOKING" : "running hot")
                : throttle < 0.999 ? "throttled " + Mathf.RoundToInt((float)throttle * 100) + "%" : "SHIFT+INS run hot";
            return "    HEAT " + Mathf.RoundToInt((float)me.HullHeat * 100) + "% (radiators " + standing + "/" + panels + ", " + state + ")";
        }

        /// <summary>A mount's rounds left and its key, or that it is still cycling.</summary>
        static void AddMount(List<string> line, ShipCombat c, SubsystemKind kind, string name, string key)
        {
            int ammo = 0;
            bool ready = false;
            foreach (var s in c.Subsystems)
            {
                if (s.Kind != kind || s.Destroyed) continue;
                ammo += s.Ammo;
                ready |= s.Ammo > 0 && s.Cooldown <= 0;
            }
            if (ammo > 0) line.Add(name + " " + ammo + (ready ? " (" + key + ")" : " (cycling)"));
        }

        /// <summary>The fastest round the flown hull's own guns fire, km/s; 0 if they are all beams.</summary>
        static double RoundSpeed(ShipCombat c)
        {
            double speed = 0;
            foreach (var s in c.Subsystems)
            {
                var w = s.Def.Weapon;
                // The guns the player lays down the nose: those whose fire cones take in dead ahead (U.SPC-T533).
                if (s.Destroyed || w == null || w.Missile || w.AntiSmall || !s.Def.Covers(c.Spec, FireCones.Bow)) continue;
                speed = Math.Max(speed, w.MuzzleSpeedKmS);
            }
            return speed;
        }

        /// <summary>The hostile hull nearest the crosshair, within 20 degrees of it.</summary>
        BattleUnit NearestHostileToAim(Battle battle, BattleUnit me)
        {
            BattleUnit best = null;
            double bestCos = Math.Cos(20 * Math.PI / 180);
            var aim = app.PilotAimDirection;
            foreach (var o in battle.Units)
            {
                if (o == me || !battle.Hostile(me, o)) continue;
                var to = o.Pos - me.Pos;
                if (to.LengthSquared < 1e-12) continue;
                double cos = DVec3.Dot(aim, to.Normalized);
                if (cos > bestCos)
                {
                    bestCos = cos;
                    best = o;
                }
            }
            return best;
        }

        void DrawGauge(Rect r, float fraction, Color color, string caption)
        {
            fraction = Mathf.Clamp01(fraction);
            var old = GUI.color;
            GUI.color = new Color(0, 0, 0, 0.5f);
            GUI.DrawTexture(r, Icons.White);
            GUI.color = color;
            GUI.DrawTexture(new Rect(r.x, r.y, r.width * fraction, r.height), Icons.White);
            GUI.color = old;
            DrawLabel(caption, new Vector2(r.x, r.y - 17), color, true);
        }

        /// <summary>A small open ring of eight dots.</summary>
        void DrawRing(Vector2 p, float radius, Color color)
        {
            var old = GUI.color;
            GUI.color = color;
            for (int i = 0; i < 8; i++)
            {
                float a = i * Mathf.PI / 4;
                GUI.DrawTexture(new Rect(p.x + Mathf.Cos(a) * radius - 1.5f, p.y + Mathf.Sin(a) * radius - 1.5f, 3, 3), Icons.White);
            }
            GUI.color = old;
        }

        static string RangeText(double km) => km < 10 ? (km * 1000).ToString("N0", CultureInfo.InvariantCulture) + " m" : km.ToString("0.0", CultureInfo.InvariantCulture) + " km";

        // ---------------------------------------------------------------- hyperlane rides (U.SPC-T41)

        static readonly Color StreakColor = new Color(0.6f, 0.88f, 1f);
        const int StreakCount = 72;

        /// <summary>
        /// Riding a lane at the helm: streaks of light stream out past the view from the point ahead, longer
        /// and faster with the ride's speed (fading for a moment after coming off), and a ring gate gone
        /// through washes the screen in a flash.
        /// </summary>
        void DrawLaneEffects()
        {
            if (Event.current.type != EventType.Repaint || !app.IsPiloting || app.Travel.Active) return;
            float strength = app.LaneStreaks;
            if (strength > 0.01f)
            {
                var centre = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
                if (app.RidingLane && DirectionToGui(app.PilotForward, out var ahead)) centre = ahead;
                DrawStreaks(centre, strength, StreakCount, StreakColor, 0, 170f);
            }
            // Through a gate at the helm (U.SPC-T414): the gate's violet glow and its streaks held over the far side
            // while it streams in, space folding shut and open again with it (the view narrows, see HelmGateFold).
            float glow = app.HelmGateGlow;
            if (glow > 0.001f)
            {
                DrawGateLight(glow, GateGlowColor, 0.55f);
                DrawStreaks(new Vector2(Screen.width * 0.5f, Screen.height * 0.5f), glow, WarpLineCount, WarpColor, 500, 320f);
            }
            if (app.GateFlash > 0.001f)
            {
                var old = GUI.color;
                GUI.color = new Color(0.78f, 0.93f, 1f, app.GateFlash * app.GateFlash * 0.85f);
                GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Icons.White);
                GUI.color = old;
            }
        }

        static readonly Color GateGlowColor = new Color(0.4f, 0.28f, 0.85f);
        static readonly Color GateFlashColor = new Color(0.78f, 0.93f, 1f);

        /// <summary>Going through a jump gate between the stars: the screen tinted the gate's violet inside it, or washed
        /// in a flash at each ring (<paramref name="amount"/> 0..1, up to <paramref name="opacity"/>).</summary>
        static void DrawGateLight(float amount, Color color, float opacity)
        {
            if (amount < 0.001f) return;
            var old = GUI.color;
            GUI.color = new Color(color.r, color.g, color.b, amount * opacity);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Icons.White);
            GUI.color = old;
        }

        static float Frac(float x) => x - Mathf.Floor(x);

        /// <summary>Streaks of light streaming out from <paramref name="centre"/>, longer and faster with
        /// <paramref name="strength"/>; <paramref name="seed"/> gives a set its own pattern.</summary>
        void DrawStreaks(Vector2 centre, float strength, int count, Color tint, int seed, float maxLength)
        {
            float reach = new Vector2(Screen.width, Screen.height).magnitude * 0.6f;
            float t = Time.unscaledTime;
            for (int i = 0; i < count; i++)
            {
                // Fixed per streak: its bearing, pace and starting point (a cheap hash of its index).
                int k = i + seed;
                float h1 = Frac(Mathf.Sin(k * 12.9898f) * 43758.55f);
                float h2 = Frac(Mathf.Sin(k * 78.233f) * 12345.67f);
                float h3 = Frac(Mathf.Sin(k * 37.719f) * 24634.63f);
                float angle = h1 * Mathf.PI * 2f;
                float phase = Frac(t * (0.35f + 1.6f * strength) * (0.6f + h2) + h3);
                var dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                float r = 30f + phase * phase * reach;
                float length = (8f + maxLength * strength) * (0.3f + phase);
                var color = new Color(tint.r, tint.g, tint.b, strength * phase * 0.75f);
                DrawLine(centre + dir * r, centre + dir * (r + length), color, 1f + 1.5f * phase);
            }
        }

        // ---------------------------------------------------------------- between the stars

        static readonly Color WarpColor = new Color(0.82f, 0.9f, 1f);
        static readonly Color DustColor = new Color(0.88f, 0.84f, 0.78f);
        const int WarpLineCount = 110, DustCount = 240;
        Vector2 warpCentre;

        /// <summary>
        /// Travelling between stars: warp lines stream out from the star ahead, long and fast at the height of the
        /// crossing, and stellar dust drifts past, slow near the centre and quickening toward the edges of the view.
        /// </summary>
        void DrawWarp()
        {
            var travel = app.Travel;
            float warp = travel.Warp;
            var screenCentre = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            if (warp < 0.01f) { warpCentre = screenCentre; return; }
            // The lines pour from the star ahead once the view has turned to it, gliding there rather than jumping.
            var target = travel.Stage == StarTravel.Leg.Cross && DirectionToGui(travel.Heading, out var ahead) ? ahead : screenCentre;
            warpCentre = Vector2.Lerp(warpCentre, target, 1f - Mathf.Exp(-5f * Time.unscaledDeltaTime));
            var centre = warpCentre;

            float reach = new Vector2(Screen.width, Screen.height).magnitude * 0.7f;
            float t = Time.unscaledTime;
            var old = GUI.color;
            for (int i = 0; i < DustCount; i++)
            {
                int k = i + 1000;
                float h1 = Frac(Mathf.Sin(k * 12.9898f) * 43758.55f);
                float h2 = Frac(Mathf.Sin(k * 78.233f) * 12345.67f);
                float h3 = Frac(Mathf.Sin(k * 37.719f) * 24634.63f);
                float angle = h1 * Mathf.PI * 2f;
                float phase = Frac(t * (0.06f + 0.45f * warp) * (0.5f + h2) + h3);
                // Cubed, so a mote creeps out of the distance and rushes past at the edge, as if coming nearer.
                float r = 6f + phase * phase * phase * reach;
                float size = 1f + 2.2f * phase;
                GUI.color = new Color(DustColor.r, DustColor.g, DustColor.b, Mathf.Clamp01(warp * 1.6f) * Mathf.Min(1f, phase * 4f) * (0.2f + 0.45f * h3));
                var p = centre + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * r;
                GUI.DrawTexture(new Rect(p.x - size * 0.5f, p.y - size * 0.5f, size, size), Icons.White);
            }
            GUI.color = old;
            DrawStreaks(centre, warp, WarpLineCount, WarpColor, 500, 320f);
        }

        /// <summary>Above the status bar while riding a lane: the lane, how far along, the speed, the time to the off-ramp and its exits.</summary>
        void DrawLaneRide()
        {
            if (!app.RidingLane) return;
            var ship = app.Piloted.Ship;
            var hop = ship.LaneLeg;
            double progress = Fleet.LaneProgress(ship, app.Clock.Jd);
            var lanes = app.Fleet.Lanes;
            // The ring chain flown through (U.SPC-T379): how many rings are behind the hull, and whether they are pushing it on or braking it.
            int rings = app.RideRingCount(out int passed, out bool accelerating);
            string head = (hop.Lane.IsGateSpur ? "GATE SPUR  ·  " : "HYPERLANE  ·  ") + hop.Lane.Name.ToUpperInvariant() + "  ·  bound for " + lanes.EndName(hop.Lane, hop.To) +
                          "  ·  " + Percent(progress) + "  ·  " + SpeedText(app.PilotSpeedKmS) +
                          "  ·  ring " + passed + "/" + rings + (accelerating ? " accelerating" : " braking") +
                          "  ·  off-ramp in " + Mathf.CeilToInt((float)app.RideSecondsLeft) + " s";
            var exits = new List<string> { (app.RideChoice < 0 ? "> " : "") + "1 come off at " + lanes.EndName(hop.Lane, hop.To) };
            for (int i = 0; i < app.RideOnward.Count && i < 8; i++)
            {
                var next = app.RideOnward[i];
                exits.Add((app.RideChoice == i ? "> " : "") + (i + 2) + " on to " + lanes.EndName(next.Lane, next.To));
            }
            string tail = "AT THE OFF-RAMP:  " + string.Join("    ", exits) + "    (P hands the ride to the crew)";
            var a = new GUIContent(head);
            var b = new GUIContent(tail);
            float w = Mathf.Max(text.CalcSize(a).x, small.CalcSize(b).x) + 28;
            var r = Panel(new Rect((Screen.width - w) * 0.5f, Screen.height - 128, w, 50));
            GUI.contentColor = StreakColor;
            GUI.Label(new Rect(r.x + 14, r.y + 5, w, 20), a, text);
            GUI.contentColor = Dim;
            GUI.Label(new Rect(r.x + 14, r.y + 27, w, 18), b, small);
            GUI.contentColor = Color.white;
        }

        static readonly Color InterdictColor = new Color(1f, 0.45f, 0.35f);

        /// <summary>
        /// A lane interdiction at the helm (U.SPC-T42): the field picked up ahead with its countdown and the answers to it
        /// (surge through, drop out), the drives knocked out after a pulse with the seconds until they come back, and, out
        /// of the lane near where the hull left it, the key to get back in.
        /// </summary>
        void DrawInterdiction()
        {
            if (!app.IsPiloting) return;
            string head = null, tail = null;
            if (app.RidingLane && app.RideThreat is LaneThreat t)
            {
                var ship = app.Piloted.Ship;
                head = "INTERDICTION FIELD AHEAD  ·  " + Mathf.RoundToInt((float)(t.DistanceKm / 1000)).ToString("N0", CultureInfo.InvariantCulture) + " thousand km  ·  pulse in " +
                       Mathf.CeilToInt((float)app.ThreatRealSeconds) + " s" + (ship.LaneSurged ? "  ·  SURGING" : "");
                tail = (t.Braced ? "a raider's hull made out lying dark: braced, the drives should hold    " : "") +
                       (ship.LaneSurged ? "" : "Shift surge through (" + Fleet.SurgeFactor.ToString("0.#", CultureInfo.InvariantCulture) + "x pace)    ") +
                       "Ctrl drop out here    or ride on into it";
            }
            else if (app.PilotStunSeconds > 0)
            {
                head = "EMP  ·  DRIVES AND SHIELDS DOWN  ·  " + Mathf.CeilToInt((float)app.PilotStunSeconds) + " s";
                tail = "knocked out by an electromagnetic pulse: drifting, no thrust, no turning, no guns until the systems come back";
            }
            else if (app.CanRejoinLane)
            {
                head = "BESIDE THE " + app.Piloted.Ship.LaneLeg.Lane.Name.ToUpperInvariant() + " LANE";
                var leg = app.Piloted.Ship.LaneLeg;
                tail = "Y  back into the lane where you left it, on to " + app.Fleet.Lanes.EndName(leg.Lane, leg.To);
            }
            if (head == null) return;
            var a = new GUIContent(head);
            var b = new GUIContent(tail);
            float w = Mathf.Max(text.CalcSize(a).x, small.CalcSize(b).x) + 28;
            var r = Panel(new Rect((Screen.width - w) * 0.5f, Screen.height - 184, w, 50));
            GUI.contentColor = app.CanRejoinLane && !app.RidingLane && app.PilotStunSeconds <= 0 ? StreakColor : InterdictColor;
            GUI.Label(new Rect(r.x + 14, r.y + 5, w, 20), a, text);
            GUI.contentColor = Dim;
            GUI.Label(new Rect(r.x + 14, r.y + 27, w, 18), b, small);
            GUI.contentColor = Color.white;
        }

        /// <summary>Where a direction (ecliptic, unit) vanishes on screen, if it is in front of the camera and on screen.</summary>
        bool DirectionToGui(DVec3 direction, out Vector2 p)
        {
            var d = direction.SwapYZ;
            if (!app.RelativeToGui(new Vector3((float)d.X, (float)d.Y, (float)d.Z) * 1e6f, out p)) return false;
            return p.x >= 0 && p.y >= 0 && p.x <= Screen.width && p.y <= Screen.height;
        }

        /// <summary>An engine telegraph step as the helm calls it (U.SPC-T499).</summary>
        static string TelegraphText(EngineTelegraph t) =>
            t == EngineTelegraph.FullAstern ? "FULL ASTERN"
            : t == EngineTelegraph.Stop ? "STOP"
            : t == EngineTelegraph.Slow ? "SLOW AHEAD"
            : t == EngineTelegraph.Half ? "HALF AHEAD"
            : t == EngineTelegraph.Full ? "FULL AHEAD"
            : "FLANK";

        /// <summary>The flight keys for the way the flown hull is steered (U.SPC-T269).</summary>
        static string ControlHint(PilotControl control) =>
            control == PilotControl.FreeAim ? "WASD throttle/strafe, mouse aims, the hull turns after it"
            : control == PilotControl.Helm ? "W/S ring the engine telegraph, A/D Q/E turn the hull, Z/X roll, mouse looks around, 1-9 0 captain's orders"
            : "WASD throttle/strafe, mouse steers the nose";

        /// <summary>
        /// The band of the screen the HUD's corner panels keep to (widescreen support, U.SPC-T289): the whole width up to
        /// 21:9, and on anything wider (32:9 at 5120 x 1440) a 21:9 band in the middle, so the readouts stay where the eye
        /// is rather than out at the far edges of the screen. The view itself still fills the whole width.
        /// </summary>
        public static float SafeLeft => (Screen.width - SafeWidth) * 0.5f;
        public static float SafeRight => SafeLeft + SafeWidth;
        static float SafeWidth => Mathf.Min(Screen.width, Screen.height * MaxHudAspect);

        /// <summary>The widest aspect the HUD spreads across.</summary>
        const float MaxHudAspect = 21f / 9f;

        static readonly Color CrosshairColor = new Color(0.55f, 0.95f, 1f, 0.9f);

        /// <summary>The crosshair: four ticks about an open centre, so the target under it stays visible.</summary>
        void DrawCrosshair(Vector2 p, Color color)
        {
            var old = GUI.color;
            GUI.color = color;
            var w = Icons.White;
            GUI.DrawTexture(new Rect(p.x - 16, p.y - 1, 9, 2), w);
            GUI.DrawTexture(new Rect(p.x + 7, p.y - 1, 9, 2), w);
            GUI.DrawTexture(new Rect(p.x - 1, p.y - 16, 2, 9), w);
            GUI.DrawTexture(new Rect(p.x - 1, p.y + 7, 2, 9), w);
            GUI.color = old;
        }

        void DrawBoresight(Vector2 p, Color color)
        {
            var old = GUI.color;
            GUI.color = color;
            var w = Icons.White;
            GUI.DrawTexture(new Rect(p.x - 12, p.y - 0.75f, 8, 1.5f), w);
            GUI.DrawTexture(new Rect(p.x + 4, p.y - 0.75f, 8, 1.5f), w);
            GUI.DrawTexture(new Rect(p.x - 0.75f, p.y + 4, 1.5f, 6), w);
            GUI.DrawTexture(new Rect(p.x - 1, p.y - 1, 2, 2), w);
            GUI.color = old;
        }

        void DrawLine(Vector2 a, Vector2 b, Color color, float thickness)
        {
            var d = b - a;
            float length = d.magnitude;
            if (length < 1) return;
            var matrix = GUI.matrix;
            var old = GUI.color;
            GUIUtility.RotateAroundPivot(Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg, a);
            GUI.color = color;
            GUI.DrawTexture(new Rect(a.x, a.y - thickness * 0.5f, length, thickness), Icons.White);
            GUI.color = old;
            GUI.matrix = matrix;
        }

        void DrawTargetMarker(Target t, bool selected)
        {
            if (t.IsNone) return;
            var scene = app.TargetScene(t);
            // A lost contact is bracketed on its question mark, where the scanners last had it.
            if (t.IsShip && Picture?.Of(t.Ship) is Contact lost && lost.Cold && !lastSeenScene.TryGetValue(t.Ship, out scene)) return;
            double radius = app.TargetRadius(t);
            bool onScreen = app.ToGui(scene, out var p, out var depth) &&
                            p.x >= 0 && p.x <= Screen.width && p.y >= 0 && p.y <= Screen.height;
            if (!onScreen)
            {
                if (selected) DrawOffscreenPointer(scene, TargetName(t));
                return;
            }
            float px = (float)(radius / depth * app.PixelsPerUnitAtUnitDistance());
            float half = Mathf.Max(px * 1.18f, 13f);
            var color = selected ? Accent : new Color(1, 1, 1, 0.55f);
            DrawBrackets(new Rect(p.x - half, p.y - half, half * 2, half * 2), color, selected ? 2f : 1f);
        }

        // ---------------------------------------------------------------- opportunities (U.SPC-T44)

        static Color OpportunityColor(Opportunity op, float alpha = 1)
        {
            var c = op.MarkerColor;
            return new Color(c.R, c.G, c.B, alpha);
        }

        /// <summary>Every open adventure is bracketed where it is, in its kind's colour, with its tag and how far it is.</summary>
        void DrawOpportunityMarkers()
        {
            var director = app.World.Adventures;
            if (director == null || director.Active.Count == 0) return;
            var player = app.PlayerHelioKm();
            foreach (var op in director.Active)
            {
                if (!app.OpportunityScene(op, out var scene) || !app.ToGui(scene, out var p, out _)) continue;
                if (p.x < 0 || p.x > Screen.width || p.y < 0 || p.y > Screen.height) continue;
                var color = OpportunityColor(op, 0.95f);
                DrawBrackets(new Rect(p.x - 11, p.y - 11, 22, 22), color, 1.5f);
                DrawLabel(op.MarkerLabel + "  " + Km((op.HelioKm - player).Length), p + new Vector2(14, 6), color, true);
            }
        }

        /// <summary>The open adventures near the player, most urgent first: tag, title, distance, time left and the hook.</summary>
        void DrawOpportunityBoard()
        {
            var director = app.World.Adventures;
            if (director == null || director.Active.Count == 0) return;
            var list = new List<Opportunity>(director.Active);
            double jd = app.Clock.Jd;
            list.Sort((a, b) => a.ExpiresJd.CompareTo(b.ExpiresJd));
            int n = Mathf.Min(5, list.Count);
            float w = 380, rowH = 40;
            var r = Panel(new Rect(SafeRight - w - 10, 72, w, 30 + n * rowH));
            GUI.Label(new Rect(r.x + 12, r.y + 6, w - 24, 18), "OPPORTUNITIES" + (app.IsPiloting ? "" : "   (pilot a ship to answer)"), small);
            var player = app.PlayerHelioKm();
            var old = GUI.contentColor;
            opportunityHook ??= new GUIStyle(small) { wordWrap = true, clipping = TextClipping.Clip };
            for (int i = 0; i < n; i++)
            {
                var op = list[i];
                float y = r.y + 26 + i * rowH;
                GUI.contentColor = OpportunityColor(op);
                GUI.Label(new Rect(r.x + 12, y, w - 24, 16), op.MarkerLabel + "   " + op.Title + "   " + Km((op.HelioKm - player).Length) + "   " +
                    Duration(op.DaysLeft(jd)) + " left" + (op.Engaged ? "   · on scene" : ""), small);
                GUI.contentColor = Dim;
                GUI.Label(new Rect(r.x + 12, y + 15, w - 24, rowH - 16), op.Hook, opportunityHook);
            }
            GUI.contentColor = old;
        }

        GUIStyle opportunityHook;

        void DrawBrackets(Rect r, Color color, float thickness)
        {
            float len = Mathf.Clamp(r.width * 0.28f, 5f, 18f);
            var old = GUI.color;
            GUI.color = color;
            var w = Icons.White;
            GUI.DrawTexture(new Rect(r.xMin, r.yMin, len, thickness), w);
            GUI.DrawTexture(new Rect(r.xMin, r.yMin, thickness, len), w);
            GUI.DrawTexture(new Rect(r.xMax - len, r.yMin, len, thickness), w);
            GUI.DrawTexture(new Rect(r.xMax - thickness, r.yMin, thickness, len), w);
            GUI.DrawTexture(new Rect(r.xMin, r.yMax - thickness, len, thickness), w);
            GUI.DrawTexture(new Rect(r.xMin, r.yMax - len, thickness, len), w);
            GUI.DrawTexture(new Rect(r.xMax - len, r.yMax - thickness, len, thickness), w);
            GUI.DrawTexture(new Rect(r.xMax - thickness, r.yMax - len, thickness, len), w);
            GUI.color = old;
        }

        void DrawOffscreenPointer(DVec3 scene, string name)
        {
            var rel = app.Relative(scene);
            var local = Quaternion.Inverse(app.SpaceCamera.Rotation) * rel;
            var dir = new Vector2(local.x, -local.y);
            if (dir.sqrMagnitude < 1e-12f) return;
            dir.Normalize();
            var center = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            float margin = 34f;
            float sx = (center.x - margin) / Mathf.Max(Mathf.Abs(dir.x), 1e-4f);
            float sy = (center.y - margin) / Mathf.Max(Mathf.Abs(dir.y), 1e-4f);
            var p = center + dir * Mathf.Min(sx, sy);

            float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg + 90f;
            var matrix = GUI.matrix;
            GUIUtility.RotateAroundPivot(angle, p);
            var old = GUI.color;
            GUI.color = Accent;
            GUI.DrawTexture(new Rect(p.x - 9, p.y - 9, 18, 18), Icons.ForShip(ShipClass.Cruiser));
            GUI.color = old;
            GUI.matrix = matrix;
            DrawLabel(name, p + new Vector2(dir.x < 0 ? 14 : -14 - label.CalcSize(new GUIContent(name)).x, dir.y < 0 ? 10 : -26), Accent, false);
        }

        void DrawIcon(Texture2D icon, Vector2 p, float size, Color tint)
        {
            var old = GUI.color;
            GUI.color = new Color(0, 0, 0, tint.a * 0.6f);
            GUI.DrawTexture(new Rect(p.x - size * 0.5f + 1, p.y - size * 0.5f + 1, size, size), icon);
            GUI.color = tint;
            GUI.DrawTexture(new Rect(p.x - size * 0.5f, p.y - size * 0.5f, size, size), icon);
            GUI.color = old;
        }

        void DrawLabel(string s, Vector2 p, Color color, bool minor)
        {
            var style = minor ? minorLabel : label;
            var content = new GUIContent(s);
            var size = style.CalcSize(content);
            var r = new Rect(p.x, p.y, size.x, size.y);
            labelShadow.fontSize = style.fontSize;
            GUI.Label(new Rect(r.x + 1, r.y + 1, r.width, r.height), content, labelShadow);
            var old = GUI.contentColor;
            GUI.contentColor = minor ? Color.Lerp(color, Color.gray, 0.3f) : color;
            GUI.Label(r, content, style);
            GUI.contentColor = old;
        }

        static Color BodyTint(BodyDefinition b)
        {
            var c = new Color(b.Color.R, b.Color.G, b.Color.B);
            float max = Mathf.Max(c.r, Mathf.Max(c.g, c.b));
            c = max > 0 ? c / max : Color.white;
            return Color.Lerp(c, Color.white, 0.35f);
        }

        // ---------------------------------------------------------------- panels

        Rect Panel(Rect r)
        {
            GUI.Box(r, GUIContent.none, panel);
            if (Event.current.type == EventType.Layout || Event.current.type == EventType.Repaint) RegisterUi(r);
            return r;
        }

        void RegisterUi(Rect r)
        {
            if (!app.UiRects.Contains(r)) app.UiRects.Add(r);
        }

        void DrawTopBar()
        {
            float w = 640, h = 52;
            var r = Panel(new Rect((Screen.width - w) * 0.5f, 10, w, h));
            var clock = app.Clock;

            GUI.Label(new Rect(r.x + 14, r.y + 7, 220, 20), clock.Utc.ToString("yyyy-MM-dd  HH:mm", CultureInfo.InvariantCulture) + " UTC", title);
            string rate = clock.Paused ? "PAUSED"
                : clock.Pausing ? "PAUSING  x" + clock.Speed.ToString("0.00", CultureInfo.InvariantCulture)
                : clock.Resuming ? "RESUMING  x" + clock.Speed.ToString("0.00", CultureInfo.InvariantCulture) + "  of " + clock.Warp.Label
                : clock.Warp.Label;
            GUI.Label(new Rect(r.x + 14, r.y + 28, 220, 18), rate, small);

            float bx = r.x + 200;
            if (GUI.Button(new Rect(bx, r.y + 12, 34, 28), "<<", button)) clock.Slower();
            if (GUI.Button(new Rect(bx + 38, r.y + 12, 44, 28), clock.PauseHeld ? ">" : "||", clock.PauseHeld ? buttonActive : button)) app.TogglePause();
            if (GUI.Button(new Rect(bx + 86, r.y + 12, 34, 28), ">>", button)) app.TimeFaster();
            if (GUI.Button(new Rect(bx + 124, r.y + 12, 44, 28), "Now", button)) app.ResetToNow();

            float sx = r.x + 390;
            bool gameplay = app.ScaleGoal > 0.5;
            if (GUI.Button(new Rect(sx, r.y + 9, 236, 22), gameplay ? "GAMEPLAY SCALE   [G]" : "REALISTIC SCALE   [G]", buttonActive))
                app.ToggleScale();
            var bar = new Rect(sx, r.y + 36, 236, 6);
            GUI.DrawTexture(bar, buttonTexture);
            var old = GUI.color;
            GUI.color = Accent;
            float blend = (float)app.Scale.Blend;
            GUI.DrawTexture(new Rect(bar.x, bar.y, bar.width * blend, bar.height), Icons.White);
            GUI.color = old;
            GUI.Label(new Rect(sx - 2, r.y + 40, 120, 14), "real", small);
            var g = new GUIContent("gameplay");
            GUI.Label(new Rect(sx + 238 - small.CalcSize(g).x, r.y + 40, 120, 14), g, small);
        }

        void DrawNavigator()
        {
            float w = 268;
            float h = navigatorOpen ? Mathf.Min(Screen.height - 24, 640) : 34;
            var r = Panel(new Rect(SafeLeft + 10, 10, w, h));
            if (GUI.Button(new Rect(r.x + 8, r.y + 6, w - 16, 22), (navigatorOpen ? "[-]  " : "[+]  ") + "NAVIGATOR", title)) navigatorOpen = !navigatorOpen;
            if (!navigatorOpen) return;

            // Star systems: one button each; the active one is where the body tree below belongs.
            float sy = r.y + 34;
            var oldColor = GUI.contentColor;
            GUI.contentColor = Accent;
            GUI.Label(new Rect(r.x + 12, sy, w - 24, 16), "STAR SYSTEMS", small);
            GUI.contentColor = oldColor;
            // The whole neighbourhood at once: territory, traffic, fronts and raiders, as far as the news goes (U.SPC-T103).
            if (GUI.Button(new Rect(r.x + w - 126, sy - 2, 116, 18), "GALAXY MAP   [", button)) app.ShowGalaxyMap = true;
            sy += 17;
            var network = app.Galaxy.Network;
            double jd = app.Clock.Jd;
            foreach (var system in app.Galaxy.Systems)
            {
                bool here = system == app.ActiveSystem;
                bool going = app.Travel.Active && app.Travel.To == system;
                // How many gates away it is, by the quickest open way (a gate network, not all-pairs).
                var route = here ? null : network.Route(app.ActiveSystem, system, null, jd);
                string hops = route == null ? "" : "  " + route.Count + (route.Count == 1 ? " gate" : " gates");
                string details = here ? "◉ here" : going ? "travelling…" : Galaxy.DistanceLy(app.ActiveSystem, system).ToString("0.00", CultureInfo.InvariantCulture) + " ly" + hops + NewsAge(system, jd) + "  ›";
                // Rows tighten as the list of systems grows, so the body tree below keeps its room.
                float rowHeight = app.Galaxy.Systems.Count > 4 ? 18 : 22;
                var systemRow = new Rect(r.x + 10, sy, w - 20, rowHeight);
                if (GUI.Button(systemRow, GUIContent.none, here ? buttonActive : button) && !here) app.TravelTo(system);
                NameAndDetails(systemRow, system.Name, details, here ? buttonActive : button);
                sy += rowHeight + (app.Galaxy.Systems.Count > 4 ? 2 : 3);
                if (!here) continue;
                // Who holds it (U.SPC-T91): the polity owning its biggest settlement, which may hold other systems too.
                var holder = system.Holder;
                if (holder != null)
                {
                    var old = GUI.contentColor;
                    GUI.contentColor = new Color(holder.Color.R, holder.Color.G, holder.Color.B);
                    NameAndDetails(new Rect(r.x + 12, sy, w - 24, 15), "   Held by", holder.Name, small);
                    GUI.contentColor = old;
                    sy += 15;
                }
                // This system's own gates: where each leads, how long the crossing, and whether it is shut.
                foreach (var gate in system.Gates)
                {
                    bool shut = gate.Blockaded(jd) || gate.Partner.Blockaded(jd);
                    bool down = !gate.Link.Working;
                    string line = gate.Link.Ly.ToString("0.00", CultureInfo.InvariantCulture) + " ly, " +
                                  (gate.Link.TransitDays * 24).ToString("0", CultureInfo.InvariantCulture) + " h" + (down ? "   DOWN" : shut ? "   SHUT" : "") +
                                  (gate.Integrity < 1 ? "  " + (gate.Integrity * 100).ToString("0", CultureInfo.InvariantCulture) + "% whole" : "");
                    var old = GUI.contentColor;
                    GUI.contentColor = down || shut ? new Color(1f, 0.5f, 0.42f) : new Color(0.7f, 0.82f, 0.95f);
                    NameAndDetails(new Rect(r.x + 12, sy, w - 24, 15), "   " + gate.Name, line, small);
                    GUI.contentColor = old;
                    sy += 15;
                }
                // Gates being surveyed, built or rebuilt here (U.SPC-T97), and how far along.
                if (app.Galaxy.Construction != null)
                    foreach (var p in app.Galaxy.Construction.Active)
                    {
                        if (p.From != system) continue;
                        string what = p.Work == GateJob.Repair ? "rebuilding the " + p.Gate.Name
                            : p.Stage == GateStage.Survey ? "surveying for a gate to " + p.TargetName
                            : "building a gate to " + p.TargetName;
                        string line = what +
                                      (p.Stage == GateStage.Building ? "  " + (p.Progress * 100).ToString("0", CultureInfo.InvariantCulture) + "%" : "");
                        var old = GUI.contentColor;
                        GUI.contentColor = new Color(0.85f, 0.8f, 0.55f);
                        NameAndDetails(new Rect(r.x + 12, sy, w - 24, 15), "   " + p.Builder.Name, line, small);
                        GUI.contentColor = old;
                        sy += 15;
                    }
            }
            int crossing = app.Galaxy.Traffic.InGates.Count;
            if (crossing > 0)
            {
                NameAndDetails(new Rect(r.x + 12, sy, w - 24, 15), "   " + crossing + (crossing == 1 ? " ship" : " ships"), "in the gates", small);
                sy += 15;
            }
            // Groups out in open space under their own drives (U.SPC-T412).
            int cruising = app.Galaxy.DeepSpace.Cruises.Count;
            if (cruising > 0)
            {
                NameAndDetails(new Rect(r.x + 12, sy, w - 24, 15), "   " + cruising + (cruising == 1 ? " group" : " groups"), "between the stars", small);
                sy += 15;
            }
            float treeTop = sy + 4 - r.y;

            var catalog = app.Catalog;
            var rows = new List<(string name, string details, Target target, int indent, Color color)>();
            var war = app.World.War;
            if (war.Battles.Count > 0)
            {
                rows.Add(("BATTLES", "", Target.None, 0, new Color(1f, 0.5f, 0.4f)));
                foreach (var b in war.Battles)
                {
                    var flag = Flagship(b);
                    rows.Add((b.Name, BattleScore(b), flag != null ? Target.OfShip(flag) : Target.OfBody(b.Body), 1, new Color(1f, 0.72f, 0.6f)));
                }
                rows.Add(("", "", Target.None, 0, Color.clear));
            }
            AddBody(rows, 0, 0);
            foreach (var faction in app.Fleet.Factions)
            {
                rows.Add(("", "", Target.None, 0, Color.clear));
                var fc = new Color(faction.Color.R, faction.Color.G, faction.Color.B);
                rows.Add((faction.Name.ToUpperInvariant(), Credits(faction.Treasury), Target.None, 0, fc));
                string polity = PolityText(faction);
                int cut = polity.IndexOf(",  ", StringComparison.Ordinal);
                rows.Add((cut < 0 ? polity : polity.Substring(0, cut), cut < 0 ? "" : polity.Substring(cut + 3), Target.None, 1, Color.Lerp(fc, Color.gray, 0.35f)));
                foreach (var ship in app.Fleet.Ships)
                {
                    if (ship.Faction != faction || ship.IsEscort || ship.Destroyed) continue;
                    rows.Add((ship.Name, ShortStatus(ship), Target.OfShip(ship), 1, fc));
                    foreach (var escort in app.Fleet.Ships)
                        if (escort.Leader == ship && !escort.Destroyed) rows.Add((escort.Name, "· " + escort.Spec.DisplayName, Target.OfShip(escort), 2, Color.Lerp(fc, Color.gray, 0.35f)));
                }
            }
            // The trading companies that own the merchant fleet, in their liveries.
            var companies = app.World.Companies;
            if (companies != null && companies.Enabled)
            {
                rows.Add(("", "", Target.None, 0, Color.clear));
                rows.Add(("TRADING COMPANIES", "", Target.None, 0, new Color(0.9f, 0.86f, 0.7f)));
                foreach (var co in companies.All)
                {
                    var lc = new Color(co.Livery.R, co.Livery.G, co.Livery.B);
                    if (!co.Trading) lc = Color.Lerp(lc, Color.gray, 0.6f);
                    rows.Add((co.Name, CompanyText(co).Substring(co.Name.Length).TrimStart(), Target.None, 1, lc));
                }
            }
            // The player's own fleet: its house flag is no faction of the system's, but its ships are no visitors.
            var house = app.World.Holdings?.House;
            bool houseHeader = false;
            if (house != null)
                foreach (var ship in app.Fleet.Ships)
                {
                    if (ship.IsEscort || ship.Destroyed || ship.Faction != house) continue;
                    if (!houseHeader)
                    {
                        rows.Add(("", "", Target.None, 0, Color.clear));
                        rows.Add(("YOUR FLEET", "", Target.None, 0, new Color(0.85f, 0.95f, 0.7f)));
                        houseHeader = true;
                    }
                    var hc = new Color(house.Color.R, house.Color.G, house.Color.B);
                    rows.Add((ship.Name, ShortStatus(ship), Target.OfShip(ship), 1, hc));
                }
            // Ships from other systems, come through the gates (their factions belong to their own worlds).
            bool visitorsHeader = false;
            var local = new HashSet<Faction>(app.Fleet.Factions);
            if (house != null) local.Add(house);
            foreach (var ship in app.Fleet.Ships)
            {
                if (ship.IsEscort || ship.Destroyed || local.Contains(ship.Faction)) continue;
                if (!visitorsHeader)
                {
                    rows.Add(("", "", Target.None, 0, Color.clear));
                    rows.Add(("FROM BEYOND THE GATES", "", Target.None, 0, new Color(0.7f, 0.82f, 0.95f)));
                    visitorsHeader = true;
                }
                var fc = new Color(ship.Faction.Color.R, ship.Faction.Color.G, ship.Faction.Color.B);
                string from = ship.Voyage != null ? "· " + ship.Voyage.Home.Name + "  " : "";
                rows.Add((ship.Name, from + ShortStatus(ship), Target.OfShip(ship), 1, fc));
            }

            // Docking rings and what is in orbit, after the bodies.
            var infra = app.World.Infrastructure;
            if (infra != null && infra.Rings.Count > 0)
            {
                rows.Add(("", "", Target.None, 0, Color.clear));
                rows.Add(("DOCKING RINGS", "", Target.None, 0, new Color(0.6f, 0.9f, 1f)));
                foreach (var ring in infra.Rings)
                {
                    int queued = 0;
                    foreach (var s in app.Fleet.Ships)
                        if (!s.Destroyed && s.Phase == ShipPhase.Queued && s.Ring == ring) queued++;
                    rows.Add((ring.Name, "· " + catalog[ring.Body].Name + (queued > 0 ? "  · " + queued + " queued" : ""), Target.OfRing(ring), 1, new Color(0.6f, 0.9f, 1f)));
                }
            }
            if (infra != null && infra.Structures.Count > 0)
            {
                rows.Add(("", "", Target.None, 0, Color.clear));
                rows.Add(("IN ORBIT", "", Target.None, 0, new Color(0.85f, 0.88f, 0.92f)));
                foreach (var s in infra.Structures)
                {
                    var c = s.Owner != null ? new Color(s.Owner.Color.R, s.Owner.Color.G, s.Owner.Color.B) : new Color(0.85f, 0.88f, 0.92f);
                    rows.Add((s.Name, "· " + catalog[s.Body].Name, Target.OfStructure(s), 1, c));
                }
            }

            const float rowH = 19;
            var view = new Rect(r.x + 4, r.y + treeTop, w - 8, h - treeTop - 6);
            var content = new Rect(0, 0, w - 26, rows.Count * rowH);
            navigatorScroll = GUI.BeginScrollView(view, navigatorScroll, content);
            for (int i = 0; i < rows.Count; i++)
            {
                var (s, details, target, indent, color) = rows[i];
                var rr = new Rect(0, i * rowH, content.width, rowH);
                if (target.IsNone)
                {
                    if (s.Length == 0) continue;
                    var oldC = GUI.contentColor;
                    GUI.contentColor = color;
                    NameAndDetails(new Rect(rr.x + 6, rr.y + 2, rr.width - 6, rr.height - 2), s, details, small);
                    GUI.contentColor = oldC;
                    continue;
                }
                bool selected = app.Selected == target;
                var old = GUI.contentColor;
                GUI.contentColor = selected ? Color.white : color;
                var rowRect = new Rect(rr.x + 4 + indent * 14, rr.y, rr.width - indent * 14 - 4, rr.height);
                if (GUI.Button(rowRect, GUIContent.none, selected ? rowSelected : row))
                {
                    bool doubleClick = lastRowTarget == target && Time.unscaledTime - lastRowClick < 0.4f;
                    app.Select(target);
                    if (doubleClick && !app.IsPiloting) app.SpaceCamera.FocusOn(target);
                    lastRowClick = Time.unscaledTime;
                    lastRowTarget = target;
                }
                NameAndDetails(rowRect, s, details, selected ? rowSelected : row);
                GUI.contentColor = old;
            }
            GUI.EndScrollView();
        }

        readonly Dictionary<GUIStyle, GUIStyle> textOnly = new Dictionary<GUIStyle, GUIStyle>();

        /// <summary><paramref name="style"/>'s text alone, left-aligned and unclipped, to draw over a row it has already drawn.</summary>
        GUIStyle TextOnly(GUIStyle style)
        {
            if (textOnly.TryGetValue(style, out var t)) return t;
            var colour = style.normal.textColor;
            t = new GUIStyle(style)
            {
                normal = { background = null, textColor = colour },
                hover = { background = null, textColor = colour },
                active = { background = null, textColor = colour },
                focused = { background = null, textColor = colour },
                padding = new RectOffset(0, 0, style.padding.top, style.padding.bottom),
                margin = new RectOffset(),
                wordWrap = false,
                clipping = TextClipping.Overflow,
                alignment = style.alignment == TextAnchor.MiddleCenter || style.alignment == TextAnchor.MiddleRight ? TextAnchor.MiddleLeft
                    : style.alignment == TextAnchor.UpperCenter || style.alignment == TextAnchor.UpperRight ? TextAnchor.UpperLeft
                    : style.alignment,
            };
            textOnly[style] = t;
            return t;
        }

        const float MarqueeSpeed = 30, MarqueeHold = 1.5f, MarqueeGap = 40;

        /// <summary>
        /// One navigator line: <paramref name="name"/> stays put at the left, and <paramref name="details"/> (how far,
        /// how many gates, how old the news, what a ship is doing...) follow it; when they do not fit in what is left
        /// of the line they scroll past to the right of the name as a marquee, holding still at the start of each pass.
        /// </summary>
        void NameAndDetails(Rect r, string name, string details, GUIStyle style)
        {
            var t = TextOnly(style);
            float left = r.x + style.padding.left, right = r.xMax - Mathf.Max(style.padding.right, 2);
            float nameW = Mathf.Min(t.CalcSize(new GUIContent(name)).x, right - left);
            GUI.BeginGroup(new Rect(left, r.y, nameW, r.height));
            GUI.Label(new Rect(0, 0, nameW + 40, r.height), name, t);
            GUI.EndGroup();
            if (string.IsNullOrEmpty(details)) return;
            var area = new Rect(left + nameW + 8, r.y, right - left - nameW - 8, r.height);
            if (area.width < 12) return;
            var content = new GUIContent(details);
            float dw = t.CalcSize(content).x;
            float offset = 0, loop = dw + MarqueeGap;
            if (dw > area.width)
            {
                float phase = Time.unscaledTime % (MarqueeHold + loop / MarqueeSpeed);
                offset = phase < MarqueeHold ? 0 : (phase - MarqueeHold) * MarqueeSpeed;
            }
            GUI.BeginGroup(area);
            GUI.Label(new Rect(-offset, 0, dw + 2, r.height), content, t);
            if (dw > area.width) GUI.Label(new Rect(loop - offset, 0, dw + 2, r.height), content, t);
            GUI.EndGroup();
        }

        void AddBody(List<(string, string, Target, int, Color)> rows, int index, int indent)
        {
            var b = app.Catalog[index];
            var tint = BodyTint(b);
            if (!b.IsVisible)
            {
                // A barycentre heads the tree as a title, not a selectable body.
                rows.Add((b.Name.ToUpperInvariant(), "(barycentre)", Target.None, 0, Dim));
                foreach (int child in app.Catalog.ChildrenOf(index)) AddBody(rows, child, indent);
                return;
            }
            rows.Add((b.Name, StatusMark(b).Trim(), Target.OfBody(index), indent, b.Kind == BodyKind.Moon ? Color.Lerp(tint, Color.gray, 0.3f) : tint));
            foreach (int child in app.Catalog.ChildrenOf(index)) AddBody(rows, child, Math.Min(indent + 1, 2));
        }

        static Ship Flagship(Battle b)
        {
            Ship best = null;
            foreach (var u in b.Units)
                if (u.Ship != null && u.Present && (best == null || u.Ship.Combat.Spec.Hull > best.Combat.Spec.Hull)) best = u.Ship;
            return best;
        }

        static string BattleScore(Battle b)
        {
            int a = 0, d = 0;
            foreach (var u in b.Units)
            {
                if (!u.Present || u.Kind == UnitKind.Fighter) continue;
                if (u.Side == 0) a++;
                else d++;
            }
            return a + " v " + d;
        }

        string ShortStatus(Ship ship)
        {
            if (ship.Battle != null) return "IN BATTLE  " + ship.Battle.Name;
            var c = ship.Controller;
            if (ship.Derelict) return "DERELICT";
            if (c.Phase == ShipPhase.Lurk && c.Stalled) return "stalled on " + c.LaneLeg.Lane.Name;
            if (c.Phase == ShipPhase.Lurk) return (c.Faction != null && !c.Faction.IsPirate ? "lane picket on " : "lurking on ") + c.LaneLeg.Lane.Name;
            if (c.Phase == ShipPhase.Adrift) return "adrift by " + c.LaneLeg.Lane.Name;
            if (c.GateKm is DVec3 gateKm)
            {
                // Bound for a jump gate: name it, and the system beyond.
                // A cruise voyage's GateKm is the edge of the well, not a gate (U.SPC-T576).
                bool cruiseOut = c.Voyage != null && c.Voyage.ByCruise;
                string gate = cruiseOut ? "the edge of the well" : "a jump gate";
                if (!cruiseOut)
                    foreach (var g in app.ActiveSystem.Gates)
                        if ((g.PositionKm - gateKm).Length < 1) gate = "the " + g.Name;
                if (c.Voyage != null && c.Voyage.AtGate) return "at " + gate;
                if (c.Phase == ShipPhase.Transit) return "-> " + gate;
            }
            if (c.Phase == ShipPhase.Parked) return (c.Role == ShipRole.Free ? "drifting @ " : "@ ") + app.Catalog[c.Orbit.Body].Name;
            if (c.Phase == ShipPhase.Transit && c.Wander) return "free flight near " + app.Catalog[c.Orbit.Body].Name;
            if (c.Phase == ShipPhase.Queued) return "queued @ " + c.Ring.Name;
            if (c.Phase == ShipPhase.Descent) return "descending to " + c.Ring.City;
            if (c.Phase == ShipPhase.Landed) return "landed @ " + c.Ring.City;
            if (c.Phase == ShipPhase.Ascent) return "climbing to " + c.Ring.Name;
            int dest = c.Journey != null ? c.FinalBody : c.Orbit.Body;
            return (c.Phase == ShipPhase.Lane ? "=> " : "-> ") + app.Catalog[dest].Name;
        }

        void DrawSelection()
        {
            var t = app.Selected;
            if (t.IsNone) return;
            var lines = new List<(string key, string value)>();
            string heading, sub;
            Color headingColor = Accent;

            if (t.IsStructure) StructureLines(t.Structure, lines, out heading, out sub, out headingColor);
            else if (t.IsRing) RingLines(t.Ring, lines, out heading, out sub, out headingColor);
            else if (t.IsBody)
            {
                var b = app.Catalog[t.Body];
                heading = b.Name;
                sub = KindName(b) + (b.Status == BodyStatus.Candidate ? "  ·  CANDIDATE, unconfirmed" : b.Status == BodyStatus.Disputed ? "  ·  DISPUTED" : b.Status == BodyStatus.Fictional ? "  ·  fiction (game layer)" : "");
                var eph = app.Ephemeris;
                if (b.IsStar) lines.Add(("Photosphere", b.TeffK.ToString("N0", CultureInfo.InvariantCulture) + " K,  " + b.LuminositySolar.ToString(b.LuminositySolar < 0.01 ? "0.00000" : "0.000", CultureInfo.InvariantCulture) + " x the Sun's light"));
                lines.Add(("Radius", Km(b.RadiusKm)));
                if (b.DiscoveryYear > 0)
                {
                    double earths = b.Gm / 398600.435;
                    lines.Add(("Mass", (b.MassIsMinimum ? "at least " : "") + earths.ToString(earths < 10 ? "0.00" : "N0", CultureInfo.InvariantCulture) + " Earth masses"));
                    lines.Add(("Habitable zone", b.HabitableZone == HabitableZone.Conservative ? "inside"
                        : b.HabitableZone == HabitableZone.Optimistic ? "inside the optimistic zone only" : "outside"));
                    lines.Add(("Found", b.DiscoveryYear.ToString(CultureInfo.InvariantCulture)));
                }
                if (b.ParentIndex >= 0)
                {
                    var parent = app.Catalog[b.ParentIndex];
                    var k = b.Elements.At(app.Clock.Jd);
                    double dist = eph.Relative(t.Body).Length;
                    if (parent.UsesStellarMapping) lines.Add(("From " + (parent.IsVisible ? parent.Name : "barycentre"), StellarDistance(dist) + "  (" + LightTime(dist) + " light)"));
                    else lines.Add(("From " + parent.Name, Km(dist)));
                    lines.Add(("Orbital speed", eph.OrbitalSpeedKmS(t.Body).ToString("0.00", CultureInfo.InvariantCulture) + " km/s"));
                    lines.Add(("Period", Duration(k.PeriodDays)));
                    lines.Add(("Eccentricity", k.E.ToString("0.0000", CultureInfo.InvariantCulture)));
                    lines.Add(("Semi-major axis", parent.UsesStellarMapping ? StellarDistance(k.A) : Km(k.A)));
                }
                if (b.RotationHours != 0)
                    lines.Add(("Day (sidereal)", (Math.Abs(b.RotationHours) < 48 ? Math.Abs(b.RotationHours).ToString("0.00", CultureInfo.InvariantCulture) + " h" : Duration(Math.Abs(b.RotationHours) / 24)) + (b.RotationHours < 0 ? " retro" : "")));
                if (b.Kind != BodyKind.Station) lines.Add(("Surface gravity", (b.Gm / (b.RadiusKm * b.RadiusKm) * 1000 / 9.80665).ToString("0.000", CultureInfo.InvariantCulture) + " g"));
                var ringsHere = new List<DockingRing>(app.World.Infrastructure?.RingsAt(t.Body) ?? new DockingRing[0]);
                if (ringsHere.Count > 0) lines.Add(("Docking rings", ringsHere.Count + ": " + string.Join(", ", ringsHere.ConvertAll(x => x.City))));
                if (!string.IsNullOrEmpty(b.Summary)) lines.Add(("", b.Summary));
                foreach (var s in app.Economy.At(t.Body))
                    if (s.Discovered) AddSettlementLines(lines, s);
                var battleHere = app.World.War.BattleAt(t.Body);
                if (battleHere != null) lines.Add(("Battle", battleHere.Name + ", " + BattleScore(battleHere) + ", " + Duration(battleHere.Seconds / Astro.SecondsPerDay)));
            }
            else
            {
                var ship = t.Ship;
                var c = ship.Controller;
                var st = app.ShipStateOf(ship);
                heading = ship.Name;
                var company = c.Company;
                sub = company != null
                    ? company.Name + " " + ship.Spec.DisplayName.ToLowerInvariant() + ", under the " + ship.Faction.Name + " flag"
                    : ship.Faction.Name + " " + ship.Spec.DisplayName.ToLowerInvariant();
                var livery = company?.Livery ?? ship.Faction.Color;
                headingColor = new Color(livery.R, livery.G, livery.B);
                lines.Add(("Length", Km(ship.LengthKm)));
                lines.Add(("Drive", ship.Spec.AccelG.ToString("0.##", CultureInfo.InvariantCulture) + " g, flip-and-burn; hyperlanes between systems"));
                lines.Add(("Propellant", PropellantText(ship)));
                if (ship.IsEscort) lines.Add(("Escorting", ship.Leader.Name));
                if (app.IsPiloting && app.Piloted.Ship == ship && ship.Battle == null)
                {
                    // Under the player's own hand: where it really is, not the spot its schedule was pinned at.
                    var near = app.Catalog[app.PilotFrameBody];
                    lines.Add(("Status", "Under your helm near " + near.Name));
                    lines.Add(("Altitude", Km(app.PilotOffsetKm.Length - near.RadiusKm)));
                    lines.Add(("Speed", SpeedText(app.PilotSpeedKmS)));
                }
                else if (c.Phase == ShipPhase.Lurk && c.Stalled)
                {
                    lines.Add(("Status", "Stalled beside the " + c.LaneLeg.Lane.Name + " hyperlane (rings misaligned)"));
                    lines.Add(("Until", Duration(Fleet.RemainingDays(c, app.Clock.Jd)) + " more"));
                }
                else if (c.Phase == ShipPhase.Lurk)
                {
                    lines.Add(("Status", "Lying in wait beside the " + c.LaneLeg.Lane.Name + " hyperlane"));
                    lines.Add(("Until", Duration(Fleet.RemainingDays(c, app.Clock.Jd)) + " more, unless prey comes by"));
                }
                else if (c.Phase == ShipPhase.Adrift)
                {
                    lines.Add(("Status", "Pulled out of the " + c.LaneLeg.Lane.Name + " hyperlane"));
                }
                else if (c.Phase == ShipPhase.Lane)
                {
                    lines.Add(("Status", "In the " + c.LaneLeg.Lane.Name + " hyperlane, bound for " + app.Catalog[c.FinalBody].Name));
                    lines.Add(("Speed", SpeedText(st.SpeedKmS)));
                    lines.Add(("Remaining", Km(c.LaneLengthKm * (1 - st.Progress))));
                    lines.Add(("Lane exit", Duration(Fleet.RemainingDays(c, app.Clock.Jd))));
                }
                else if (c.Phase == ShipPhase.Queued)
                {
                    lines.Add(("Status", "Waiting off the " + c.Ring.Name + " to be cleared down to " + c.Ring.City));
                    lines.Add(("Cleared in", Duration(Fleet.RemainingDays(c, app.Clock.Jd)) + "  (" + QueuePosition(c) + " in line)"));
                }
                else if (c.Phase == ShipPhase.Descent || c.Phase == ShipPhase.Ascent)
                {
                    double alt = (st.HelioKm - app.Ephemeris.Helio(c.Ring.Body)).Length - c.Ring.SurfaceKm;
                    lines.Add(("Status", c.Phase == ShipPhase.Descent ? "Descending from the " + c.Ring.Name + " to " + c.Ring.City : "Climbing from " + c.Ring.City + " to the " + c.Ring.Name));
                    lines.Add(("Altitude", Km(Math.Max(0, alt)) + (alt < c.Ring.EntryInterfaceKm ? "  ·  IN THE ATMOSPHERE" : "")));
                    lines.Add(("Vertical speed", SpeedText(st.SpeedKmS)));
                    lines.Add(("Done in", Duration(Fleet.RemainingDays(c, app.Clock.Jd))));
                }
                else if (c.Phase == ShipPhase.Landed)
                {
                    lines.Add(("Status", "Landed at " + c.Ring.City + ", " + app.Catalog[c.Ring.Body].Name));
                    if (c.WaitingForPropellant && c.DestinationBody == c.Ring.Body) lines[lines.Count - 1] = ("Status", "Landed at " + c.Ring.City + ", waiting for propellant");
                    else lines.Add(("Next", app.Catalog[c.DestinationBody].Name + " in " + Duration(Fleet.RemainingDays(c, app.Clock.Jd)) + ", by the " + c.Ring.Name));
                }
                else if (c.Phase == ShipPhase.Transit)
                {
                    // A fixed point is a lane's ramp, unless the ship is bound for a jump gate or a raider stakeout off one (U.SPC-T435).
                    string where = c.Ring != null && c.Orbit == c.HoldPoint ? c.Ring.Name + ", " + app.Catalog[c.Orbit.Body].Name
                        : c.Orbit.Fixed && c.GateKm != null && c.Voyage != null && c.Voyage.ByCruise ? "the edge of the well"
                        : c.Orbit.Fixed && (c.GateKm != null || c.StakeoutKm != null) ? "a jump gate's approach"
                        : c.Orbit.Fixed ? app.Catalog[c.Orbit.Body].Name + " hyperlane ramp" : app.Catalog[c.Orbit.Body].Name;
                    lines.Add(("Status", (st.Progress < 0.5 ? "Burning toward " : "Braking for ") + where));
                    if (c.Journey != null) lines.Add(("Bound for", app.Catalog[c.FinalBody].Name + " by " + JourneyText(c)));
                    lines.Add(("From", app.Catalog[c.OriginBody].Name));
                    lines.Add(("Speed", st.SpeedKmS.ToString("N0", CultureInfo.InvariantCulture) + " km/s"));
                    lines.Add(("Remaining", Km(c.PathLength * (1 - st.Progress))));
                    lines.Add(("ETA", Duration(Fleet.RemainingDays(c, app.Clock.Jd))));
                }
                else
                {
                    var body = app.Catalog[c.Orbit.Body];
                    lines.Add(("Status", ship.Battle != null ? "Fighting in the " + ship.Battle.Name
                        : c.WaitingForPropellant && c.DestinationBody == c.Orbit.Body ? "Waiting at " + body.Name + " for propellant"
                        : c.OutOfPropellant ? "Drifting off " + body.Name + " with dry tanks; a tanker is " + Duration(Fleet.RemainingDays(c, app.Clock.Jd)) + " out"
                        : (c.Orbit.Fixed ? "Holding position at " : "Holding orbit at ") + body.Name));
                    // From where it actually is: a held position (a released or spawned hull) is a fixed point
                    // with no orbit radius, and a ring's holding point sits off the body's centre.
                    lines.Add(("Altitude", Km(c.Orbit.OffsetAt(app.Clock.Jd).Length - body.RadiusKm)));
                    lines.Add(("Orbital speed", c.Orbit.SpeedKmS.ToString("0.00", CultureInfo.InvariantCulture) + " km/s"));
                    // A freshly spawned or not-yet-scheduled ship has no destination yet (-1).
                    if (c.DestinationBody >= 0 && !(c.WaitingForPropellant && c.DestinationBody == c.Orbit.Body))
                        lines.Add(("Next", app.Catalog[c.DestinationBody].Name + " in " + Duration(Fleet.RemainingDays(c, app.Clock.Jd))));
                }
                var giving = app.Fleet.EvadingFrom(ship);
                var off = app.Fleet.EvasionOf(ship);
                if (giving != null || off.Length > 0.5)
                    lines.Add(("Manoeuvring", (giving != null ? "giving way to " + giving.Name + ", " : "easing back onto course, ") + Km(off.Length) + " off its path"));
                if (c.Role == ShipRole.Free) lines.Add(("Pilot", "Independent: goes where it pleases" + (c.Wander ? " (a hop out to open space)" : "")));
                var combat = ship.Combat;
                lines.Add(("Hull", Percent(combat.HullFraction) + "   Shields " + Percent(combat.Spec.Shield > 0 ? combat.Shield / combat.Spec.Shield : 0)));
                lines.Add(("Armament", ShipLod.Armament(combat.Subsystems)));
                int lost = 0;
                foreach (var hp in combat.Subsystems) if (hp.Destroyed) lost++;
                if (lost > 0) lines.Add(("Damage", lost + " of " + combat.Subsystems.Count + " hardpoints destroyed"));
                if (combat.Spec.Squadrons > 0) lines.Add(("Fighters", CarrierWings.Describe(combat)));
                var inside = combat.Interior;
                lines.Add(("Aboard", inside.Crew.ToString("0", CultureInfo.InvariantCulture) + " crew, " + inside.Marines.ToString("0", CultureInfo.InvariantCulture) + " marines" +
                    (combat.Disabled ? "  ·  DISABLED" : "")));
                if (inside.UnderBoarding)
                {
                    lines.Add(("BOARDED", inside.BoarderNames + ": " + inside.Boarders.ToString("0", CultureInfo.InvariantCulture) + " boarders hold " + inside.TakenCount + " of " + inside.Compartments.Count + " spaces"));
                    foreach (var space in inside.Compartments) lines.Add(("  " + space.Name, SpaceText(space)));
                    if (inside.Log.Count > 0) lines.Add(("", inside.Log[inside.Log.Count - 1].Text));
                }
                if (ship.Derelict) lines.Add(("DERELICT", "Boarded, stripped and abandoned"));
                if (ship.Battle != null) lines.Add(("In battle", ship.Battle.Name + ", " + BattleScore(ship.Battle)));
                if (ship.Spec.IsMilitary || ship.Faction.IsPirate)
                {
                    var order = app.World.War.OrderOf(c);
                    var outlaws = app.World.Outlaws;
                    var hunter = outlaws?.HunterOf(c);
                    var outlaw = outlaws?.Of(c);
                    if (hunter != null)
                    {
                        var target = hunter.Contract?.Target;
                        string after = target == null ? "looking for a contract" : "hunting " + (outlaws.Of(target)?.Title ?? (target.IsPlayer ? "YOU" : target.Name));
                        lines.Add(("Bounty hunter", after + (hunter.Takes > 0 ? ",  " + hunter.Takes + " taken for " + Credits(hunter.Collected) : "")));
                    }
                    else lines.Add(("Orders", order != null ? OrderText(order) : "Patrol " + RouteText(c)));
                    if (outlaw != null)
                        lines.Add(("Outlaw", outlaw.Title + ",  " + outlaw.Deeds() + ",  " + outlaw.DaysAtLarge(app.Clock.Jd).ToString("0", CultureInfo.InvariantCulture) + " days at large" +
                            (outlaw.Haven != null && outlaw.Haven.Discovered ? ",  sails from " + outlaw.Haven.Name : "")));
                }
                else
                {
                    lines.Add(("Cargo", string.IsNullOrEmpty(c.Cargo) ? "Empty" : c.Cargo));
                    lines.Add(("Hold", Tonnes(c.CapacityTonnes)));
                    if (c.HasCargo) lines.Add(("Paid", Credits(c.CargoCost) + (c.CargoBonus > 0 ? "  (+" + Credits(c.CargoBonus) + " contract)" : "")));
                    lines.Add(("Trade profit", Credits(c.Credits)));
                    if (c.Company != null) lines.Add(("Owner", CompanyText(c.Company)));
                }
                lines.Add(("Trips", c.TripsCompleted.ToString(CultureInfo.InvariantCulture)));
                // The law's file on whoever commands it: the player's own record for the ship they fly.
                var record = app.World.Law?.FindRecord(ship);
                if (record != null && (record.Wanted || record.Crimes.Count > 0))
                    lines.Add((record.IsPlayer ? "Your record" : "Captain", (record.IsPlayer ? "" : record.Name + ", ") + RecordText(record)));
            }

            float w = 410;
            float linesH = lines.Count * 19;
            // A long card (a boarded hull lists every compartment) never runs off the top of the screen: its lines scroll.
            float maxLinesH = Mathf.Max(19 * 4, Screen.height - 140 - (64 + 30));
            bool scrolls = linesH > maxLinesH;
            float shownH = scrolls ? maxLinesH : linesH;
            float h = 64 + shownH + 30;
            var r = Panel(new Rect(SafeRight - w - 10, Screen.height - h - 10, w, h));
            var old = GUI.contentColor;
            GUI.contentColor = headingColor;
            GUI.Label(new Rect(r.x + 14, r.y + 8, w - 28, 24), heading, title);
            GUI.contentColor = Dim;
            GUI.Label(new Rect(r.x + 14, r.y + 30, w - 28, 18), sub, small);
            GUI.contentColor = old;

            if (selectionScrollFor != t)
            {
                selectionScrollFor = t;
                selectionScroll = Vector2.zero;
            }
            float x0 = r.x, y = r.y + 54, valueW = w - 146;
            if (scrolls)
            {
                selectionScroll = GUI.BeginScrollView(new Rect(r.x, y, w - 4, shownH), selectionScroll, new Rect(0, 0, w - 20, linesH));
                x0 = 0;
                y = 0;
                valueW = w - 162;
            }
            foreach (var (key, value) in lines)
            {
                if (key.Length == 0)
                {
                    GUI.Label(new Rect(x0 + 14, y, w - 28, 36), value, small);
                    y += 19;
                    continue;
                }
                GUI.contentColor = Dim;
                GUI.Label(new Rect(x0 + 14, y, 120, 19), key, small);
                GUI.contentColor = Color.white;
                GUI.Label(new Rect(x0 + 132, y, valueW, 19), value, text);
                y += 19;
            }
            if (scrolls) GUI.EndScrollView();
            GUI.contentColor = old;

            bool focused = app.SpaceCamera.FocusTarget == t && app.SpaceCamera.Mode != SpaceCamera.CameraMode.Free;
            if (GUI.Button(new Rect(r.x + 14, r.yMax - 32, 150, 24), focused ? "Re-frame  [F]" : "Focus  [F]", buttonActive)) app.FocusSelected();
            if (GUI.Button(new Rect(r.x + 172, r.yMax - 32, 150, 24), "Deselect  [Esc]", button)) app.Select(Target.None);
        }

        /// <summary>The tanks: how full, how many tonnes, and the delta-v that leaves at the ship's present mass.</summary>
        static string PropellantText(Ship ship)
        {
            if (ship.OutOfPropellant || ship.Propellant <= 1e-6) return "DRY  ·  0 of " + Tonnes(ship.PropellantCapacity);
            return Percent(ship.PropellantFraction) + "  ·  " + Tonnes(ship.Propellant) + " of " + Tonnes(ship.PropellantCapacity) +
                   "  ·  Δv " + ship.DeltaVBudgetKmS.ToString("N0", CultureInfo.InvariantCulture) + " km/s";
        }

        string QueuePosition(Ship ship)
        {
            int ahead = 1;
            foreach (var s in app.Fleet.Ships)
                if (!s.Destroyed && s.Phase == ShipPhase.Queued && s.Ring == ship.Ring && s.PhaseEndJd < ship.PhaseEndJd) ahead++;
            return ahead == 1 ? "1st" : ahead == 2 ? "2nd" : ahead == 3 ? "3rd" : ahead + "th";
        }

        void StructureLines(OrbitalStructure s, List<(string key, string value)> lines, out string heading, out string sub, out Color headingColor)
        {
            heading = s.Name;
            string kind = s.Kind == StructureKind.Shipyard ? "Orbital shipyard" : s.Kind == StructureKind.Factory ? "Factory station"
                : s.Kind == StructureKind.Habitat ? "Orbital habitat" : s.Kind == StructureKind.CrewedStation ? "Crewed space station"
                : s.Kind == StructureKind.Telescope ? "Space telescope" : s.Kind == StructureKind.Depot ? "Propellant depot"
                : s.Kind == StructureKind.Relay ? "Communications relay" : s.Kind == StructureKind.Gateway ? "Transfer station" : "Robotic orbiter";
            sub = kind + (s.Real ? "  ·  real spacecraft" : "  ·  " + (s.Owner?.Name ?? ""));
            headingColor = s.Owner != null ? new Color(s.Owner.Color.R, s.Owner.Color.G, s.Owner.Color.B) : Accent;
            var b = app.Catalog[s.Body];
            if (s.AtLagrange)
            {
                // Co-orbital: the point moves with its pair, so it is always this far from the smaller body.
                lines.Add(("Where", Lagrange.Name(app.Catalog, s.Body, s.Lagrange)));
                lines.Add(("From " + b.Name, Km((s.Helio(app.Ephemeris) - app.Ephemeris.Helio(s.Body)).Length)));
            }
            else
            {
                lines.Add(("Orbiting", b.Name));
                lines.Add(("Altitude", Km(s.Orbit.RadiusKm - b.RadiusKm)));
                lines.Add(("Period", Duration(Astro.TwoPi / Math.Max(s.Orbit.RateRadPerDay, 1e-12))));
                lines.Add(("Speed", s.Orbit.SpeedKmS.ToString("0.00", CultureInfo.InvariantCulture) + " km/s"));
            }
            lines.Add(("Size", Km(s.LengthKm)));
            if (s.Kind == StructureKind.Shipyard && s.Settlement != null)
            {
                var slip = s.Settlement.OnSlip;
                lines.Add(("On the slip", slip != null ? ShipClassSpec.Of(slip.Value).DisplayName + ", " + Percent(s.SlipProgress) + " built" : "Empty"));
                lines.Add(("Build points", s.Settlement.BuildPoints.ToString("N0", CultureInfo.InvariantCulture)));
            }
            if (s.Kind == StructureKind.Factory && s.Settlement != null)
                foreach (var f in s.Settlement.Facilities)
                    if (f.Recipe.Kind == s.Industry)
                        lines.Add(("Output", f.Recipe.Name + ", level " + f.Level.ToString("0.0", CultureInfo.InvariantCulture) + ", running " + Percent(f.Utilisation)));
            if (s.Settlement != null) lines.Add(("Serves", s.Settlement.Name + ", pop " + People(s.Settlement.Population)));
            if (!string.IsNullOrEmpty(s.Note)) lines.Add(("", s.Note));
        }

        void RingLines(DockingRing r, List<(string key, string value)> lines, out string heading, out string sub, out Color headingColor)
        {
            heading = r.Name;
            sub = "Docking ring over " + r.City + ", " + app.Catalog[r.Body].Name + "  ·  fiction: held on station by thrust";
            headingColor = new Color(0.6f, 0.9f, 1f);
            lines.Add(("Over", r.City + "  " + Math.Abs(r.LatDeg).ToString("0.00", CultureInfo.InvariantCulture) + (r.LatDeg >= 0 ? "° N  " : "° S  ")
                + Math.Abs(r.LonDeg).ToString("0.00", CultureInfo.InvariantCulture) + (r.LonDeg >= 0 ? "° E" : "° W")));
            lines.Add(("Altitude", Km(r.RadiusKm - r.SurfaceKm) + (Math.Abs(r.RadiusKm - Infrastructure.RingRadiusKm(app.Catalog[r.Body])) < 1 && r.RadiusKm > r.SurfaceKm * 3.5 ? "  (stationary-orbit height)" : "")));
            int queued = 0, down = 0, landed = 0, up = 0;
            foreach (var s in app.Fleet.Ships)
            {
                if (s.Destroyed || s.Ring != r) continue;
                if (s.Phase == ShipPhase.Queued) queued++;
                else if (s.Phase == ShipPhase.Descent) down++;
                else if (s.Phase == ShipPhase.Landed) landed++;
                else if (s.Phase == ShipPhase.Ascent) up++;
            }
            lines.Add(("Traffic", queued + " queued, " + down + " descending, " + landed + " landed, " + up + " climbing"));
            lines.Add(("Clearance", "one ship every " + (r.EntryIntervalDays * 1440).ToString("0", CultureInfo.InvariantCulture) + " min" +
                (r.NextEntryJd > app.Clock.Jd ? ", next in " + Duration(r.NextEntryJd - app.Clock.Jd) : ", clear now")));
            lines.Add(("Descent", Duration(r.DescentDays) + " to the ground; entry interface " + Km(r.EntryInterfaceKm) + " up"));
            if (r.Settlement != null) lines.Add(("Serves", r.Settlement.Name + " (" + r.Owner?.Name + ")"));
            lines.Add(("", "Merchants and couriers land through the rings; warships and pirates hold orbit."));
        }

        /// <summary>A trading company in a line: name, home port, hulls, cash, income, standing with its flag, or its fate.</summary>
        string CompanyText(Company co)
        {
            if (co.Fate == CompanyFate.Merged) return co.Name + "  · merged into " + co.MergedInto?.Name;
            if (co.Fate == CompanyFate.Bankrupt) return co.Name + "  · bankrupt";
            string order = co.OnOrder != null ? ",  " + co.OnOrder.Value.ToString().ToLowerInvariant() + " on order at " + co.OrderYard?.Name : "";
            return co.Name + " (" + co.HomePort?.Name + ")  " + co.Hulls + (co.Hulls == 1 ? " hull" : " hulls") + ",  " + Credits(co.Cash) +
                   ",  " + Credits(co.IncomePerDay) + "/day" + order;
        }

        /// <summary>
        /// A polity across the whole network (U.SPC-T91): what it is, its capital (and the system, when elsewhere), the
        /// systems it holds and its warships wherever they are; its treasury is the one on the line above.
        /// </summary>
        string PolityText(Faction f)
        {
            var polities = app.World.Polities;
            string kind = f.Kind switch
            {
                PolityKind.Company => "company",
                PolityKind.FreeTraders => "free traders",
                PolityKind.PirateClan => "pirate clan",
                _ => "state",
            };
            var capital = polities.CapitalOf(f);
            string seat = "";
            if (capital != null)
            {
                var at = polities.WorldOf(capital);
                string system = null;
                foreach (var x in app.Galaxy.Systems)
                    if (x.World == at && at != app.World) system = x.Name;
                seat = (f.IsPirate ? ",  den " : ",  capital ") + capital.Name + (system != null ? " (" + system + ")" : "");
            }
            int held = f.IsPirate ? 0 : polities.SystemsHeldBy(f).Count;
            int navy = f.IsMilitary || f.IsPirate ? polities.Navy(f).Count : 0;
            return kind + seat + (held > 0 ? ",  holds " + held + (held == 1 ? " system" : " systems") : "") +
                   (navy > 0 ? ",  " + navy + (f.IsPirate ? " raiders" : " warships") : "");
        }

        void AddSettlementLines(List<(string key, string value)> lines, Settlement s)
        {
            lines.Add(("", ""));
            bool seat = app.World.Polities.CapitalOf(s.Owner) == s;
            lines.Add((s.Name, s.Owner.Name + " " + KindName(s.Kind) + (seat ? " (capital)" : "") + ", pop " + People(s.Population)));
            var houses = app.World.Companies;
            if (houses != null)
                foreach (var co in houses.All)
                    if (co.Trading && co.HomePort == s) lines.Add(("Company", CompanyText(co)));
            if (s.Kind != SettlementKind.Haven && !s.Fallen)
            {
                double moving = s.NetMigration;
                string migration = Math.Abs(moving) < 1 ? "no migration" :
                    (moving > 0 ? "+" : "-") + Math.Abs(moving).ToString("N0", CultureInfo.InvariantCulture) + "/day " + (moving > 0 ? "moving in" : "leaving");
                lines.Add(("People", migration + "  ·  jobs " + (s.Staffing * 100).ToString("0", CultureInfo.InvariantCulture) + "% filled  ·  wage tax " + Credits(s.WageTax) + "/day"));
            }
            var exports = new List<(double value, string name)>();
            var shortages = new List<(double ratio, string name)>();
            for (int g = 0; g < GoodInfo.Count; g++)
            {
                var info = GoodInfo.Of((Good)g);
                double net = s.Supply[g] - s.Demand[g];
                if (net > 0) exports.Add((net * info.BasePrice, info.Name));
                double ratio = s.Price[g] / info.BasePrice;
                if (s.Demand[g] > 0 && ratio > 1.4) shortages.Add((ratio, info.Name + " x" + ratio.ToString("0.0", CultureInfo.InvariantCulture)));
            }
            exports.Sort((a, b) => b.value.CompareTo(a.value));
            shortages.Sort((a, b) => b.ratio.CompareTo(a.ratio));
            if (exports.Count > 0) lines.Add(("Exports", string.Join(", ", exports.GetRange(0, Math.Min(3, exports.Count)).ConvertAll(e => e.name))));
            lines.Add(("Short of", shortages.Count > 0 ? string.Join(", ", shortages.GetRange(0, Math.Min(3, shortages.Count)).ConvertAll(e => e.name)) : "nothing"));
            double siege = app.World.War.SiegeDays(s);
            lines.Add(("Defences", s.Platforms.Count + " of " + s.MaxPlatforms + " platforms" +
                (siege > 0 ? "  ·  under siege " + siege.ToString("0", CultureInfo.InvariantCulture) + " of " + War.SiegeNeeded(s).ToString("0", CultureInfo.InvariantCulture) + " days" : "")));
        }

        /// <summary>A found pirate haven is marked on its world's label (a hidden one is not, until found).</summary>
        string HavenMark(BodyDefinition body)
        {
            foreach (var s in app.Economy.At(body.Index))
                if (s.Kind == SettlementKind.Haven && s.Discovered) return "  · " + s.Name + (s.Fallen ? " (fallen)" : " (pirate haven)");
            return "";
        }

        static string KindName(SettlementKind k)
        {
            switch (k)
            {
                case SettlementKind.Station: return "station";
                case SettlementKind.MiningOutpost: return "mining outpost";
                case SettlementKind.Haven: return "pirate haven";
                default: return "colony";
            }
        }

        // ---------------------------------------------------------------- bounty board

        /// <summary>
        /// The bounty board (F4) of the port in view -- the selected world's biggest, else the one the piloted ship is
        /// alongside: everyone its owner has a bounty on (outlaws, and the player when wanted), the price, the record,
        /// where each was last seen and who is hunting them (see <see cref="Outlaws.BoardAt"/>).
        /// </summary>
        void DrawBounties()
        {
            var outlaws = app.World.Outlaws;
            if (outlaws == null) return;
            Settlement port = null;
            var t = app.Selected;
            if (t.IsBody)
                foreach (var s in app.Economy.At(t.Body))
                    if (s.Discovered && (port == null || s.Population > port.Population)) port = s;
            if (port == null && app.IsPiloting)
                foreach (var s in app.Economy.Settlements)
                    if (s.Discovered && app.NearPort(s)) { port = s; break; }
            const float w = 760, rowH = 18;
            var board = port != null ? outlaws.BoardAt(port) : new List<BountyPosting>();
            int rows = Mathf.Min(board.Count, 14);
            // Captains in the player's brig, and kills waiting on a scan of the wreck (U.SPC-T63).
            var ship = app.IsPiloting ? app.World.PilotedShip : null;
            var held = outlaws.HeldAboard(ship);
            var claims = ship != null ? outlaws.Claims : new List<KillClaim>();
            int extra = held.Count + claims.Count;
            float h = 54 + Mathf.Max(1, rows) * rowH + (extra > 0 ? 8 + extra * 22 : 0);
            var r = Panel(new Rect((Screen.width - w) * 0.5f, 120, w, h));
            GUI.Label(new Rect(r.x + 12, r.y + 6, w - 24, 22), "BOUNTY BOARD  ·  " + (port != null ? port.Name + "  ·  " + port.Owner.Name : "no port in view"), title);
            var old = GUI.contentColor;
            GUI.contentColor = Dim;
            string note = port == null ? "Select a world with a port, or fly alongside one.   F4 to close"
                : port.Owner.IsPirate ? "Nobody here answers to the law: a haven keeps no board.   F4 to close"
                : board.Count == 0 ? "Nobody is wanted here.   F4 to close"
                : "Bounties posted by " + port.Owner.Name + ", biggest first; the price pays in full alive, half dead on a scanned wreck.   F4 to close";
            GUI.Label(new Rect(r.x + 12, r.y + 28, w - 24, 18), note, small);
            for (int i = 0; i < rows; i++)
            {
                var p = board[i];
                GUI.contentColor = p.IsPlayer ? new Color(1f, 0.55f, 0.45f) : p.Outlaw != null && p.Outlaw.Feared ? new Color(1f, 0.78f, 0.35f) : Color.white;
                GUI.Label(new Rect(r.x + 12, r.y + 48 + i * rowH, w - 24, rowH), Outlaws.Describe(p, app.Clock.Jd), small);
            }
            float y = r.y + 56 + Mathf.Max(1, rows) * rowH;
            bool alongside = port != null && app.NearPort(port);
            foreach (var o in held)
            {
                GUI.contentColor = new Color(0.6f, 0.9f, 1f);
                GUI.Label(new Rect(r.x + 12, y + 2, w - 200, rowH), "IN YOUR BRIG  ·  " + o.Title + "  ·  " + Credits(o.Record.TotalBounty) + " alive  ·  ransom " + Credits(Outlaws.RansomFor(o)), small);
                GUI.contentColor = Color.white;
                if (alongside && !port.Owner.IsPirate && (!o.Record.Wanted || o.Record.WantedBy(port.Owner)) &&
                    GUI.Button(new Rect(r.xMax - 180, y, 168, 20), "Hand over  +" + Credits(o.Record.TotalBounty), button))
                {
                    if (!CoopSends("deliver", K(port), K(held, o))) DeliverOutlaw(outlaws, o, port); // US-T43: an entry in co-op; the original's lines in DeliverOutlaw (Hud.Coop.cs)
                }
                else if (alongside && port.Owner.IsPirate && GUI.Button(new Rect(r.xMax - 180, y, 168, 20), "Ransom  +" + Credits(Outlaws.RansomFor(o)), button))
                {
                    if (!CoopSends("ransom", K(port), K(held, o))) RansomOutlaw(outlaws, o, port); // US-T43: likewise
                }
                y += 22;
            }
            foreach (var c in claims)
            {
                GUI.contentColor = Dim;
                GUI.Label(new Rect(r.x + 12, y + 2, w - 24, rowH), "KILL UNPROVEN  ·  " + c.Outlaw.Title + "  ·  scan the wreck of the " + (c.Wreck != null ? c.Wreck.Name : "flagship") +
                    " for " + Credits(Outlaws.DeadRate(c.Outlaw.Record)) + ", the dead rate", small);
                y += 22;
            }
            GUI.contentColor = old;
        }

        // ---------------------------------------------------------------- contracts board (U.SPC-T101)

        /// <summary>
        /// The contracts board (F7) of the port in view -- the selected world's biggest, else the one the piloted ship is
        /// alongside: the jobs it posts from what the system needs (freight, passengers and colonists, escorts, patrols,
        /// surveys, bounties), best paid first, with Take while the ship is alongside; and the player's own contracts,
        /// with Abandon (which costs standing with the patron). See <see cref="Contracts"/>.
        /// </summary>
        void DrawContracts()
        {
            var contracts = app.World.Contracts;
            if (contracts == null) return;
            Settlement port = null;
            var t = app.Selected;
            if (t.IsBody)
                foreach (var s in app.Economy.At(t.Body))
                    if (s.Discovered && (port == null || s.Population > port.Population)) port = s;
            if (port == null && app.IsPiloting)
                foreach (var s in app.Economy.Settlements)
                    if (s.Discovered && app.NearPort(s)) { port = s; break; }
            var board = port != null ? contracts.BoardAt(port) : new List<Contract>();
            var mine = contracts.PlayerActive();
            const float w = 860, rowH = 20;
            int rows = Mathf.Min(board.Count, 12), ownRows = Mathf.Min(mine.Count, 6);
            float h = 54 + Mathf.Max(1, rows) * rowH + (ownRows > 0 ? 24 + ownRows * rowH : 0);
            var r = Panel(new Rect((Screen.width - w) * 0.5f, 120, w, h));
            GUI.Label(new Rect(r.x + 12, r.y + 6, w - 24, 22), "CONTRACTS  ·  " + (port != null ? port.Name + "  ·  " + port.Owner.Name : "no port in view"), title);
            var old = GUI.contentColor;
            GUI.contentColor = Dim;
            var ship = app.World.PilotedShip;
            bool alongside = port != null && ship != null && app.NearPort(port);
            string note = port == null ? "Select a world with a port, or fly alongside one.   F7 to close"
                : port.Owner.IsPirate ? "A haven posts no contracts.   F7 to close"
                : board.Count == 0 ? "Nothing posted here today.   F7 to close"
                : alongside ? "Jobs posted from what the system needs. Freight: buy the crates at any market and bring them in.   F7 to close"
                : "Fly alongside to take a job (P to pilot a ship).   F7 to close";
            GUI.Label(new Rect(r.x + 12, r.y + 28, w - 24, 18), note, small);
            double jd = app.Clock.Jd;
            for (int i = 0; i < rows; i++)
            {
                var c = board[i];
                float y = r.y + 48 + i * rowH;
                GUI.contentColor = c.Urgency >= 0.6 ? new Color(1f, 0.78f, 0.35f) : Color.white;
                GUI.Label(new Rect(r.x + 12, y, w - 110, rowH), Contracts.Describe(c, jd), small);
                if (alongside && GUI.Button(new Rect(r.xMax - 90, y, 78, rowH - 2), "Take", button) &&
                    !CoopSends("ctake", K(port), i)) // US-T43: an entry in co-op
                    TakeContract(contracts, c, ship, jd); // US-T43: the original's lines, moved into TakeContract (Hud.Coop.cs)
            }
            if (ownRows > 0)
            {
                float y0 = r.y + 52 + Mathf.Max(1, rows) * rowH;
                GUI.contentColor = Dim;
                GUI.Label(new Rect(r.x + 12, y0, w - 24, 18), "YOUR CONTRACTS  (giving one up costs standing with whoever posted it)", small);
                for (int i = 0; i < ownRows; i++)
                {
                    var c = mine[i];
                    float y = y0 + 22 + i * rowH;
                    GUI.contentColor = c.DueJd - jd < 1 ? new Color(1f, 0.55f, 0.45f) : new Color(0.6f, 0.9f, 1f);
                    GUI.Label(new Rect(r.x + 12, y, w - 110, rowH), Contracts.Describe(c, jd), small);
                    if (GUI.Button(new Rect(r.xMax - 90, y, 78, rowH - 2), "Abandon", button) && !CoopSends("cabandon", i) && contracts.Abandon(c, jd)) // US-T43: an entry in co-op
                        app.ShowToast("Contract abandoned: " + c.Title);
                }
            }
            GUI.contentColor = old;
        }

        // ---------------------------------------------------------------- the player's fleet (U.SPC-T102)

        Vector2 fleetScroll;

        /// <summary>
        /// The player's fleet and property (F10): the house account and its books, every owned ship with its captain,
        /// standing orders and running profit, and what else the house owns. At the port in view (the selected world's
        /// biggest, else the one the piloted ship is alongside) it buys hulls, a warehouse or a share; on a selected
        /// world it stakes a claim. A ship's Orders button steps through what it can be told to do, using the selected
        /// world (a route or patrol to it) or the selected ship (an escort).
        /// </summary>
        void DrawFleet()
        {
            var world = app.World;
            var h = world.Holdings;
            if (h == null) return;
            double jd = app.Clock.Jd;
            var sel = app.Selected;
            Settlement port = null;
            if (sel.IsBody)
                foreach (var s in app.Economy.At(sel.Body))
                    if (s.Discovered && (port == null || s.Population > port.Population)) port = s;
            if (port == null && app.IsPiloting)
                foreach (var s in app.Economy.Settlements)
                    if (s.Discovered && app.NearPort(s)) { port = s; break; }
            var flown = world.PilotedShip;
            var purse = flown != null && flown.Faction != h.House ? flown.Faction : null;

            const float w = 820, rowH = 20;
            int shipRows = 0, propRows = 0;
            foreach (var o in h.Ships) if (!o.Lost) shipRows++;
            foreach (var p in h.Properties) if (!p.Lost) propRows++;
            float listH = Mathf.Min((shipRows + propRows + 2) * rowH, 260);
            float h0 = 128 + listH + 6 * 16;
            var r = Panel(new Rect((Screen.width - w) * 0.5f, 110, w, h0));
            GUI.Label(new Rect(r.x + 12, r.y + 6, w - 24, 22), "YOUR FLEET  ·  account " + Credits(h.Balance) + "  ·  earned " + Credits(h.TotalEarned) +
                "  ·  costs " + Credits(h.TotalCosts) + "  ·  lost " + Credits(h.TotalLost), title);
            var old = GUI.contentColor;

            // The account.
            float x = r.x + 12, y = r.y + 30;
            if (purse != null)
            {
                if (GUI.Button(new Rect(x, y, 150, 20), "Deposit 10 M cr", button) && !CoopSends("deposit")) // US-T43: an entry in co-op (as each button below)
                    app.ShowToast(h.Deposit(purse, 1e7, jd) ? "10 M cr into the house account" : "The purse cannot spare it");
                if (GUI.Button(new Rect(x + 156, y, 150, 20), "Withdraw 10 M cr", button) && !CoopSends("withdraw"))
                    app.ShowToast(h.Withdraw(purse, 1e7, jd) ? "10 M cr out to the " + purse.Name : "Not that much in the account");
                if (!h.Owns(flown) && GUI.Button(new Rect(x + 312, y, 220, 20), "Take " + flown.Name + " into the fleet", button) && !CoopSends("enlist"))
                    app.ShowToast(h.Enlist(flown, jd) != null ? flown.Name + " flies your flag now" : "It cannot join the fleet");
            }
            else
            {
                GUI.contentColor = Dim;
                GUI.Label(new Rect(x, y + 2, w - 24, 18), flown != null ? "Flying your own " + flown.Name + ": its purse is the house account." : "Pilot a ship (P) to move money between its purse and the house account.", small);
            }

            // Buying at the port in view, and staking a claim on the selected world.
            y += 24;
            GUI.contentColor = Color.white;
            if (port != null)
            {
                GUI.Label(new Rect(x, y + 2, 170, 18), port.Name + ":", small);
                float bx = x + 170;
                if (h.CanBuyAt(port))
                    foreach (var cls in new[] { ShipClass.Freighter, ShipClass.Miner, ShipClass.Courier, ShipClass.Destroyer })
                    {
                        if (GUI.Button(new Rect(bx, y, 112, 20), ShipClassSpec.Of(cls).DisplayName + " " + Credits(Economy.HullPrice(cls)), button) &&
                            !CoopSends("hull", K(port), (int)cls)) // US-T43
                        {
                            var bought = h.Buy(port, cls, null, jd);
                            app.ShowToast(bought != null ? "Bought the " + bought.Name + " at " + port.Name : "Cannot afford it here");
                        }
                        bx += 116;
                    }
                if (GUI.Button(new Rect(bx, y, 96, 20), "Warehouse", button) && !CoopSends("warehouse", K(port))) // US-T43
                    app.ShowToast(h.BuyWarehouse(port, jd) != null ? "A warehouse at " + port.Name + " for " + Credits(Holdings.WarehousePrice) : "Not for sale");
                bx += 100;
                if (GUI.Button(new Rect(bx, y, 108, 20), "5% share " + Credits(Holdings.SharePrice(port, 0.05)), button) && !CoopSends("share", K(port))) // US-T43
                    app.ShowToast(h.BuyShare(port, 0.05, jd) != null ? "5% of " + port.Name + " is yours" : "Not for sale");
            }
            else
            {
                GUI.contentColor = Dim;
                GUI.Label(new Rect(x, y + 2, 420, 18), "Select a world with a port (or fly alongside one) to buy there.", small);
            }
            if (sel.IsBody && !world.Catalog[sel.Body].IsStar)
            {
                GUI.contentColor = Color.white;
                float cx = r.xMax - 12 - 2 * 150;
                foreach (var ore in new[] { Good.NickelIron, Good.Water })
                {
                    if (GUI.Button(new Rect(cx, y + 24, 146, 20), "Claim " + GoodInfo.Of(ore).Name.ToLowerInvariant() + " " + Credits(Holdings.ClaimPrice(ore)), button) &&
                        !CoopSends("claim", sel.Body, (int)ore)) // US-T43
                        app.ShowToast(h.StakeClaim(sel.Body, ore, jd) != null ? "Claim staked on " + world.Catalog[sel.Body].Name : "Cannot stake that claim");
                    cx += 150;
                }
            }
            GUI.contentColor = Dim;
            GUI.Label(new Rect(x, y + 26, 460, 18), "Orders use the selected world (a route or patrol to it) or the selected ship (escort).   F10 to close", small);

            // Ships and property.
            var view = new Rect(r.x + 8, y + 48, w - 16, listH);
            var content = new Rect(0, 0, w - 36, (shipRows + propRows + 2) * rowH);
            fleetScroll = GUI.BeginScrollView(view, fleetScroll, content);
            float ly = 0;
            GUI.contentColor = Dim;
            GUI.Label(new Rect(4, ly, w, rowH), shipRows == 0 ? "No ships yet." : "SHIPS", small);
            ly += rowH;
            foreach (var o in h.Ships)
            {
                if (o.Lost) continue;
                GUI.contentColor = o.Ship == flown ? new Color(0.55f, 1f, 0.6f) : Color.white;
                string captain = (o.Captain != null ? "Capt. " + o.Captain.Name : "no captain") + (world.Trade?.Insurance.Of(o) != null ? "  ·  insured" : "");
                GUI.Label(new Rect(4, ly, 540, rowH), o.Name + "  (" + o.Ship.Spec.DisplayName + ")  ·  " + captain + "  ·  " + o.Orders.Describe(world.Catalog) + "  ·  net " + Credits(o.Net), small);
                if (GUI.Button(new Rect(w - 36 - 236, ly, 70, rowH - 2), o.Captain == null ? "Hire" : "Dismiss", button) && !CoopSends("captain", K(h.Ships, o))) // US-T43
                {
                    if (o.Captain == null) app.ShowToast("Captain " + h.Hire(o, jd).Name + " takes the " + o.Name);
                    else h.Dismiss(o, jd);
                }
                if (o.OnCharter)
                {
                    GUI.contentColor = Dim;
                    GUI.Label(new Rect(w - 36 - 162, ly, 160, rowH), "on charter (\\)", small);
                }
                else if (GUI.Button(new Rect(w - 36 - 162, ly, 160, rowH - 2), "Orders: next", button) &&
                         !CoopSends("orders", K(h.Ships, o), app.CoopTargetKey(sel).Replace(' ', '|'))) // US-T43
                {
                    var next = NextOrders(o, sel);
                    app.ShowToast(o.Name + ": " + next.Describe(world.Catalog));
                }
                ly += rowH;
            }
            GUI.contentColor = Dim;
            GUI.Label(new Rect(4, ly, w, rowH), propRows == 0 ? "No property yet." : "PROPERTY", small);
            ly += rowH;
            GUI.contentColor = Color.white;
            foreach (var p in h.Properties)
            {
                if (p.Lost) continue;
                string extra = p.Kind == PropertyKind.Warehouse ? "  ·  " + Tonnes(p.StoredTonnes) + " of " + Tonnes(p.CapacityTonnes) + " stored"
                    : p.Kind == PropertyKind.MiningClaim ? "  ·  " + Tonnes(p.YieldPerDay) + " a day" : "";
                GUI.Label(new Rect(4, ly, w - 40, rowH), p.Name + extra + "  ·  income " + Credits(p.Income) + "  ·  upkeep " + Credits(p.Upkeep), small);
                ly += rowH;
            }
            GUI.EndScrollView();

            // The latest lines in the books.
            GUI.contentColor = Dim;
            float by = view.yMax + 4;
            int n = h.Ledger.Count;
            for (int i = Math.Max(0, n - 6); i < n; i++, by += 16)
            {
                var e = h.Ledger[i];
                GUI.Label(new Rect(r.x + 12, by, w - 24, 16), Astro.ToDateTime(e.Jd).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "  " + e.Text +
                    (Math.Abs(e.Amount) >= 1 ? "  " + (e.Amount > 0 ? "+" : "") + Credits(e.Amount) + (e.Cash ? "" : " (loss)") : ""), small);
            }
            GUI.contentColor = old;
        }

        /// <summary>Steps a ship to the next standing orders it can carry out: hold, a trade route between where it is and the
        /// selected world, mining the house's first claim it can work, escorting the selected (or flown) ship, a patrol.</summary>
        StandingOrder NextOrders(OwnedShip o, Target sel)
        {
            var h = app.World.Holdings;
            int here = o.Ship.Orbit?.Body ?? -1;
            int there = sel.IsBody ? sel.Body : -1;
            int tradeThere = there >= 0 ? h.MarketBodyFor(there) : -1; // a planet's port moon stands in for it
            Property claim = null;
            foreach (var p in h.OfKind(PropertyKind.MiningClaim))
                if (Economy.Carries(o.Ship.Spec.Class, p.Ore)) { claim = p; break; }
            var charge = sel.IsShip && sel.Ship != o.Ship ? sel.Ship : app.World.PilotedShip;
            var choices = new List<StandingOrder>
            {
                StandingOrder.Hold(),
                here >= 0 && tradeThere >= 0 && here != tradeThere ? StandingOrder.TradeRoute(here, tradeThere) : null,
                claim != null ? StandingOrder.Mining(claim) : null,
                charge != null && charge != o.Ship ? StandingOrder.Escort(charge) : null,
                there >= 0 ? (here >= 0 && here != there ? StandingOrder.Patrol(here, there) : StandingOrder.Patrol(there)) : null,
                StandingOrder.FreeTrade(),
            };
            int start = (int)o.Orders.Kind;
            for (int k = 1; k <= choices.Count; k++)
            {
                var c = choices[(start + k) % choices.Count];
                if (c != null && h.Order(o, c)) return c;
            }
            h.Order(o, StandingOrder.Hold());
            return o.Orders;
        }

        // ---------------------------------------------------------------- market board

        void DrawMarket()
        {
            Settlement market = null;
            var t = app.Selected;
            if (t.IsBody) foreach (var s in app.Economy.At(t.Body)) if (s.Discovered) { market = s; break; }
            if (t.IsShip && t.Ship.Controller.CargoDestination != null) market = t.Ship.Controller.CargoDestination;

            // Buy/Sell only make sense for a market the player's own ship could actually hand goods
            // over at right now -- not one merely being looked at from across the system.
            bool canTrade = market != null && app.CanTradeNow(market);
            // Prices seen in port go into the trade desk's intelligence (U.SPC-T426).
            if (canTrade && Event.current.type == EventType.Layout && !CoopObserve(market)) app.World.Trade?.Intel.Observe(market, app.Clock.Jd, "seen in port"); // US-T43: an entry in co-op
            var law = app.World.Law;
            var pilotRecord = app.IsPiloting && law != null ? law.Player : null;
            bool lawful = market != null && market.Owner != null && !market.Owner.IsPirate;
            // A row for the law at this port: the bounty to pay off here, or why it will not dock the ship.
            bool atPort = market != null && pilotRecord != null && lawful && app.NearPort(market);
            bool wantedHere = atPort && pilotRecord.WantedBy(market.Owner);
            bool refused = atPort && !canTrade && app.Economy.LawRefuses(app.World.PilotedShip, market);
            float lawRow = wantedHere || refused ? 22 : 0;

            // What is shaking this market right now (a strike, a blight, a blockade, rationing), from the sim's own events.
            string shocks = null;
            if (market != null)
                foreach (var e in app.World.Shocks.At(market))
                    shocks = shocks == null ? e.Label : shocks + "  ·  " + e.Label;
            float extra = shocks != null ? 18 : 0;

            const float rowH = 17;
            float w = canTrade ? 600 : 430;
            float h = marketOpen ? 62 + GoodInfo.Count * rowH + (canTrade ? 20 : 0) + extra + lawRow : 34;
            var r = Panel(new Rect(SafeRight - w - 10, 72, w, h));
            string heading = market != null ? "MARKET  ·  " + market.Name : "MARKET  ·  system average";
            if (GUI.Button(new Rect(r.x + 8, r.y + 6, w - 16, 22), (marketOpen ? "[-]  " : "[+]  ") + heading, title)) marketOpen = !marketOpen;
            if (!marketOpen) return;
            var old = GUI.contentColor;
            GUI.contentColor = Dim;
            GUI.Label(new Rect(r.x + 12, r.y + 30, w - 24, 18), market != null
                ? market.Owner.Name + (pilotRecord != null && lawful ? StandingText(pilotRecord, market.Owner) : "   (select a body or a trader to change, M to close)")
                : "Select a port to see its market.   M to close", small);
            // At a yard the ship can refit: open the outfitting panel (F2) from here too.
            if (canTrade && Outfitting.IsYard(market) && GUI.Button(new Rect(r.xMax - 110, r.y + 28, 98, 18), app.ShowOutfit ? "Outfit  [-]" : "Outfit", button))
                app.ShowOutfit = !app.ShowOutfit;
            if (shocks != null)
            {
                GUI.contentColor = new Color(1f, 0.72f, 0.4f);
                GUI.Label(new Rect(r.x + 12, r.y + 48, w - 24, 18), "NOW:  " + shocks, small);
            }
            if (lawRow > 0)
            {
                GUI.contentColor = new Color(1f, 0.55f, 0.45f);
                GUI.Label(new Rect(r.x + 12, r.y + 50 + extra, w - 230, 18), refused ? "DOCKING REFUSED: " + market.Owner.Name + " will not let you in"
                    : "WANTED by " + market.Owner.Name, small);
                // Fines are paid at the port's office even when it will not let the ship dock to trade.
                if (wantedHere && GUI.Button(new Rect(r.xMax - 210, r.y + 48 + extra, 196, 20), "Pay bounty  " + Credits(pilotRecord.BountyBy(market.Owner)), button) &&
                    !CoopSends("bounty", K(market))) // US-T43: an entry in co-op
                    PayBounty(market); // US-T43: the original's call, moved into PayBounty (Hud.Coop.cs)
                GUI.contentColor = Dim;
            }
            float top = r.y + 50 + extra + lawRow;
            if (canTrade)
            {
                GUI.contentColor = Color.white;
                var pilotShip = app.World.PilotedShip;
                GUI.Label(new Rect(r.x + 12, top, 200, 18), "Your credits: " + Credits(pilotShip.Faction.Treasury), small);
                // Fill the tanks here: water or hydrocarbons and their helium-3, at this market's price.
                GUI.Label(new Rect(r.x + 215, top, 110, 18), "Tanks " + Percent(pilotShip.PropellantFraction), small);
                string refuel = "Refuel  ~" + Credits(Economy.PropellantPrice(market) * (pilotShip.PropellantCapacity - pilotShip.Propellant));
                if (GUI.Button(new Rect(r.x + 300, top - 2, 140, 20), refuel, button)) ClickRefuel(market);
                GUI.Label(new Rect(r.xMax - 150, top, 40, 18), "Qty", small);
                if (GUI.Button(new Rect(r.xMax - 95, top - 2, 22, 20), "-", button)) tradeCrates = Math.Max(1, tradeCrates - 1);
                GUI.Label(new Rect(r.xMax - 70, top, 30, 18), tradeCrates.ToString(CultureInfo.InvariantCulture), small);
                if (GUI.Button(new Rect(r.xMax - 40, top - 2, 22, 20), "+", button)) tradeCrates++;
            }
            float y = top + (canTrade ? 20 : 0);
            GUI.contentColor = Dim;
            GUI.Label(new Rect(r.x + 12, y, 150, rowH), "Good", small);
            GUI.Label(new Rect(r.x + 170, y, 80, rowH), market != null ? "Stock" : "", small);
            GUI.Label(new Rect(r.x + 250, y, 80, rowH), "Price", small);
            GUI.Label(new Rect(r.x + 340, y, 80, rowH), market != null ? "Flow/day" : "vs base", small);
            y += rowH;
            for (int g = 0; g < GoodInfo.Count; g++)
            {
                var good = (Good)g;
                var info = GoodInfo.Of(good);
                double ratio = market != null ? market.Price[g] / info.BasePrice : app.Economy.PriceIndex(good);
                GUI.contentColor = Color.white;
                GUI.Label(new Rect(r.x + 12, y, 160, rowH), info.Name, small);
                if (market != null)
                {
                    GUI.Label(new Rect(r.x + 170, y, 80, rowH), Tonnes(market.Stock[g]), small);
                    double net = market.Supply[g] - market.Demand[g];
                    GUI.contentColor = net > 0 ? new Color(0.55f, 0.9f, 0.6f) : net < 0 ? new Color(1f, 0.7f, 0.45f) : Dim;
                    GUI.Label(new Rect(r.x + 340, y, 90, rowH), (net > 0 ? "+" : "") + Tonnes(net), small);
                }
                GUI.contentColor = ratio > 1.5 ? new Color(1f, 0.55f, 0.45f) : ratio < 0.6 ? new Color(0.55f, 0.85f, 1f) : Color.white;
                string price = (market != null ? market.Price[g] : info.BasePrice * ratio).ToString("N0", CultureInfo.InvariantCulture);
                GUI.Label(new Rect(r.x + 250, y, 90, rowH), price, small);
                if (market == null) GUI.Label(new Rect(r.x + 340, y, 90, rowH), "x" + ratio.ToString("0.00", CultureInfo.InvariantCulture), small);
                if (canTrade)
                {
                    if (GUI.Button(new Rect(r.x + 440, y - 1, 68, rowH), "Buy", button)) ClickBuy(market, good);
                    if (GUI.Button(new Rect(r.x + 515, y - 1, 68, rowH), "Sell", button)) ClickSell(market, good);
                }
                y += rowH;
            }
            GUI.contentColor = old;
        }

        /// <summary>The player's standing with a port's owner, and what it does to the prices there.</summary>
        static string StandingText(LawRecord r, Faction owner)
        {
            double s = r.StandingWith(owner);
            return "   ·   your standing " + (s > 0 ? "+" : "") + s.ToString("0", CultureInfo.InvariantCulture) +
                   ", prices x" + Law.PriceFactor(r, owner).ToString("0.00", CultureInfo.InvariantCulture);
        }

        void ClickBuy(Settlement market, Good good)
        {
            if (CoopSends("buy", K(market), (int)good, tradeCrates)) return; // US-T43: an entry in co-op
            var ship = app.World.PilotedShip;
            var outcome = app.Economy.BuyIntoHold(ship, market, good, tradeCrates, out int bought, out var stoppedBy);
            if (bought > 0) CaptureEvents.Trade("buy", GoodInfo.Of(good).Name, bought, market.Name); // US-T72
            if (bought > 0)
                app.ShowToast("Bought " + bought + " crate" + (bought == 1 ? "" : "s") + " of " + GoodInfo.Of(good).Name +
                    (bought < tradeCrates ? stoppedBy switch
                    {
                        Economy.TradeOutcome.NoStock => "  (" + market.Name + " ran out of stock)",
                        Economy.TradeOutcome.CantAfford => "  (out of credits)",
                        Economy.TradeOutcome.NoRoom => "  (cargo hold is full)",
                        _ => "",
                    } : ""));
            else
                app.ShowToast(outcome switch
                {
                    Economy.TradeOutcome.NoStock => market.Name + " has none to sell",
                    Economy.TradeOutcome.CantAfford => "Not enough credits",
                    Economy.TradeOutcome.NoRoom => "Cargo hold is full",
                    Economy.TradeOutcome.CannotDock => "Not welcome to trade here",
                    Economy.TradeOutcome.Refused => "Docking refused: your standing with " + market.Owner.Name + " is too low",
                    Economy.TradeOutcome.Contraband => "No lawful market will take stolen goods",
                    _ => "Can't buy that right now",
                });
        }

        void ClickRefuel(Settlement market)
        {
            if (CoopSends("refuel", K(market))) return; // US-T43: an entry in co-op
            var ship = app.World.PilotedShip;
            var outcome = app.Economy.RefuelAt(ship, market, out double tonnes, out double cost);
            app.ShowToast(outcome switch
            {
                Economy.TradeOutcome.Ok => "Took on " + Tonnes(tonnes) + " of propellant for " + Credits(cost) +
                    (ship.PropellantFraction < 0.99 ? "  (" + (market.Available(Good.Water) + market.Available(Good.Hydrocarbons) < 1 ? "the port ran out" : "credits ran out") + ")" : ""),
                Economy.TradeOutcome.NoRoom => "Tanks are already full",
                Economy.TradeOutcome.NoStock => market.Name + " has no reaction mass to spare",
                Economy.TradeOutcome.CantAfford => "Not enough credits",
                Economy.TradeOutcome.CannotDock => "Not welcome to trade here",
                Economy.TradeOutcome.Refused => "Docking refused: your standing with " + market.Owner.Name + " is too low",
                _ => "Can't refuel right now",
            });
        }

        void ClickSell(Settlement market, Good good)
        {
            if (CoopSends("sell", K(market), (int)good, tradeCrates)) return; // US-T43: an entry in co-op
            var ship = app.World.PilotedShip;
            var outcome = app.Economy.SellFromHold(ship, market, good, tradeCrates, out int sold);
            if (sold > 0) CaptureEvents.Trade("sell", GoodInfo.Of(good).Name, sold, market.Name); // US-T72
            if (sold > 0)
                app.ShowToast("Sold " + sold + " crate" + (sold == 1 ? "" : "s") + " of " + GoodInfo.Of(good).Name);
            else
                app.ShowToast(outcome switch
                {
                    Economy.TradeOutcome.NothingToSell => "None of that in the hold",
                    Economy.TradeOutcome.CannotDock => "Not welcome to trade here",
                    Economy.TradeOutcome.Refused => "Docking refused: your standing with " + market.Owner.Name + " is too low",
                    Economy.TradeOutcome.Contraband => "No lawful market will take stolen goods",
                    _ => "Can't sell that right now",
                });
        }

        // ---------------------------------------------------------------- outfitting (U.SPC-T65)

        /// <summary>
        /// The yard's outfitting panel: the piloted hull's hardpoints and what is on each, then its facilities inside the
        /// hull and the internal space left (U.SPC-T470). For a chosen hardpoint every module that fits it, for a fitted
        /// facility stripping it, and for the free space every facility the hull has room for, each with its price at
        /// this yard's market (materials at its prices, labour, less what the old part fetches). Prisoners in the brig
        /// are handed over here too, at any port the ship may dock at.
        /// </summary>
        void DrawOutfit()
        {
            var ship = app.World.PilotedShip;
            const float w = 600;
            if (ship == null || ship.Combat == null)
            {
                var r0 = Panel(new Rect((Screen.width - w) * 0.5f, 190, w, 52));
                GUI.Label(new Rect(r0.x + 12, r0.y + 6, w - 24, 22), "OUTFITTING", title);
                GUI.Label(new Rect(r0.x + 12, r0.y + 28, w - 24, 18), "Pilot a ship (P) and bring it to a shipyard to refit it.   F2 to close", small);
                return;
            }
            Settlement yard = null, port = null;
            foreach (var st in app.Economy.Settlements)
            {
                if (!st.Discovered || !app.CanTradeNow(st)) continue;
                port ??= st;
                if (yard == null && Outfitting.IsYard(st)) yard = st;
            }
            var loadout = Outfitting.LoadoutOf(ship);
            var spec = ship.Combat.Spec;
            const float rowH = 18;
            // Rows: the hardpoints, then the facilities, then the free space.
            int hardpoints = loadout.Slots.Count, facilities = loadout.Facilities.Count, rows = hardpoints + facilities + 1;
            int options = 0;
            if (yard != null && outfitSlot >= 0 && outfitSlot < rows) options = OutfitOptions(loadout, outfitSlot).Count;
            else outfitSlot = -1;
            float listH = Mathf.Min(rows * rowH, 230);
            float h = 112 + listH + (options > 0 ? 26 + options * rowH : 0);
            var r = Panel(new Rect((Screen.width - w) * 0.5f, 190, w, h));
            GUI.Label(new Rect(r.x + 12, r.y + 6, w - 24, 22), "OUTFITTING  ·  " + ship.Name + "  ·  " + (yard != null ? yard.Name : port != null ? port.Name + " (no yard)" : "not alongside a port"), title);
            var old = GUI.contentColor;
            GUI.contentColor = Dim;
            GUI.Label(new Rect(r.x + 12, r.y + 28, w - 24, 18),
                "Hull " + spec.Hull.ToString("N0", CultureInfo.InvariantCulture) + "  ·  shield " + spec.Shield.ToString("N0", CultureInfo.InvariantCulture) +
                "  ·  armour " + spec.Armor.ToString("0", CultureInfo.InvariantCulture) + "  ·  dry mass " + Tonnes(spec.DryMassTonnes) +
                "  ·  delta-v " + ship.DeltaVBudgetKmS.ToString("N0", CultureInfo.InvariantCulture) + " km/s  ·  scan " + (Outfitting.ScanRangeKm(ship) / 1e6).ToString("0.#", CultureInfo.InvariantCulture) + " M km", small);
            GUI.contentColor = Color.white;
            GUI.Label(new Rect(r.x + 12, r.y + 46, 220, 18), "Credits: " + Credits(ship.Faction.Treasury), small);
            double brig = Outfitting.BrigCapacity(ship);
            if (brig > 0 || ship.Prisoners > 0)
            {
                GUI.Label(new Rect(r.x + 230, r.y + 46, 200, 18), "Brig " + ship.Prisoners.ToString("0", CultureInfo.InvariantCulture) + " / " + brig.ToString("0", CultureInfo.InvariantCulture) + " prisoners", small);
                if (ship.Prisoners > 0 && port != null && GUI.Button(new Rect(r.xMax - 170, r.y + 44, 158, 20), "Hand over  +" + Credits(ship.PrisonerBounty), button) &&
                    !CoopSends("prisoners", K(port))) // US-T43: an entry in co-op
                    HandOverPrisoners(ship, port); // US-T43: the original's lines, moved into HandOverPrisoners (Hud.Coop.cs)
            }
            GUI.contentColor = Dim;
            GUI.Label(new Rect(r.x + 12, r.y + 64, w - 24, 18), yard != null
                ? "Pick a hardpoint, a facility or the free space, then what to fit.   Prices are this yard's market.   F2 to close"
                : "Bring the ship alongside a shipyard (or a haven) to refit.   F2 to close", small);

            // The hardpoints and what is on them, the facilities inside the hull, and the space left.
            var view = new Rect(r.x + 8, r.y + 84, w - 16, listH);
            var content = new Rect(0, 0, w - 36, rows * rowH);
            outfitScroll = GUI.BeginScrollView(view, outfitScroll, content);
            for (int i = 0; i < rows; i++)
            {
                string label;
                if (i < hardpoints)
                {
                    var slot = loadout.Slots[i];
                    string fitted = loadout.Fitted[i] is ModuleKind m ? loadout.PartAt(i, m).Name : "(empty)";
                    if (slot.LayoutIndex >= 0 && slot.LayoutIndex < ship.Combat.Subsystems.Count)
                    {
                        var sub = ship.Combat.Subsystems[slot.LayoutIndex];
                        fitted += sub.Destroyed ? "  (shot away)" : sub.Health < 0.99 ? "  (" + Percent(sub.Health) + ")" : "";
                    }
                    if (slot.Stock != null && loadout.Fitted[i] != slot.Stock) fitted += "   was " + loadout.PartAt(i, slot.Stock.Value).Name.ToLowerInvariant();
                    // Each mount's type and size (U.SPC-T508), and where a turret mount takes a spinal part (a railgun, a tube,
                    // the lance): one bearing on the bow, or a fixed tube.
                    string where = slot.IsWeapon && slot.Type != MountType.Spinal && slot.Spinal && slot.Size != HardpointSize.Small
                        ? (slot.BearsOnBow ? " (bow)" : " (fixed)") : "";
                    label = "  " + slot.Name + where + "   " + fitted;
                }
                else if (i < hardpoints + facilities)
                {
                    var f = loadout.Facilities[i - hardpoints];
                    label = "  Facility   " + f.Name + "   " + FacilityCost(f.Def, f.Size) + (loadout.IsStockFacility(f) ? "" : "   fitted");
                }
                // The space, reactor power and hands left for fittings (U.SPC-T521).
                else label = "  Free internal space   " + Sections(loadout.FreeSpace) + " of " + Sections(loadout.InternalSpace)
                             + "   ·   reactor " + Megawatts(loadout.FreePowerMw) + " of " + Megawatts(loadout.ReactorMw)
                             + "   ·   crew " + loadout.FreeCrew.ToString("0", CultureInfo.InvariantCulture) + " of " + loadout.FittingCrew.ToString("0", CultureInfo.InvariantCulture) + " spare";
                var rr = new Rect(0, i * rowH, content.width, rowH - 1);
                if (GUI.Button(rr, label, i == outfitSlot ? rowSelected : row))
                    outfitSlot = i == outfitSlot ? -1 : i;
            }
            GUI.EndScrollView();

            if (options == 0) { GUI.contentColor = old; return; }
            float y = r.y + 92 + listH;
            GUI.contentColor = Accent;
            string into = outfitSlot < hardpoints ? "Fit onto " + loadout.Slots[outfitSlot].Name.ToLowerInvariant() + ":"
                : outfitSlot < hardpoints + facilities ? loadout.Facilities[outfitSlot - hardpoints].Name + ":" : "Fit into the free space:";
            GUI.Label(new Rect(r.x + 12, y, w - 24, 18), into, small);
            y += 22;
            foreach (var option in OutfitOptions(loadout, outfitSlot))
            {
                OutfitOption(ship, yard, loadout, option, r.x, y, w, rowH);
                y += rowH;
            }
            GUI.contentColor = old;
        }

        static string Sections(double n) => n.ToString("0.#", CultureInfo.InvariantCulture) + (Math.Abs(n - 1) < 1e-9 ? " section" : " sections");

        static string Megawatts(double mw) => mw.ToString("0", CultureInfo.InvariantCulture) + " MW";

        /// <summary>What a facility takes (U.SPC-T521): its sections, and the reactor power and crew it needs, if any.</summary>
        static string FacilityCost(FacilityKind k, double size)
        {
            string s = Sections(size);
            if (k == null) return s;
            if (k.PowerMw > 0) s += ", " + Megawatts(k.PowerMw);
            if (k.Crew > 0) s += ", " + k.Crew.ToString("0", CultureInfo.InvariantCulture) + " crew";
            return s;
        }

        /// <summary>What the chosen row offers: the modules a hardpoint takes, stripping a fitted facility, or the
        /// facilities the free space has room for.</summary>
        static List<(ModuleKind? module, FacilityKind facility, string name)> OutfitOptions(Loadout loadout, int row)
        {
            var list = new List<(ModuleKind?, FacilityKind, string)>();
            int hardpoints = loadout.Slots.Count, facilities = loadout.Facilities.Count;
            if (row < hardpoints)
            {
                foreach (var m in ModuleSpec.Catalogue) if (Outfitting.CanFit(loadout, row, m.Kind)) list.Add((m.Kind, null, loadout.PartAt(row, m.Kind).Name));
            }
            else if (row < hardpoints + facilities)
            {
                if (loadout.CanRemove(row - hardpoints)) list.Add((null, null, "Strip it out"));
            }
            else
                foreach (var k in FacilityKinds.Catalogue)
                {
                    // One the hull has room for but not the reactor power or the hands (U.SPC-T521) is listed too, with why.
                    var can = loadout.CanAdd(k);
                    string why = can == OutfitOutcome.NoPower ? "  · not enough power" : can == OutfitOutcome.NoCrew ? "  · not enough crew" : "";
                    if (can == OutfitOutcome.Ok || why.Length > 0) list.Add((null, k, k.Name + "  (" + FacilityCost(k, k.Size) + ")" + why));
                }
            return list;
        }

        void OutfitOption(Ship ship, Settlement yard, Loadout loadout, (ModuleKind? module, FacilityKind facility, string name) option, float x, float y, float w, float rowH)
        {
            int hardpoints = loadout.Slots.Count;
            bool onHardpoint = outfitSlot < hardpoints, strip = !onHardpoint && option.facility == null;
            bool current = onHardpoint && loadout.Fitted[outfitSlot] == option.module;
            var q = onHardpoint ? Outfitting.Quote(ship, yard, outfitSlot, option.module)
                : strip ? Outfitting.QuoteStrip(ship, yard, outfitSlot - hardpoints)
                : Outfitting.QuoteFacility(ship, yard, option.facility);
            GUI.contentColor = current ? Accent : Color.white;
            GUI.Label(new Rect(x + 20, y, 200, rowH), option.name, small);
            GUI.contentColor = Dim;
            // Only the greatest yards build a tachyon lance (U.SPC-T487).
            bool builds = option.module == null || ModuleSpec.YardBuilds(option.module.Value, yard);
            string price = current ? "fitted"
                : !builds ? "this yard cannot build it"
                : !q.InStock ? "yard is short of " + GoodInfo.Of(q.Short).Name.ToLowerInvariant()
                : q.Net >= 0 ? Credits(q.Net) : "+" + Credits(-q.Net) + " back";
            GUI.Label(new Rect(x + 220, y, 250, rowH), price, small);
            GUI.contentColor = Color.white;
            if (current || !q.InStock || !builds) return;
            if (!GUI.Button(new Rect(x + w - 100, y - 1, 84, rowH - 1), strip ? "Strip" : "Fit", button)) return;
            if (CoopSendsOutfit(yard, option)) return; // US-T43: an entry in co-op
            OutfitApply(ship, yard, loadout, option); // US-T43: the original's lines below, moved into OutfitApply so a frame can run them too
        }

        void OutfitApply(Ship ship, Settlement yard, Loadout loadout, (ModuleKind? module, FacilityKind facility, string name) option)
        {
            int hardpoints = loadout.Slots.Count;
            bool onHardpoint = outfitSlot < hardpoints, strip = !onHardpoint && option.facility == null;
            string what = strip ? loadout.Facilities[outfitSlot - hardpoints].Name : option.name;
            OutfitQuote paid;
            var outcome = onHardpoint ? Outfitting.Fit(app.Economy, ship, yard, outfitSlot, option.module, out paid)
                : strip ? Outfitting.StripFacility(app.Economy, ship, yard, outfitSlot - hardpoints, out paid)
                : Outfitting.FitFacility(app.Economy, ship, yard, option.facility, out paid);
            if (outcome == OutfitOutcome.Ok && strip) outfitSlot = -1;
            app.ShowToast(outcome switch
            {
                OutfitOutcome.Ok => (strip ? what + " stripped out" : (onHardpoint ? option.name : option.facility.Name) + " fitted") + (paid.Net >= 0 ? " for " + Credits(paid.Net) : ", " + Credits(-paid.Net) + " back"),
                OutfitOutcome.CantAfford => "Not enough credits",
                OutfitOutcome.NoStock => yard.Name + " is short of the materials",
                OutfitOutcome.NoRoom => "Not enough internal space: strip something out first",
                OutfitOutcome.NoPower => "Not enough reactor power to spare: strip something that draws on it first",
                OutfitOutcome.NoCrew => "Not enough crew to spare to man it",
                OutfitOutcome.HoldTooFull => "Empty the hold before taking the racks out",
                OutfitOutcome.BrigOccupied => "Hand over the prisoners before taking the brig out",
                OutfitOutcome.Busy => "Not while fighting or boarded",
                OutfitOutcome.CannotDock => "Not welcome to refit here",
                OutfitOutcome.YardCannotBuild => yard.Name + " cannot build that",
                _ => "Can't fit that here",
            });
        }

        // ---------------------------------------------------------------- spawn (dev/sandbox)

        static readonly ShipClass[] SpawnClasses = (ShipClass[])Enum.GetValues(typeof(ShipClass));

        /// <summary>
        /// U.SPC-T9 dev/sandbox tool, redesigned as its own full screen rather than a small corner panel:
        /// every <see cref="ShipClass"/> laid out as a grid of real rendered hull previews
        /// (<see cref="Rendering.ShipThumbnails"/>, the same model a live ship uses) -- click one to
        /// spawn a ship of it (a random design, if the class has more than one) at the camera's current
        /// position and immediately pilot it. See <see cref="UnitySpaceApp.SpawnShip"/>.
        /// </summary>
        void DrawSpawn()
        {
            GUI.Box(new Rect(0, 0, Screen.width, Screen.height), GUIContent.none, panel);
            RegisterUi(new Rect(0, 0, Screen.width, Screen.height));

            GUI.Label(new Rect(40, 24, 500, 30), "SPAWN & PILOT", title);
            GUI.contentColor = Dim;
            GUI.Label(new Rect(40, 56, 700, 20), "Click a hull to spawn and pilot it at the camera   ·   V to close", small);
            GUI.contentColor = Color.white;

            if (GUI.Button(new Rect(Screen.width - 210, 28, 160, 26), spawnHostile ? "FACTION: HOSTILE" : "FACTION: HOME", spawnHostile ? buttonActive : button))
                spawnHostile = !spawnHostile;
            // U.SPC-T601: the system's one practice fighter, at the camera, the player at its helm (Shift+V does the same).
            if (GUI.Button(new Rect(Screen.width - 470, 28, 250, 26), "PRACTICE FIGHTER HERE  (Shift+V)", button))
                app.SpawnPracticeFighter();
            if (GUI.Button(new Rect(Screen.width - 40, 24, 30, 30), "X", button))
                app.ShowSpawn = false;

            var faction = spawnHostile ? app.World.Pirates : app.World.Sol;
            const float cell = 176f, gap = 20f, top = 96f, labelH = 30f;
            int perRow = Mathf.Max(1, Mathf.FloorToInt((Screen.width - 80 + gap) / (cell + gap)));
            float gridW = perRow * (cell + gap) - gap;
            float startX = (Screen.width - gridW) * 0.5f;

            for (int i = 0; i < SpawnClasses.Length; i++)
            {
                var cls = SpawnClasses[i];
                int col = i % perRow, line = i / perRow;
                var r = new Rect(startX + col * (cell + gap), top + line * (cell + labelH + gap), cell, cell + labelH);
                bool hover = r.Contains(Event.current.mousePosition);
                GUI.Box(r, GUIContent.none, hover ? rowSelected : row);
                var tex = ShipThumbnails.For(cls, faction);
                if (tex != null) GUI.DrawTexture(new Rect(r.x + 6, r.y + 6, cell - 12, cell - 12), tex, ScaleMode.ScaleToFit);
                GUI.Label(new Rect(r.x, r.yMax - labelH, cell, labelH), ShipClassSpec.Of(cls).DisplayName, title);
                if (GUI.Button(r, GUIContent.none, GUIStyle.none))
                {
                    app.SpawnShip(cls, spawnHostile);
                    app.ShowSpawn = false;
                }
            }
        }

        // ---------------------------------------------------------------- cargo hold

        /// <summary>
        /// The player-piloted ship's grid cargo hold (<see cref="CargoHold"/>): click an occupied cell
        /// to pick up its crate, then click an empty cell to drop it there; clicking the picked-up
        /// crate again cancels. Separate from, and drawn independently of, the market board above.
        /// </summary>
        void DrawCargo()
        {
            var ship = app.World.PilotedShip;
            if (ship == null)
            {
                cargoSelected = null;
                return;
            }
            var hold = ship.PersonalHold;
            if (cargoSelectedHold != hold) cargoSelected = null;
            cargoSelectedHold = hold;
            // A picked-up crate that has left the hold (sold at the market above, say) is no longer held.
            if (cargoSelected != null && !InHold(hold, cargoSelected)) cargoSelected = null;
            const float cell = 28f;
            float w = Math.Max(260, hold.Width * cell + 24);
            float h = cargoOpen ? 74 + hold.Height * cell + (cargoSelected != null ? 28 : 0) : 34;
            var r = Panel(new Rect(SafeLeft + 10, Screen.height - h - 56, w, h));

            if (GUI.Button(new Rect(r.x + 8, r.y + 6, w - 16, 22), (cargoOpen ? "[-]  " : "[+]  ") + "CARGO HOLD  ·  " + ship.Name, title)) cargoOpen = !cargoOpen;
            if (!cargoOpen) return;
            GUI.contentColor = Dim;
            GUI.Label(new Rect(r.x + 12, r.y + 28, w - 24, 32),
                cargoSelected != null ? "Click a cell to move the crate there  (click it again to cancel)" : "Click a crate to pick it up   [I to close]", small);
            GUI.contentColor = Color.white;

            var atCell = new CargoItem[hold.Width, hold.Height];
            foreach (var item in hold.Items)
                for (int dx = 0; dx < item.Width; dx++)
                    for (int dy = 0; dy < item.Height; dy++)
                        atCell[item.X + dx, item.Y + dy] = item;

            float gx = r.x + 12, gy = r.y + 48;
            for (int y = 0; y < hold.Height; y++)
            {
                for (int x = 0; x < hold.Width; x++)
                {
                    var occupant = atCell[x, y];
                    var cellRect = new Rect(gx + x * cell, gy + y * cell, cell - 2, cell - 2);
                    string label = occupant != null ? GoodAbbreviation(occupant.Good) : "";
                    bool isSelected = occupant != null && occupant == cargoSelected;
                    if (GUI.Button(cellRect, label, isSelected ? buttonActive : button))
                        ClickCargoCell(hold, x, y, occupant);
                }
            }

            if (cargoSelected != null)
            {
                float by = gy + hold.Height * cell + 6;
                if (GUI.Button(new Rect(r.x + 12, by, 90, 22), "Rotate", button))
                    hold.Move(cargoSelected, cargoSelected.X, cargoSelected.Y, !cargoSelected.Rotated);
            }
        }

        static bool InHold(CargoHold hold, CargoItem item)
        {
            foreach (var held in hold.Items)
                if (held == item) return true;
            return false;
        }

        void ClickCargoCell(CargoHold hold, int x, int y, CargoItem occupant)
        {
            if (cargoSelected == null)
            {
                if (occupant != null) cargoSelected = occupant;
                return;
            }
            if (occupant == cargoSelected)
            {
                cargoSelected = null;
                return;
            }
            if (hold.Move(cargoSelected, x, y, cargoSelected.Rotated)) cargoSelected = null;
            else app.ShowToast("Doesn't fit there");
        }

        static string GoodAbbreviation(Good g)
        {
            string name = GoodInfo.Of(g).Name;
            return name.Length <= 3 ? name : name.Substring(0, 3);
        }

        static string Tonnes(double t)
        {
            double a = Math.Abs(t);
            if (a >= 1e6) return (t / 1e6).ToString("0.0", CultureInfo.InvariantCulture) + " Mt";
            if (a >= 1e4) return (t / 1e3).ToString("0", CultureInfo.InvariantCulture) + " kt";
            return t.ToString("N0", CultureInfo.InvariantCulture) + " t";
        }

        static string Credits(double c)
        {
            double a = Math.Abs(c);
            if (a >= 1e9) return (c / 1e9).ToString("0.00", CultureInfo.InvariantCulture) + " B cr";
            if (a >= 1e6) return (c / 1e6).ToString("0.0", CultureInfo.InvariantCulture) + " M cr";
            if (a >= 1e3) return (c / 1e3).ToString("0", CultureInfo.InvariantCulture) + " k cr";
            return c.ToString("0", CultureInfo.InvariantCulture) + " cr";
        }

        static string People(double p) =>
            p >= 1e6 ? (p / 1e6).ToString("0.0", CultureInfo.InvariantCulture) + " M" : (p / 1e3).ToString("0", CultureInfo.InvariantCulture) + " k";

        static string SpaceText(Compartment c)
        {
            string who = c.Taken ? "TAKEN, " + c.Boarders.ToString("0", CultureInfo.InvariantCulture) + " boarders"
                : c.Contested ? "FIGHTING: " + c.Boarders.ToString("0", CultureInfo.InvariantCulture) + " boarders v " + (c.Marines + c.Crew).ToString("0", CultureInfo.InvariantCulture)
                : (c.Crew + c.Marines).ToString("0", CultureInfo.InvariantCulture) + " aboard";
            return who + (c.Vented ? (c.SealIn > 0 ? "  ·  VENTING" : "  ·  sealed, in vacuum") : "");
        }

        string OrderText(GroupOrder o)
        {
            string where = o.Body >= 0 ? app.Catalog[o.Body].Name : "";
            switch (o.Kind)
            {
                case OrderKind.Ambush: return "Lie in wait on the hyperlanes (toward " + where + ")";
                case OrderKind.Attack: return "Attack " + where;
                case OrderKind.Defend: return "Defend " + where;
                case OrderKind.Siege: return "Besiege " + where;
                case OrderKind.Repair: return "Refit at " + where;
                case OrderKind.Raid: return "Raid shipping at " + where;
                case OrderKind.Hunt: return "Hunt pirates at " + where;
                case OrderKind.GateWatch: return "Lie in wait off the jump gates (" + where + ")";
                case OrderKind.HoldGate: return "Hold the jump gates against raiders (" + where + ")";
                case OrderKind.Assault: return "Storm " + where;
                case OrderKind.Blockade: return "Blockade " + where;
                case OrderKind.Escort: return "Escort, at " + where;
                case OrderKind.Explore: return "Explore, next " + where;
                default: return "Patrol, next " + where;
            }
        }

        GUIStyle tickerStyle;

        static string Percent(double f) => (Math.Max(0, f) * 100).ToString("0", CultureInfo.InvariantCulture) + "%";

        void DrawTicker()
        {
            var events = app.World.Events;
            int n = Math.Min(4, events.Count);
            if (n == 0) return;
            float w = 640, rowH = 17;
            var r = Panel(new Rect((Screen.width - w) * 0.5f, 70, w, 10 + n * rowH));
            var old = GUI.contentColor;
            for (int i = 0; i < n; i++)
            {
                var e = events[events.Count - n + i];
                GUI.contentColor = KindColor(e.Kind);
                tickerStyle ??= new GUIStyle(small) { wordWrap = false, clipping = TextClipping.Clip };
                GUI.Label(new Rect(r.x + 10, r.y + 5 + i * rowH, w - 20, rowH), NewsLine(e), tickerStyle);
            }
            GUI.contentColor = old;
        }

        static Color KindColor(WorldEventKind k) => k switch
        {
            WorldEventKind.Battle => new Color(1f, 0.7f, 0.6f),
            WorldEventKind.Capture => new Color(1f, 0.85f, 0.4f),
            WorldEventKind.Opportunity => new Color(0.65f, 1f, 0.75f),
            WorldEventKind.Diplomacy => new Color(0.7f, 0.9f, 1f),
            WorldEventKind.Economy => new Color(1f, 0.72f, 0.4f),
            WorldEventKind.Market => new Color(0.95f, 0.95f, 0.55f),
            _ => Dim,
        };

        string NewsLine(WorldEvent e) => app.Clock.Utc.Year == Astro.ToDateTime(e.Jd).Year
            ? Astro.ToDateTime(e.Jd).ToString("MMM dd", CultureInfo.InvariantCulture) + "   " + e.Text
            : Astro.ToDateTime(e.Jd).ToString("yyyy MMM dd", CultureInfo.InvariantCulture) + "   " + e.Text;

        // ---------------------------------------------------------------- news feed

        static readonly string[] NewsFilters = { "ALL", "MARKETS", "WAR", "SHIPS & OTHER" };
        int newsFilter;
        Vector2 newsScroll;
        GUIStyle newsRow;
        readonly List<WorldEvent> newsShown = new List<WorldEvent>();

        /// <summary>Which filter tabs a report shows under: diplomacy (ceasefires, embargoes) is both markets and war news.</summary>
        static bool InFilter(WorldEventKind k, int filter) => filter switch
        {
            1 => k == WorldEventKind.Economy || k == WorldEventKind.Market || k == WorldEventKind.Diplomacy,
            2 => k == WorldEventKind.Battle || k == WorldEventKind.Capture || k == WorldEventKind.Loss || k == WorldEventKind.Diplomacy,
            3 => k == WorldEventKind.Info || k == WorldEventKind.Launch || k == WorldEventKind.Opportunity,
            _ => true,
        };

        /// <summary>
        /// The news feed (X), in place of the ticker: everything the sim has reported, newest first, filtered
        /// to markets (shocks, price spikes, embargoes), war (battles, captures, surrenders) or the rest. Every
        /// report is what actually happened; clicking one selects where it happened (and opens the market for
        /// market news) so the player can act on it.
        /// </summary>
        void DrawNews()
        {
            const float w = 760;
            float h = Mathf.Min(Screen.height * 0.5f, 420);
            var r = Panel(new Rect((Screen.width - w) * 0.5f, 70, w, h));
            var events = app.World.Events;
            if (GUI.Button(new Rect(r.x + 8, r.y + 6, 120, 22), "[-]  NEWS", title)) app.ShowNews = false;
            var old = GUI.contentColor;
            GUI.contentColor = Dim;
            GUI.Label(new Rect(r.x + 130, r.y + 9, w - 140, 18), "as it happens  ·  click a report to select where it happened  ·  X to close", small);
            GUI.contentColor = old;
            for (int i = 0; i < NewsFilters.Length; i++)
                if (GUI.Button(new Rect(r.x + 10 + i * 128, r.y + 32, 122, 20), NewsFilters[i], newsFilter == i ? buttonActive : button))
                {
                    newsFilter = i;
                    newsScroll = Vector2.zero;
                }

            newsRow ??= new GUIStyle(row) { fontSize = 11, wordWrap = true, padding = new RectOffset(6, 4, 2, 2) };
            newsShown.Clear();
            for (int i = events.Count - 1; i >= 0 && newsShown.Count < 200; i--)
                if (InFilter(events[i].Kind, newsFilter)) newsShown.Add(events[i]);

            var view = new Rect(r.x + 4, r.y + 58, w - 8, h - 62);
            float width = view.width - 20, total = 0;
            var heights = new float[newsShown.Count];
            for (int i = 0; i < newsShown.Count; i++) total += heights[i] = newsRow.CalcHeight(new GUIContent(NewsLine(newsShown[i])), width);
            newsScroll = GUI.BeginScrollView(view, newsScroll, new Rect(0, 0, width, Mathf.Max(total, 1)));
            float y = 0;
            for (int i = 0; i < newsShown.Count; i++)
            {
                var e = newsShown[i];
                var rr = new Rect(0, y, width, heights[i]);
                y += heights[i];
                GUI.contentColor = KindColor(e.Kind);
                if (e.Body < 0 || e.Body >= app.Catalog.Count)
                {
                    GUI.Label(rr, NewsLine(e), newsRow);
                    continue;
                }
                if (GUI.Button(rr, NewsLine(e), newsRow))
                {
                    app.Select(Target.OfBody(e.Body));
                    if (e.Kind == WorldEventKind.Economy || e.Kind == WorldEventKind.Market) app.ShowMarket = true;
                }
            }
            GUI.contentColor = old;
            if (newsShown.Count == 0) GUI.Label(new Rect(8, 4, width, 18), "Nothing to report yet.", small);
            GUI.EndScrollView();
        }

        /// <summary>How old the news here is from a settled system beyond the gates (U.SPC-T100): what is known of it is that old.</summary>
        string NewsAge(StarSystem system, double jd)
        {
            var r = system.Settled ? app.Galaxy.Traffic?.News.Known(app.ActiveSystem, system) : null;
            return r == null ? "" : "  · news " + LightSpeed.Describe(r.AgeDays(jd)) + " old";
        }

        static string JourneyText(Ship s)
        {
            var names = new List<string>();
            foreach (var hop in s.Journey) names.Add(hop.Lane.Name);
            return string.Join(", ", names);
        }

        string RouteText(Ship s)
        {
            if (s.Route == null) return "";
            var names = new string[s.Route.Length];
            for (int i = 0; i < names.Length; i++) names[i] = app.Catalog[s.Route[i]].Name;
            return string.Join(" › ", names);
        }

        void DrawStatus()
        {
            var cam = app.SpaceCamera;
            string s;
            switch (cam.Mode)
            {
                case SpaceCamera.CameraMode.Focus when cam.PilotLocked:
                    s = "PILOTING  ·  " + TargetName(cam.FocusTarget) + (app.Piloted.Ship != null ? "  ·  PROPELLANT " + (app.Piloted.Ship.Propellant <= 1e-6 ? "DRY" : Percent(app.Piloted.Ship.PropellantFraction)) : "") +
                        (app.World.Law != null && app.World.Law.Player.Wanted ? "  ·  WANTED " + Credits(app.World.Law.Player.TotalBounty) : "") +
                        SensorText() +
                        "   (" + ControlHint(app.PilotControlMode) + ", Z/X roll, F flight assist, Shift boost, F3 silent running, P to release)";
                    break;
                case SpaceCamera.CameraMode.Focus:
                    s = "FOCUS  ·  " + TargetName(cam.FocusTarget) + "   (WASD to swim free, wheel to zoom, right-drag to orbit)";
                    break;
                case SpaceCamera.CameraMode.Travel:
                    s = "TRAVELLING  ·  " + TargetName(cam.FocusTarget);
                    break;
                default:
                    double kmPerSec = SceneSpeedToKmS(cam.CurrentSpeed);
                    s = "FREE  ·  swim x" + cam.SpeedMultiplier.ToString("0.##", CultureInfo.InvariantCulture) + "  ·  " + SpeedText(kmPerSec) +
                        "  ·  near " + app.Catalog[cam.FrameBody].Name;
                    break;
            }
            var content = new GUIContent(s);
            float w = text.CalcSize(content).x + 28;
            var r = Panel(new Rect((Screen.width - w) * 0.5f, Screen.height - 40, w, 30));
            GUI.Label(new Rect(r.x + 14, r.y + 6, w, 20), content, text);

            // Tanks running low under the player's own hand: an emergency tanker can be called out.
            var flown = app.IsPiloting ? app.Piloted.Ship : null;
            if (flown != null && flown.Battle == null && (flown.PropellantFraction < 0.15 || app.TankerDueS >= 0))
            {
                var call = Panel(new Rect(r.x - 178, r.y, 170, 30));
                string label = app.TankerDueS >= 0 ? "TANKER IN " + Mathf.CeilToInt(app.TankerDueS) + " s" : "CALL A TANKER";
                if (GUI.Button(new Rect(call.x + 4, call.y + 4, call.width - 8, 22), label, app.TankerDueS >= 0 ? buttonActive : button)) app.CallTanker();
            }

            // Auto-reorient: keep the active system's own plane level (a smooth swing, not a snap).
            var level = Panel(new Rect(r.xMax + 8, r.y, 200, 30));
            string reorient = cam.Levelling ? "REORIENTING…" : app.AutoReorient ? "AUTO-REORIENT  ON  [R]" : "AUTO-REORIENT  OFF  [R]";
            if (GUI.Button(new Rect(level.x + 4, level.y + 4, level.width - 8, 22), reorient, app.AutoReorient ? buttonActive : button))
                app.ToggleAutoReorient();
        }

        /// <summary>The helm's sensor readout: running silent or not, the ship's own signature (lit up by a burn or a
        /// fight), and the contacts its scanners hold -- identified, by class only, and unknown blips.</summary>
        string SensorText()
        {
            var sensors = app.World.Sensors;
            var ship = app.Piloted.Ship;
            if (sensors == null || ship == null) return "";
            var book = sensors.Player;
            int id = book.CountLive(ContactLevel.Identified), cls = book.CountLive(ContactLevel.Class), blips = book.CountLive(ContactLevel.Blip);
            return (ship.SilentRunning ? "  ·  SILENT RUNNING" : "") +
                "  ·  SIG " + sensors.PilotSignature.ToString(sensors.PilotSignature < 10 ? "0.00" : "0", CultureInfo.InvariantCulture) +
                "  ·  CONTACTS " + id + " id / " + cls + " class / " + blips + " unknown";
        }

        /// <summary>A patrol's hail of the piloted ship, above the salvage readout: hold still for a scan, or pay the fine (1) or be fired on.</summary>
        void DrawHail()
        {
            var hail = app.IsPiloting ? app.World.Law?.Hail : null;
            if (hail == null) return;
            int seconds = Mathf.CeilToInt((float)hail.SecondsLeft(app.Clock.Jd));
            string s = "HAILED  ·  " + hail.By.Name.ToUpperInvariant() + " PATROL " + hail.Patrol.Name.ToUpperInvariant() + "  ·  " +
                (hail.Interdiction ? "HEAVE TO:  1 pay the " + Credits(hail.Fine) + " fine, or be fired on in " + seconds + " s"
                    : "CARGO SCAN: hold position, " + seconds + " s  (running is a crime)") + "    F12 parley";
            var line = new GUIContent(s);
            float lw = text.CalcSize(line).x + 28;
            var r = Panel(new Rect((Screen.width - lw) * 0.5f, Screen.height - 112, lw, 30));
            GUI.contentColor = hail.Interdiction ? new Color(1f, 0.55f, 0.45f) : new Color(1f, 0.85f, 0.4f);
            GUI.Label(new Rect(r.x + 14, r.y + 6, lw, 20), line, text);
            GUI.contentColor = Color.white;
        }

        /// <summary>A record in a line: the bounties posted, notoriety, and how many crimes are on file.</summary>
        static string RecordText(LawRecord r)
        {
            var parts = new List<string>();
            if (r.Wanted)
            {
                var by = new List<string>();
                foreach (var kv in r.Bounty) by.Add(kv.Key.Name + " " + Credits(kv.Value));
                parts.Add("WANTED: " + string.Join(", ", by));
            }
            parts.Add("notoriety " + r.Notoriety.ToString("0", CultureInfo.InvariantCulture));
            parts.Add(r.Crimes.Count + " crime" + (r.Crimes.Count == 1 ? "" : "s") + " on file");
            return string.Join(",  ", parts);
        }

        /// <summary>
        /// Above the status bar while piloting: the approach readout for the nearest derelict or wreck (range,
        /// closing speed, what is still needed to latch), or -- latched on -- the hulk's compartments with who is
        /// in each, what is still aboard, the part most worth stripping and the number-key choices.
        /// </summary>
        void DrawSalvage()
        {
            var s = app.Salvage;
            if (s == null)
            {
                if (string.IsNullOrEmpty(app.SalvageApproach)) return;
                var line = new GUIContent(app.SalvageApproach);
                float lw = text.CalcSize(line).x + 28;
                var lr = Panel(new Rect((Screen.width - lw) * 0.5f, Screen.height - 76, lw, 30));
                GUI.contentColor = Accent;
                GUI.Label(new Rect(lr.x + 14, lr.y + 6, lw, 20), line, text);
                GUI.contentColor = Color.white;
                return;
            }

            var rows = new List<string>();
            foreach (var c in s.Interior.Compartments)
            {
                var who = new List<string>();
                if (c.Marines > 0.5) who.Add(c.Marines.ToString("0", CultureInfo.InvariantCulture) + " raiders");
                if (c.Boarders > 0.5) who.Add(c.Boarders.ToString("0", CultureInfo.InvariantCulture) + " our marines");
                if (c.Survivors > 0.5) who.Add(c.Survivors.ToString("0", CultureInfo.InvariantCulture) + " survivors");
                if (c.Vented) who.Add("open to space");
                rows.Add(c.Name + ":  " + (who.Count == 0 ? "empty" : string.Join(", ", who)));
            }
            var aboard = new List<string>();
            foreach (var stack in s.Manifest)
                aboard.Add(GoodInfo.Of(stack.Good).Name + " " + stack.Tonnes.ToString("0", CultureInfo.InvariantCulture) + " t");
            rows.Add("Aboard:  " + (aboard.Count == 0 ? "nothing left" : string.Join(", ", aboard)));
            var part = s.BestPart();
            if (part != null)
                rows.Add("Best part:  " + SalvageSession.KindName(part.Kind) + " (" + part.Health.ToString("P0", CultureInfo.InvariantCulture) + ")" +
                         (s.FitFor(part) != null ? ", fits our own shot-away mount" : ", scrap for " + GoodInfo.Of(SalvageSession.ScrapOf(part.Kind)).Name.ToLowerInvariant()));
            if (s.SurvivorsRescued > 0.5) rows.Add("Rescued:  " + s.SurvivorsRescued.ToString("0", CultureInfo.InvariantCulture) + " survivors");

            const float rowH = 16;
            float w = 520, h = 62 + rows.Count * rowH;
            var r = Panel(new Rect((Screen.width - w) * 0.5f, Screen.height - h - 48, w, h));
            GUI.Label(new Rect(r.x + 12, r.y + 6, w - 24, 20), "LATCHED  ·  " + s.Name.ToUpperInvariant() + (s.Hostile ? "  ·  HOSTILES ABOARD" : ""), title);
            for (int i = 0; i < rows.Count; i++)
                GUI.Label(new Rect(r.x + 12, r.y + 30 + i * rowH, w - 24, rowH), rows[i], small);
            GUI.contentColor = Accent;
            GUI.Label(new Rect(r.x + 12, r.yMax - 26, w - 24, 20), app.SalvageChoices(), text);
            GUI.contentColor = Color.white;
        }

        /// <summary>
        /// The boarding panel (B at the controls, U.SPC-T468): the fight aboard the hull the player commands one side of, compartment
        /// by compartment (who holds it, the fighting, breaches), the compartment picked, the side's standing order and
        /// the number-key choices. The fight itself is the resolver's; the orders say only where people go.
        /// </summary>
        void DrawBoarding()
        {
            if (!app.BoardingPanelOpen) return;
            var (hull, role) = app.BoardingFight.Value;
            var inside = hull.Combat.Interior;
            var (order, objective) = BoardingCommand.OrdersOf(inside, role);
            const float rowH = 16;
            float w = 560, h = 82 + inside.Compartments.Count * rowH;
            var r = Panel(new Rect((Screen.width - w) * 0.5f, Screen.height - h - 48, w, h));
            GUI.Label(new Rect(r.x + 12, r.y + 6, w - 24, 20), "BOARDING  ·  " + hull.Name.ToUpperInvariant() + "  ·  " +
                (role == BoardingRole.Boarders ? "OUR MARINES ABOARD" : "REPELLING " + inside.BoarderNames.ToUpperInvariant()), title);
            for (int i = 0; i < inside.Compartments.Count; i++)
            {
                var c = inside.Compartments[i];
                string mark = (i == app.BoardingPick ? "> " : "   ") + (order == BoardingOrder.Push && i == objective ? "[objective] " : "");
                GUI.contentColor = c.Contested ? new Color(1f, 0.55f, 0.45f) : c.Taken ? new Color(1f, 0.85f, 0.4f) : Color.white;
                GUI.Label(new Rect(r.x + 12, r.y + 30 + i * rowH, w - 24, rowH), mark + c.Name + ":  " + SpaceText(c), small);
            }
            GUI.contentColor = Color.white;
            float y = r.y + 34 + inside.Compartments.Count * rowH;
            GUI.Label(new Rect(r.x + 12, y, w - 24, rowH), "Orders:  " + BoardingCommand.Label(inside, role) +
                (inside.Log.Count > 0 ? "   ·   " + inside.Log[inside.Log.Count - 1].Text : ""), small);
            GUI.contentColor = Accent;
            GUI.Label(new Rect(r.x + 12, r.yMax - 26, w - 24, 20),
                "1 pick   2 " + (role == BoardingRole.Boarders ? "push for it" : "muster on it") + "   3 hold   0 auto-resolve   B close", text);
            GUI.contentColor = Color.white;
        }

        /// <summary>Converts a scene speed near the camera into an equivalent true speed, using the local scale.</summary>
        double SceneSpeedToKmS(double sceneSpeed)
        {
            var cam = app.SpaceCamera;
            var body = app.Catalog[cam.FrameBody];
            double sceneDist = (cam.ScenePosition - app.BodyScene(cam.FrameBody)).Length;
            double trueDist = app.Scale.InverseLocalDistance(body, sceneDist);
            double perUnit = sceneDist > 0 ? trueDist / sceneDist : ScaleModel.KmPerUnit;
            return sceneSpeed * perUnit;
        }

        /// <summary>
        /// At a seat taken from command (U.SPC-T422: a fighter's cockpit, a gun mount): a crosshair where the stick is
        /// pointing (the middle of the screen, which the view follows) and a line saying what is held and how to leave it.
        /// </summary>
        void DrawSeat()
        {
            string line = app.SeatLine();
            if (line == null) return;
            DrawSeatSight();
            DrawDeckGuide();
            var content = new GUIContent(line);
            float w = toast.CalcSize(content).x + 40;
            var r = new Rect((Screen.width - w) * 0.5f, Screen.height - 96, w, 34);
            GUI.Box(r, GUIContent.none, panel);
            GUI.Label(new Rect(r.x + 20, r.y + 7, w, 22), content, toast);
        }

        // ---------------------------------------------------------------- the take-charge picker (U.SPC-T446)

        Vector2 pickerScroll;

        /// <summary>
        /// The take-charge picker (F9): every seat the player could take now, from <see cref="UnitySpaceApp.PickerSeats"/>
        /// (the hull in hand's own seat and its gun mounts, indented under it, then the fight in view, then the ships near
        /// the camera), each with what it is and whose. Clicking one jumps into it, the seat held handed back to its AI
        /// first; the clock never stops while it is open.
        /// </summary>
        void DrawPicker()
        {
            var seats = app.PickerSeats();
            var held = app.HeldSeat;
            const float w = 560, rowH = 22;
            float listH = Mathf.Clamp(seats.Count * rowH, rowH, 360);
            var r = Panel(new Rect((Screen.width - w) * 0.5f, 110, w, 64 + listH + 24));
            GUI.Label(new Rect(r.x + 12, r.y + 6, w - 24, 22), "TAKE CHARGE  ·  " + seats.Count + (seats.Count == 1 ? " seat" : " seats"), title);
            var old = GUI.contentColor;
            GUI.contentColor = Dim;
            GUI.Label(new Rect(r.x + 12, r.y + 30, w - 24, 18),
                held != null ? "Holding " + held.Name + ": pick another seat and it goes back to its AI." : "Select a hull for its guns, or look at a fight.", small);
            var view = new Rect(r.x + 8, r.y + 52, w - 16, listH);
            var content = new Rect(0, 0, w - 36, Mathf.Max(seats.Count, 1) * rowH);
            pickerScroll = GUI.BeginScrollView(view, pickerScroll, content);
            if (seats.Count == 0)
            {
                GUI.contentColor = Dim;
                GUI.Label(new Rect(4, 2, w - 40, rowH), "Nothing to take charge of here.", small);
            }
            IEmbodiable chosen = null;
            for (int i = 0; i < seats.Count; i++)
            {
                var s = seats[i];
                bool isHeld = Equals(s, held);
                bool mounted = s.Host != null && seats.Contains(s.Host);
                var rr = new Rect(0, i * rowH, w - 36, rowH - 2);
                GUI.contentColor = Color.white;
                if (GUI.Button(rr, GUIContent.none, isHeld ? rowSelected : row)) chosen = s;
                var fc = s.Faction != null ? new Color(s.Faction.Color.R, s.Faction.Color.G, s.Faction.Color.B) : Dim;
                GUI.contentColor = fc;
                float x = mounted ? 22 : 6;
                GUI.Label(new Rect(x, rr.y + 2, 70, 18), s.Kind.ToString().ToUpperInvariant(), small);
                GUI.contentColor = isHeld ? DeckLinedUpColor : Color.white;
                GUI.Label(new Rect(x + 72, rr.y + 2, 300 - x, 18), s.Name + (isHeld ? "  (held)" : ""), small);
                GUI.contentColor = Dim;
                GUI.Label(new Rect(380, rr.y + 2, w - 420, 18), s.Faction?.Name ?? "", small);
            }
            GUI.EndScrollView();
            GUI.contentColor = Dim;
            GUI.Label(new Rect(r.x + 12, r.yMax - 22, 300, 18), "Click a seat to take it.   F9 or Esc to close", small);
            GUI.contentColor = old;
            if (app.AtControls && GUI.Button(new Rect(r.xMax - 172, r.yMax - 24, 160, 20), "Back to command (P)", button)) app.PickCommand();
            if (chosen != null) app.PickSeat(chosen);
        }

        static readonly Color DeckLinedUpColor = new Color(0.45f, 1f, 0.55f, 0.95f);

        /// <summary>
        /// Landing guidance in a fighter's cockpit (U.SPC-T518, see <see cref="FlightDecks"/>): the nearest friendly flight deck's
        /// approach gate as a ring, its stern mouth bracketed and the glide path between them, green once the craft is lined up
        /// closely enough for the deck to take it in (or the deck has it), amber until then; launching, the bow mouth it goes out of.
        /// </summary>
        void DrawDeckGuide()
        {
            if (Event.current.type != EventType.Repaint || !(app.Seat is FighterSeat f)) return;
            var b = f.Battle;
            var deck = FlightDecks.GuideDeck(b, f.Unit);
            if (deck == null) return;
            var visit = FlightDecks.VisitOf(f.Unit);
            bool lined = visit != null || FlightDecks.Capturing(b, f.Unit) == deck;
            var color = lined ? DeckLinedUpColor : LockColor;
            bool gateOn = app.ToGui(app.BattleScene(b, deck.Gate), out var gate, out _);
            bool sternOn = app.ToGui(app.BattleScene(b, deck.Stern), out var stern, out _);
            if (visit?.Stage == DeckStage.Launching)
            {
                if (app.ToGui(app.BattleScene(b, deck.Bow), out var bow, out _))
                {
                    DrawBrackets(new Rect(bow.x - 12, bow.y - 8, 24, 16), color, 1.5f);
                    DrawLabel("LAUNCH", bow + new Vector2(16, -8), color, true);
                }
                return;
            }
            if (gateOn) DrawRing(gate, 9, color);
            if (gateOn && sternOn) DrawLine(gate, stern, new Color(color.r, color.g, color.b, 0.5f), 1f);
            if (sternOn)
            {
                DrawBrackets(new Rect(stern.x - 12, stern.y - 8, 24, 16), color, 1.5f);
                string tag = visit == null ? "DECK " + RangeText((deck.Gate - f.Unit.Pos).Length) : visit.Stage == DeckStage.Final ? "LANDING" : "ON DECK";
                DrawLabel(tag, stern + new Vector2(16, -8), color, true);
            }
        }

        /// <summary>A seat's sight: four ticks about the middle of the screen, which the view follows.</summary>
        void DrawSeatSight()
        {
            var old = GUI.color;
            GUI.color = new Color(0.6f, 0.95f, 1f, 0.9f);
            float cx = Screen.width * 0.5f, cy = Screen.height * 0.5f;
            var tex = Texture2D.whiteTexture;
            GUI.DrawTexture(new Rect(cx - 14, cy - 1, 9, 2), tex);
            GUI.DrawTexture(new Rect(cx + 5, cy - 1, 9, 2), tex);
            GUI.DrawTexture(new Rect(cx - 1, cy - 14, 2, 9), tex);
            GUI.DrawTexture(new Rect(cx - 1, cy + 5, 2, 9), tex);
            GUI.color = old;
        }

        void DrawToast()
        {
            if (string.IsNullOrEmpty(app.Toast) || app.ToastAge > 3.5f) return;
            float a = Mathf.Clamp01(3.5f - app.ToastAge);
            var content = new GUIContent(app.Toast);
            float w = toast.CalcSize(content).x + 40;
            var old = GUI.color;
            GUI.color = new Color(1, 1, 1, a);
            var r = new Rect((Screen.width - w) * 0.5f, 150, w, 34);
            GUI.Box(r, GUIContent.none, panel);
            GUI.Label(new Rect(r.x + 20, r.y + 7, w, 22), content, toast);
            GUI.color = old;
        }

        // ---------------------------------------------------------------- formatting

        string TargetName(Target t) => t.IsBody ? app.Catalog[t.Body].Name : t.IsShip ? t.Ship.Name : t.IsStructure ? t.Structure.Name : t.IsRing ? t.Ring.Name : "";

        static string KindName(BodyDefinition b)
        {
            switch (b.Kind)
            {
                case BodyKind.Star:
                    if (b.Singularity) return "Artificial gravity generator";
                    string kind = b.TeffK < 2400 ? "Brown dwarf" : b.RadiusKm < 0.05 * 695700 ? (b.TeffK >= 7500 ? "White dwarf" : "Tiny sun")
                        : b.TeffK < 3900 ? "Red dwarf star" : b.TeffK < 5300 ? "Orange dwarf star" : b.TeffK < 7500 ? "Yellow dwarf star" : "White main-sequence star";
                    return string.IsNullOrEmpty(b.SpectralType) ? kind : kind + "  (" + b.SpectralType + ")";
                case BodyKind.DwarfPlanet: return "Dwarf planet";
                case BodyKind.Asteroid: return "Asteroid";
                case BodyKind.Moon: return "Moon";
                case BodyKind.Station: return "Free-flying station";
                case BodyKind.Barycentre: return "Barycentre";
                default:
                    if (b.GasGiant || b.Name == "Jupiter" || b.Name == "Saturn") return "Gas giant";
                    if (b.Name == "Uranus" || b.Name == "Neptune") return "Ice giant";
                    double earths = b.Gm / 398600.435;
                    return earths >= 8 ? "Neptune-like planet" : earths >= 2 ? "Super-Earth" : "Terrestrial planet";
            }
        }

        static string Km(double km)
        {
            if (km < 1) return (km * 1000).ToString("N0", CultureInfo.InvariantCulture) + " m";
            if (km < 1e7) return km.ToString("N0", CultureInfo.InvariantCulture) + " km";
            return Au(km);
        }

        /// <summary>A distance in a star's frame: AU as a rule, but a Void Base's tiny suns circle their generator within a few hundred km, which would read 0.000 AU.</summary>
        static string StellarDistance(double km) =>
            km < 1e6 ? Km(km) : km > 1000 * Astro.AuKm ? (km / Astro.AuKm).ToString("N0", CultureInfo.InvariantCulture) + " AU" : Au(km);

        static string Au(double km) => (km / Astro.AuKm).ToString(km / Astro.AuKm < 10 ? "0.000" : "0.00", CultureInfo.InvariantCulture) + " AU";

        static string LightTime(double km)
        {
            double s = km / Astro.LightSpeedKmPerSec;
            return s < 120 ? s.ToString("0", CultureInfo.InvariantCulture) + " s" : s < 7200 ? (s / 60).ToString("0.0", CultureInfo.InvariantCulture) + " min"
                : s < 172800 ? (s / 3600).ToString("0.0", CultureInfo.InvariantCulture) + " h" : (s / 86400).ToString("0.0", CultureInfo.InvariantCulture) + " days";
        }

        static string Duration(double days)
        {
            if (days < 1.0 / 24) return (days * 1440).ToString("0", CultureInfo.InvariantCulture) + " min";
            if (days < 2) return (days * 24).ToString("0.0", CultureInfo.InvariantCulture) + " h";
            if (days < 400) return days.ToString("0.0", CultureInfo.InvariantCulture) + " days";
            return (days / 365.25).ToString("0.00", CultureInfo.InvariantCulture) + " years";
        }

        static string SpeedText(double kmPerSec)
        {
            if (kmPerSec > Astro.LightSpeedKmPerSec * 0.5) return (kmPerSec / Astro.LightSpeedKmPerSec).ToString("0.0", CultureInfo.InvariantCulture) + " c";
            if (kmPerSec >= 1) return kmPerSec.ToString("N0", CultureInfo.InvariantCulture) + " km/s";
            return (kmPerSec * 1000).ToString("N0", CultureInfo.InvariantCulture) + " m/s";
        }

        // ---------------------------------------------------------------- styles

        void EnsureStyles()
        {
            if (stylesReady) return;
            stylesReady = true;
            panelTexture = Solid(new Color(0.03f, 0.05f, 0.08f, 0.78f), new Color(0.3f, 0.6f, 0.75f, 0.45f));
            rowHoverTexture = Solid(new Color(0.2f, 0.45f, 0.6f, 0.3f), Color.clear);
            rowSelectedTexture = Solid(new Color(0.2f, 0.55f, 0.75f, 0.45f), Color.clear);
            buttonTexture = Solid(new Color(0.12f, 0.18f, 0.24f, 0.95f), new Color(0.3f, 0.5f, 0.6f, 0.6f));
            buttonActiveTexture = Solid(new Color(0.1f, 0.3f, 0.42f, 0.95f), new Color(0.45f, 0.85f, 1f, 0.8f));

            panel = new GUIStyle { normal = { background = panelTexture }, border = new RectOffset(2, 2, 2, 2) };
            title = new GUIStyle { fontSize = 15, fontStyle = FontStyle.Bold, normal = { textColor = Color.white } };
            text = new GUIStyle { fontSize = 13, normal = { textColor = Color.white }, clipping = TextClipping.Clip };
            small = new GUIStyle { fontSize = 11, normal = { textColor = Dim }, wordWrap = true };
            label = new GUIStyle { fontSize = 12, fontStyle = FontStyle.Bold, normal = { textColor = Color.white } };
            minorLabel = new GUIStyle { fontSize = 11, normal = { textColor = Color.white } };
            labelShadow = new GUIStyle(label) { normal = { textColor = new Color(0, 0, 0, 0.85f) } };
            button = new GUIStyle
            {
                fontSize = 12, alignment = TextAnchor.MiddleCenter, border = new RectOffset(2, 2, 2, 2),
                normal = { background = buttonTexture, textColor = Color.white },
                hover = { background = buttonActiveTexture, textColor = Color.white },
                active = { background = buttonActiveTexture, textColor = Accent },
            };
            buttonActive = new GUIStyle(button) { normal = { background = buttonActiveTexture, textColor = Color.white }, fontStyle = FontStyle.Bold };
            row = new GUIStyle
            {
                fontSize = 12, alignment = TextAnchor.MiddleLeft, padding = new RectOffset(6, 4, 0, 0), clipping = TextClipping.Clip,
                normal = { textColor = Color.white }, hover = { background = rowHoverTexture, textColor = Color.white },
            };
            rowSelected = new GUIStyle(row) { normal = { background = rowSelectedTexture, textColor = Color.white }, fontStyle = FontStyle.Bold };
            toast = new GUIStyle { fontSize = 14, fontStyle = FontStyle.Bold, normal = { textColor = Accent } };
        }

        internal static Texture2D Solid(Color fill, Color edge)
        {
            const int n = 8;
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { hideFlags = HideFlags.DontSave, filterMode = FilterMode.Point };
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                    t.SetPixel(x, y, x == 0 || y == 0 || x == n - 1 || y == n - 1 ? (edge.a > 0 ? edge : fill) : fill);
            t.Apply();
            return t;
        }

        /// <summary>The HUD's own styles are going with the app that owns it (U.SPC-T366): DontSave outlives the scene
        /// otherwise, so each game session (and every galaxy map it opened) would leave its textures behind.</summary>
        public void DestroyOwnedTextures()
        {
            if (panelTexture != null) UnityEngine.Object.Destroy(panelTexture);
            if (rowHoverTexture != null) UnityEngine.Object.Destroy(rowHoverTexture);
            if (rowSelectedTexture != null) UnityEngine.Object.Destroy(rowSelectedTexture);
            if (buttonTexture != null) UnityEngine.Object.Destroy(buttonTexture);
            if (buttonActiveTexture != null) UnityEngine.Object.Destroy(buttonActiveTexture);
            panelTexture = rowHoverTexture = rowSelectedTexture = buttonTexture = buttonActiveTexture = null;
            galaxyMap?.DestroyOwnedTextures();
            helpScreen?.DestroyOwnedTextures();
        }
    }
}
