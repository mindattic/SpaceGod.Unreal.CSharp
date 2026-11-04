using System;
using System.Collections.Generic;

namespace Unity.SpaceGod.Core
{
    public enum CompartmentKind
    {
        Bow,
        Hangar,
        Hold,
        Quarters,
        Battery,
        Midships,
        Bridge,
        Engineering,
        /// <summary>A Breacher bay that fires Breacher rounds (U.SPC-T521, once the assault bay), or a round's own troop
        /// compartment (U.SPC-T466).</summary>
        BreacherBay,

        /// <summary>A marine habitat (U.SPC-T465): where the ship's marines live and drill, and muster from to repel boarders.</summary>
        Barracks,
    }

    /// <summary>
    /// One space inside a hull. Who is in it (crew, defending marines, boarders), whether it is open
    /// to vacuum, and its place in the ship: ship lengths along the keel and its neighbours. This is
    /// everything a shipboard battle view will need to draw the fight deck by deck.
    /// </summary>
    public sealed class Compartment
    {
        public int Index;
        public string Name;
        public CompartmentKind Kind;

        /// <summary>Position along the keel (ship lengths, nose +0.5) and height, for mapping hits and pods to a space.</summary>
        public double Z, Y;

        public readonly List<int> Links = new List<int>();

        /// <summary>Buried at the centre of the hull (U.SPC-T492, a warship's bridge): no pod, Breacher round or airlock
        /// opens into it (<see cref="Interior.Nearest"/> passes it by), so boarders reach it only through its neighbours.</summary>
        public bool Buried;

        public double Crew;
        public double Marines;
        public double Boarders;

        /// <summary>People sheltering here who take no part in a fight: the crew left behind on a derelict,
        /// sealed in and waiting for a rescue (see <see cref="Interior.Abandon"/>).</summary>
        public double Survivors;

        /// <summary>True once boarders hold it (no defenders left in it).</summary>
        public bool Taken;

        /// <summary>Open to space: a pod or a Breacher round's drill holed it. Crew die until the bulkheads seal.</summary>
        public bool Vented;
        public double SealIn;

        public bool Contested => Boarders > 0.01 && (Crew + Marines) > 0.01;
    }

    /// <summary>A line in a ship's boarding log: what happened, where, and when (battle seconds).</summary>
    public readonly struct BoardingNote
    {
        public readonly double Seconds;
        public readonly int Compartment;
        public readonly string Text;

        public BoardingNote(double seconds, int compartment, string text)
        {
            Seconds = seconds;
            Compartment = compartment;
            Text = text;
        }
    }

    /// <summary>
    /// One faction's part in a boarding (U.SPC-T392): the marines it has landed and the fighting strength they brought
    /// (marines times quality). Allied factions on one side fight a hull's defenders as one party; what each committed is
    /// its claim on the prize when it falls (see <see cref="PrizeSettlement"/>).
    /// </summary>
    public sealed class BoardingParty
    {
        public Faction Faction;
        public double Landed;
        public double Strength;

        /// <summary>Order of landing: the first faction aboard is 0.</summary>
        public int Order;
    }

    /// <summary>
    /// The inside of a hull and any fight going on in it. Defending marines sit in their barracks
    /// and react to a breach after a delay; the fight in each compartment trades casualties by
    /// numbers, training (<see cref="Faction.MarineQuality"/>) and a defender's advantage; boarders
    /// who clear a space push on toward the bridge and engineering by the shortest way. Holding both
    /// takes the ship; losing every boarder repels them. A breach vents its compartment: crew there
    /// die until the emergency bulkheads close.
    /// </summary>
    public sealed class Interior
    {
        public readonly List<Compartment> Compartments = new List<Compartment>();
        public readonly List<BoardingNote> Log = new List<BoardingNote>();

        /// <summary>Who is boarding (null when nobody is aboard who should not be): with allies boarding together
        /// (U.SPC-T392), the faction that landed first, which leads the party.</summary>
        public Faction Boarder;

