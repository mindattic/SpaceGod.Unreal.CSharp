using System;
using System.Collections.Generic;
using System.Globalization;

namespace Unity.SpaceGod.Core
{
    /// <summary>
    /// A standing agreement between two polities (U.SPC-T427): a non-aggression pact, a trade pact, an alliance, tribute or
    /// vassalage. What it does is its kind's: whether it keeps the two at peace, whether each joins the other's wars, and its
    /// daily upkeep (trade, tribute, a vassal's levy). A new kind is one <see cref="Register"/> call.
    /// </summary>
    public sealed class PactKind
    {
        static readonly List<PactKind> all = new List<PactKind>();

        public static IReadOnlyList<PactKind> All => all;

        public readonly string Name, Description;

        /// <summary>One side gives and the other takes (<see cref="Pact.A"/> pays or serves <see cref="Pact.B"/>); else both alike.</summary>
        public readonly bool Directed;

        /// <summary>Neither side may go to war with the other while it is in force, short of breaking it.</summary>
        public readonly bool KeepsPeace;

        /// <summary>Each side joins the wars the other is attacked in (a vassal joins every war of its overlord).</summary>
        public readonly bool CallsToArms;

        /// <summary>How long it runs (days); NaN for until it is broken.</summary>
        public readonly double Days;

        /// <summary>How much warmer it makes each side to the other (the opinion it draws toward, see <see cref="Statecraft.Opinion"/>).</summary>
        public readonly double Warmth;

        /// <summary>Its upkeep each day; false when it can no longer be kept (the payer cannot pay), and it lapses.</summary>
        public readonly Func<Statecraft, Pact, double, bool> Daily;

        PactKind(string name, string description, bool directed, bool keepsPeace, bool callsToArms, double days, double warmth,
            Func<Statecraft, Pact, double, bool> daily)
        {
            Name = name;
            Description = description;
            Directed = directed;
            KeepsPeace = keepsPeace;
            CallsToArms = callsToArms;
            Days = days;
            Warmth = warmth;
            Daily = daily;
        }

        public static PactKind Register(string name, string description, bool directed, bool keepsPeace, bool callsToArms, double days,
            double warmth, Func<Statecraft, Pact, double, bool> daily = null)
        {
            var k = new PactKind(name, description, directed, keepsPeace, callsToArms, days, warmth, daily);
            all.Add(k);
            return k;
        }

        public static PactKind Find(string name) => all.Find(k => k.Name == name);

        public static readonly PactKind NonAggression = Register("Non-aggression pact", "Neither goes to war with the other for five years",
            false, true, false, 5 * 365.25, 10);

        public static readonly PactKind Trade = Register("Trade pact", "Tariffs lifted between them: both treasuries gain from the trade every day",
            false, false, false, double.NaN, 15, (s, p, jd) => s.KeepTrade(p));

        public static readonly PactKind Alliance = Register("Alliance", "At peace with each other, and each joins the wars the other is attacked in",
            false, true, true, double.NaN, 35);

        public static readonly PactKind Tribute = Register("Tribute", "One pays the other a sum every month for two years",
            true, true, false, 2 * 365.25, -5, (s, p, jd) => s.KeepTribute(p, jd));

        public static readonly PactKind Vassalage = Register("Vassalage", "The vassal pays a share of its treasury to its overlord, fights its wars and is defended in its own",
            true, true, true, double.NaN, 5, (s, p, jd) => s.KeepVassalage(p, jd));

        public override string ToString() => Name;
    }

    /// <summary>A pact in force between two polities (U.SPC-T427). For a directed kind <see cref="A"/> pays or serves <see cref="B"/>.</summary>
    public sealed class Pact
    {
        public PactKind Kind;
        public Faction A, B;
        public double SinceJd, UntilJd = double.NaN;

        /// <summary>Tribute a month (credits).</summary>
        public double Amount;

        public bool Involves(Faction f) => f != null && (A == f || B == f);

        public bool Between(Faction x, Faction y) => (A == x && B == y) || (A == y && B == x);

        public Faction Other(Faction f) => f == A ? B : f == B ? A : null;

        public string Describe()
        {
            if (Kind == PactKind.Tribute) return $"the {A.Name} pays the {B.Name} {Amount.ToString("N0", CultureInfo.InvariantCulture)} cr a month";
            if (Kind == PactKind.Vassalage) return $"the {A.Name} is the {B.Name}'s vassal";
            return $"{Kind.Name.ToLowerInvariant()} between the {A.Name} and the {B.Name}";
        }

        public override string ToString() => Describe();
    }

    /// <summary>What one polity puts to another (U.SPC-T427): a <see cref="Deal"/> from <see cref="From"/> to <see cref="To"/>, with a sum for tribute.</summary>
    public sealed class Proposal
    {
        public Deal Deal;
        public Faction From, To;
        public double Amount;
        public double Jd;

        public string Describe() => Deal.Describe(this);

        public override string ToString() => Describe();
    }

    /// <summary>How a polity weighs a proposal: a score (above zero it agrees) and its reasons, the weightiest first.</summary>
    public sealed class Appraisal
    {
        public double Score;
        public readonly List<(double Weight, string Why)> Reasons = new List<(double, string)>();

        public bool Accepted => Score > 0;

        public Appraisal Add(double weight, string why)
        {
            Score += weight;
            if (why != null && Math.Abs(weight) >= 0.5) Reasons.Add((weight, why));
            return this;
        }

        /// <summary>The reason that weighed most on the side it came down on.</summary>
        public string Main
        {
            get
            {
                string best = null;
                double w = 0;
                foreach (var (weight, why) in Reasons)
                    if ((Accepted ? weight > w : weight < w))
                    {
                        w = weight;
                        best = why;
                    }
                return best ?? (Accepted ? "it suits them" : "it does not suit them");
            }
        }
    }

    /// <summary>
    /// Something one polity can put to another across the table (U.SPC-T427): peace, a pact, tribute offered or demanded,
    /// fealty offered or demanded. Each says when it cannot be offered, how the other side weighs it (the same for a polity
    /// the AI runs and for an answer the player's envoy reads), and what it does when agreed. A new deal is one
    /// <see cref="Register"/> call.
    /// </summary>
    public sealed class Deal
    {
        static readonly List<Deal> all = new List<Deal>();

        public static IReadOnlyList<Deal> All => all;

        public readonly string Name, Description;

        /// <summary>Whether it carries a sum (tribute a month).</summary>
        public readonly bool HasAmount;

        /// <summary>Why it cannot be put from one to the other now, or null.</summary>
        public readonly Func<Statecraft, Faction, Faction, double, string> Unavailable;

        /// <summary>How the side it is put to weighs it.</summary>
        public readonly Func<Statecraft, Proposal, double, Appraisal> Appraise;

        /// <summary>What it does once agreed.</summary>
        public readonly Action<Statecraft, Proposal, double> Enact;

