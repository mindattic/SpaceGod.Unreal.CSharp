using System;
using System.Collections.Generic;

namespace Unity.SpaceGod.Core
{
    /// <summary>How the piloted ship stands toward a hulk it wants to latch on to.</summary>
    public enum LatchState
    {
        /// <summary>Further off than <see cref="SalvageSession.LatchRangeKm"/>.</summary>
        TooFar,

        /// <summary>Close enough, but still moving relative to it faster than <see cref="SalvageSession.MatchSpeedKmS"/>.</summary>
        TooFast,

        /// <summary>Alongside with velocities matched: the docking clamps can take hold.</summary>
        Ready,
    }

    /// <summary>What became of a part stripped from a hulk.</summary>
    public enum StripUse
    {
        /// <summary>Nothing was stripped (no such part, still fighting aboard, or nothing there).</summary>
        None,

        /// <summary>It replaced a shot-away hardpoint of the same kind on the player's own hull.</summary>
        Fitted,

        /// <summary>Broken down for parts: crates of a good into the grid hold.</summary>
        Scrapped,

        /// <summary>No room in the hold for what it breaks down into: it stays where it is.</summary>
        NoRoom,
    }

    /// <summary>The result of <see cref="SalvageSession.Strip"/>.</summary>
    public readonly struct StripResult
    {
        public readonly StripUse Use;
        public readonly Subsystem Part;

        /// <summary>The player's hardpoint it was fitted in place of (for <see cref="StripUse.Fitted"/>).</summary>
        public readonly Subsystem FittedTo;

        /// <summary>What it broke down into (for <see cref="StripUse.Scrapped"/>).</summary>
        public readonly Good Good;
        public readonly int Crates;

        public StripResult(StripUse use, Subsystem part, Subsystem fittedTo, Good good, int crates)
        {
            Use = use;
            Part = part;
            FittedTo = fittedTo;
            Good = good;
            Crates = crates;
        }
    }

    /// <summary>
    /// The player's ship latched on to a hulk: a derelict (<see cref="Ship.Derelict"/>, stripped and left
    /// adrift by <see cref="War"/>) or a battle's wreck (<see cref="Wreck"/>). The pilot first matches
    /// velocity alongside it (<see cref="Check"/>), the clamps take hold (<see cref="Latch(Ship, Ship)"/>), and
    /// then, once nobody hostile is left aboard, chooses: salvage what is in its hold into the grid hold
    /// (<see cref="SalvageCargo"/>), strip a component (<see cref="Strip"/>: fitted in place of a shot-away
    /// hardpoint of the same kind, or broken down into crates), or claim the hull if it can still fly
    /// (<see cref="Claim"/>). The inside of a derelict is its own <see cref="Interior"/>: raiders left aboard
    /// to finish stripping it hold it as defenders, and have to be fought through compartment by
    /// compartment by the player's marines (<see cref="SendBoarders"/>, <see cref="Step"/>) before anything
    /// can be taken; crew left behind (<see cref="Compartment.Survivors"/>) are rescued once it is clear.
    /// </summary>
    public sealed class SalvageSession
    {
        /// <summary>Gap the clamps can bridge between the two hulls, km, on top of half of each one's length.</summary>
        public const double LatchClearanceKm = 1.0;

        /// <summary>The fastest the two hulls may still be moving apart or together for the clamps to hold, km/s (20 m/s).</summary>
        public const double MatchSpeedKmS = 0.02;

        /// <summary>The share of a merchant's cargo raiders in a hurry leave behind when they strip it and cast it adrift.</summary>
        public const double LeftAboardShare = 0.2;

        /// <summary>How close the piloted ship's centre must come to a hulk's for the clamps to reach, km.</summary>
        public static double LatchRangeKm(double hulkLengthKm, double playerLengthKm) =>
            LatchClearanceKm + 0.5 * (Math.Max(0, hulkLengthKm) + Math.Max(0, playerLengthKm));

