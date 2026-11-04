using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace Unity.SpaceGod.Core
{
    /// <summary>
    /// Everything a save holds: the clock, the whole galaxy (every system's world with its ships,
    /// cargo, battles, markets, factions, wrecks, pods, law and adventures, the jump gates and the
    /// traffic between them) and the player's session at the helm.
    /// </summary>
    public sealed class SaveData
    {
        public SimClock Clock;
        public Galaxy Galaxy;
        public SaveSession Session = new SaveSession();
    }

    /// <summary>
    /// The parts of the player's session the simulation does not hold itself: which system was being
    /// watched, and, while a ship was being flown, its motion (the hull's schedule is pinned while it is
    /// piloted, so its true pose lives with the pilot).
    /// </summary>
    public sealed class SaveSession
    {
        public string ActiveSystem;

        /// <summary>The ship at the helm, or null when only watching (the world's <see cref="World.PilotedShip"/>).</summary>
        public Ship Piloted;
        public int PilotFrameBody;
        public PilotState Pilot;
        public DVec3 PilotAim;
        public bool FlightAssistOff;

        /// <summary>1 gameplay, 0 realistic.</summary>
        public double ScaleGoal = 1;
    }

    /// <summary>What a save slot shows before it is loaded.</summary>
    public sealed class SaveHeader
    {
        public int Format;
        public string Slot;
        public string Label;
        public DateTime SavedUtc;
        public double SimJd;
        public string ActiveSystem;
        public string PilotedShip;

        /// <summary>The player's purse when it was saved: the treasury of the flown ship's faction (0 when only watching).</summary>
        public double Credits;
        public string Path;

        public DateTime SimUtc => Astro.ToDateTime(SimJd);
    }

    /// <summary>
    /// The save file: a small readable header (when, where, who was flying, their credits) and then the
    /// compressed object graph of a <see cref="SaveData"/> (see <see cref="ObjectGraph"/>).
    /// </summary>
    public static class SaveGame
    {
        const string Magic = "USS-SAVE";
        public const int Format = 1;

        public static void Write(Stream stream, SaveData data, string slot, string label, DateTime savedUtc)
        {
            var w = new BinaryWriter(stream);
            w.Write(Magic);
            w.Write(Format);
            var h = HeaderOf(data, slot, label, savedUtc);
            w.Write(h.Slot ?? "");
            w.Write(h.Label ?? "");
            w.Write(h.SavedUtc.ToBinary());
            w.Write(h.SimJd);
            w.Write(h.ActiveSystem ?? "");
            w.Write(h.PilotedShip ?? "");
            w.Write(h.Credits);
            w.Flush();
            using (var z = new DeflateStream(stream, CompressionLevel.Fastest, true))
            using (var buffered = new BufferedStream(z, 1 << 16))
                ObjectGraph.Write(buffered, data);
        }

        public static SaveHeader ReadHeader(Stream stream)
        {
            var r = new BinaryReader(stream);
            string magic;
            try { magic = r.ReadString(); }
            catch (EndOfStreamException) { throw new InvalidDataException("Not a save file"); }
            if (magic != Magic) throw new InvalidDataException("Not a save file");
            int format = r.ReadInt32();
            if (format != Format) throw new InvalidDataException("A save from another version (format " + format + ")");
            return new SaveHeader
            {
                Format = format,
                Slot = r.ReadString(),
                Label = r.ReadString(),
                SavedUtc = DateTime.FromBinary(r.ReadInt64()),
                SimJd = r.ReadDouble(),
                ActiveSystem = r.ReadString(),
                PilotedShip = NullIfEmpty(r.ReadString()),
                Credits = r.ReadDouble(),
            };
        }

        public static SaveData Read(Stream stream, out SaveHeader header)
        {
            header = ReadHeader(stream);
            using (var z = new DeflateStream(stream, CompressionMode.Decompress, true))
            using (var buffered = new BufferedStream(z, 1 << 16))
            {
                if (!(ObjectGraph.Read(buffered) is SaveData data)) throw new InvalidDataException("The save holds no game");
                return data;
            }
        }

        public static byte[] ToBytes(SaveData data, string slot = "", string label = "") =>
            ToBytes(data, slot, label, DateTime.UtcNow);

        public static byte[] ToBytes(SaveData data, string slot, string label, DateTime savedUtc)
        {
            using (var ms = new MemoryStream())
            {
                Write(ms, data, slot, label, savedUtc);
                return ms.ToArray();
            }
        }

        public static SaveData FromBytes(byte[] bytes)
        {
            using (var ms = new MemoryStream(bytes)) return Read(ms, out _);
        }

        /// <summary>Writes to a temporary file beside <paramref name="path"/> and then swaps it in, so a crash mid-save never costs the old one.</summary>
        public static void WriteFile(string path, byte[] bytes)
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path)));
            string temp = path + ".tmp";
            File.WriteAllBytes(temp, bytes);
            // Swapped in one step: deleting the old first would leave no save at all if the move then failed.
            if (File.Exists(path)) File.Replace(temp, path, null);
            else File.Move(temp, path);
        }

        static SaveHeader HeaderOf(SaveData data, string slot, string label, DateTime savedUtc)
        {
            var piloted = data.Session?.Piloted;
            return new SaveHeader
            {
                Format = Format,
                Slot = slot,
                Label = label,
                SavedUtc = savedUtc,
                SimJd = data.Clock?.Jd ?? 0,
                ActiveSystem = data.Session?.ActiveSystem,
                PilotedShip = piloted?.Name,
                Credits = piloted?.Faction?.Treasury ?? 0,
            };
        }

        static string NullIfEmpty(string s) => string.IsNullOrEmpty(s) ? null : s;
    }

    /// <summary>
    /// The save slots in one folder: a quicksave, as many numbered saves as the player makes, and autosaves
    /// that take turns (the oldest of them is overwritten), each a <c>.sav</c> file named after its slot.
    /// </summary>
    public sealed class SaveSlots
    {
        public const string Quick = "quicksave";
        public const int AutosaveCount = 3;

        const string ManualPrefix = "slot-";

        public readonly string Directory;

        public SaveSlots(string directory) => Directory = directory;

        public static string Manual(int n) => ManualPrefix + n;
        public static string Autosave(int n) => "autosave-" + n;

        public static bool IsManual(string slot) => ManualNumber(slot) > 0;

        public static bool IsAutosave(string slot) => slot.StartsWith("autosave-", StringComparison.Ordinal);

        static int ManualNumber(string slot) =>
            slot != null && slot.StartsWith(ManualPrefix, StringComparison.Ordinal)
            && int.TryParse(slot.Substring(ManualPrefix.Length), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int n)
            && n > 0 ? n : 0;

        /// <summary>The numbered saves on disk, in the order they were first made.</summary>
        public List<string> ManualSlots() =>
            !System.IO.Directory.Exists(Directory)
                ? new List<string>()
                : System.IO.Directory.GetFiles(Directory, ManualPrefix + "*.sav")
                    .Select(Path.GetFileNameWithoutExtension).Where(IsManual).OrderBy(ManualNumber).ToList();

        /// <summary>The slot a new save goes to: the number after the highest there is.</summary>
        public string NewManual() => Manual(ManualSlots().Select(ManualNumber).DefaultIfEmpty(0).Max() + 1);

        /// <summary>Every slot there is or could be loaded from, in the order a list shows them: the quicksave, the player's saves, the autosaves.</summary>
        public IEnumerable<string> AllSlots()
        {
            yield return Quick;
            foreach (var slot in ManualSlots()) yield return slot;
            for (int i = 1; i <= AutosaveCount; i++) yield return Autosave(i);
        }

        /// <summary>Removes the save in <paramref name="slot"/>, if there is one.</summary>
        public void Delete(string slot)
        {
            if (Exists(slot)) File.Delete(PathOf(slot));
        }

        public string PathOf(string slot) => Path.Combine(Directory, slot + ".sav");

        public bool Exists(string slot) => File.Exists(PathOf(slot));

        /// <summary>The header of the save in <paramref name="slot"/>, or null when it is empty or unreadable.</summary>
        public SaveHeader Header(string slot)
        {
            string path = PathOf(slot);
            if (!File.Exists(path)) return null;
            try
            {
                using (var f = File.OpenRead(path))
                {
                    var h = SaveGame.ReadHeader(f);
                    h.Slot = slot;
                    h.Path = path;
                    return h;
                }
            }
            // A corrupt header can also fail as a bad string length (FormatException) or out-of-range timestamp (ArgumentException).
            catch (Exception e) when (e is IOException || e is InvalidDataException || e is UnauthorizedAccessException || e is FormatException || e is ArgumentException)
            {
                return null;
            }
        }

        /// <summary>The filled slots, newest first.</summary>
        public List<SaveHeader> List() =>
            AllSlots().Select(Header).Where(h => h != null).OrderByDescending(h => h.SavedUtc).ToList();

        /// <summary>The most recent save in any slot, or null.</summary>
        public SaveHeader Latest() => List().FirstOrDefault();

        public SaveHeader Save(string slot, SaveData data, string label = null) => Save(slot, data, label, DateTime.UtcNow);

        public SaveHeader Save(string slot, SaveData data, string label, DateTime savedUtc)
        {
            SaveGame.WriteFile(PathOf(slot), SaveGame.ToBytes(data, slot, label ?? slot, savedUtc));
            return Header(slot);
        }

        /// <summary>The autosave slot the next autosave takes: an empty one, else the one saved longest ago.</summary>
        public string NextAutosave()
        {
            string oldest = null;
            DateTime oldestAt = DateTime.MaxValue;
            for (int i = 1; i <= AutosaveCount; i++)
            {
                var slot = Autosave(i);
                var h = Header(slot);
                if (h == null) return slot;
                if (h.SavedUtc < oldestAt)
                {
                    oldestAt = h.SavedUtc;
                    oldest = slot;
                }
            }
            return oldest;
        }

        public SaveHeader Autosave(SaveData data) => Autosave(data, DateTime.UtcNow);

        public SaveHeader Autosave(SaveData data, DateTime savedUtc) => Save(NextAutosave(), data, "Autosave", savedUtc);

        public SaveData Load(string slot) => Load(slot, out _);

        public SaveData Load(string slot, out SaveHeader header)
        {
            using (var f = File.OpenRead(PathOf(slot)))
            {
                var data = SaveGame.Read(f, out header);
                header.Slot = slot;
                header.Path = PathOf(slot);
                return data;
            }
        }
    }
}