        /// <summary>It in words, for the news and the envoy screen.</summary>
        public readonly Func<Proposal, string> Words;

        Deal(string name, string description, bool hasAmount, Func<Statecraft, Faction, Faction, double, string> unavailable,
            Func<Statecraft, Proposal, double, Appraisal> appraise, Action<Statecraft, Proposal, double> enact, Func<Proposal, string> words)
        {
            Name = name;
            Description = description;
            HasAmount = hasAmount;
            Unavailable = unavailable;
            Appraise = appraise;
            Enact = enact;
            Words = words;
        }

        public static Deal Register(string name, string description, bool hasAmount, Func<Statecraft, Faction, Faction, double, string> unavailable,
            Func<Statecraft, Proposal, double, Appraisal> appraise, Action<Statecraft, Proposal, double> enact, Func<Proposal, string> words = null)
        {
            var d = new Deal(name, description, hasAmount, unavailable, appraise, enact, words);
            all.Add(d);
            return d;
        }

        public static Deal Find(string name) => all.Find(d => d.Name == name);

        public string Describe(Proposal p) => Words != null ? Words(p) : $"the {p.From.Name} proposes {Name.ToLowerInvariant()} to the {p.To.Name}";

        static string Cr(double v) => v.ToString("N0", CultureInfo.InvariantCulture) + " cr";

        public static readonly Deal Peace = Register("Peace", "End the war between you: white peace, all occupied ground given back unless a treaty cedes it", false,
            (s, a, b, jd) => s.Polities.Diplomacy.Get(a, b) == Relation.Peace ? "You are not at war" : null,
            (s, p, jd) => s.AppraisePeace(p.To, p.From, jd),
            (s, p, jd) => s.MakePeace(p.From, p.To, jd, "terms agreed through their envoys"),
            p => $"the {p.From.Name} offers the {p.To.Name} peace");

        public static readonly Deal NonAggression = Register("Non-aggression pact", PactKind.NonAggression.Description, false,
            (s, a, b, jd) => s.NotAtPeace(a, b) ?? (s.PactBetween(a, b, PactKind.NonAggression) != null || s.PactBetween(a, b, PactKind.Alliance) != null ? "Already bound not to fight" : null),
            (s, p, jd) => s.AppraiseNonAggression(p.To, p.From),
            (s, p, jd) => s.Sign(PactKind.NonAggression, p.From, p.To, jd));

        public static readonly Deal TradePact = Register("Trade pact", PactKind.Trade.Description, false,
            (s, a, b, jd) => s.NotAtPeace(a, b) ?? (s.PactBetween(a, b, PactKind.Trade) != null ? "Already trading under a pact"
                : s.Polities.Diplomacy.IsEmbargoed(a, b) || s.Polities.Diplomacy.IsEmbargoed(b, a) ? "An embargo stands between you" : null),
            (s, p, jd) => s.AppraiseTrade(p.To, p.From),
            (s, p, jd) => s.Sign(PactKind.Trade, p.From, p.To, jd));

        public static readonly Deal Alliance = Register("Alliance", PactKind.Alliance.Description, false,
            (s, a, b, jd) => s.NotAtPeace(a, b) ?? (s.PactBetween(a, b, PactKind.Alliance) != null ? "Already allies" : s.Liege(a, b) ? "A vassal and its overlord" : null),
            (s, p, jd) => s.AppraiseAlliance(p.To, p.From),
            (s, p, jd) => s.Sign(PactKind.Alliance, p.From, p.To, jd));

        public static readonly Deal OfferTribute = Register("Offer tribute", "Pay them a sum every month for two years: goodwill, and peace bought", true,
            (s, a, b, jd) => s.NotAtPeace(a, b) ?? (s.TributeBetween(a, b) != null ? "Tribute already flows between you" : null),
            (s, p, jd) => s.AppraiseTributeReceived(p.To, p.From, p.Amount),
            (s, p, jd) => s.Sign(PactKind.Tribute, p.From, p.To, jd, p.Amount),
            p => $"the {p.From.Name} offers the {p.To.Name} tribute of {Cr(p.Amount)} a month");

        public static readonly Deal DemandTribute = Register("Demand tribute", "They pay you a sum every month for two years, or face your navy", true,
            (s, a, b, jd) => s.NotAtPeace(a, b) ?? (s.TributeBetween(a, b) != null ? "Tribute already flows between you" : null),
            (s, p, jd) => s.AppraiseTributePaid(p.To, p.From, p.Amount),
            (s, p, jd) => s.Sign(PactKind.Tribute, p.To, p.From, jd, p.Amount),
            p => $"the {p.From.Name} demands tribute of {Cr(p.Amount)} a month from the {p.To.Name}");

        public static readonly Deal OfferFealty = Register("Offer fealty", "Become their vassal: their protection, for a share of your treasury and your navy in their wars", false,
            (s, a, b, jd) => s.NotAtPeace(a, b) ?? (s.OverlordOf(a) != null ? "Already a vassal" : s.OverlordOf(b) == a ? "They are your vassal" : null),
            (s, p, jd) => s.AppraiseVassalTaken(p.To, p.From),
            (s, p, jd) => s.Sign(PactKind.Vassalage, p.From, p.To, jd),
            p => $"the {p.From.Name} offers its fealty to the {p.To.Name}");

        public static readonly Deal DemandFealty = Register("Demand fealty", "They become your vassal (a war between you ends with it)", false,
            (s, a, b, jd) => s.OverlordOf(b) != null ? "They already serve an overlord" : s.OverlordOf(a) == b ? "You are their vassal" : s.OverlordOf(a) != null ? "A vassal takes no vassals" : null,
            (s, p, jd) => s.AppraiseFealty(p.To, p.From, jd),
            (s, p, jd) =>
            {
                if (s.Polities.Diplomacy.Get(p.From, p.To) != Relation.Peace) s.MakePeace(p.From, p.To, jd, $"the {p.To.Name} submits to the {p.From.Name}");
                s.Sign(PactKind.Vassalage, p.To, p.From, jd);
            },
            p => $"the {p.From.Name} demands the {p.To.Name}'s fealty");

        public override string ToString() => Name;
    }

    /// <summary>One line of the envoy screen (U.SPC-T427): a deal, its sum, why it cannot be put (or null), and how the other side would take it.</summary>
    public sealed class EnvoyOption
    {
        public Deal Deal;
        public double Amount;
        public string Unavailable;
        public Appraisal Read;
    }

    /// <summary>A polity fighting at a friend's side (U.SPC-T427): it went to war with <see cref="Enemy"/> for <see cref="Friend"/>'s sake.</summary>
    public sealed class CoBelligerence
    {
        public Faction Ally, Friend, Enemy;
        public double SinceJd;
    }

