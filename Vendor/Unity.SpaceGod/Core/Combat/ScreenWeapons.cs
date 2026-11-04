using System;
using System.Globalization;

namespace Unity.SpaceGod.Core
{
    /// <summary>
    /// Heavy warheads and the anti-fighter screen (U.SPC-T489, see docs/SHIP_ROSTER.md). Antimatter (photon) torpedoes are
    /// heavy guided warheads at medium range for frigates and capitals; a destroyer's screen of rotary flak cannons and
    /// interceptor missiles, beside its point-defence lasers, is the umbrella that shoots down missile swarms and fighters
    /// before they reach the capitals it escorts. The battle (<see cref="Battle"/>) fires them; this holds what they are
    /// worth, how a screen picks what to shoot and where a screening hull keeps station. Tunables are declared settings.
    /// </summary>
    [SettingsSource]
    public static class ScreenWeapons
    {
        // ------------------------------------------------------------ settings

        public static readonly RangeSetting AntimatterYield = new RangeSetting("weapons.antimatterYield", SettingGroups.Weapons,
            "Antimatter torpedo yield", 1, 0.25, 3, 0.05,
            "How hard an antimatter torpedo hits, against its design (1): its warhead is the hardest single hit in the game.",
            v => v.ToString("0.##", CultureInfo.InvariantCulture) + "x", "antimatter photon torpedo warhead damage frigate capital");

        public static readonly RangeSetting AntimatterToughness = new RangeSetting("weapons.antimatterToughness", SettingGroups.Weapons,
            "Antimatter torpedo toughness", 60, 10, 200, 5,
            "The damage a screen must do to an antimatter torpedo to stop it (a missile takes 12, a tube's torpedo 40).",
            v => v.ToString("0", CultureInfo.InvariantCulture) + " hp", "antimatter photon torpedo intercept point defence flak armour");

        public static readonly RangeSetting FlakBurst = new RangeSetting("weapons.flakBurst", SettingGroups.Weapons,
            "Flak burst size", 1, 0.25, 2.5, 0.05,
            "How wide a flak shell's burst is, against its design (1, 0.4 km): every hostile missile and fighter inside it is hurt, not only the one it was laid on.",
            v => v.ToString("0.##", CultureInfo.InvariantCulture) + "x", "flak burst radius area screen destroyer missile swarm fighter");

        public static readonly RangeSetting FlakDamage = new RangeSetting("weapons.flakDamage", SettingGroups.Weapons,
            "Flak damage", 1, 0, 3, 0.1,
            "How hard a flak burst hits what is inside it, against its design (1); 0 makes flak harmless.",
            v => v.ToString("0.##", CultureInfo.InvariantCulture) + "x", "flak damage screen destroyer");

        public static readonly RangeSetting InterceptorReliability = new RangeSetting("weapons.interceptorReliability", SettingGroups.Weapons,
            "Interceptor reliability", 0.85, 0, 1, 0.05,
            "The chance an interceptor missile that reaches the round it is hunting kills it.",
            v => (v * 100).ToString("0", CultureInfo.InvariantCulture) + "%", "interceptor missile screen destroyer kill chance");

        public static readonly RangeSetting ScreenStation = new RangeSetting("weapons.screenStation", SettingGroups.Weapons,
            "Screen station-keeping", 0.6, 0, 1, 0.05,
            "How closely a destroyer carrying flak or interceptors keeps between the capital it screens and the enemy, rather than fighting its own fight (0 lets it roam).",
            v => (v * 100).ToString("0", CultureInfo.InvariantCulture) + "%", "screen escort destroyer umbrella station capital");

        // ------------------------------------------------------------ fixed figures

        /// <summary>An interceptor's own toughness and how long it flies before it gives up, seconds.</summary>
        public const double InterceptorHp = 8, InterceptorLifeSeconds = 30;

        /// <summary>How near an interceptor's proximity fuze goes off, km, and how far off it looks for another round when
        /// the one it was hunting is gone, km.</summary>
        public const double FuzeKm = 0.12, ReacquireKm = 4;

        /// <summary>How far a screening hull keeps out from its ward's hull toward the threat, km, and how far off a ward
        /// can be for a hull to screen it at all.</summary>
        public const double StandoffKm = 1.5, WardSearchKm = 30;

        // ------------------------------------------------------------ rules

        /// <summary>A screen mount: a flak cannon or an interceptor launcher (point defence lasers are every hull's own).</summary>
        public static bool IsScreenMount(SubsystemKind k) => k == SubsystemKind.FlakCannon || k == SubsystemKind.InterceptorMissile;

