using System;
using System.Collections.Generic;

namespace Unity.SpaceGod.Core
{
    /// <summary>A hardpoint's live state: health, reload, what it is shooting at and where it points.</summary>
    public sealed class Subsystem
    {
        public readonly SubsystemDef Def;
        public readonly int Index;
        public double Hp;
        public double Cooldown;
        public int Ammo;

        /// <summary>This shield emitter's own field charge and its cap (its rating's share of the hull's total),
        /// meaningful only for <see cref="SubsystemKind.ShieldGenerator"/>; set by <see cref="ShipCombat"/>. The field
        /// covers the emitter's own patch of hull (U.SPC-T501, see <see cref="ShieldEmitters"/>).</summary>
        public double Shield, MaxShield;

        /// <summary>Battle-clock seconds this emitter's patch was last hit, for its own regen pause.</summary>
        public double LastHitSeconds = -999;

        /// <summary>Work (hit points' worth) emergency repair crews have put into bringing this mount back since it was
        /// shot away (U.SPC-T501, see <see cref="Broadsides.StepDamageControl"/>); 0 while it stands.</summary>
        public double Restoring;

        /// <summary>World direction (ecliptic) the mount is aimed, for turret rendering; zero when idle.</summary>
        public DVec3 Aim;

        public Subsystem(SubsystemDef def, int index)
        {
            Def = def;
            Index = index;
            Hp = def.MaxHp;
            Ammo = def.Weapon?.Ammo ?? 0;
        }

        public SubsystemKind Kind => Def.Kind;
        public bool Destroyed => Hp <= 0;
        public double Health => Math.Max(0, Hp / Def.MaxHp);

        /// <summary>
        /// This emitter's live field as a fraction of its cap, 0 to 1: what its field glow on the hull and the bubble a
        /// hit raises on its patch are shown at (U.SPC-T40, U.SPC-T501). Zero for a collapsed or shot-away emitter,
        /// whose patch then shows no bubble, and for anything that is not a shield emitter.
        /// </summary>
        public double ShieldFraction => Kind == SubsystemKind.ShieldGenerator && !Destroyed && MaxShield > 0 && Shield > 0
            ? Math.Min(1, Shield / MaxShield) : 0;
    }

    /// <summary>
    /// The damage a hull carries between battles: hull points, shields and every hardpoint. Repairs
    /// at a friendly yard restore it, paid for in alloys and electronics from the yard's market.
    /// </summary>
    public sealed class ShipCombat
    {
        public readonly CombatSpec Spec;
        public readonly List<Subsystem> Subsystems = new List<Subsystem>();
        public double Hull;

        /// <summary>
        /// The craft docked in the hangar (carriers, U.SPC-T526, <see cref="CarrierWings"/>), in berth order: each a hull of
        /// its own class with its own damage and pilot, launched as itself when a fight starts and docked again when it lands.
        /// Losses are made good at a yard. Empty for a hull with no hangar.
        /// </summary>
        public List<Ship> Hangar = new List<Ship>();

        /// <summary>How many craft are docked aboard (<see cref="Hangar"/>). Setting it builds new craft for the first empty
        /// berths, or takes craft out from the last berths, until that many are aboard.</summary>
        public int Fighters
        {
            get => Hangar.Count;
            set
            {
                if (value < Hangar.Count) CarrierWings.Trim(this, value);
                else if (value > Hangar.Count) CarrierWings.Fill(this, Math.Min(Spec.HangarCraft, value) - Hangar.Count);
            }
        }

        /// <summary>
        /// The boarding shuttles docked in the shuttle berths (carriers, U.SPC-T486, <see cref="BoardingShuttles"/>), in berth
        /// order: each an assault shuttle hull of its own with its own damage and pilot (U.SPC-T590, <see cref="CarrierWings"/>),
        /// sent out as itself and docked again when it lands. Losses are made good at a yard. Empty for a hull with no shuttles.
        /// </summary>
        public List<Ship> ShuttleBay = new List<Ship>();

        /// <summary>How many boarding shuttles are docked aboard (<see cref="ShuttleBay"/>). Setting it builds new shuttles for
        /// the first empty berths, or takes shuttles out from the last berths, until that many are aboard.</summary>
        public int Shuttles
        {
            get => ShuttleBay.Count;
            set
            {
                if (value < ShuttleBay.Count) CarrierWings.TrimShuttles(this, value);
                else if (value > ShuttleBay.Count) CarrierWings.FillShuttles(this, Math.Min(Spec.Shuttles, value) - ShuttleBay.Count);
            }
        }

        /// <summary>The player holds this station (a settlement's defence platform or a gate fort) at its
        /// <see cref="StationSeat"/> (U.SPC-T447): its guns are laid on the player's aim, in a fight or out of one. Kept on
        /// the station itself, so it stays held as a fight starts or ends around it.</summary>
        public bool StationHeld;

