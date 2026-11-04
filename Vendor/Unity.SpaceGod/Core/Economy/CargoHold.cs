using System.Collections.Generic;

namespace Unity.SpaceGod.Core
{
    /// <summary>
    /// Grid size of the player's personal cargo hold, per hull class. Separate from
    /// <see cref="Ship.CapacityTonnes"/>, which is the single tonnage number the AI trade loop
    /// (Economy.cs) uses for every ship and must keep meaning exactly what it already means. Keyed
    /// by <see cref="ShipClass"/> the same way <see cref="PilotTuning"/> keys arcade flight feel: how
    /// many crates physically fit is a hull-wide call, not a per-design balance knob.
    /// </summary>
    public readonly struct CargoHoldSpec
    {
        public readonly int Width, Height;

        public CargoHoldSpec(int width, int height)
        {
            Width = width;
            Height = height;
        }

        static readonly Dictionary<ShipClass, CargoHoldSpec> ByClass = new Dictionary<ShipClass, CargoHoldSpec>
        {
            [ShipClass.Courier] = new CargoHoldSpec(3, 3),
            [ShipClass.Fighter] = new CargoHoldSpec(2, 2),
            [ShipClass.Gunship] = new CargoHoldSpec(3, 2),
            [ShipClass.Interceptor] = new CargoHoldSpec(1, 2),
            [ShipClass.Bomber] = new CargoHoldSpec(3, 2),
            [ShipClass.Recon] = new CargoHoldSpec(1, 2),
            // Its troop bay is for marines (U.SPC-T486): a locker for their kit.
            [ShipClass.AssaultShuttle] = new CargoHoldSpec(3, 2),
            [ShipClass.GatePicket] = new CargoHoldSpec(3, 2),
            // A truss freighter's hold is its pods (U.SPC-T482): six cells wide, a row for each of its stock design's eight pods.
            [ShipClass.Freighter] = new CargoHoldSpec(6, BulkFreighters.StockPods * BulkFreighters.PodRows),
            [ShipClass.Miner] = new CargoHoldSpec(7, 6),
            // An ore tug carries nothing aboard: its load is held in its claws (U.SPC-T483). A locker for the crew's kit.
            [ShipClass.OreTug] = new CargoHoldSpec(2, 2),
            // So does a capture tug (U.SPC-T522).
            [ShipClass.CaptureTug] = new CargoHoldSpec(2, 2),
            [ShipClass.Destroyer] = new CargoHoldSpec(6, 5),
            // Magazines, not holds (U.SPC-T475): a frigate's stores fit a locker.
            [ShipClass.Frigate] = new CargoHoldSpec(4, 3),
            // An impound hold for what its patrol seizes (U.SPC-T476).
            [ShipClass.Corvette] = new CargoHoldSpec(5, 4),
            [ShipClass.Cruiser] = new CargoHoldSpec(7, 6),
            [ShipClass.CommandCruiser] = new CargoHoldSpec(8, 6),
            [ShipClass.Battleship] = new CargoHoldSpec(9, 7),
            // Its siege bay's stores take the room a battleship keeps for a hold (U.SPC-T478).
            [ShipClass.GateBreaker] = new CargoHoldSpec(7, 6),
            [ShipClass.Carrier] = new CargoHoldSpec(10, 8),
        };

        public static CargoHoldSpec Of(ShipClass c) => ByClass.TryGetValue(c, out var s) ? s : new CargoHoldSpec(5, 5);
    }

    /// <summary>
    /// The cell footprint one crate of a good occupies before rotation, and how many tonnes that
    /// crate holds. Bulk minerals sprawl across big cheap crates; dense, high-value goods (medical
    /// supplies, electronics, platinum) pack into a single cell, roughly tracking each good's
    /// <see cref="GoodInfo.BasePrice"/>.
    /// </summary>
    public readonly struct CargoFootprint
    {
        public readonly int Width, Height;
        public readonly double Tonnes;

        public CargoFootprint(int width, int height, double tonnes)
        {
            Width = width;
            Height = height;
            Tonnes = tonnes;
        }

        static readonly Dictionary<Good, CargoFootprint> ByGood = new Dictionary<Good, CargoFootprint>
        {
            [Good.Food] = new CargoFootprint(2, 2, 20),
            [Good.Water] = new CargoFootprint(2, 2, 20),
            [Good.MedicalSupplies] = new CargoFootprint(1, 1, 5),
            [Good.HeliumThree] = new CargoFootprint(2, 1, 12),
            [Good.NickelIron] = new CargoFootprint(3, 3, 60),
            [Good.Copper] = new CargoFootprint(2, 2, 25),
            [Good.Zinc] = new CargoFootprint(2, 2, 25),
            [Good.Platinum] = new CargoFootprint(1, 1, 4),
            [Good.RareEarths] = new CargoFootprint(1, 1, 8),
            [Good.Titanium] = new CargoFootprint(2, 2, 22),
            [Good.Hydrocarbons] = new CargoFootprint(2, 1, 15),
            [Good.Nitrogen] = new CargoFootprint(2, 1, 15),
            [Good.Alloys] = new CargoFootprint(2, 1, 16),
            [Good.Electronics] = new CargoFootprint(1, 1, 5),
            [Good.Machinery] = new CargoFootprint(2, 2, 18),
            [Good.Munitions] = new CargoFootprint(1, 2, 10),
        };

        public static CargoFootprint Of(Good g) => ByGood.TryGetValue(g, out var f) ? f : new CargoFootprint(2, 2, 20);
    }