    /// <summary>
    /// The diplomacy game (U.SPC-T427): every state, the player's own among them, holds an opinion of every other, signs pacts
    /// (non-aggression, trade, alliance, tribute, vassalage, see <see cref="PactKind"/>), puts deals to the others
    /// (<see cref="Deal"/>), declares war and makes peace. One set of rules for all: a proposal to a polity the AI runs is
    /// weighed by <see cref="Deal.Appraise"/> and answered at once; one to the player's polity waits in its <see cref="Inbox"/>,
    /// and the player's own proposals are weighed by the other side exactly as an AI's would be. The AI puts deals of its own
    /// each month (<see cref="AutoDiplomacy"/>). Pacts bind the wars (<see cref="InterstellarWars.Declare"/> and the states'
    /// ambitions respect them), allies and vassals are called to arms when their friend is attacked, tribute and a vassal's levy
    /// move money between treasuries and stir unrest among the people who pay it, and trade pacts pay both sides.
    /// </summary>
    [SettingsSource]
    public sealed class Statecraft
    {
        public readonly Polities Polities;
        readonly Random rng = new Random(4271);
        readonly Dictionary<(Faction, Faction), double> opinions = new Dictionary<(Faction, Faction), double>();
        readonly List<Pact> pacts = new List<Pact>();
        readonly List<Proposal> inbox = new List<Proposal>();
        readonly List<CoBelligerence> coBelligerents = new List<CoBelligerence>();
        readonly Dictionary<(Faction, Faction), (Faction By, double Since)> warsBegun = new Dictionary<(Faction, Faction), (Faction, double)>();
        readonly List<string> record = new List<string>();
        double nextDailyJd = double.NaN, nextRoundJd = double.NaN;

        /// <summary>The galaxy whose wars a declaration starts and a peace settles (null for a system on its own).</summary>
        public Galaxy Galaxy;

        /// <summary>Whether the polities the AI runs put deals of their own each month. The default galaxy turns it on; a bare one (a test) leaves it off.</summary>
        public bool AutoDiplomacy;

        public static readonly ToggleSetting AiDiplomacy = new ToggleSetting("diplomacy.ai", SettingGroups.Civilizations, "AI diplomacy", true,
            "Whether the polities put pacts, tribute, fealty and peace to each other (and to you) of their own accord.", "diplomacy treaty alliance pact ai");

        public static readonly RangeSetting OpinionPace = new RangeSetting("diplomacy.opinionPace", SettingGroups.Civilizations, "Diplomatic memory", 1, 0.25, 4, 0.25,
            "How fast the polities' opinions of each other move toward what their pacts, wars and rivalries make them (higher forgets faster).",
            v => v.ToString("0.##", CultureInfo.InvariantCulture) + "x", "diplomacy opinion relations");

        public static readonly RangeSetting TradePactIncome = new RangeSetting("diplomacy.tradeIncome", SettingGroups.Civilizations, "Trade pact income", 1, 0, 5, 0.25,
            "How much a trade pact pays each side a day, as a multiple of the standard.",
            v => v.ToString("0.##", CultureInfo.InvariantCulture) + "x", "diplomacy trade pact treasury");

        public static readonly RangeSetting VassalLevy = new RangeSetting("diplomacy.vassalLevy", SettingGroups.Civilizations, "Vassal levy", 0.1, 0, 0.5, 0.05,
            "The share of its treasury a vassal pays its overlord each month.",
            v => (v * 100).ToString("0", CultureInfo.InvariantCulture) + "%", "diplomacy vassal levy tribute");

        /// <summary>Credits a day a trade pact pays each side, per thousand people of the smaller of the two (as a square root).</summary>
        public const double TradePerDay = 2e4;

        /// <summary>Unrest a day among the people of a polity paying tribute or a vassal's levy, up to <see cref="TributeUnrestCap"/>.</summary>
        public const double TributeUnrestPerDay = 0.002, TributeUnrestCap = 0.4;

        /// <summary>Days between the AI's rounds of proposals.</summary>
        public const double RoundDays = 30;

        /// <summary>Days a proposal to the player waits for an answer before it is taken as declined.</summary>
        public const double InboxDays = 30;

        public Statecraft(Polities polities) => Polities = polities;

        /// <summary>The polity the player speaks for (the one commanded from the galaxy map), or null: then every polity is the AI's.</summary>
        public Faction Player => Polities.Command.Player;

        public IReadOnlyList<Pact> Pacts => pacts;

        /// <summary>Proposals put to the player's polity, waiting for an answer.</summary>
        public IReadOnlyList<Proposal> Inbox => inbox;

        public IReadOnlyList<CoBelligerence> CoBelligerents => coBelligerents;

        /// <summary>What has passed between the polities, newest last.</summary>
        public IReadOnlyList<string> Record => record;

        /// <summary>Whether <paramref name="f"/> plays the diplomacy game: a state (no company, free trader or raider clan).</summary>
        public static bool Party(Faction f) => f != null && !f.IsPirate && f.Kind == PolityKind.State;

        /// <summary>Every state in play.</summary>
        public List<Faction> Parties()
        {
            var list = new List<Faction>();
            foreach (var f in Polities.Factions)
                if (Party(f)) list.Add(f);
            return list;
        }

        // ---------------------------------------------------------------- opinion

        /// <summary>What <paramref name="of"/> thinks of <paramref name="toward"/>, -100 (hatred) to 100 (fast friends).</summary>
        public double Opinion(Faction of, Faction toward) =>
            of == null || toward == null || of == toward ? 100 : opinions.TryGetValue((of, toward), out double v) ? v : 0;

        /// <summary>Moves <paramref name="of"/>'s opinion of <paramref name="toward"/> by <paramref name="by"/>.</summary>
        public void Sway(Faction of, Faction toward, double by)
        {
            if (of == null || toward == null || of == toward) return;
            opinions[(of, toward)] = Math.Max(-100, Math.Min(100, Opinion(of, toward) + by));
        }

        /// <summary>What <paramref name="of"/>'s opinion of <paramref name="toward"/> drifts toward: its pacts, wars, rivals and losses.</summary>
        public double Baseline(Faction of, Faction toward)
        {
            double b = 0;
            var dip = Polities.Diplomacy;
            if (dip.Get(of, toward) == Relation.War) b -= 50;
            else if (dip.Get(of, toward) == Relation.Ceasefire) b -= 20;
            foreach (var p in pacts)
                if (p.Between(of, toward)) b += p.Kind == PactKind.Tribute && p.A == of ? -15 : p.Kind.Warmth;
            // A common enemy draws two together; a friend's enemy is not loved.
            foreach (var g in Polities.Factions)
            {
                if (g == of || g == toward || !Party(g)) continue;
                bool ofFights = dip.Get(of, g) == Relation.War, theyFight = dip.Get(toward, g) == Relation.War;
                if (ofFights && theyFight) b += 20;
            }
            // Ground it lost to them still rankles.
            foreach (var s in Polities.SettlementsOf(toward))
                if (s.OccupiedFrom == of)
                {
                    b -= 25;
                    break;
                }
            // A navy that could swallow it is a worry.
            if (Power(toward) > 3 * Power(of)) b -= 10;
            if (dip.IsEmbargoed(toward, of)) b -= 15;
            return Math.Max(-100, Math.Min(100, b));
        }