        /// <summary>The quality of the boarders aboard: with several landings (allies', or the same faction's from hulls
        /// that drilled differently) the strength-weighted mean of everyone who came aboard.</summary>
        public double BoarderQuality = 1;

        /// <summary>The battle side the boarding party fights for, or -1 when the caller gave none (then no other faction
        /// may join it). Allies on that side may land and fight as one party with the first (U.SPC-T392).</summary>
        public int BoarderSide = -1;

        /// <summary>Every faction with marines in the boarding, in order of landing (U.SPC-T392). Kept once the hull is
        /// taken, until it is handed over, so the allies' claims on the prize can be settled.</summary>
        public readonly List<BoardingParty> Parties = new List<BoardingParty>();
        public double DefenderQuality = 1;

        /// <summary>How far the marines aboard have drilled in their habitats (U.SPC-T465), 0 (raw) to 1 (fully trained):
        /// it raises their quality (<see cref="DrillFactor"/>) both repelling boarders and boarding from this hull.</summary>
        public double Training;

        /// <summary>What the marines' drill multiplies their quality by.</summary>
        public double DrillFactor => MarineHabitats.Factor(Training);

        /// <summary>Set when the boarders hold the bridge and engineering.</summary>
        public bool Captured;

        /// <summary>Marines that boarded in total, and how many of them fell.</summary>
        public double BoardersLanded, BoardersLost, CrewLost, MarinesLost;

        double reactIn;

        /// <summary>The player's standing orders to the boarders and to the defenders (U.SPC-T468, see
        /// <see cref="BoardingCommand"/>): <see cref="BoardingOrder.Auto"/>, the resolver's own choice, unless a player
        /// commanding that side has said otherwise. Each order names its objective compartment, or -1.</summary>
        public BoardingOrder BoarderOrder, DefenderOrder;
        public int BoarderObjective = -1, DefenderObjective = -1;

        /// <summary>Casualties per second per unit of effective strength.</summary>
        const double Lethality = 0.012;

        /// <summary>Ship's crew fight, badly; marines are the defence.</summary>
        const double CrewFight = 0.15;

        const double DefenderAdvantage = 1.4;

        public double Crew
        {
            get
            {
                double n = 0;
                foreach (var c in Compartments) n += c.Crew;
                return n;
            }
        }

        public double Marines
        {
            get
            {
                double n = 0;
                foreach (var c in Compartments) n += c.Marines;
                return n;
            }
        }

        public double Boarders
        {
            get
            {
                double n = 0;
                foreach (var c in Compartments) n += c.Boarders;
                return n;
            }
        }

        public bool UnderBoarding => Boarder != null && Boarders > 0.5 && !Captured;

        /// <summary>Whether <paramref name="faction"/>, fighting for <paramref name="side"/>, may land boarders now: nobody
        /// is boarding, it is that faction's own boarding, or (U.SPC-T392) an ally's on the same side. Never once taken.</summary>
        public bool Admits(Faction faction, int side = -1) =>
            !Captured && (Boarder == null || Boarder == faction || (side >= 0 && side == BoarderSide));

        /// <summary>Whether <paramref name="faction"/> has marines in the boarding (as its lead or an ally).</summary>
        public bool IsParty(Faction faction)
        {
            if (faction == null || Boarder == null) return false;
            if (Boarder == faction) return true;
            foreach (var p in Parties) if (p.Faction == faction) return true;
            return false;
        }

        /// <summary>Whether more than one faction is boarding together (U.SPC-T392).</summary>
        public bool Joint => Parties.Count > 1;