        /// <summary>Boost charge, 0-1: drains over <see cref="CombatSpec.BoostDurationS"/> while held and
        /// recharges (twice as slowly) otherwise. Shared by AI and player flight so boost behaves the same
        /// whoever is flying.</summary>
        public double BoostFraction = 1;

        /// <summary>The inside of the hull: compartments, who is in them, and any boarding fight.</summary>
        public readonly Interior Interior;

        /// <summary>
        /// How fit to fight the hull's war fleet is for what it has left of its propellant, munitions and spares (U.SPC-T93),
        /// 1 in supply down to <see cref="FleetSupply.ReadinessFloor"/> with nothing left: its guns reload that much slower in
        /// battle, its crew strike their colours sooner, and it mends itself in the field that much slower. Set each day by
        /// <see cref="MilitaryLogistics"/> for a fleet on campaign; 1 again at any port of its own.
        /// </summary>
        public double Readiness = 1;

        /// <summary>How the captain has tuned the hull's phaser arrays (U.SPC-T488, <see cref="EnergyWeapons"/>): a narrow lance
        /// for hulls or a wide sweep for fighters and missiles, all its arrays at once.</summary>
        public PhaserTuning PhaserTuning;

        ModularHull structure;

        /// <summary>The hull as the modules it is built from and the joints holding them together (U.SPC-T596, see
        /// <see cref="ModularHull"/>): where it is coming apart, beside <see cref="Hull"/>, which says whether it lives. Laid
        /// out from <see cref="Spec"/> the first time it is asked for, whole.</summary>
        public ModularHull Structure => structure ??= ModularHull.For(Spec);

        /// <summary>Whether <see cref="Structure"/> has been laid out yet (a hull nothing has asked about is whole).</summary>
        public bool HasStructure => structure != null;

        /// <param name="hull">The ship class, or null for a defence platform.</param>
        public ShipCombat(CombatSpec spec, ShipClass? hull = null) : this(spec, hull, null) { }

        /// <param name="interior">The hull's inside, built from its facilities (<see cref="Interior.For(IReadOnlyList{FacilitySite}, double, double)"/>);
        /// null for its class's (or a platform's) as designed.</param>
        public ShipCombat(CombatSpec spec, ShipClass? hull, Interior interior)
        {
            Spec = spec;
            Hull = spec.Hull;
            for (int i = 0; i < spec.Layout.Length; i++) Subsystems.Add(new Subsystem(spec.Layout[i], i));
            double ratings = GeneratorRatings;
            if (ratings > 0)
                foreach (var s in Subsystems)
                    if (s.Kind == SubsystemKind.ShieldGenerator) s.MaxShield = s.Shield = spec.Shield * s.Def.Rating / ratings;
            Interior = interior ?? (hull.HasValue ? Interior.For(hull.Value, spec.Crew, spec.Marines) : Interior.ForPlatform(spec.Crew, spec.Marines));
            CarrierWings.Fill(this, spec.HangarCraft);
            CarrierWings.FillShuttles(this, spec.Shuttles);
        }

        /// <summary>
        /// The same hull after a yard refit (U.SPC-T65) to <paramref name="spec"/>, whose hardpoint list lines up
        /// with the old one slot for slot: its people (the same <see cref="Interior"/>), the share of its hull
        /// still standing, its fighters and boost carry over, and so does every hardpoint but
        /// <paramref name="changed"/> (its health, rounds and its share of shield charge); the part fitted at
        /// <paramref name="changed"/> is new, whole and loaded. A refit that changes the hull's spaces (a facility that is a
        /// compartment of its own, U.SPC-T470) passes the new <paramref name="interior"/>.
        /// </summary>
        public ShipCombat Refitted(CombatSpec spec, ShipClass? hull, int changed, Interior interior = null)
        {
            var c = new ShipCombat(spec, hull, interior ?? Interior)
            {
                Hull = spec.Hull * HullFraction,
                BoostFraction = BoostFraction,
                Readiness = Readiness,
                PhaserTuning = PhaserTuning,
            };
            // The same craft stay aboard, as many as the refitted hangars hold (U.SPC-T526).
            c.Hangar = Hangar;
            // A refit changes what is mounted, not the hull's frame: its structure and any strain in it carry over (U.SPC-T596).
            if (structure != null && spec.Layout.Length == Spec.Layout.Length && spec.LengthKm == Spec.LengthKm) c.structure = structure;
            CarrierWings.Trim(c, spec.HangarCraft);
            // And the same shuttles, as many as its shuttle berths hold (U.SPC-T590).
            c.ShuttleBay = ShuttleBay;
            CarrierWings.TrimShuttles(c, spec.Shuttles);
            for (int i = 0; i < c.Subsystems.Count && i < Subsystems.Count; i++)
            {
                if (i == changed) continue;
                var was = Subsystems[i];
                var now = c.Subsystems[i];
                if (was.Kind != now.Kind) continue;
                now.Hp = now.Def.MaxHp * (was.Hp <= 0 ? 0 : was.Health);
                now.Ammo = Math.Min(now.Ammo, was.Ammo);
                now.Cooldown = was.Cooldown;
                now.LastHitSeconds = was.LastHitSeconds;
                if (now.Kind == SubsystemKind.ShieldGenerator) now.Shield = was.MaxShield > 0 ? now.MaxShield * Math.Min(1, was.Shield / was.MaxShield) : 0;
            }
            return c;
        }