        /// <summary>The strength of <paramref name="f"/>'s whole navy (at least 1).</summary>
        public double Power(Faction f)
        {
            double total = 1;
            if (f == null) return total;
            // Within a day's reckoning each navy is counted once: a count walks every ship in the galaxy.
            if (powerCache != null && powerCache.TryGetValue(f, out double known)) return known;
            foreach (var s in Polities.Navy(f)) total += s.Combat.Strength;
            if (powerCache != null) powerCache[f] = total;
            return total;
        }

        /// <summary>Each navy's strength, held while a day is reckoned (null outside it, so a change shows at once).</summary>
        Dictionary<Faction, double> powerCache;

        // ---------------------------------------------------------------- pacts

        public Pact PactBetween(Faction a, Faction b, PactKind kind)
        {
            foreach (var p in pacts)
                if (p.Kind == kind && p.Between(a, b)) return p;
            return null;
        }

        public List<Pact> PactsOf(Faction f)
        {
            var list = new List<Pact>();
            foreach (var p in pacts)
                if (p.Involves(f)) list.Add(p);
            return list;
        }

        public Pact TributeBetween(Faction a, Faction b) => PactBetween(a, b, PactKind.Tribute);

        /// <summary><paramref name="vassal"/>'s overlord, or null.</summary>
        public Faction OverlordOf(Faction vassal)
        {
            foreach (var p in pacts)
                if (p.Kind == PactKind.Vassalage && p.A == vassal) return p.B;
            return null;
        }

        /// <summary>Whether one of the two is the other's vassal.</summary>
        public bool Liege(Faction a, Faction b) => OverlordOf(a) == b || OverlordOf(b) == a;

        /// <summary>Whether a pact keeps <paramref name="a"/> and <paramref name="b"/> from war (the states' ambitions and <see cref="InterstellarWars.Declare"/> respect it).</summary>
        public bool BoundToPeace(Faction a, Faction b)
        {
            foreach (var p in pacts)
                if (p.Kind.KeepsPeace && p.Between(a, b)) return true;
            return false;
        }

        /// <summary>Signs a pact of <paramref name="kind"/> (<paramref name="a"/> the payer or vassal of a directed one) from <paramref name="jd"/>.</summary>
        public Pact Sign(PactKind kind, Faction a, Faction b, double jd, double amount = 0)
        {
            if (kind == null || a == null || b == null || a == b) return null;
            var p = new Pact { Kind = kind, A = a, B = b, SinceJd = jd, UntilJd = double.IsNaN(kind.Days) ? double.NaN : jd + kind.Days, Amount = amount };
            pacts.Add(p);
            if (kind == PactKind.Trade)
            {
                // Trade under a pact: neither keeps the other's ships out.
                foreach (var e in new List<Embargo>(Polities.Diplomacy.Embargoes))
                    if ((e.By == a && e.Against == b) || (e.By == b && e.Against == a)) Polities.Diplomacy.LiftEmbargo(e);
            }
            Sway(a, b, 10);
            Sway(b, a, 10);
            Tell(jd, Capitalise(p.Describe()) + ": " + (kind == PactKind.Tribute || kind == PactKind.Vassalage ? "signed" : "signed, " + kind.Description.ToLowerInvariant()), a, b);
            return p;
        }

        /// <summary>
        /// <paramref name="by"/> breaks <paramref name="p"/>: the other side resents it, the more for a pact that kept the peace
        /// or called it to arms, and every other state trusts <paramref name="by"/> a little less. A vassal breaking its vassalage
        /// declares its independence.
        /// </summary>
        public bool Break(Pact p, Faction by, double jd, string why = null)
        {
            if (p == null || !pacts.Remove(p)) return false;
            var other = p.Other(by);
            if (other != null)
            {
                double hurt = p.Kind == PactKind.Vassalage && p.A == by ? 60 : p.Kind.KeepsPeace || p.Kind.CallsToArms ? 35 : 15;
                Sway(other, by, -hurt);
                if (p.Kind.KeepsPeace)
                    foreach (var g in Parties())
                        if (g != by && g != other) Sway(g, by, -8);
            }
            string what = p.Kind == PactKind.Vassalage && p.A == by ? $"The {by.Name} throws off the {p.B.Name}'s rule and declares its independence"
                : $"The {by?.Name ?? "one side"} breaks the {Lower(p.Describe())}";
            Tell(jd, what + (why != null ? ": " + why : ""), p.A, p.B);
            return true;
        }

        /// <summary>A timed pact runs out.</summary>
        void Expire(Pact p, double jd)
        {
            pacts.Remove(p);
            Tell(jd, Capitalise(p.Describe()) + " has run its course", p.A, p.B);
        }

        internal bool KeepTrade(Pact p)
        {
            double each = TradePactIncome.Value * TradePerDay * Math.Sqrt(Math.Max(0, Math.Min(People(p.A), People(p.B))) / 1e6);
            p.A.Treasury += each;
            p.B.Treasury += each;
            return true;
        }

        internal bool KeepTribute(Pact p, double jd)
        {
            double due = p.Amount / 30;
            if (p.A.Treasury < due) return false;
            p.A.Treasury -= due;
            p.B.Treasury += due;
            Stir(p.A);
            return true;
        }

        internal bool KeepVassalage(Pact p, double jd)
        {
            double levy = Math.Max(0, p.A.Treasury) * VassalLevy.Value / 30;
            p.A.Treasury -= levy;
            p.B.Treasury += levy;
            Stir(p.A);
            return true;
        }

        /// <summary>Paying another power is resented by the people who pay it (civ dynamics: unrest rises in its ports).</summary>
        void Stir(Faction payer)
        {
            foreach (var s in Polities.SettlementsOf(payer))
                if (s.Unrest < TributeUnrestCap) s.Unrest = Math.Min(TributeUnrestCap, s.Unrest + TributeUnrestPerDay);
        }

        double People(Faction f)
        {
            double n = 0;
            foreach (var s in Polities.SettlementsOf(f)) n += s.Population;
            return n;
        }

        // ---------------------------------------------------------------- proposals

        /// <summary>Why <paramref name="deal"/> cannot be put from <paramref name="from"/> to <paramref name="to"/> now, or null.</summary>
        public string Unavailable(Deal deal, Faction from, Faction to, double jd)
        {
            if (deal == null || from == null || to == null || from == to) return "Nobody to put it to";
            if (!Party(from) || !Party(to)) return "Only states treat with each other";
            return deal.Unavailable?.Invoke(this, from, to, jd);
        }

