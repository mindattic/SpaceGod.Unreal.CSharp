using System;

namespace Unity.SpaceGod.Core
{
    /// <summary>What kind of adventure an <see cref="Opportunity"/> is.</summary>
    public enum OpportunityKind
    {
        /// <summary>A ship under attack in a battle right now, or drifting with dead drives after an interdiction pulse.</summary>
        Distress,

        /// <summary>A hull boarded, stripped and left adrift (<see cref="Ship.Derelict"/>).</summary>
        Derelict,

        /// <summary>The wreck a destroyed ship left at its battle site (<see cref="Wreck"/>), and whatever is still in its hold.</summary>
        Wreck,

        /// <summary>Raiders lying in wait (<see cref="ShipPhase.Lurk"/>) beside a lane that has seen trouble.</summary>
        PirateAmbush,

        /// <summary>A merchant riding a dangerous lane who would pay for an escort to the far ramp.</summary>
        ConvoyEscort,

        /// <summary>A price on a named pirate captain's head: the leader of a raider group.</summary>
        Bounty,

        /// <summary>Something odd out in the dark: rare, and the only kind not driven by the sim's own state.</summary>
        Anomaly,

        /// <summary>Escape pods drifting where a hull died (<see cref="EscapePod"/>), broadcasting until someone picks them up or their air runs out.</summary>
        EscapePods,
    }

    /// <summary>Where an <see cref="Opportunity"/> is in its life.</summary>
    public enum OpportunityState
    {
        /// <summary>Open: on the HUD, waiting for the player.</summary>
        Active,

        /// <summary>The player saw it through and was paid.</summary>
        Completed,

        /// <summary>Ignored until it expired, or the situation resolved (badly, or without the player) first.</summary>
        Failed,
    }

    /// <summary>What seeing an <see cref="Opportunity"/> through pays: credits, cargo crates and standing with a faction.</summary>
    public struct OpportunityReward
    {
        public double Credits;

        /// <summary>Crates of <see cref="CargoGood"/> put into the player's grid hold (<see cref="CargoHold"/>), 0 for none.</summary>
        public int CargoCrates;
        public Good CargoGood;

        /// <summary>Whose regard the player gains (null for none), and by how much.</summary>
        public Faction ReputationWith;
        public double Reputation;

        /// <summary>How much of <see cref="Credits"/> the <see cref="ReputationWith"/> faction itself pays (null: all of it).</summary>
        public double? PatronShare;

        public bool IsEmpty => Credits <= 0 && CargoCrates <= 0 && (ReputationWith == null || Reputation == 0);

        public override string ToString()
        {
            var parts = new System.Collections.Generic.List<string>();
            if (Credits > 0) parts.Add(Credits.ToString("N0", System.Globalization.CultureInfo.InvariantCulture) + " cr");
            if (CargoCrates > 0) parts.Add(CargoCrates + " x " + GoodInfo.Of(CargoGood).Name);
            if (ReputationWith != null && Reputation != 0)
                parts.Add((Reputation > 0 ? "+" : "") + Reputation.ToString("0", System.Globalization.CultureInfo.InvariantCulture) + " " + ReputationWith.Name);
            return parts.Count == 0 ? "nothing" : string.Join(", ", parts);
        }
    }

    /// <summary>
    /// One timed, location-anchored adventure near the player, spawned by <see cref="EventDirector"/>
    /// from something actually happening in the sim (a ship under attack, a derelict, raiders lurking
    /// off a dangerous lane...). It is anchored to the thing it is about -- a ship, a wreck, or a point
    /// in a body's frame -- so its HUD marker follows that thing; it carries a short text hook, an expiry
    /// and the reward it pays, and ends <see cref="OpportunityState.Completed"/> or
    /// <see cref="OpportunityState.Failed"/>.
    /// </summary>
    public sealed class Opportunity
    {
        public int Id;
        public OpportunityKind Kind;

        /// <summary>Identifies the sim thing this is about ("ship:12", "wreck:3", "lane:4"...), so it is
        /// never offered twice at once and is rested a while after it ends.</summary>
        public string SourceKey;

        /// <summary>A few words for the HUD marker ("Distress: Kestrel's Folly").</summary>
        public string Title;