        /// <summary>Who is boarding, as a log or the HUD names them: the lead, or every allied faction in it.</summary>
        public string BoarderNames
        {
            get
            {
                if (Parties.Count <= 1) return Boarder?.Name ?? "";
                var names = new List<string>();
                foreach (var p in Parties) names.Add(p.Faction.Name);
                return names.Count == 2 ? names[0] + " and " + names[1] : string.Join(", ", names.GetRange(0, names.Count - 1)) + " and " + names[names.Count - 1];
            }
        }

        /// <summary>Non-combatants sheltering aboard (a derelict's left-behind crew).</summary>
        public double Survivors
        {
            get
            {
                double n = 0;
                foreach (var c in Compartments) n += c.Survivors;
                return n;
            }
        }

        public int TakenCount
        {
            get
            {
                int n = 0;
                foreach (var c in Compartments) if (c.Taken) n++;
                return n;
            }
        }

        public Compartment Of(CompartmentKind kind)
        {
            foreach (var c in Compartments)
                if (c.Kind == kind) return c;
            return null;
        }

        /// <summary>The compartment nearest a point on the hull (ship lengths along the keel, height) that a breach or an
        /// airlock can open into: never one buried at the centre (U.SPC-T492), unless the hull has no other.</summary>
        public Compartment Nearest(double z, double y)
        {
            Compartment best = null;
            double bestD = double.MaxValue;
            foreach (var c in Compartments)
            {
                if (c.Buried) continue;
                double d = Math.Abs(c.Z - z) + Math.Abs(c.Y - y) * 0.5;
                if (d < bestD)
                {
                    bestD = d;
                    best = c;
                }
            }
            return best ?? (Compartments.Count > 0 ? Compartments[0] : null);
        }

        // ---------------------------------------------------------------- building

        /// <summary>The inside of a ship of the class as designed: its facilities' spaces (<see cref="FacilityLayouts.For"/>).</summary>
        public static Interior For(ShipClass cls, double crew, double marines) => For(FacilityLayouts.For(cls), crew, marines);

        /// <summary>A defence platform (or any station or satellite): a gun deck around a control room and its reactor.</summary>
        public static Interior ForPlatform(double crew, double marines) => For(FacilityLayouts.Platform, crew, marines);

        /// <summary>
        /// The inside of a hull built from its facilities (U.SPC-T470): each facility that is a space of its own
        /// (<see cref="FacilityKind.Compartment"/>) is a compartment, named and placed as its site says, joined on to the
        /// one before it along the keel or branching off the facility its site links to. A facility fitted later with no
        /// place of its own opens off midships (or the middle of the ship). Plate, racks and the like are no space.
        /// </summary>
        public static Interior For(IReadOnlyList<FacilitySite> facilities, double crew, double marines)
        {
            var i = new Interior();
            var made = new Compartment[facilities?.Count ?? 0];
            Compartment last = null;
            for (int f = 0; f < made.Length; f++)
            {
                var site = facilities[f];
                if (!(site.Def?.Compartment is CompartmentKind kind)) continue;
                var c = new Compartment { Index = i.Compartments.Count, Name = site.Name, Kind = kind, Z = site.Z, Y = site.Y, Buried = site.Buried };
                var parent = site.Link == FacilitySite.Keel ? last
                    : site.Link >= 0 && site.Link < made.Length ? made[site.Link]
                    : i.Of(CompartmentKind.Midships) ?? (i.Compartments.Count > 0 ? i.Compartments[i.Compartments.Count / 2] : null);
                if (site.Link == FacilitySite.Amidships && parent != null)
                {
                    c.Z = parent.Z;
                    c.Y = parent.Y - 0.05;
                }
                if (parent != null)
                {
                    parent.Links.Add(c.Index);
                    c.Links.Add(parent.Index);
                }
                i.Compartments.Add(c);
                made[f] = c;
                if (site.Link == FacilitySite.Keel) last = c;
            }
            if (i.Compartments.Count == 0)
                i.Compartments.Add(new Compartment { Index = 0, Name = "Hull", Kind = CompartmentKind.Midships });
            i.Crew0(crew, marines);
            return i;
        }