        /// <summary>How <paramref name="to"/> would weigh <paramref name="deal"/> from <paramref name="from"/> (what an envoy reads of the mood).</summary>
        public Appraisal Weigh(Deal deal, Faction from, Faction to, double jd, double amount = 0) =>
            deal.Appraise(this, new Proposal { Deal = deal, From = from, To = to, Amount = amount, Jd = jd }, jd);

        /// <summary>
        /// <paramref name="from"/> puts <paramref name="deal"/> to <paramref name="to"/>. A polity the AI runs weighs it and
        /// answers at once (agreed, it is enacted); the player's polity gets it in its <see cref="Inbox"/>. Returns null and the
        /// answer (<paramref name="answer"/>: agreed, refused or waiting, with the main reason), or why it could not be put.
        /// </summary>
        public string Propose(Deal deal, Faction from, Faction to, double jd, out Appraisal answer, double amount = 0)
        {
            answer = null;
            var why = Unavailable(deal, from, to, jd);
            if (why != null) return why;
            var p = new Proposal { Deal = deal, From = from, To = to, Amount = amount, Jd = jd };
            if (to == Player && from != Player)
            {
                if (inbox.Exists(x => x.From == from && x.To == to && x.Deal == deal)) return "Already put to them";
                inbox.Add(p);
                Tell(jd, Capitalise(p.Describe()) + ", and awaits an answer", from, to);
                return null;
            }
            answer = deal.Appraise(this, p, jd);
            if (answer.Accepted)
            {
                deal.Enact(this, p, jd);
                if (deal != Deal.Peace && deal != Deal.DemandFealty) record.Add(Capitalise(p.Describe()) + ": agreed");
            }
            else
            {
                Sway(from, to, deal == Deal.DemandTribute || deal == Deal.DemandFealty ? -2 : -1);
                Sway(to, from, deal == Deal.DemandTribute || deal == Deal.DemandFealty ? -10 : 0);
                Tell(jd, Capitalise(p.Describe()) + ": refused, " + answer.Main, from, to);
            }
            return null;
        }

        /// <summary>The player answers a proposal in the <see cref="Inbox"/>: agreed, it is enacted; refused, the other side remembers.</summary>
        public bool Answer(Proposal p, bool accept, double jd)
        {
            if (p == null || !inbox.Remove(p)) return false;
            if (Unavailable(p.Deal, p.From, p.To, jd) != null) return false;
            if (accept)
            {
                p.Deal.Enact(this, p, jd);
                record.Add(Capitalise(p.Describe()) + ": agreed");
            }
            else
            {
                Sway(p.From, p.To, p.Deal == Deal.DemandTribute || p.Deal == Deal.DemandFealty ? -15 : -5);
                Tell(jd, Capitalise(p.Describe()) + ": refused", p.From, p.To);
                if (p.Deal == Deal.DemandTribute || p.Deal == Deal.DemandFealty) Spurned(p.From, p.To, jd);
            }
            return true;
        }

        // ---------------------------------------------------------------- acts

        /// <summary>
        /// <paramref name="by"/> declares war on <paramref name="against"/>: every pact that kept the two at peace is broken, and
        /// across the galaxy it is a war with goals (<see cref="InterstellarWars.Declare"/>) when one can be fought, else a war
        /// in the one diplomacy table, fought wherever their ships meet. The defender's allies and vassals are called to arms.
        /// Returns null, or why it cannot be declared.
        /// </summary>
        public string DeclareWar(Faction by, Faction against, double jd)
        {
            if (by == null || against == null || by == against) return "Nobody to declare war on";
            if (!Party(by) || !Party(against)) return "Only states declare war on each other";
            if (Polities.Diplomacy.Get(by, against) == Relation.War) return "Already at war";
            var liege = OverlordOf(by);
            if (liege != null && liege != against) return "A vassal makes no war of its own: its overlord, the " + liege.Name + ", decides";
            foreach (var p in new List<Pact>(pacts))
                if (p.Between(by, against)) Break(p, by, jd, "war declared");
            Conflict c = null;
            if (Galaxy?.Wars != null && !Polities.Diplomacy.InTruce(by, against, jd)) c = Galaxy.Wars.Declare(by, against, jd);
            if (c == null)
            {
                Polities.Diplomacy.Set(by, against, Relation.War);
                foreach (var w in Polities.Worlds) w.War?.RevokeBookings();
                Tell(jd, $"The {by.Name} declares war on the {against.Name}", by, against);
            }
            else record.Add($"The {by.Name} declares war on the {against.Name}");
            // Breaking a truce or just going to war on someone is noticed by everyone.
            foreach (var g in Parties())
                if (g != by && g != against) Sway(g, by, Polities.Diplomacy.InTruce(by, against, jd) ? -15 : -5);
            Sway(against, by, -40);
            Began(by, against, jd);
            CallToArms(jd);
            return null;
        }

        /// <summary>
        /// <paramref name="a"/> and <paramref name="b"/> make peace: the war between them ends (a war across the galaxy is settled
        /// by its treaty the next day, see <see cref="InterstellarWars"/>; one only in the table gets a white peace and a truce),
        /// and their friends who fought at their side make peace too.
        /// </summary>
        public void MakePeace(Faction a, Faction b, double jd, string why)
        {
            var dip = Polities.Diplomacy;
            if (dip.Get(a, b) == Relation.Peace) return;
            dip.Set(a, b, Relation.Peace);
            if (Galaxy?.Wars?.Between(a, b) == null)
                dip.Sign(new Treaty { A = a, B = b, Jd = jd, TruceUntilJd = jd + InterstellarWars.TruceDays, Terms = $"Peace between the {a.Name} and the {b.Name}: {why}" });
            warsBegun.Remove(Key(a, b));
            coBelligerents.RemoveAll(x => (x.Ally == a && x.Enemy == b) || (x.Ally == b && x.Enemy == a));
            Sway(a, b, 15);
            Sway(b, a, 15);
            Tell(jd, $"Peace between the {a.Name} and the {b.Name}: {why}", a, b);
            foreach (var w in Polities.Worlds) w.War?.RevokeBookings();
        }

        /// <summary><paramref name="by"/> gives <paramref name="to"/> <paramref name="amount"/> credits: goodwill, the more the bigger it is to them.</summary>
        public string Gift(Faction by, Faction to, double amount, double jd)
        {
            if (by == null || to == null || by == to || amount <= 0) return "Nothing to give";
            if (by.Treasury < amount) return "The treasury cannot spare it";
            by.Treasury -= amount;
            to.Treasury += amount;
            Sway(to, by, Math.Min(30, 40 * amount / Math.Max(1e6, Math.Abs(to.Treasury))));
            Tell(jd, $"The {by.Name} sends the {to.Name} a gift of {amount.ToString("N0", CultureInfo.InvariantCulture)} cr", by, to);
            return null;
        }