    /// <summary>One crate placed in a hold: a good occupying a rectangle of cells with its top-left
    /// corner at (X, Y), optionally rotated 90 degrees (swapping its footprint's width and height).</summary>
    public sealed class CargoItem
    {
        public readonly Good Good;
        public int X, Y;
        public bool Rotated;

        /// <summary>Bought at a pirate haven: stolen goods, contraband to any lawful patrol (see <see cref="Law.ContrabandIn"/>)
        /// and refused at a lawful market.</summary>
        public bool Stolen;

        public CargoItem(Good good, int x, int y, bool rotated)
        {
            Good = good;
            X = x;
            Y = y;
            Rotated = rotated;
        }

        public double Tonnes => CargoFootprint.Of(Good).Tonnes;
        public int Width => Rotated ? CargoFootprint.Of(Good).Height : CargoFootprint.Of(Good).Width;
        public int Height => Rotated ? CargoFootprint.Of(Good).Width : CargoFootprint.Of(Good).Height;
    }

    /// <summary>
    /// The player's personal grid-based cargo hold: a fixed W x H grid of cells that crates must
    /// physically fit into, rotation included. Entirely separate from the single-slot
    /// <see cref="Ship.CapacityTonnes"/>/<see cref="Ship.CargoGood"/>/<see cref="Ship.CargoTonnes"/>
    /// fields every AI trader reads and writes in Economy.cs -- this hold exists only for the one
    /// ship a human is flying and never touches those fields.
    /// </summary>
    public sealed class CargoHold
    {
        public readonly int Width, Height;
        readonly List<CargoItem> items = new List<CargoItem>();

        public IReadOnlyList<CargoItem> Items => items;

        public CargoHold(int width, int height)
        {
            Width = width;
            Height = height;
        }

        public CargoHold(CargoHoldSpec spec) : this(spec.Width, spec.Height) { }

        /// <summary>Whether a w x h footprint at (x, y) lands fully on the grid and clear of every other
        /// crate. <paramref name="ignore"/> excludes one item from the overlap check, so a crate can be
        /// tested against a new position that overlaps its own current cells.</summary>
        public bool Fits(int x, int y, int w, int h, CargoItem ignore = null)
        {
            if (x < 0 || y < 0 || w <= 0 || h <= 0 || x + w > Width || y + h > Height) return false;
            foreach (var item in items)
            {
                if (item == ignore) continue;
                if (x < item.X + item.Width && item.X < x + w && y < item.Y + item.Height && item.Y < y + h) return false;
            }
            return true;
        }

        public bool Fits(Good good, int x, int y, bool rotated, CargoItem ignore = null)
        {
            var f = CargoFootprint.Of(good);
            int w = rotated ? f.Height : f.Width;
            int h = rotated ? f.Width : f.Height;
            return Fits(x, y, w, h, ignore);
        }

        /// <summary>Places a new crate at an explicit position/rotation; null (hold left unchanged) if it
        /// doesn't fit.</summary>
        public CargoItem Add(Good good, int x, int y, bool rotated)
        {
            if (!Fits(good, x, y, rotated)) return null;
            var item = new CargoItem(good, x, y, rotated);
            items.Add(item);
            return item;
        }

        public bool Remove(CargoItem item) => items.Remove(item);

        /// <summary>How many more crates of this good the free cells take, packed first-fit as AddFirstFit would.</summary>
        public int CrateRoom(Good good)
        {
            var trial = new CargoHold(Width, Height);
            foreach (var item in items) trial.items.Add(new CargoItem(item.Good, item.X, item.Y, item.Rotated));
            int n = 0;
            while (trial.AddFirstFit(good) != null) n++;
            return n;
        }

        /// <summary>The tonnes of this good the free cells carry: free crates x the crate's tonnes.</summary>
        public double RoomTonnes(Good good) => CrateRoom(good) * CargoFootprint.Of(good).Tonnes;

        /// <summary>Scans the grid row-major, unrotated then rotated, for the first cell a new crate of
        /// this good fits in, and places it there. Null if the hold has no room left for it.</summary>
        public CargoItem AddFirstFit(Good good)
        {
            var f = CargoFootprint.Of(good);
            foreach (bool rotated in new[] { false, true })
            {
                int w = rotated ? f.Height : f.Width;
                int h = rotated ? f.Width : f.Height;
                for (int y = 0; y <= Height - h; y++)
                    for (int x = 0; x <= Width - w; x++)
                        if (Fits(x, y, w, h))
                            return Add(good, x, y, rotated);
            }
            return null;
        }

        /// <summary>Moves an existing crate to a new position/rotation, rejecting overlap or going out of
        /// bounds; on failure the crate stays exactly where it was.</summary>
        public bool Move(CargoItem item, int newX, int newY, bool newRotated)
        {
            if (!Fits(item.Good, newX, newY, newRotated, item)) return false;
            item.X = newX;
            item.Y = newY;
            item.Rotated = newRotated;
            return true;
        }
    }
}
