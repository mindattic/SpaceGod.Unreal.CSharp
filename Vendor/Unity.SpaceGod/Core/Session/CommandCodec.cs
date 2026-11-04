using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Unity.SpaceGod.Core
{
    /// <summary>
    /// Writes every session command as one line of text and reads it back equal (U.SPC-T410, docs/BIBLE.md U.SPC-LAW-1):
    /// what a command log, a replay or a wire would carry. Numbers are written round-trip exact in the invariant culture.
    /// <code>
    /// take 0 7
    /// release 1
    /// host 0
    /// control 0 aimX aimY aimZ throttle strafeRight strafeUp roll flags   (flags: 1 primary, 2 secondary, 4 boost)
    /// </code>
    /// </summary>
    public static class CommandCodec
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static string Write(IGameCommand command)
        {
            switch (command)
            {
                case TakeSeatCommand t: return "take " + Int(t.Player.Seat) + " " + Int(t.Seat);
                case ReleaseSeatCommand r: return "release " + Int(r.Player.Seat);
                case MoveToHostCommand h: return "host " + Int(h.Player.Seat);
                case SeatControlCommand c:
                    var i = c.Input;
                    int flags = (i.Primary ? 1 : 0) | (i.Secondary ? 2 : 0) | (i.Boost ? 4 : 0);
                    return "control " + Int(c.Player.Seat) + " " + Num(i.Aim.X) + " " + Num(i.Aim.Y) + " " + Num(i.Aim.Z) + " " +
                           Num(i.Throttle) + " " + Num(i.StrafeRight) + " " + Num(i.StrafeUp) + " " + Num(i.Roll) + " " + Int(flags);
                default:
                    throw new ArgumentException("No codec for " + (command?.GetType().Name ?? "null"), nameof(command));
            }
        }

        public static IGameCommand Read(string line)
        {
            var f = (line ?? "").Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (f.Length < 2) throw new FormatException("Not a command: '" + line + "'");
            var p = new PlayerId(ParseInt(f[1]));
            switch (f[0])
            {
                case "take":
                    Need(f, 3, line);
                    return new TakeSeatCommand(p, ParseInt(f[2]));
                case "release": return new ReleaseSeatCommand(p);
                case "host": return new MoveToHostCommand(p);
                case "control":
                    Need(f, 10, line);
                    int flags = ParseInt(f[9]);
                    return new SeatControlCommand(p, new EmbodimentInput
                    {
                        Aim = new DVec3(ParseNum(f[2]), ParseNum(f[3]), ParseNum(f[4])),
                        Throttle = ParseNum(f[5]),
                        StrafeRight = ParseNum(f[6]),
                        StrafeUp = ParseNum(f[7]),
                        Roll = ParseNum(f[8]),
                        Primary = (flags & 1) != 0,
                        Secondary = (flags & 2) != 0,
                        Boost = (flags & 4) != 0,
                    });
                default:
                    throw new FormatException("Unknown command '" + f[0] + "'");
            }
        }

        /// <summary>A command log, one command a line.</summary>
        public static string WriteLog(IEnumerable<IGameCommand> commands)
        {
            var sb = new StringBuilder();
            foreach (var c in commands) sb.Append(Write(c)).Append('\n');
            return sb.ToString();
        }

        public static List<IGameCommand> ReadLog(string text)
        {
            var list = new List<IGameCommand>();
            using (var r = new StringReader(text ?? ""))
            {
                string line;
                while ((line = r.ReadLine()) != null)
                    if (line.Trim().Length > 0) list.Add(Read(line));
            }
            return list;
        }

        static void Need(string[] f, int n, string line)
        {
            if (f.Length < n) throw new FormatException("Too few fields: '" + line + "'");
        }

        static string Int(int v) => v.ToString(Inv);
        static string Num(double v) => v.ToString("R", Inv);
        static int ParseInt(string s) => int.Parse(s, NumberStyles.Integer, Inv);
        static double ParseNum(string s) => double.Parse(s, NumberStyles.Float, Inv);
    }
}