        /// <summary>A sum worth offering or demanding as tribute from <paramref name="payer"/> a month.</summary>
        public static double SuggestedTribute(Faction payer) => Math.Max(1e5, Math.Round(Math.Max(0, payer?.Treasury ?? 0) * 0.02 / 1e4) * 1e4);

        /// <summary>A gift worth sending from <paramref name="giver"/>: a hundredth of its treasury, rounded.</summary>
        public static double SuggestedGift(Faction giver) => Math.Max(1e5, Math.Round(Math.Max(0, giver?.Treasury ?? 0) * 0.01 / 1e4) * 1e4);

        // ---------------------------------------------------------------- the envoy

        /// <summary>
        /// What <paramref name="from"/>'s envoy can put to <paramref name="to"/> now (the envoy screen): every <see cref="Deal"/>,
        /// with the sum it would carry, why it cannot be put (or null), and, when it can, how the other side would weigh it:
        /// the same <see cref="Weigh"/> the answer comes from, so what the envoy reads is what <see cref="Propose"/> gets.
        /// </summary>
        public List<EnvoyOption> Envoy(Faction from, Faction to, double jd)
        {
            var list = new List<EnvoyOption>();
            foreach (var deal in Deal.All)
            {
                double amount = deal == Deal.OfferTribute ? SuggestedTribute(from) : deal == Deal.DemandTribute ? SuggestedTribute(to) : 0;
                var why = Unavailable(deal, from, to, jd);
                list.Add(new EnvoyOption { Deal = deal, Amount = amount, Unavailable = why, Read = why == null ? Weigh(deal, from, to, jd, amount) : null });
            }
            return list;
        }

        /// <summary>An opinion in a word.</summary>
        public static string Mood(double opinion) =>
            opinion <= -60 ? "hostile" : opinion <= -25 ? "cold" : opinion < -5 ? "wary" : opinion < 15 ? "indifferent" : opinion < 45 ? "cordial" : opinion < 75 ? "friendly" : "warm";

        /// <summary>
        /// <paramref name="from"/> hails <paramref name="to"/> (one of its ships at the helm, or its envoy from the map): its answer
        /// in words, by how it stands with and thinks of the hailer, and any proposals of its own waiting on the hailer's answer.
        /// </summary>
        public string Hail(Faction from, Faction to, double jd)
        {
            if (from == null || to == null || from == to) return "Nobody answers.";
            if (!Party(to)) return $"The {to.Name} does not treat with states.";
            if (!Party(from)) return $"The {to.Name} treats only with states: speak for one (the galaxy map's DIPLOMACY panel).";
            double op = Opinion(to, from);
            var dip = Polities.Diplomacy;
            string say;
            if (dip.Get(from, to) == Relation.War)
                say = op <= -60 ? $"\"We have nothing to say to the {from.Name}, save with our guns.\""
                    : AppraisePeace(to, from, jd).Accepted ? "\"We would hear your terms for peace.\"" : "\"Peace? Not while the war goes our way.\"";
            else if (OverlordOf(to) == from) say = "\"Our liege's word is heard.\"";
            else if (OverlordOf(from) == to) say = $"\"The {to.Name} hears its vassal.\"";
            else if (PactBetween(from, to, PactKind.Alliance) != null) say = "\"Well met, ally.\"";
            else
                say = op <= -25 ? "\"State your business and be gone.\"" : op < 15 ? "\"We are listening.\"" : "\"Always glad to hear from friends.\"";
            int waiting = 0;
            foreach (var p in inbox)
                if (p.From == to && p.To == from) waiting++;
            return $"The {to.Name} ({Mood(op)}, {Standing(from, to)}): {say}" +
                   (waiting > 0 ? $" {waiting} proposal{(waiting == 1 ? "" : "s")} of theirs await{(waiting == 1 ? "s" : "")} your answer." : "");
        }

        // ---------------------------------------------------------------- weighing

        internal string NotAtPeace(Faction a, Faction b) => Polities.Diplomacy.Get(a, b) != Relation.Peace ? "Make peace first" : null;

        /// <summary>How many days <paramref name="a"/> and <paramref name="b"/> have been at war.</summary>
        public double WarDays(Faction a, Faction b, double jd)
        {
            var c = Galaxy?.Wars?.Between(a, b);
            if (c != null) return jd - c.StartJd;
            return warsBegun.TryGetValue(Key(a, b), out var w) ? jd - w.Since : 0;
        }

        static (Faction, Faction) Key(Faction a, Faction b) => string.CompareOrdinal(a.Name, b.Name) <= 0 ? (a, b) : (b, a);

        void Began(Faction by, Faction against, double jd)
        {
            if (!warsBegun.ContainsKey(Key(by, against))) warsBegun[Key(by, against)] = (by, jd);
        }

        /// <summary>Whether <paramref name="f"/> has a common enemy with <paramref name="g"/> (both at war with, or both loathing, a third).</summary>
        bool CommonEnemy(Faction f, Faction g)
        {
            var dip = Polities.Diplomacy;
            foreach (var h in Parties())
            {
                if (h == f || h == g) continue;
                bool fHates = dip.Get(f, h) == Relation.War || Opinion(f, h) < -40;
                bool gHates = dip.Get(g, h) == Relation.War || Opinion(g, h) < -40;
                if (fHates && gHates) return true;
            }
            return false;
        }

        double Ratio(Faction a, Faction b) => Power(a) / Power(b);

        static double Clamp(double v, double lo, double hi) => Math.Max(lo, Math.Min(hi, v));

        internal Appraisal AppraisePeace(Faction me, Faction them, double jd)
        {
            var a = new Appraisal();
            double ratio = Ratio(them, me);
            a.Add(-10, "they would rather fight on");
            a.Add(0.3 * Opinion(me, them), Opinion(me, them) >= 0 ? "they bear you little ill will" : "they hate you");
            a.Add(25 * Clamp(ratio - 1, -1, 2), ratio > 1 ? "your navy outweighs theirs" : "they think they are winning");
            double days = WarDays(me, them, jd);
            a.Add(30 * Math.Min(1, days / 365), days > 90 ? "they are weary of the war" : null);
            int lost = 0;
            foreach (var s in Polities.SettlementsOf(them))
                if (s.OccupiedFrom == me) lost++;
            if (lost > 0) a.Add(-10 * lost, "you hold ground taken from them");
            if (me.CrewMorale < 0.7) a.Add(15, "their crews have lost heart");
            return a;
        }

        internal Appraisal AppraiseNonAggression(Faction me, Faction them)
        {
            var a = new Appraisal();
            a.Add(5, "peace costs them nothing");
            a.Add(0.5 * Opinion(me, them), Opinion(me, them) >= 0 ? "they think well of you" : "they distrust you");
            double ratio = Ratio(them, me);
            a.Add(20 * Clamp(ratio - 1, 0, 2), "your navy worries them");
            if (Ratio(me, them) >= InterstellarWars.DeclareRatio) a.Add(-25, "they covet what you hold");
            return a;
        }