        /// <summary>The marine habitats' barracks aboard (U.SPC-T465).</summary>
        public IEnumerable<Compartment> Barracks
        {
            get
            {
                foreach (var c in Compartments) if (c.Kind == CompartmentKind.Barracks) yield return c;
            }
        }

        /// <summary>Whether the hull has a marine habitat at all; one without repels boarders with its crew alone.</summary>
        public bool HasBarracks => Of(CompartmentKind.Barracks) != null;

        /// <summary>Whether the ship's company works in a compartment: everywhere but the marines' barracks (unless the
        /// barracks are all there is).</summary>
        bool CrewSpace(Compartment c) => c.Kind != CompartmentKind.Barracks || !HasCrewSpace;

        bool HasCrewSpace
        {
            get
            {
                foreach (var c in Compartments) if (c.Kind != CompartmentKind.Barracks) return true;
                return false;
            }
        }

        int CrewSpaces
        {
            get
            {
                int n = 0;
                foreach (var c in Compartments) if (CrewSpace(c)) n++;
                return n;
            }
        }

        /// <summary>Spreads <paramref name="crew"/> evenly through the spaces the ship's company works in.</summary>
        void SpreadCrew(double crew, bool add)
        {
            int n = CrewSpaces;
            foreach (var c in Compartments)
            {
                if (!CrewSpace(c)) { if (!add) c.Crew = 0; continue; }
                c.Crew = (add ? c.Crew : 0) + crew / n;
            }
        }

        /// <summary>Quarters <paramref name="marines"/>: shared among the habitats' barracks, or, on a hull with none (a
        /// prize crew, a Breacher round's team in its troop compartment), amidships or wherever there is room.</summary>
        void Quarter(double marines)
        {
            if (marines <= 0) return;
            int n = 0;
            foreach (var _ in Barracks) n++;
            if (n > 0)
            {
                foreach (var c in Barracks) c.Marines += marines / n;
                return;
            }
            (Of(CompartmentKind.Quarters) ?? Of(CompartmentKind.Midships) ?? Of(CompartmentKind.BreacherBay) ?? Compartments[Compartments.Count / 2]).Marines += marines;
        }

        /// <summary>Crew spread through the ship; marines in their habitats' barracks (or amidships, or wherever there is room).</summary>
        void Crew0(double crew, double marines)
        {
            SpreadCrew(crew, false);
            Quarter(marines);
        }

        /// <summary>
        /// A day's drill (U.SPC-T465): marines in their habitats' barracks train, the whole complement going from raw to
        /// fully trained in <see cref="MarineHabitats.DrillDays"/> days. Nothing is learned without a habitat, with
        /// nobody in it, or while the ship is being boarded.
        /// </summary>
        public void Drill(double days)
        {
            if (days <= 0 || UnderBoarding || Captured) return;
            double inBarracks = 0;
            foreach (var c in Barracks) inBarracks += c.Marines;
            if (inBarracks < 0.5) return;
            Training = Math.Min(1, Training + days / Math.Max(1e-9, MarineHabitats.DrillDays.Value));
        }

        // ---------------------------------------------------------------- orders (U.SPC-T468)

        /// <summary>
        /// Gives one side of the fight a standing order. <see cref="BoardingOrder.Push"/> needs an objective
        /// compartment; without a valid one the order is refused (false). Orders change only where people move:
        /// the fighting in each compartment is the resolver's as ever.
        /// </summary>
        public bool Command(BoardingRole role, BoardingOrder order, int objective = -1)
        {
            if (Boarder == null || Captured) return false;
            if (order == BoardingOrder.Push && (objective < 0 || objective >= Compartments.Count)) return false;
            if (order != BoardingOrder.Push) objective = -1;
            if (role == BoardingRole.Boarders)
            {
                BoarderOrder = order;
                BoarderObjective = objective;
            }
            else
            {
                DefenderOrder = order;
                DefenderObjective = objective;
            }
            return true;
        }