        /// <summary>Whether a ship <paramref name="relativeKm"/> from a hulk and moving at <paramref name="relativeVelocityKmS"/>
        /// relative to it can latch on, given the clamps' reach <paramref name="rangeKm"/>.</summary>
        public static LatchState Check(DVec3 relativeKm, DVec3 relativeVelocityKmS, double rangeKm)
        {
            if (relativeKm.Length > rangeKm) return LatchState.TooFar;
            if (relativeVelocityKmS.Length > MatchSpeedKmS) return LatchState.TooFast;
            return LatchState.Ready;
        }

        // ---------------------------------------------------------------- left adrift

        /// <summary>
        /// Fills a hull <paramref name="raiders"/> have just stripped and cast adrift with what a salvager
        /// will find: <paramref name="leftTonnes"/> of <paramref name="good"/> they had no room or time for,
        /// the ship's own stores, and -- by a roll fixed per hull -- crew who hid and survived, raiders still
        /// aboard finishing the job, both or neither.
        /// </summary>
        public static void Strand(Ship ship, Faction raiders, Good good, double leftTonnes, double jd, double aboard = double.PositiveInfinity)
        {
            ship.Salvage.Clear();
            if (leftTonnes > 0) ship.Salvage.Add(new SalvageStack(good, leftTonnes));
            var crewSpec = ship.Combat?.Spec;
            if (crewSpec != null && crewSpec.Crew >= 1)
            {
                ship.Salvage.Add(new SalvageStack(Good.Food, CargoFootprint.Of(Good.Food).Tonnes));
                ship.Salvage.Add(new SalvageStack(Good.MedicalSupplies, CargoFootprint.Of(Good.MedicalSupplies).Tonnes));
            }
            if (ship.Spec != null && ship.Spec.IsMilitary) ship.Salvage.Add(new SalvageStack(Good.Munitions, 2 * CargoFootprint.Of(Good.Munitions).Tonnes));
            if (ship.Combat == null) return;

            var rng = new Random(unchecked(ship.Id * 7919 + (int)Math.Floor(jd * 24)));
            double survivors = 0, lurkers = 0;
            if (rng.NextDouble() < 0.35) survivors = Math.Max(1, Math.Round(ship.Combat.Spec.Crew * (0.05 + 0.15 * rng.NextDouble())));
            // Only those still aboard can hide there.
            survivors = Math.Min(survivors, Math.Floor(aboard));
            if (rng.NextDouble() < 0.3) lurkers = 2 + rng.Next(7);
            ship.Combat.Interior.Abandon(survivors, lurkers, raiders?.MarineQuality ?? 1);
        }

        // ---------------------------------------------------------------- the session

        public readonly Ship Player;

        /// <summary>The derelict latched on to, or null for a wreck.</summary>
        public readonly Ship Derelict;

        /// <summary>The wreck latched on to, or null for a derelict.</summary>
        public readonly Wreck Wreck;

        /// <summary>The inside of the hulk: a derelict's own, or the gutted, open-to-space shell of a wreck.</summary>
        public readonly Interior Interior;

        /// <summary>What is still aboard to be salvaged, crate by crate.</summary>
        public List<SalvageStack> Manifest => Derelict != null ? Derelict.Salvage : Wreck.Salvage;

        public string Name => Derelict != null ? Derelict.Name : "the wreck of " + Wreck.Name;

        /// <summary>Whose the hulk was when the clamps took hold: the people any survivors belong to.</summary>
        public readonly Faction Owners;

        /// <summary>The raiders still aboard, if any are (the ones who stripped it).</summary>
        public Faction Holders => Derelict?.DerelictBy;

        /// <summary>Raiders aboard who will fight: nothing can be taken until they are dealt with.</summary>
        public bool Hostile => Interior.Marines > 0.5;

        /// <summary>The player's marines are fighting aboard right now.</summary>
        public bool Fighting => Interior.UnderBoarding;

        /// <summary>Seconds since the clamps took hold (the clock the fight aboard runs on).</summary>
        public double Seconds;

        public int CratesTaken;
        public int PartsStripped;
        public double SurvivorsRescued;
        public bool HostilesCleared;
        public bool Claimed;