        internal Appraisal AppraiseTrade(Faction me, Faction them)
        {
            var a = new Appraisal();
            a.Add(10, "trade pays them too");
            a.Add(0.6 * Opinion(me, them), Opinion(me, them) >= 0 ? "they think well of you" : "they distrust you");
            return a;
        }

        internal Appraisal AppraiseAlliance(Faction me, Faction them)
        {
            var a = new Appraisal();
            a.Add(-30, "an alliance drags them into your wars");
            a.Add(0.8 * Opinion(me, them), Opinion(me, them) >= 0 ? "they count you a friend" : "they distrust you");
            if (CommonEnemy(me, them)) a.Add(30, "you share an enemy");
            if (PactBetween(me, them, PactKind.NonAggression) != null) a.Add(5, "your pact has held");
            if (PactBetween(me, them, PactKind.Trade) != null) a.Add(5, "your trade binds you");
            if (Polities.AtWar(them) && !CommonEnemy(me, them)) a.Add(-20, "you are at war with someone they have no quarrel with");
            return a;
        }

        internal Appraisal AppraiseTributeReceived(Faction me, Faction them, double amount)
        {
            var a = new Appraisal();
            a.Add(amount > 0 ? 50 : -10, amount > 0 ? "money is money" : "an empty offer");
            return a;
        }

        internal Appraisal AppraiseTributePaid(Faction me, Faction them, double amount)
        {
            var a = new Appraisal();
            double ratio = Ratio(them, me);
            a.Add(30 * (ratio - 2.5), ratio > 2.5 ? "your navy could crush theirs" : "they do not fear you");
            a.Add(0.2 * Opinion(me, them), null);
            double year = amount * 12;
            a.Add(-40 * Math.Min(2, year / Math.Max(1e6, me.Treasury)), "the sum would ruin them");
            if (me.Treasury < amount) a.Add(-100, "they cannot pay it");
            return a;
        }

        internal Appraisal AppraiseVassalTaken(Faction me, Faction them)
        {
            var a = new Appraisal();
            a.Add(20, "a vassal's levy and its ships");
            a.Add(0.3 * Opinion(me, them), null);
            foreach (var g in Parties())
                if (g != me && g != them && Polities.Diplomacy.Get(them, g) == Relation.War && Power(g) > Power(me))
                {
                    a.Add(-30, "your enemies are stronger than they are");
                    break;
                }
            return a;
        }

        internal Appraisal AppraiseFealty(Faction me, Faction them, double jd)
        {
            var a = new Appraisal();
            double ratio = Ratio(them, me);
            a.Add(25 * (ratio - 4), ratio > 4 ? "they cannot stand against you" : "they will not bend the knee");
            a.Add(0.2 * Opinion(me, them), null);
            if (Polities.Diplomacy.Get(me, them) == Relation.War)
            {
                int lost = 0;
                foreach (var s in Polities.SettlementsOf(them))
                    if (s.OccupiedFrom == me) lost++;
                if (lost > 0) a.Add(15 * lost, "you hold their ground");
                if (ratio > 2) a.Add(20, "they are losing the war");
            }
            return a;
        }

        // ---------------------------------------------------------------- the day

        public void Update(double jd)
        {
            if (double.IsNaN(nextDailyJd)) nextDailyJd = jd;
            if (double.IsNaN(nextRoundJd)) nextRoundJd = jd + 15;
            while (jd >= nextDailyJd)
            {
                powerCache = new Dictionary<Faction, double>();
                try
                {
                    Daily(nextDailyJd);
                }
                finally
                {
                    powerCache = null;
                }
                nextDailyJd += 1;
            }
        }

        void Daily(double jd)
        {
            var parties = Parties();
            // Opinions drift toward what pacts, wars and rivalries make them.
            double pace = 0.02 * OpinionPace.Value;
            foreach (var f in parties)
                foreach (var g in parties)
                    if (f != g)
                    {
                        double now = Opinion(f, g);
                        double to = Baseline(f, g);
                        if (Math.Abs(to - now) > 1e-6) opinions[(f, g)] = now + (to - now) * pace;
                    }
            // Pacts: run out, are kept up, or lapse.
            foreach (var p in new List<Pact>(pacts))
            {
                if (!pacts.Contains(p)) continue;
                if (!Party(p.A) || !Party(p.B) || !Alive(p.A) || !Alive(p.B))
                {
                    pacts.Remove(p);
                    continue;
                }
                if (!double.IsNaN(p.UntilJd) && jd >= p.UntilJd)
                {
                    Expire(p, jd);
                    continue;
                }
                if (p.Kind.Daily != null && !p.Kind.Daily(this, p, jd))
                {
                    pacts.Remove(p);
                    Sway(p.B, p.A, -30);
                    Tell(jd, $"The {p.A.Name} cannot pay and defaults on the {Lower(p.Describe())}", p.A, p.B);
                    Spurned(p.B, p.A, jd);
                }
            }
            // Wars noticed (a declaration made anywhere), friends called to arms, and friends' wars ended.
            foreach (var f in parties)
                foreach (var g in parties)
                    if (f != g && Polities.Diplomacy.Get(f, g) == Relation.War && !warsBegun.ContainsKey(Key(f, g)))
                    {
                        var c = Galaxy?.Wars?.Between(f, g);
                        Began(c?.Aggressor ?? f, c?.Defender ?? g, c?.StartJd ?? jd);
                    }
            foreach (var key in new List<(Faction, Faction)>(warsBegun.Keys))
                if (Polities.Diplomacy.Get(key.Item1, key.Item2) != Relation.War) warsBegun.Remove(key);
            StandDown(jd);
            CallToArms(jd);
            // Proposals to the player left unanswered are taken as declined.
            foreach (var p in new List<Proposal>(inbox))
                if (jd - p.Jd > InboxDays || p.To != Player) Answer(p, false, jd);
            if (jd >= nextRoundJd)
            {
                nextRoundJd = jd + RoundDays;
                if (AutoDiplomacy && AiDiplomacy.Value) Round(jd);
            }
        }

        bool Alive(Faction f)
        {
            foreach (var _ in Polities.SettlementsOf(f)) return true;
            return Polities.Navy(f).Count > 0;
        }