        /// <summary>Hands one side back to the resolver: the auto-resolve.</summary>
        public void AutoResolve(BoardingRole role) => Command(role, BoardingOrder.Auto);

        /// <summary>Whether either side is under orders other than the resolver's.</summary>
        public bool Commanded => BoarderOrder != BoardingOrder.Auto || DefenderOrder != BoardingOrder.Auto;

        /// <summary>Every order back to the resolver's (a fight over, or nobody left to command it).</summary>
        public void ClearOrders()
        {
            BoarderOrder = DefenderOrder = BoardingOrder.Auto;
            BoarderObjective = DefenderObjective = -1;
        }

        // ---------------------------------------------------------------- the fight

        /// <summary>Marines come aboard at a compartment (a pod, a Breacher round's drill, or an airlock after docking).
        /// Returns false when nobody was landed (the ship is already taken, or a faction not allied with this one is
        /// boarding it), so the caller can take its marines back rather than lose them. <paramref name="side"/> is the battle
        /// side the marines fight for: an ally on the side already boarding joins the party and fights the defenders with
        /// it as one (U.SPC-T392); without a side, only the faction already boarding may land more.</summary>
        public bool Board(Faction boarder, double quality, double marines, Compartment at, bool breach, double seconds, Random rng, int side = -1)
        {
            if (!Admits(boarder, side)) return false;
            // Defenders are roused once per boarding: more boarders landing later don't stand them down again.
            bool fresh = Boarder == null;
            if (fresh)
            {
                Boarder = boarder;
                BoarderSide = side;
                Parties.Clear();
                BoarderQuality = quality;
            }
            else
            {
                // Everyone aboard fights as one party, as well as the mix of them who landed.
                double aboard = Boarders;
                BoarderQuality = aboard + marines > 1e-9 ? (aboard * BoarderQuality + marines * quality) / (aboard + marines) : quality;
            }
            JoinParty(boarder, marines, quality);
            at.Boarders += marines;
            BoardersLanded += marines;
            if (breach && !at.Vented)
            {
                at.Vented = true;
                // Emergency bulkheads: quick with the bridge giving the order, slower without.
                at.SealIn = 4 + rng.NextDouble() * 10;
            }
            double react = 15 + rng.NextDouble() * 30;
            if (fresh)
            {
                reactIn = react;
                ClearOrders();
            }
            Note(seconds, at.Index, $"{marines:0} {boarder.Name} marines board at {at.Name}" + (breach ? ", hull breached" : "")
                + (boarder != Boarder ? $", joining the {Boarder.Name}" : ""));
            return true;
        }

        void JoinParty(Faction faction, double marines, double quality)
        {
            BoardingParty party = null;
            foreach (var p in Parties) if (p.Faction == faction) party = p;
            if (party == null)
            {
                party = new BoardingParty { Faction = faction, Order = Parties.Count };
                Parties.Add(party);
            }
            party.Landed += marines;
            party.Strength += marines * quality;
        }

        /// <summary>The boarding is over without a prize (repelled, recalled, gone with the battle) or the prize is
        /// handed over: nobody's party now.</summary>
        void EndBoarding()
        {
            Boarder = null;
            BoarderSide = -1;
            Parties.Clear();
            ClearOrders();
        }