        /// <summary>The short text hook: one or two sentences, in the voice of whoever is calling.</summary>
        public string Hook;

        public double SpawnJd, ExpiresJd;
        public OpportunityState State;

        /// <summary>When it completed or failed, and a line saying how.</summary>
        public double ResolvedJd;
        public string Outcome;

        public OpportunityReward Reward;

        /// <summary>The ship this is about (the one calling for help, the derelict, the merchant to escort,
        /// the captain or the lead raider); its marker rides on it.</summary>
        public Ship Ship;

        /// <summary>Who is asking (and pays): the ship's own faction for a distress call or an escort, the
        /// pirate hunters for an ambush or a bounty; null when nobody is (salvage, anomalies).</summary>
        public Faction Patron;

        /// <summary>The battle a distress call came out of, while it lasts.</summary>
        public Battle Battle;

        /// <summary>The wreck to investigate.</summary>
        public Wreck Wreck;

        /// <summary>The escape pods to pick up.</summary>
        public EscapePod Pod;

        /// <summary>The lane an ambush or an escort is on.</summary>
        public Lane Lane;

        /// <summary>For an anchor that is neither a ship nor a wreck (an anomaly): a point fixed in this body's frame, km.</summary>
        public int Body = -1;
        public DVec3 OffsetKm;

        /// <summary>The named pirate captain a bounty is on.</summary>
        public string Captain;

        /// <summary>Where it is now (heliocentric ecliptic km), refreshed every update from its anchor.</summary>
        public DVec3 HelioKm;

        /// <summary>The player has been on the scene (within <see cref="EventDirector.ArriveKm"/>) at least once.</summary>
        public bool Engaged;

        /// <summary>The piloted ship that reached it: paid on completion even if the player has let go of
        /// the helm (or lost the ship) by the time the situation resolves.</summary>
        public Ship EngagedBy;

        /// <summary>That ship's faction when it came (the player's purse), kept in case the ship later changes hands.</summary>
        public Faction EngagedFaction;

        /// <summary>A distress call from a ship actually in a fight (rather than one drifting after interdiction).</summary>
        public bool UnderAttack;

        /// <summary>A distress call from a ship that has run out of propellant and drifts waiting for a tanker:
        /// seen through by pumping some of the player's own reaction mass across.</summary>
        public bool DryTanks;

        public bool IsActive => State == OpportunityState.Active;

        public double DaysLeft(double jd) => Math.Max(0, ExpiresJd - jd);

        /// <summary>The marker's short tag on the HUD.</summary>
        public string MarkerLabel => LabelOf(Kind);

        /// <summary>The marker's colour on the HUD.</summary>
        public Rgb MarkerColor => ColorOf(Kind);

        public static string LabelOf(OpportunityKind k)
        {
            switch (k)
            {
                case OpportunityKind.Distress: return "DISTRESS";
                case OpportunityKind.Derelict: return "DERELICT";
                case OpportunityKind.Wreck: return "WRECK";
                case OpportunityKind.PirateAmbush: return "AMBUSH";
                case OpportunityKind.ConvoyEscort: return "ESCORT";
                case OpportunityKind.Bounty: return "BOUNTY";
                case OpportunityKind.EscapePods: return "PODS";
                default: return "ANOMALY";
            }
        }

        public static Rgb ColorOf(OpportunityKind k)
        {
            switch (k)
            {
                case OpportunityKind.Distress: return new Rgb(1f, 0.35f, 0.3f);
                case OpportunityKind.Derelict: return new Rgb(0.75f, 0.78f, 0.82f);
                case OpportunityKind.Wreck: return new Rgb(0.95f, 0.6f, 0.3f);
                case OpportunityKind.PirateAmbush: return new Rgb(1f, 0.2f, 0.55f);
                case OpportunityKind.ConvoyEscort: return new Rgb(0.45f, 0.9f, 0.55f);
                case OpportunityKind.Bounty: return new Rgb(1f, 0.85f, 0.3f);
                case OpportunityKind.EscapePods: return new Rgb(0.35f, 0.95f, 1f);
                default: return new Rgb(0.6f, 0.55f, 1f);
            }
        }

        public override string ToString() => MarkerLabel + ": " + Title;
    }
}
