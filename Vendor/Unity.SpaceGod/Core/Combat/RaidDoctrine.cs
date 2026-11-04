using System;

namespace Unity.SpaceGod.Core
{
    /// <summary>
    /// How a pirate band weighs a mark and how it works one (U.SPC-T374). Raiders are in it for the money, and they size
    /// up a target before they spring on it:
    ///
    ///  - <b>a juicy, lightly escorted mark is worth more risk.</b> A band takes on escorts and guns up to a share of its
    ///    own strength (its <see cref="Nerve"/>), and that share grows with what the mark is worth: an empty hauler has to
    ///    be all but unguarded, a hold of electronics is worth tangling with a real escort for;
    ///  - <b>a civilian hull draws disproportionate heat.</b> Robbing a passenger liner, a yacht or a survey ship brings the
    ///    navies down far harder than robbing a freighter (<see cref="CivilianHeat"/>), so a band leaves it alone unless
    ///    the contract it flies on pays well or someone aboard is worth ransoming (<see cref="CivilianMinPrize"/>);
    ///  - <b>disable and loot, not destroy.</b> Raiders strip a merchant's shields and engines (<see cref="SubsystemWeight"/>),
    ///    hold their fire once it lies dead in space, demand it jettison its cargo, send drones to collect it and then
    ///    withdraw (see <see cref="Battle"/>): a wrecked hull is one less future mark, and a hull destroyed brings down far
    ///    more heat on the clan than a clean robbery (<see cref="DestroyHeat"/>, paid as a price on the clan's head).
    ///
    /// Pure functions of a mark's worth, its heat and the guns about it, so the doctrine can be tuned and exercised without
    /// a running war; <see cref="War"/> asks it before a band springs an ambush, pulses a lane or falls on a port's
    /// merchants, and <see cref="Battle"/> fights by it.
    /// </summary>
    public static class RaidDoctrine
    {
        /// <summary>The share of its own strength in hostile guns a band takes on for a mark worth nothing much: the old
        /// gate-ambush rule.</summary>
        public const double BaseNerve = 0.8;

        /// <summary>The most a band will ever risk, however rich the mark: hostile guns up to this share of its own strength.</summary>
        public const double MaxNerve = 1.6;

        /// <summary>How much more nerve each tenfold of <see cref="ReferenceHaul"/> buys.</summary>
        public const double Greed = 0.5;

        /// <summary>A haul worth a band's while (credits at base prices): a freighter's hold of mid-priced goods.</summary>
        public const double ReferenceHaul = 5e6;

        /// <summary>How much harder the navies come down on a clan for working a civilian hull than a merchant one.</summary>
        public const double CivilianHeat = 4;

        /// <summary>A civilian hull is left alone unless it is worth at least this (credits): a contract that pays well, or
        /// someone aboard worth ransoming.</summary>
        public const double CivilianMinPrize = 3e6;

        /// <summary>A passenger fare at least this big (credits) means someone aboard worth ransoming.</summary>
        public const double VipFare = 4e5;

        /// <summary>A VIP's ransom, as a multiple of the fare they paid.</summary>
        public const double VipRansomMultiple = 10;

        /// <summary>The price states put on a clan's head for a merchant hull it destroys, as a share of the hull and its
        /// cargo (a robbery costs it only the cargo's worth, see <see cref="PirateClans.PledgeShare"/>).</summary>
        public const double DestroyHeatShare = 1.5;

        /// <summary>
        /// A civilian hull, not a merchant: a free pilot's yacht, survey ship or prospector, or any hull with passengers or
        /// colonists aboard. Raiding one draws <see cref="CivilianHeat"/>.
        /// </summary>
        public static bool IsCivilian(Ship ship, Contracts contracts = null)
        {
            if (ship == null || ship.Spec.IsMilitary || ship.Faction == null || ship.Faction.IsPirate) return false;
            if (ship.Role == ShipRole.Free) return true;
            if (contracts != null)
                foreach (var c in contracts.All)
                    if (c.Taker == ship && c.IsActive && c.Aboard && c.People > 0 && (c.Kind == ContractKind.Passengers || c.Kind == ContractKind.Colonists))
                        return true;
            return false;
        }

        /// <summary>The heat working <paramref name="ship"/> draws: <see cref="CivilianHeat"/> for a civilian hull, 1 for a merchant.</summary>
        public static double Heat(Ship ship, Contracts contracts = null) => IsCivilian(ship, contracts) ? CivilianHeat : 1;