        /// <summary>Advances the fight aboard by <paramref name="dt"/> seconds. Returns true the moment the ship falls.</summary>
        public bool Step(double dt, double seconds, Random rng)
        {
            if (Captured || Boarder == null) return false;

            // Vacuum: crew caught in a holed compartment die until it is sealed.
            foreach (var c in Compartments)
            {
                if (!c.Vented || c.SealIn <= 0) continue;
                double killed = c.Crew * Math.Min(1, 0.06 * dt);
                c.Crew -= killed;
                CrewLost += killed;
                c.SealIn -= dt;
                if (c.SealIn <= 0) Note(seconds, c.Index, $"Emergency bulkheads seal {c.Name}");
            }

            // Defending marines move toward the fighting once they have been roused.
            reactIn -= dt;
            if (reactIn <= 0 && Boarders > 0.01 && DefenderOrder != BoardingOrder.Hold) Reinforce(dt);

            foreach (var c in Compartments)
            {
                if (c.Boarders <= 0.01)
                {
                    // Defenders who have fought their way back in hold it again.
                    if (c.Taken && c.Crew + c.Marines > 0.01)
                    {
                        c.Taken = false;
                        Note(seconds, c.Index, $"The crew retake {c.Name}");
                    }
                    continue;
                }
                double defenders = c.Crew + c.Marines;
                if (defenders <= 0.01)
                {
                    if (!c.Taken)
                    {
                        c.Taken = true;
                        c.Crew = c.Marines = 0;
                        Note(seconds, c.Index, $"Boarders take {c.Name}");
                    }
                    continue;
                }
                double attack = c.Boarders * BoarderQuality;
                // The bridge and engineering are fortified: blast doors, prepared positions, a last stand.
                double fortified = c.Kind == CompartmentKind.Bridge || c.Kind == CompartmentKind.Engineering ? 1.6 : 1;
                double defence = (c.Marines * DefenderQuality * DrillFactor + c.Crew * CrewFight) * DefenderAdvantage * fortified * (c.Vented ? 0.7 : 1);
                double defenderLosses = Math.Min(defenders, attack * Lethality * dt);
                double boarderLosses = Math.Min(c.Boarders, defence * Lethality * dt);
                // Marines stand in front; crew take what the marines cannot.
                double fromMarines = Math.Min(c.Marines, Math.Min(defenderLosses, defenderLosses * (c.Marines / defenders + 0.3)));
                double fromCrew = Math.Min(c.Crew, defenderLosses - fromMarines);
                c.Marines -= fromMarines;
                c.Crew -= fromCrew;
                MarinesLost += fromMarines;
                CrewLost += fromCrew;
                c.Boarders -= boarderLosses;
                BoardersLost += boarderLosses;
            }

            // Boarders who hold a space push on toward the bridge and engineering (or the objective they were given).
            if (BoarderOrder != BoardingOrder.Hold) Advance(dt);
            // An objective the boarders were sent for is theirs: they press on as the resolver would.
            if (BoarderOrder == BoardingOrder.Push && Compartments[BoarderObjective].Taken)
            {
                Note(seconds, BoarderObjective, $"{Compartments[BoarderObjective].Name} taken as ordered; the boarders press on");
                BoarderOrder = BoardingOrder.Auto;
                BoarderObjective = -1;
            }

            var bridge = Of(CompartmentKind.Bridge);
            var engineering = Of(CompartmentKind.Engineering);
            if (bridge.Taken && !bridge.Contested && engineering.Taken && !engineering.Contested)
            {
                Captured = true;
                Note(seconds, bridge.Index, $"The {BoarderNames} hold the bridge and engineering: the ship is taken ({Crew:0} crew surrender)");
                return true;
            }
            if (Boarders <= 0.5)
            {
                Note(seconds, -1, $"The {BoarderNames} boarders are wiped out; the crew hold the ship");
                foreach (var c in Compartments)
                {
                    c.Boarders = 0;
                    if (c.Taken && c.Crew + c.Marines <= 0.01) c.Taken = false;
                }
                EndBoarding();
                SealBreaches();
            }
            return false;
        }