        /// <summary>The clamps let go (<see cref="CastOff"/>, or the hull was claimed).</summary>
        public bool Ended;

        /// <summary>Anything came of it: something taken, someone saved, the raiders aboard beaten or the hull claimed.</summary>
        public bool Worked => CratesTaken > 0 || PartsStripped > 0 || SurvivorsRescued > 0 || HostilesCleared || Claimed;

        /// <summary>What happened, in order, for the HUD.</summary>
        public readonly List<string> Log = new List<string>();

        /// <summary>Rescued survivors waiting in the airlock: they crew the hull if it is claimed, else go aboard the player's ship.</summary>
        double survivorsHeld;
        readonly Random rng;

        SalvageSession(Ship player, Ship derelict, Wreck wreck, Interior interior, int seed)
        {
            Player = player;
            Derelict = derelict;
            Wreck = wreck;
            Interior = interior;
            Owners = derelict != null ? derelict.Faction : wreck.Faction;
            rng = new Random(seed);
            Note("Clamps on: latched to " + Name);
            if (Hostile) Note($"{Interior.Marines:0} {Holders?.Name ?? "raiders"} still aboard, armed");
            else RescueSurvivors();
        }

        /// <summary>Latches <paramref name="player"/> on to a derelict.</summary>
        public static SalvageSession Latch(Ship player, Ship derelict)
        {
            if (player == null || derelict == null || !derelict.Derelict || derelict.Destroyed || derelict.Combat == null) return null;
            return new SalvageSession(player, derelict, null, derelict.Combat.Interior, derelict.Id * 31 + player.Id);
        }

        /// <summary>Latches <paramref name="player"/> on to a wreck: a gutted shell, open to space, with nobody aboard.</summary>
        public static SalvageSession Latch(Ship player, Wreck wreck)
        {
            if (player == null || wreck == null) return null;
            var shell = wreck.Ship?.Spec != null ? Interior.For(wreck.Ship.Spec.Class, 0, 0) : Interior.ForPlatform(0, 0);
            foreach (var c in shell.Compartments) c.Vented = true;
            return new SalvageSession(player, null, wreck, shell, wreck.Id * 37 + player.Id);
        }

        /// <summary>Marines the player's ship could send across.</summary>
        public double MarinesReady => Player.Combat?.MarinesAvailable ?? 0;

        /// <summary>
        /// Sends every marine aboard the player's ship through the airlock to fight the raiders aboard.
        /// False if there is nobody to fight, a fight is already on, or the ship carries no marines.
        /// </summary>
        public bool SendBoarders()
        {
            if (Ended || !Hostile || Fighting || Player.Combat == null) return false;
            double marines = Player.Combat.Interior.TakeMarines(MarinesReady);
            if (marines < 0.5) return false;
            var airlock = Interior.Nearest(0, 0);
            Interior.Board(Player.Faction, Player.Faction.MarineQuality, marines, airlock, false, Seconds, rng);
            Note($"{marines:0} marines through the airlock at {airlock.Name}");
            return true;
        }

        /// <summary>Advances the fight aboard by <paramref name="dt"/> seconds.</summary>
        public void Step(double dt)
        {
            if (Ended || dt <= 0) return;
            Seconds += dt;
            if (!Fighting) return;
            Interior.Step(dt, Seconds, rng);
            if (Interior.Marines <= 0.5 || Interior.Captured)
            {
                // The last of them (or, with the bridge and engineering lost, all of them) throw down
                // their guns; the boarders come back aboard.
                foreach (var c in Interior.Compartments) c.Marines = 0;
                double back = Interior.Recall();
                Player.Combat.Interior.Embark(0, back);
                HostilesCleared = true;
                Note($"The raiders aboard are beaten; {back:0} marines come back aboard");
                RescueSurvivors();
            }
            else if (Interior.Boarder == null)
            {
                Note("The boarding party is wiped out");
            }
        }