        /// <summary>The hull's shield generators' grades summed: each holds and regenerates its rating's share.</summary>
        double GeneratorRatings
        {
            get
            {
                double total = 0;
                foreach (var s in Subsystems) if (s.Kind == SubsystemKind.ShieldGenerator) total += s.Def.Rating;
                return total;
            }
        }

        /// <summary>
        /// Crippled but not destroyed: no working engines, a hull nearly gone, or a lost bridge on a
        /// badly holed ship. A disabled ship drifts and can be boarded.
        /// </summary>
        public bool Disabled => Alive && (EngineFactor <= 0 || HullFraction < 0.12 || (!BridgeIntact && HullFraction < 0.4));

        /// <summary>Marines aboard and free to go boarding (those not already fighting off boarders).</summary>
        public double MarinesAvailable => Interior.UnderBoarding ? 0 : Interior.Marines;

        public double HullFraction => Math.Max(0, Hull / Spec.Hull);
        public bool Alive => Hull > 0;

        public bool HasShieldGenerator
        {
            get
            {
                foreach (var s in Subsystems)
                    if (s.Kind == SubsystemKind.ShieldGenerator && !s.Destroyed) return true;
                return false;
            }
        }

        /// <summary>Total shield charge across every emitter — a coarse summary (HUD display, AI
        /// heuristics); combat itself drains the emitter whose patch is struck, via <see cref="EmitterFor"/>.</summary>
        public double Shield
        {
            get
            {
                double total = 0;
                foreach (var s in Subsystems) if (s.Kind == SubsystemKind.ShieldGenerator) total += s.Shield;
                return total;
            }
        }

        /// <summary>
        /// The shield emitter whose patch a point on the hull (ship lengths, hull frame: right, up, forward) lies in, live
        /// or shot away (U.SPC-T501): the emitter nearest to it in 3D, so each emitter's field covers the part of the hull
        /// nearer to it than to any other. Null for a hull with no emitters.
        /// </summary>
        public Subsystem PatchAt(DVec3 local)
        {
            Subsystem best = null;
            double bestD = double.MaxValue;
            foreach (var s in Subsystems)
            {
                if (s.Kind != SubsystemKind.ShieldGenerator) continue;
                double dx = s.Def.X - local.X, dy = s.Def.Y - local.Y, dz = s.Def.Z - local.Z;
                double d = dx * dx + dy * dy + dz * dz;
                if (d < bestD)
                {
                    bestD = d;
                    best = s;
                }
            }
            return best;
        }

        /// <summary>The live shield emitter whose field covers a point on the hull (see <see cref="PatchAt"/>), or null when
        /// that patch's emitter has been shot away (the hull is holed there, however whole the rest is) or there is none.</summary>
        public Subsystem EmitterFor(DVec3 local)
        {
            var s = PatchAt(local);
            return s != null && !s.Destroyed ? s : null;
        }

        public void ZeroAllShields()
        {
            foreach (var s in Subsystems) if (s.Kind == SubsystemKind.ShieldGenerator) s.Shield = 0;
        }

        /// <summary>Resets every generator's regen-pause timer (a ship entering a fresh battle starts able to regen).</summary>
        public void ResetShieldTimers()
        {
            foreach (var s in Subsystems) if (s.Kind == SubsystemKind.ShieldGenerator) s.LastHitSeconds = -999;
        }

        /// <summary>Regrows every live emitter's field whose patch has not been hit for <see cref="ShieldEmitters.RegenDelay"/>
        /// battle-seconds, each toward its own cap, sharing the hull's total regen rate (times <see cref="ShieldEmitters.RegenRate"/>)
        /// across its emitters by their ratings (evenly, on a stock hull). The patch under fire stays down while the rest recover.</summary>
        public void RegenShields(double nowSeconds, double dt)
        {
            double ratings = GeneratorRatings;
            if (ratings <= 0) return;
            double delay = ShieldEmitters.RegenDelay.Value, rate = Spec.ShieldRegen * ShieldEmitters.RegenRate.Value;
            if (rate <= 0) return;
            foreach (var s in Subsystems)
            {
                if (s.Kind != SubsystemKind.ShieldGenerator || s.Destroyed) continue;
                if (nowSeconds - s.LastHitSeconds > delay && s.Shield < s.MaxShield)
                {
                    double add = rate * s.Def.Rating / ratings * dt;
                    s.Shield = add >= s.MaxShield - s.Shield ? s.MaxShield : s.Shield + add;
                }
            }
        }