        void Reinforce(double dt)
        {
            Compartment target = null;
            // Ordered to muster somewhere: to hold it before the boarders reach it, or to retake it.
            if (DefenderOrder == BoardingOrder.Push) target = Compartments[DefenderObjective];
            else
                foreach (var c in Compartments)
                    if (c.Boarders > 0.01 && (target == null || c.Kind == CompartmentKind.Bridge || c.Kind == CompartmentKind.Engineering)) target = c;
            if (target == null) return;
            foreach (var c in Compartments)
            {
                if (c == target || c.Marines <= 0.01 || c.Boarders > 0.01) continue;
                // A tenth of a compartment's marines move up each second.
                double move = c.Marines * Math.Min(1, 0.1 * dt);
                c.Marines -= move;
                target.Marines += move;
            }
        }

        void Advance(double dt)
        {
            // With nobody left fighting at the front, the rear guards have nothing to cover: they
            // gather and press on instead of holding what was taken for ever.
            bool front = false;
            foreach (var c in Compartments)
                if (c.Boarders > 0.01 && !c.Taken) front = true;
            // Every party's move is worked out from where they all stood at the start of the step, then made: a party
            // arriving in a held compartment does not move on again in the same step.
            var moves = new List<(Compartment from, Compartment to, double marines)>();
            foreach (var c in Compartments)
            {
                if (!c.Taken || c.Boarders <= (front ? 1 : 0.01)) continue;
                int next = NextToward(c.Index, BoarderOrder == BoardingOrder.Push ? BoarderObjective : -1);
                if (next < 0) continue;
                // Most of the party moves on; a rear guard holds what was taken.
                double move = c.Boarders * Math.Min(1, 0.08 * dt);
                if (front && c.Boarders - move < 2) move = Math.Max(0, c.Boarders - 2);
                moves.Add((c, Compartments[next], move));
            }
            foreach (var (from, to, marines) in moves)
            {
                from.Boarders -= marines;
                to.Boarders += marines;
            }
        }

        /// <summary>The neighbour on the shortest way from a compartment to <paramref name="objective"/>, or (-1) to the
        /// nearest of the bridge and engineering not yet held; -1 when there is no way, or the party is already there.</summary>
        int NextToward(int from, int objective)
        {
            var prev = new int[Compartments.Count];
            for (int k = 0; k < prev.Length; k++) prev[k] = -2;
            var queue = new Queue<int>();
            queue.Enqueue(from);
            prev[from] = -1;
            while (queue.Count > 0)
            {
                int here = queue.Dequeue();
                var c = Compartments[here];
                if (here != from && (objective >= 0 ? here == objective : !c.Taken && (c.Kind == CompartmentKind.Bridge || c.Kind == CompartmentKind.Engineering)))
                {
                    int step = here;
                    while (prev[step] != from) step = prev[step];
                    return step;
                }
                foreach (int n in c.Links)
                {
                    if (prev[n] != -2) continue;
                    prev[n] = here;
                    queue.Enqueue(n);
                }
            }
            return -1;
        }

        void Note(double seconds, int compartment, string text)
        {
            Log.Add(new BoardingNote(seconds, compartment, text));
            if (Log.Count > 60) Log.RemoveAt(0);
        }

        /// <summary>After a fight, survivors regroup: boarders aboard a taken ship become its prize crew.</summary>
        public void Settle()
        {
            ClearOrders();
            foreach (var c in Compartments)
            {
                c.Vented = false;
                c.SealIn = 0;
            }
        }

        /// <summary>
        /// The ship got away (released, fled, or the battle ran out) with the fight aboard undecided: the
        /// boarders are gone with the battle, and what they held is the crew's again. Without this the
        /// boarding would stay "under way" for good, keeping the ship's marines tied up.
        /// </summary>
        public void BoardersGone()
        {
            if (Captured || Boarder == null) return;
            foreach (var c in Compartments)
            {
                c.Boarders = 0;
                c.Taken = false;
            }
            EndBoarding();
            SealBreaches();
        }

        /// <summary>The bulkheads round every breach still venting close (they take seconds, and nothing aboard stops
        /// them once the boarders are gone): its timer would otherwise stand frozen, as <see cref="Step"/> runs only
        /// while boarders are aboard, and go on killing crew at the start of the next boarding.</summary>
        void SealBreaches()
        {
            foreach (var c in Compartments)
                if (c.Vented && c.SealIn > 0) c.SealIn = 0;
        }