        void RescueSurvivors()
        {
            double n = Interior.Survivors;
            if (n < 0.5) return;
            foreach (var c in Interior.Compartments) c.Survivors = 0;
            SurvivorsRescued += n;
            survivorsHeld += n;
            Note($"{n:0} survivors found sealed in and brought into the airlock");
        }

        /// <summary>
        /// Moves crates from the hulk into the player's grid hold until it runs out or the hold is full;
        /// what is taken comes off the manifest, so the next salvager finds less. Returns the crates moved.
        /// </summary>
        public int SalvageCargo()
        {
            if (Ended || Hostile) return 0;
            var hold = Player.PersonalHold;
            var manifest = Manifest;
            int moved = 0;
            for (int i = 0; i < manifest.Count;)
            {
                var stack = manifest[i];
                double crate = CargoFootprint.Of(stack.Good).Tonnes;
                double left = stack.Tonnes;
                bool full = false;
                // Half a crate's worth or more fills a crate; less is not worth the space.
                while (left >= crate * 0.5)
                {
                    if (hold.AddFirstFit(stack.Good) == null) { full = true; break; }
                    left -= crate;
                    moved++;
                }
                if (left < crate * 0.5) manifest.RemoveAt(i);
                else
                {
                    manifest[i] = new SalvageStack(stack.Good, left);
                    i++;
                }
                if (full) break;
            }
            CratesTaken += moved;
            Note(moved > 0 ? $"{moved} crate{(moved == 1 ? "" : "s")} into the hold" + (manifest.Count > 0 ? "; the hold is full" : "")
                : manifest.Count > 0 ? "No room in the hold" : "Nothing left aboard to take");
            return moved;
        }

        /// <summary>The hulk's parts still worth taking: guns, launchers, engines and shield generators that are not shot away.</summary>
        public List<Subsystem> Strippable
        {
            get
            {
                var list = new List<Subsystem>();
                var combat = Derelict?.Combat ?? Wreck?.Ship?.Combat;
                if (combat == null) return list;
                foreach (var s in combat.Subsystems)
                    if (!s.Destroyed && CanStrip(s.Kind)) list.Add(s);
                return list;
            }
        }

        static bool CanStrip(SubsystemKind k) => k != SubsystemKind.Bridge && k != SubsystemKind.Hangar && k != SubsystemKind.Reactor;

        /// <summary>The player's own hardpoint a part could be fitted in place of: a shot-away one of the same kind.</summary>
        public Subsystem FitFor(Subsystem part)
        {
            if (part == null || Player.Combat == null) return null;
            foreach (var s in Player.Combat.Subsystems)
                // A spinal mount's gun replaces only a spinal mount's, and a turret's only a turret's (U.SPC-T490).
                if (s.Destroyed && s.Kind == part.Kind && s.Def.Spinal == part.Def.Spinal) return s;
            return null;
        }

        /// <summary>The part most worth stripping: one that replaces a shot-away hardpoint on the player's hull
        /// first, else the soundest; null when nothing is left.</summary>
        public Subsystem BestPart()
        {
            Subsystem best = null;
            double bestScore = double.MinValue;
            foreach (var s in Strippable)
            {
                double score = s.Health + (FitFor(s) != null ? 10 : 0);
                if (score > bestScore) { bestScore = score; best = s; }
            }
            return best;
        }

        /// <summary>What a stripped part breaks down into when it is not fitted.</summary>
        public static Good ScrapOf(SubsystemKind k)
        {
            switch (k)
            {
                case SubsystemKind.LaserTurret:
                case SubsystemKind.HeavyLaser:
                case SubsystemKind.PointDefense:
                case SubsystemKind.ShieldGenerator:
                case SubsystemKind.TachyonLance:
                case SubsystemKind.PhaserArray:
                    return Good.Electronics;
                case SubsystemKind.MissileLauncher:
                case SubsystemKind.Torpedo:
                case SubsystemKind.TorpedoTube:
                case SubsystemKind.AntimatterTorpedo:
                case SubsystemKind.InterceptorMissile:
                case SubsystemKind.FlakCannon:
                case SubsystemKind.FighterGuns:
                    return Good.Munitions;
                case SubsystemKind.Engine:
                    return Good.Machinery;
                default:
                    return Good.Alloys;
            }
        }