        public bool Armed
        {
            get
            {
                // Mines, sentry turrets and EMP rounds are a hull's means of getting away, not of fighting (U.SPC-T271).
                foreach (var s in Subsystems)
                    if (s.Def.IsWeapon && !s.Destroyed && !s.Def.Weapon.AntiSmall && (!s.Def.Weapon.Missile || s.Ammo > 0) && !s.Def.Weapon.Supporting) return true;
                return false;
            }
        }

        /// <summary>Share of engines still working (1 if the hull has none).</summary>
        public double EngineFactor
        {
            get
            {
                int total = 0, alive = 0;
                foreach (var s in Subsystems)
                {
                    if (s.Kind != SubsystemKind.Engine) continue;
                    total++;
                    if (!s.Destroyed) alive++;
                }
                return total == 0 ? 1 : (double)alive / total;
            }
        }

        public bool BridgeIntact
        {
            get
            {
                foreach (var s in Subsystems)
                    if (s.Kind == SubsystemKind.Bridge) return !s.Destroyed;
                return true;
            }
        }

        /// <summary>How hurt the hull is, 0 = pristine: the figure for repair and retreat decisions. Unlike <see cref="Damage"/> it does not
        /// thin out as more parts are fitted, so a hull at 30% reads 0.7 whatever it carries (U.SPC-T506).</summary>
        public double HullDamage => Math.Min(1, 1 - HullFraction);

        /// <summary>Fraction of everything (hull and hardpoints) that is damaged, 0 = pristine; diluted by part count, so use <see cref="HullDamage"/> to decide how hurt a ship is.</summary>
        public double Damage
        {
            get
            {
                double missing = 1 - HullFraction;
                foreach (var s in Subsystems) missing += (1 - s.Health) * 0.1;
                return Math.Min(1, missing / (1 + Subsystems.Count * 0.1));
            }
        }

        /// <summary>Rough fighting strength for strategic planning: weapon damage per second, scaled by health.</summary>
        public double Strength
        {
            get
            {
                if (!Alive) return 0;
                double dps = 0;
                foreach (var s in Subsystems)
                {
                    var w = s.Def.Weapon;
                    if (w == null || s.Destroyed || w.Supporting) continue;
                    dps += w.Damage * w.Salvo / w.Cooldown * (w.AntiSmall ? 0.3 : 1);
                }
                dps += Fighters * 3;
                return dps * (0.3 + 0.7 * HullFraction) * (Spec.Hull + Spec.Shield) / 1000;
            }
        }

        /// <summary>Repairs up to <paramref name="share"/> of the hull and every hardpoint by the same share.
        /// Does not restock ammo or crew: callers that owe a cost for those (<see cref="War"/>'s yard refit)
        /// gate them separately; callers with no such cost (defence platforms) call <see cref="FreeResupply"/> too.</summary>
        public void Repair(double share)
        {
            Hull = Math.Min(Spec.Hull, Hull + Spec.Hull * share);
            foreach (var s in Subsystems)
            {
                s.Hp = Math.Min(s.Def.MaxHp, Math.Max(s.Hp, 0) + s.Def.MaxHp * share * 1.5);
                if (!s.Destroyed) s.Restoring = 0;
                if (s.Kind == SubsystemKind.ShieldGenerator) s.Shield = s.MaxShield;
            }
            // A full repair rebuilds the hull's structure whole, pieces lost included; a partial one mends what is still on it (U.SPC-T596).
            if (share >= 1) structure = null;
            else structure?.Repair(share);
        }

        /// <summary>Fully restocks ammo and musters crew/marines back to spec, free. Only for callers (defence
        /// platforms) with no costed resupply logic of their own; ships are resupplied via <see cref="War"/>'s refit.</summary>
        public void FreeResupply()
        {
            foreach (var s in Subsystems)
                if (s.Def.Weapon != null) s.Ammo = s.Def.Weapon.Ammo;
            Interior.Muster(Spec.Crew, Spec.Marines);
        }

        /// <summary>Builds new craft for up to <paramref name="count"/> of the hangar's empty berths (U.SPC-T526).</summary>
        public void RestoreFighters(int count) => CarrierWings.Fill(this, count);

        /// <summary>Builds new boarding shuttles for up to <paramref name="count"/> of the empty shuttle berths (U.SPC-T486, U.SPC-T590).</summary>
        public void RestoreShuttles(int count) => CarrierWings.FillShuttles(this, count);
    }
}