        /// <summary>A prize crew takes over: the boarders become the ship's people; the old crew are prisoners.</summary>
        public void HandOver(double prizeCrew)
        {
            SpreadCrew(prizeCrew, false);
            foreach (var c in Compartments)
            {
                c.Marines = c.Boarders;
                c.Boarders = 0;
                c.Taken = false;
            }
            EndBoarding();
            Captured = false;
            // The ship's own marines are gone; the boarders now aboard never drilled in its habitats.
            Training = 0;
        }

        /// <summary>
        /// The hull is left adrift: its own people are gone, apart from <paramref name="survivors"/> who
        /// sealed themselves in at the far end from the airlocks, and <paramref name="lurkers"/> raiders
        /// still aboard stripping it, who hold the holds (or wherever there is loot) as defenders of
        /// <paramref name="lurkerQuality"/>. Any boarding that took it is over.
        /// </summary>
        public void Abandon(double survivors, double lurkers, double lurkerQuality)
        {
            foreach (var c in Compartments)
            {
                c.Crew = c.Marines = c.Boarders = c.Survivors = 0;
                c.Taken = false;
                c.Vented = false;
                c.SealIn = 0;
            }
            EndBoarding();
            Captured = false;
            DefenderQuality = lurkerQuality;
            Training = 0;
            if (survivors > 0) (Of(CompartmentKind.Engineering) ?? Compartments[Compartments.Count - 1]).Survivors = survivors;
            if (lurkers > 0) (Of(CompartmentKind.Hold) ?? Of(CompartmentKind.Midships) ?? Of(CompartmentKind.Bridge) ?? Compartments[0]).Marines = lurkers;
        }

        /// <summary>
        /// Boarders still aboard go back the way they came: they are taken off and counted, and whatever
        /// they held is nobody's again (a derelict cleared, or a boarding party called back to its ship).
        /// </summary>
        public double Recall()
        {
            double n = 0;
            foreach (var c in Compartments)
            {
                n += c.Boarders;
                c.Boarders = 0;
                c.Taken = false;
            }
            EndBoarding();
            Captured = false;
            return n;
        }

        /// <summary>Takes up to <paramref name="n"/> crew off the ship, evenly from every compartment; returns how many went.</summary>
        public double TakeCrew(double n)
        {
            double have = Crew;
            if (have <= 0 || n <= 0) return 0;
            double share = Math.Min(1, n / have);
            foreach (var c in Compartments) c.Crew -= c.Crew * share;
            return have * share;
        }

        /// <summary>Takes up to <paramref name="n"/> marines off the ship (a boarding party leaving through the airlock).</summary>
        public double TakeMarines(double n)
        {
            double have = Marines;
            if (have <= 0 || n <= 0) return 0;
            double share = Math.Min(1, n / have);
            foreach (var c in Compartments) c.Marines -= c.Marines * share;
            return have * share;
        }

        /// <summary>People come aboard: crew spread through the ship, marines to their barracks (the ship's own coming back
        /// from a boarding, so their drill is kept).</summary>
        public void Embark(double crew, double marines)
        {
            if (crew > 0) SpreadCrew(crew, true);
            Quarter(marines);
        }

        /// <summary>Back at a yard: crew and marines made up to strength. Raw recruits dilute the complement's drill.</summary>
        public void Muster(double crew, double marines)
        {
            double had = Marines;
            if (marines > had) Training *= had / marines;
            foreach (var c in Compartments) c.Marines = 0;
            Crew0(crew, marines);
            foreach (var c in Compartments)
            {
                c.Boarders = 0;
                c.Taken = false;
                c.Vented = false;
            }
            EndBoarding();
            Captured = false;
        }
    }
}