        /// <summary>
        /// Cuts <paramref name="part"/> out of the hulk. If the player's hull has a shot-away hardpoint of the
        /// same kind, the part is fitted in its place at the part's own health (and a launcher keeps the rounds
        /// it still had); otherwise it is broken down into a crate (<see cref="ScrapOf"/>) for the hold. Either
        /// way the hulk loses it -- strip its engines and it will never fly again.
        /// </summary>
        public StripResult Strip(Subsystem part)
        {
            if (Ended || Hostile || part == null || part.Destroyed || !Strippable.Contains(part)) return new StripResult(StripUse.None, part, null, default, 0);
            var fit = FitFor(part);
            if (fit != null)
            {
                fit.Hp = fit.Def.MaxHp * part.Health;
                if (fit.Def.Weapon != null) fit.Ammo = Math.Min(fit.Def.Weapon.Ammo, part.Ammo);
                if (fit.Kind == SubsystemKind.ShieldGenerator) fit.Shield = 0;
                part.Hp = 0;
                PartsStripped++;
                Note($"Stripped a {KindName(part.Kind)} and fitted it in place of our own ({fit.Health:P0})");
                return new StripResult(StripUse.Fitted, part, fit, default, 0);
            }
            var good = ScrapOf(part.Kind);
            if (Player.PersonalHold.AddFirstFit(good) == null)
            {
                Note($"No room in the hold for a stripped {KindName(part.Kind)}");
                return new StripResult(StripUse.NoRoom, part, null, good, 0);
            }
            part.Hp = 0;
            PartsStripped++;
            CratesTaken++;
            Note($"Stripped a {KindName(part.Kind)}: 1 crate of {GoodInfo.Of(good).Name.ToLowerInvariant()} into the hold");
            return new StripResult(StripUse.Scrapped, part, null, good, 1);
        }

        /// <summary>A prize crew big enough to fly the hull home.</summary>
        public double PrizeCrewNeeded => Derelict == null ? 0 : Math.Max(2, Math.Round(Derelict.Combat.Spec.Crew * 0.3));

        /// <summary>Hands the player's ship can spare (it keeps half its own crew) plus any survivors rescued.</summary>
        public double PrizeCrewAvailable => survivorsHeld + (Player.Combat?.Interior.Crew ?? 0) * 0.5;

        /// <summary>Whether the hull can be claimed now, and if not, why not.</summary>
        public bool CanClaim(out string why)
        {
            why = null;
            if (Ended) why = "the clamps are off";
            else if (Derelict == null) why = "a wreck will never fly again";
            // Broken up (a derelict's days adrift ran out) or taken back under way since the clamps went on.
            else if (Derelict.Destroyed || !Derelict.Derelict) why = "it is gone";
            else if (Hostile) why = "raiders still hold it";
            else if (!Derelict.Combat.Alive || Derelict.Combat.Disabled)
                why = Derelict.Combat.EngineFactor <= 0 ? "its engines are gone" : "it is too badly holed to fly";
            else if (PrizeCrewAvailable < PrizeCrewNeeded) why = $"it needs a prize crew of {PrizeCrewNeeded:0}, and only {PrizeCrewAvailable:0} can be spared";
            return why == null;
        }