        /// <summary>Whether a hull carries a working screen: a live flak cannon, or an interceptor launcher with rounds left.</summary>
        public static bool Screens(ShipCombat c)
        {
            foreach (var s in c.Subsystems)
                if (IsScreenMount(s.Kind) && !s.Destroyed && (s.Kind != SubsystemKind.InterceptorMissile || s.Ammo > 0)) return true;
            return false;
        }

        /// <summary>What a launcher's round flies as.</summary>
        public static MissileKind KindOf(SubsystemKind launcher, WeaponSpec w)
        {
            if (w.EmpSeconds > 0) return MissileKind.Emp;
            switch (launcher)
            {
                case SubsystemKind.AntimatterTorpedo: return MissileKind.Antimatter;
                case SubsystemKind.InterceptorMissile: return MissileKind.Interceptor;
                case SubsystemKind.Torpedo:
                case SubsystemKind.TorpedoTube: return MissileKind.Torpedo;
                default: return MissileKind.Missile;
            }
        }

        /// <summary>How much damage a screen must do to a launcher's round to stop it: a capital tube's torpedo is tougher
        /// than a bomber's (U.SPC-T377), an antimatter torpedo tougher still.</summary>
        public static double RoundHp(SubsystemKind launcher)
        {
            switch (launcher)
            {
                case SubsystemKind.AntimatterTorpedo: return AntimatterToughness.Value;
                case SubsystemKind.TorpedoTube: return 40;
                case SubsystemKind.Torpedo: return 25;
                case SubsystemKind.InterceptorMissile: return InterceptorHp;
                default: return 12;
            }
        }

        /// <summary>The warhead a launcher's round carries, with the settings applied.</summary>
        public static double Warhead(SubsystemKind launcher, WeaponSpec w) =>
            launcher == SubsystemKind.AntimatterTorpedo ? w.Damage * AntimatterYield.Value : w.Damage;

        /// <summary>A round a screen shoots at: anything guided at a hull or a small craft, but not another screen's
        /// interceptor (no threat to anything) nor a boarding pod's squad (point defence lasers still take those).</summary>
        public static bool IsThreat(Missile m) => !m.Gone && m.Kind != MissileKind.Interceptor;

        /// <summary>
        /// How badly a screen wants a hostile round dead: its warhead, more for one closing on a capital ship, and the nearer
        /// it is to what it is homing on the more urgent. A screen takes the worst threat in its reach, not the nearest
        /// round, so the torpedo bound for the battleship goes before the missile bound for a frigate.
        /// </summary>
        public static double Threat(Missile m)
        {
            double damage = Math.Max(m.Damage, 1);
            // A Breacher round tumbling adrift threatens nobody (U.SPC-T519): the last thing a screen spends itself on.
            if (m.Adrift) return 1e-3;
            var t = m.Target;
            if (t == null) return damage;
            double capital = t.Kind != UnitKind.Fighter && t.LengthKm >= 1 ? 1.5 : 1;
            double toGo = Math.Max(0, (t.Pos - m.Pos).Length - t.Radius);
            return damage * capital / (1 + toGo);
        }

        /// <summary>
        /// The hull a screening ship guards: its flagship if it is the bigger, else the biggest friendly hull at least
        /// half again its size within <see cref="WardSearchKm"/>; null when there is nothing to screen (it is the
        /// biggest there, or alone).
        /// </summary>
        public static BattleUnit WardOf(Battle b, BattleUnit u)
        {
            var leader = u.Leader;
            if (leader != null && leader.Present && !leader.Fleeing && leader.Combat.Spec.Hull > u.Combat.Spec.Hull) return leader;
            BattleUnit best = null;
            foreach (var o in b.Units)
            {
                if (o == u || o.Side != u.Side || !o.Present || o.Kind == UnitKind.Fighter || o.Captured || o.Fleeing || o.Surrendering) continue;
                if (o.Combat.Spec.Hull < u.Combat.Spec.Hull * 1.5 || (o.Pos - u.Pos).Length > WardSearchKm) continue;
                if (best == null || o.Combat.Spec.Hull > best.Combat.Spec.Hull) best = o;
            }
            return best;
        }

        /// <summary>Where a screening hull keeps station: out from its ward's hull toward the threat by <see cref="StandoffKm"/>,
        /// so what comes at the ward passes through its umbrella first.</summary>
        public static DVec3 StationOn(BattleUnit ward, DVec3 threat)
        {
            var dir = threat - ward.Pos;
            dir = dir.LengthSquared > 1e-9 ? dir.Normalized : ward.Forward;
            return ward.Pos + dir * (ward.Radius + StandoffKm);
        }
    }
}