        /// <summary>
        /// Allies and vassals join the wars their friend is attacked in (a vassal every war of its overlord): each is at war
        /// with the attacker in the diplomacy table, so their fleets fight wherever they meet, until their friend makes peace.
        /// An ally that loathes the friend it is called for refuses, and the alliance is broken.
        /// </summary>
        void CallToArms(double jd)
        {
            var dip = Polities.Diplomacy;
            foreach (var p in new List<Pact>(pacts))
            {
                if (!p.Kind.CallsToArms || !pacts.Contains(p)) continue;
                foreach (var friend in new[] { p.A, p.B })
                {
                    var ally = p.Other(friend);
                    foreach (var kv in new List<KeyValuePair<(Faction, Faction), (Faction By, double Since)>>(warsBegun))
                    {
                        var (x, y) = kv.Key;
                        if (x != friend && y != friend) continue;
                        var enemy = x == friend ? y : x;
                        if (enemy == ally || dip.Get(friend, enemy) != Relation.War || dip.Get(ally, enemy) == Relation.War) continue;
                        // An ally answers a friend who was attacked; a vassal follows its overlord into any war.
                        bool attacked = kv.Value.By == enemy;
                        bool serves = p.Kind == PactKind.Vassalage && p.A == ally;
                        bool protects = p.Kind == PactKind.Vassalage && p.B == ally && attacked;
                        if (!(serves || protects || (p.Kind != PactKind.Vassalage && attacked))) continue;
                        if (BoundToPeace(ally, enemy) || OverlordOf(ally) != null && OverlordOf(ally) != friend)
                            continue;
                        if (!serves && Opinion(ally, friend) < -20 && ally != Player)
                        {
                            Break(p, ally, jd, $"it will not fight the {enemy.Name} for the {friend.Name}");
                            goto nextPact;
                        }
                        dip.Set(ally, enemy, Relation.War);
                        // The ally declared it: its own friends are not called against an enemy that never attacked them.
                        Began(ally, enemy, jd);
                        coBelligerents.Add(new CoBelligerence { Ally = ally, Friend = friend, Enemy = enemy, SinceJd = jd });
                        foreach (var w in Polities.Worlds) w.War?.RevokeBookings();
                        Tell(jd, serves ? $"The {ally.Name} follows its overlord, the {friend.Name}, to war with the {enemy.Name}"
                            : $"The {ally.Name} honours its {(p.Kind == PactKind.Vassalage ? "duty to its vassal" : "alliance")} with the {friend.Name} and declares war on the {enemy.Name}",
                            ally, enemy);
                    }
                }
                nextPact: ;
            }
        }

        /// <summary>A friend at peace again with the enemy its ally fought for: the ally makes peace too.</summary>
        void StandDown(double jd)
        {
            foreach (var c in new List<CoBelligerence>(coBelligerents))
            {
                if (Polities.Diplomacy.Get(c.Friend, c.Enemy) == Relation.War) continue;
                coBelligerents.Remove(c);
                if (Polities.Diplomacy.Get(c.Ally, c.Enemy) == Relation.War)
                    MakePeace(c.Ally, c.Enemy, jd, $"the {c.Friend.Name}'s war over");
            }
        }

        /// <summary>A demand refused (or tribute not paid): a power strong enough, not bound to peace and with no war on, may take it by force.</summary>
        void Spurned(Faction by, Faction refuser, double jd)
        {
            if (by == Player || !AutoDiplomacy || !AiDiplomacy.Value || Galaxy?.Wars == null || !Galaxy.Wars.AutoDeclare) return;
            if (Polities.AtWar(by) || BoundToPeace(by, refuser) || OverlordOf(by) != null) return;
            if (Ratio(by, refuser) < 2.5 || rng.NextDouble() > 0.5) return;
            DeclareWar(by, refuser, jd);
        }

        /// <summary>
        /// The AI's round (monthly): each state it runs looks at each other state and puts the one deal that suits it best (the
        /// same deals the player has, weighed by the same rules on the other side): peace when weary of a war, an alliance with a
        /// friend against a common enemy, a non-aggression pact with a power that worries it, a trade pact with a state it likes,
        /// tribute from a weak neighbour it dislikes; and a vassal grown strong throws off its overlord.
        /// </summary>
        void Round(double jd)
        {
            var parties = Parties();
            var dip = Polities.Diplomacy;
            foreach (var f in parties)
            {
                if (f == Player) continue;
                var liege = OverlordOf(f);
                if (liege != null && Power(f) > 1.2 * Power(liege) && Opinion(f, liege) < -20)
                {
                    Break(PactBetween(f, liege, PactKind.Vassalage), f, jd);
                    continue;
                }
                foreach (var g in parties)
                {
                    if (g == f || rng.NextDouble() > 0.5) continue;
                    double op = Opinion(f, g);
                    Deal deal = null;
                    double amount = 0;
                    if (dip.Get(f, g) == Relation.War)
                    {
                        if (AppraisePeace(f, g, jd).Score > 0) deal = Deal.Peace;
                        else if (Ratio(f, g) > 4) deal = Deal.DemandFealty;
                    }
                    else if (dip.Get(f, g) == Relation.Ceasefire) continue;
                    else if (op > 40 && CommonEnemy(f, g) && PactBetween(f, g, PactKind.Alliance) == null) deal = Deal.Alliance;
                    else if (Ratio(g, f) > 1.5 && op > -30 && !BoundToPeace(f, g)) deal = Deal.NonAggression;
                    else if (op > 10 && PactBetween(f, g, PactKind.Trade) == null) deal = Deal.TradePact;
                    else if (Ratio(f, g) > 3 && op < -10 && TributeBetween(f, g) == null && liege == null)
                    {
                        deal = Deal.DemandTribute;
                        amount = SuggestedTribute(g);
                    }
                    if (deal == null || Unavailable(deal, f, g, jd) != null) continue;
                    Propose(deal, f, g, jd, out var answer, amount);
                    if (answer != null && !answer.Accepted && deal == Deal.DemandTribute) Spurned(f, g, jd);
                }
            }
        }

        // ---------------------------------------------------------------- the news

        void Tell(double jd, string text, Faction a, Faction b)
        {
            record.Add(text);
            if (record.Count > 200) record.RemoveRange(0, record.Count - 200);
            var told = new HashSet<World>();
            foreach (var f in new[] { a, b })
            {
                var w = Polities.CapitalSystemOf(f) ?? Polities.HomeOf(f);
                if (w != null && told.Add(w)) w.Log(jd, text, WorldEventKind.Diplomacy);
            }
        }

        static string Capitalise(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);

        static string Lower(string s) => string.IsNullOrEmpty(s) ? s : s.StartsWith("the ", StringComparison.Ordinal) ? s : char.ToLowerInvariant(s[0]) + s.Substring(1);

        /// <summary>How <paramref name="f"/> stands with <paramref name="g"/>, in words: war, peace, and the pacts between them.</summary>
        public string Standing(Faction f, Faction g)
        {
            var r = Polities.Diplomacy.Get(f, g);
            var parts = new List<string> { r == Relation.War ? "at war" : r == Relation.Ceasefire ? "in a ceasefire" : "at peace" };
            foreach (var p in pacts)
                if (p.Between(f, g))
                    parts.Add(p.Kind == PactKind.Vassalage ? (p.A == f ? "their vassal" : "your vassal")
                        : p.Kind == PactKind.Tribute ? (p.A == f ? "paying them tribute" : "taking their tribute") : p.Kind.Name.ToLowerInvariant());
            return string.Join(", ", parts);
        }
    }
}