        /// <summary>
        /// Claims the derelict: a prize crew (rescued survivors first, then hands from the player's ship) goes
        /// aboard, it flies the player's colours under a new prefix, and -- given a <paramref name="fleet"/> --
        /// goes about that faction's business from where it lies. The session ends.
        /// </summary>
        public bool Claim(Fleet fleet, double jd)
        {
            if (!CanClaim(out _)) return false;
            double need = PrizeCrewNeeded;
            double fromSurvivors = Math.Min(survivorsHeld, need);
            survivorsHeld -= fromSurvivors;
            double fromShip = Player.Combat.Interior.TakeCrew(need - fromSurvivors);
            var ship = Derelict;
            var old = ship.Faction;
            var owner = Player.Faction;
            ship.Derelict = false;
            ship.DerelictBy = null;
            ship.Cargo = null;
            ship.Faction = owner;
            // Its old owners' prisoners are long gone from its brig (cast adrift with the hulk, or freed by the raiders).
            ship.Prisoners = 0;
            ship.PrisonerBounty = 0;
            if (old != owner && owner.Prefix.Length > 0 && old.Prefix.Length > 0 && ship.Name.StartsWith(old.Prefix + " "))
                ship.Name = owner.Prefix + ship.Name.Substring(old.Prefix.Length);
            Interior.Recall();
            Interior.HandOver(fromSurvivors + fromShip);
            // Its defenders are its own crew again, not the raiders who held it.
            Interior.DefenderQuality = 1;
            Claimed = true;
            Note($"Claimed: {ship.Name} flies the {owner.Name} colours with a prize crew of {fromSurvivors + fromShip:0}");
            Finish();
            if (fleet != null && ship.Battle == null && !ship.IsEscort) fleet.ScheduleNext(ship, jd);
            return true;
        }

        /// <summary>Lets go of the hulk: any marines still fighting aboard pull back, and survivors come aboard.</summary>
        public void CastOff()
        {
            if (Ended) return;
            if (Fighting)
            {
                double back = Interior.Recall();
                Player.Combat?.Interior.Embark(0, back);
                Note($"{back:0} marines pull back to the airlock");
            }
            Note("Clamps off");
            Finish();
        }

        void Finish()
        {
            if (survivorsHeld > 0) Player.Combat?.Interior.Embark(survivorsHeld, 0);
            survivorsHeld = 0;
            Ended = true;
        }

        /// <summary>A line for the event log: what came of it.</summary>
        public string Summary()
        {
            var parts = new List<string>();
            if (HostilesCleared) parts.Add("beat the raiders aboard");
            if (SurvivorsRescued > 0) parts.Add($"rescued {SurvivorsRescued:0} survivor{(SurvivorsRescued >= 1.5 ? "s" : "")}");
            if (CratesTaken > 0) parts.Add($"took {CratesTaken} crate{(CratesTaken == 1 ? "" : "s")}");
            if (PartsStripped > 0) parts.Add($"stripped {PartsStripped} part{(PartsStripped == 1 ? "" : "s")}");
            if (Claimed) parts.Add("claimed the hull");
            return parts.Count == 0 ? "found nothing worth taking" : string.Join(", ", parts);
        }

        public static string KindName(SubsystemKind k)
        {
            switch (k)
            {
                case SubsystemKind.LaserTurret: return "laser turret";
                case SubsystemKind.HeavyLaser: return "heavy laser";
                case SubsystemKind.PhaserArray: return "phaser array";
                case SubsystemKind.PlasmaCannon: return "plasma cannon";
                case SubsystemKind.MissileLauncher: return "missile launcher";
                case SubsystemKind.PointDefense: return "point-defence mount";
                case SubsystemKind.FighterGuns: return "gun pack";
                case SubsystemKind.Torpedo: return "torpedo rack";
                case SubsystemKind.TorpedoTube: return "torpedo tube";
                case SubsystemKind.AntimatterTorpedo: return "antimatter torpedo launcher";
                case SubsystemKind.FlakCannon: return "flak cannon";
                case SubsystemKind.InterceptorMissile: return "interceptor launcher";
                case SubsystemKind.Railgun: return "railgun";
                case SubsystemKind.Coilgun: return "coilgun";
                case SubsystemKind.TachyonLance: return "tachyon lance";
                case SubsystemKind.BoardingPods: return "pod launcher";
                case SubsystemKind.Drill: return "drill prow";
                case SubsystemKind.Engine: return "engine";
                case SubsystemKind.ShieldGenerator: return "shield generator";
                default: return k.ToString().ToLowerInvariant();
            }
        }

        void Note(string text)
        {
            Log.Add(text);
            if (Log.Count > 40) Log.RemoveAt(0);
        }
    }
}
