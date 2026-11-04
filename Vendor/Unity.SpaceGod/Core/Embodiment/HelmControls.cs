namespace Unity.SpaceGod.Core
{
    /// <summary>
    /// What a hull is for at the helm (U.SPC-T449): the role its control set is fitted to. Every ship can be taken
    /// (<see cref="ShipHelm"/>), and each role gets the controls that make sense for its work, not one shared set.
    /// </summary>
    public enum HelmRole
    {
        /// <summary>A strike craft (fighter, interceptor, heavy fighter, bomber, recon, gate picket): the stick is the
        /// gunsight, the triggers fight, boost and flight assist are for the dogfight.</summary>
        Fighter,

        /// <summary>A hull flying in someone's formation: flown by its class's stick like any other, cut loose from the
        /// formation while the player holds it, with the guns on the trigger to cover its charge.</summary>
        Escort,

        /// <summary>A courier or blockade runner: the light fighter's free-aim stick on the fastest hull in the game, flown
        /// to run rather than to fight.</summary>
        Courier,

        /// <summary>A freighter, hauler, tender or explorer: a slow, deliberate stick, the hull flying heavy on its load, and
        /// docking mode on Ctrl inside a port's approach.</summary>
        Freighter,

        /// <summary>A miner or an ore or capture tug: a freighter's controls, flown to match a rock or bring a load in.</summary>
        Miner,

        /// <summary>A capital ship: the keys turn the hull and ring the engine telegraph, the mouse looks round it, the
        /// trigger calls broadsides and the helm assist can roll the weak patches away.</summary>
        Capital,
    }

    /// <summary>
    /// The control set a hull is flown with at the helm (U.SPC-T449, see <see cref="ShipHelm.Controls"/>), from its class's
    /// flight model and the role it plays: how the stick steers it (<see cref="PilotControl"/>), how quickly the mouse
    /// swings its nose, and which of the role-specific helm functions it has. The Runtime input layer reads it to turn
    /// the player's hands into <see cref="EmbodimentInput"/>; <see cref="ShipHelm.Control"/> reads it to fly the hull.
    /// </summary>
    public readonly struct HelmControls
    {
        public readonly HelmRole Role;

        /// <summary>How the stick steers the hull (U.SPC-T269).</summary>
        public readonly PilotControl Stick;

        /// <summary>How far the mouse swings the nose, as a fraction of the fighter's: a freighter is a heavy thing to point.</summary>
        public readonly double LookFactor;

        /// <summary>Ctrl inside a port's approach is docking mode (U.SPC-T443): haulers only.</summary>
        public readonly bool DockingMode;

        /// <summary>W and S ring the engine telegraph a step at a time, and the helm assist may roll the weak patches away
        /// (U.SPC-T499, U.SPC-T502): capital ships only.</summary>
        public readonly bool Telegraph;

        /// <summary>The hull carries guns, so the triggers fight.</summary>
        public readonly bool Guns;

        /// <summary>A freighter's mouse is slower than a fighter's (U.SPC-T269).</summary>
        public const double HaulerLookFactor = 0.6;

        public HelmControls(HelmRole role, PilotControl stick, double lookFactor, bool dockingMode, bool telegraph, bool guns)
        {
            Role = role;
            Stick = stick;
            LookFactor = lookFactor;
            DockingMode = dockingMode;
            Telegraph = telegraph;
            Guns = guns;
        }

        /// <summary>The controls <paramref name="ship"/> is flown with at the helm.</summary>
        public static HelmControls Of(Ship ship)
        {
            var c = ship.Spec.Class;
            var stick = PilotTuning.ControlOf(c);
            var model = FlightModelRegistry.Default.For(c);
            bool guns = ship.Combat != null && ship.Combat.Armed;
            return new HelmControls(RoleOf(ship), stick, stick == PilotControl.Hauler ? HaulerLookFactor : 1,
                model is HaulerFlightModel, model is CapitalFlightModel, guns);
        }

        /// <summary>The role <paramref name="ship"/> plays at the helm: a capital ship is always one; otherwise an escort
        /// flies as an escort, and the rest by their class and their work.</summary>
        public static HelmRole RoleOf(Ship ship)
        {
            var c = ship.Spec.Class;
            var family = FlightModelRegistry.Default.FamilyOf(c);
            if (family == FlightFamily.Capital) return HelmRole.Capital;
            if (ship.IsEscort || ship.Role == ShipRole.Escort) return HelmRole.Escort;
            if (c == ShipClass.Courier || ship.Role == ShipRole.Courier) return HelmRole.Courier;
            if (c == ShipClass.Miner || c == ShipClass.OreTug || c == ShipClass.CaptureTug || ship.Role == ShipRole.Miner || ship.Role == ShipRole.Tug)
                return HelmRole.Miner;
            return PilotTuning.ControlOf(c) == PilotControl.Hauler ? HelmRole.Freighter : HelmRole.Fighter;
        }

        /// <summary>The role's own keys, for the helm's toast (the keys every helm shares are the toast's own).</summary>
        public string Hint
        {
            get
            {
                string fire = Guns ? "" : ", unarmed";
                switch (Role)
                {
                    case HelmRole.Capital:
                        return "capital: W/S ring the engine telegraph, A/D Q/E turn the hull, mouse looks round it, hold left mouse for a broadside";
                    case HelmRole.Escort:
                        return "escort, flying free of its formation: " + StickHint + fire;
                    case HelmRole.Courier:
                        return "courier: " + StickHint + ", Shift for the long boost" + fire;
                    case HelmRole.Freighter:
                        return "freighter: " + StickHint + Heavy + fire;
                    case HelmRole.Miner:
                        return "miner: " + StickHint + Heavy + fire;
                    default:
                        return "fighter: " + StickHint + ", Shift boost" + fire;
                }
            }
        }

        string Heavy => DockingMode ? ", flies heavy on its load, Ctrl in a port's approach to dock" : "";

        string StickHint => Stick == PilotControl.FreeAim ? "mouse aims, the hull turns after it" : "mouse steers the nose";
    }
}