        /// <summary>
        /// What <paramref name="ship"/> is worth to raiders, credits: its cargo at base prices, plus, for every job it flies
        /// on, what the job pays (an owner pays to have a well-paid job's hull back), and the ransom of whoever is aboard
        /// (a head's ransom each, and a VIP's, <see cref="VipRansomMultiple"/> times a fare of <see cref="VipFare"/> or more).
        /// </summary>
        public static double PreyValue(Ship ship, Contracts contracts = null)
        {
            if (ship == null) return 0;
            double value = ship.HasCargo ? ship.CargoTonnes * GoodInfo.Of(ship.CargoGood).BasePrice : 0;
            // A lane tender (U.SPC-T481): whoever wants a system's lanes cut pays well for one.
            if (LaneUpkeep.IsTender(ship)) value += LaneUpkeep.TenderValue.Value;
            if (contracts != null)
                foreach (var c in contracts.All)
                {
                    if (c.Taker != ship || !c.IsActive) continue;
                    if (c.Kind != ContractKind.Freight && c.Kind != ContractKind.Passengers && c.Kind != ContractKind.Colonists) continue;
                    value += c.Pay;
                    if (!c.Aboard || c.People <= 0) continue;
                    value += c.People * Crews.RansomPerHead;
                    if (c.Kind == ContractKind.Passengers && c.Pay >= VipFare) value += c.Pay * VipRansomMultiple;
                }
            return value;
        }

        /// <summary>The share of its own strength in hostile guns a band will take on for a mark worth <paramref name="value"/>.</summary>
        public static double Nerve(double value) =>
            Math.Min(MaxNerve, BaseNerve + Greed * Math.Log10(1 + Math.Max(0, value) / ReferenceHaul));

        /// <summary>
        /// Whether a band of <paramref name="bandStrength"/> springs on a mark worth <paramref name="value"/> that draws
        /// <paramref name="heat"/>, with <paramref name="guns"/> of hostile strength about it: a civilian only when it is
        /// worth <see cref="CivilianMinPrize"/>, and never into more guns than its <see cref="Nerve"/> allows.
        /// </summary>
        public static bool WorthTaking(double value, double heat, double guns, double bandStrength)
        {
            if (heat > 1 && value < CivilianMinPrize) return false;
            return guns <= bandStrength * Nerve(value);
        }

        /// <summary>How a band ranks marks against each other: worth, over the heat it draws and the guns about it.</summary>
        public static double Score(double value, double heat, double guns, double bandStrength) =>
            (value + 1) / Math.Max(1, heat) / (1 + Math.Max(0, guns) / Math.Max(1e-6, bandStrength));

        /// <summary>The price states put on a clan's head for destroying <paramref name="ship"/> outright rather than robbing
        /// it, credits: <see cref="DestroyHeatShare"/> of the hull and what it was worth, times its <see cref="Heat"/>.</summary>
        public static double DestroyHeat(Ship ship, Contracts contracts = null)
        {
            if (ship == null || ship.Spec.IsMilitary || ship.Faction == null || ship.Faction.IsPirate) return 0;
            return DestroyHeatShare * (Economy.HullPrice(ship.Spec.Class) + PreyValue(ship, contracts)) * Heat(ship, contracts);
        }

        /// <summary>Whether a raider fights <paramref name="target"/> by this doctrine: an AI pirate against a merchant or
        /// civilian hull, which it means to rob, not destroy. Not a lane tender (U.SPC-T481): that it means to destroy, to cut
        /// the lane it keeps.</summary>
        public static bool Robs(Faction raider, Ship target) =>
            raider != null && raider.IsPirate && target != null && !target.Spec.IsMilitary && target.Faction != null && !target.Faction.IsPirate &&
            !LaneUpkeep.IsTender(target);

        /// <summary>
        /// How far (km, in a battle's own frame) a raider's gunners reckon a gate picket nearer than it is (U.SPC-T480): a
        /// picket alive is a navy forewarned, so a band kills it before anything else, merchants included.
        /// </summary>
        public const double PicketFirstKm = 30;

        /// <summary>The share of its own strength in hostile guns a band lying off a gate takes on to silence a gate picket
        /// there (U.SPC-T480): more than for warships alone (<see cref="BaseNerve"/>), since a picket left alive gives it away.</summary>
        public const double PicketNerve = 1.2;

        /// <summary>Whether <paramref name="ship"/> is a gate picket (see <see cref="GatePickets"/>).</summary>
        public static bool IsPicket(Ship ship) => ship != null && ship.Spec.Class == ShipClass.GatePicket;

        /// <summary>
        /// How much nearer than it is (km) a raider of <paramref name="raider"/> reckons <paramref name="target"/> when it
        /// picks what to shoot at (see <see cref="Battle.PickTarget"/>): <see cref="PicketFirstKm"/> for a gate picket of
        /// anyone's but the raiders', 0 for anything else, and 0 for anyone who is no raider.
        /// </summary>
        public static double TargetBias(Faction raider, Ship target) =>
            raider != null && raider.IsPirate && IsPicket(target) && target.Faction != null && !target.Faction.IsPirate ? PicketFirstKm : 0;

        /// <summary>
        /// How much a raider's gunners favour <paramref name="kind"/> on a hull it means to rob: shields and engines first, to
        /// leave it dead in space; its guns and the rest after; never the reactor, which would take the prize with it.
        /// </summary>
        public static double SubsystemWeight(SubsystemKind kind, bool shieldUp)
        {
            switch (kind)
            {
                case SubsystemKind.Engine: return 4;
                case SubsystemKind.ShieldGenerator: return shieldUp ? 4 : 0.3;
                case SubsystemKind.Reactor: return 0;
                case SubsystemKind.Bridge: return 0.2;
                case SubsystemKind.Hangar: return 0.3;
                default: return 1;
            }
        }
    }
}
