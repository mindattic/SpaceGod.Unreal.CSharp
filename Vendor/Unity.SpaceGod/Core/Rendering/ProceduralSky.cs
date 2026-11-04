using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Unity.SpaceGod.Core
{
    /// <summary>One nebula layer of a seeded sky: its colour and the noise it is cut from.</summary>
    public readonly struct NebulaLayer
    {
        public readonly double R, G, B, Scale, Intensity, Falloff, OffsetX, OffsetY, OffsetZ;

        public NebulaLayer(double r, double g, double b, double scale, double intensity, double falloff, double ox, double oy, double oz)
        {
            R = r; G = g; B = b;
            Scale = scale; Intensity = intensity; Falloff = falloff;
            OffsetX = ox; OffsetY = oy; OffsetZ = oz;
        }
    }

    /// <summary>
    /// A star system's own sky (U.SPC-T595): layered, domain-warped Perlin nebulae made from the system's seed, ported from
    /// wwwtyro's Space 3D (github.com/wwwtyro/space-3d, Unlicense): one to a few layers, each a random colour at a random
    /// scale, intensity and falloff, blended over black. The same name always gives the same sky. It is laid out as an
    /// ecliptic-frame panorama (<see cref="SkyFrame.Ecliptic"/>) so the view draws it like any other; the point-cloud
    /// stars are drawn over it. Pure and thread-safe: the view bakes it on a worker thread, so it never stalls a frame.
    /// </summary>
    public static class ProceduralSky
    {
        /// <summary>The seed a system's sky is made from: its name, hashed the same on every runtime (FNV-1a).</summary>
        public static ulong SeedFor(string systemName)
        {
            ulong h = 0xCBF29CE484222325UL;
            foreach (char c in systemName ?? "")
            {
                h ^= c;
                h *= 0x100000001B3UL;
            }
            return SeededRandom.Mix(unchecked((long)h), 595);
        }

        /// <summary>The nebula layers of the sky made from <paramref name="seed"/>: at least one, each further one at even odds.</summary>
        public static List<NebulaLayer> Layers(ulong seed)
        {
            var rng = new SeededRandom(seed);
            var layers = new List<NebulaLayer>();
            do
            {
                layers.Add(new NebulaLayer(
                    rng.NextDouble(), rng.NextDouble(), rng.NextDouble(),
                    rng.NextDouble() * 0.5 + 0.25,
                    rng.NextDouble() * 0.2 + 0.9,
                    rng.NextDouble() * 3.0 + 3.0,
                    rng.NextDouble() * 2000 - 1000, rng.NextDouble() * 2000 - 1000, rng.NextDouble() * 2000 - 1000));
            } while (rng.NextDouble() >= 0.5 && layers.Count < 6);
            return layers;
        }

        /// <summary>The sky's colour (0..1 each) toward ecliptic direction (<paramref name="x"/>, <paramref name="y"/>, <paramref name="z"/>), unit length.</summary>
        public static void Sample(IReadOnlyList<NebulaLayer> layers, double x, double y, double z, out double r, out double g, out double b)
        {
            r = g = b = 0;
            for (int i = 0; i < layers.Count; i++)
            {
                var l = layers[i];
                double c = Math.Min(1.0, Nebula(x * l.Scale + l.OffsetX, y * l.Scale + l.OffsetY, z * l.Scale + l.OffsetZ) * l.Intensity);
                c = Math.Pow(Math.Max(0, c), l.Falloff);
                r = r * (1 - c) + l.R * c;
                g = g * (1 - c) + l.G * c;
                b = b * (1 - c) + l.B * c;
            }
        }

        /// <summary>
        /// The sky as an RGBA32 panorama <paramref name="width"/> by width/2 (row 0 at the bottom, latitude -90), with its whole
        /// mip chain after it, ready to upload: <paramref name="mipOffsets"/> gives where each level starts. Rows are made in parallel
        /// (leaving two cores to the game); <paramref name="cancel"/> stops a bake no longer wanted.
        /// </summary>
        public static byte[] BakePanorama(ulong seed, int width, out int[] mipOffsets, CancellationToken cancel = default)
        {
            if (width < 4 || (width & (width - 1)) != 0) throw new ArgumentException("A power of two, 4 or more.", nameof(width));
            int height = width / 2;
            var layers = Layers(seed);
            var levels = new List<int[]>();
            int total = 0;
            for (int w = width, h = height; ; w = Math.Max(1, w / 2), h = Math.Max(1, h / 2))
            {
                levels.Add(new[] { w, h, total });
                total += w * h * 4;
                if (w == 1 && h == 1) break;
            }
            var data = new byte[total];
            var options = new ParallelOptions { CancellationToken = cancel, MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 2) };
            Parallel.For(0, height, options, row =>
            {
                double lat = ((row + 0.5) / height - 0.5) * Math.PI;
                double cl = Math.Cos(lat), sl = Math.Sin(lat);
                int o = row * width * 4;
                for (int col = 0; col < width; col++, o += 4)
                {
                    // u = 0.5 - longitude / 2pi, as SkyboxCatalog.PanoramaUv reads it.
                    double lon = (0.5 - (col + 0.5) / width) * 2 * Math.PI;
                    Sample(layers, cl * Math.Cos(lon), cl * Math.Sin(lon), sl, out double r, out double g, out double b);
                    data[o] = ToByte(r);
                    data[o + 1] = ToByte(g);
                    data[o + 2] = ToByte(b);
                    data[o + 3] = 255;
                }
            });
            for (int i = 1; i < levels.Count; i++) Downsample(data, levels[i - 1], levels[i]);
            mipOffsets = new int[levels.Count];
            for (int i = 0; i < levels.Count; i++) mipOffsets[i] = levels[i][2];
            return data;
        }

        static byte ToByte(double v) => (byte)Math.Max(0, Math.Min(255, (int)(v * 255 + 0.5)));

        static void Downsample(byte[] data, int[] from, int[] to)
        {
            int fw = from[0], fh = from[1], fo = from[2], tw = to[0], th = to[1], too = to[2];
            for (int y = 0; y < th; y++)
                for (int x = 0; x < tw; x++)
                    for (int c = 0; c < 4; c++)
                    {
                        int x0 = Math.Min(fw - 1, x * 2), x1 = Math.Min(fw - 1, x * 2 + 1), y0 = Math.Min(fh - 1, y * 2), y1 = Math.Min(fh - 1, y * 2 + 1);
                        int s = data[fo + (y0 * fw + x0) * 4 + c] + data[fo + (y0 * fw + x1) * 4 + c] + data[fo + (y1 * fw + x0) * 4 + c] + data[fo + (y1 * fw + x1) * 4 + c];
                        data[too + (y * tw + x) * 4 + c] = (byte)((s + 2) / 4);
                    }
        }

        /// <summary>Space 3D's nebula: six octaves of noise, each displacing the next, then one more read at the base scale.</summary>
        static double Nebula(double x, double y, double z)
        {
            double scale = 64; // 2^6
            double dx = 0, dy = 0, dz = 0;
            for (int i = 0; i < 6; i++)
            {
                double nx = Noise(x * scale + dx, y * scale + dy, z * scale + dz);
                double ny = Noise(y * scale + dx, z * scale + dy, x * scale + dz);
                double nz = Noise(z * scale + dx, x * scale + dy, y * scale + dz);
                dx = nx; dy = ny; dz = nz;
                scale *= 0.5;
            }
            return Noise(x * scale + dx, y * scale + dy, z * scale + dz);
        }

        /// <summary>Perlin noise remapped to about 0..1, as Space 3D's <c>0.5 * cnoise + 0.5</c>.</summary>
        static double Noise(double x, double y, double z) => 0.5 * Perlin(x, y, z) + 0.5;

        // Ken Perlin's improved noise (2002), with his reference permutation.
        static readonly int[] P = BuildPermutation();

        static int[] BuildPermutation()
        {
            int[] p =
            {
                151,160,137,91,90,15,131,13,201,95,96,53,194,233,7,225,140,36,103,30,69,142,8,99,37,240,21,10,23,190,6,148,
                247,120,234,75,0,26,197,62,94,252,219,203,117,35,11,32,57,177,33,88,237,149,56,87,174,20,125,136,171,168,68,175,
                74,165,71,134,139,48,27,166,77,146,158,231,83,111,229,122,60,211,133,230,220,105,92,41,55,46,245,40,244,102,143,54,
                65,25,63,161,1,216,80,73,209,76,132,187,208,89,18,169,200,196,135,130,116,188,159,86,164,100,109,198,173,186,3,64,
                52,217,226,250,124,123,5,202,38,147,118,126,255,82,85,212,207,206,59,227,47,16,58,17,182,189,28,42,223,183,170,213,
                119,248,152,2,44,154,163,70,221,153,101,155,167,43,172,9,129,22,39,253,19,98,108,110,79,113,224,232,178,185,112,104,
                218,246,97,228,251,34,242,193,238,210,144,12,191,179,162,241,81,51,145,235,249,14,239,107,49,192,214,31,181,199,106,157,
                184,84,204,176,115,121,50,45,127,4,150,254,138,236,205,93,222,114,67,29,24,72,243,141,128,195,78,66,215,61,156,180,
            };
            var q = new int[512];
            for (int i = 0; i < 512; i++) q[i] = p[i & 255];
            return q;
        }

        static double Perlin(double x, double y, double z)
        {
            double fx = Math.Floor(x), fy = Math.Floor(y), fz = Math.Floor(z);
            int X = (int)((long)fx & 255), Y = (int)((long)fy & 255), Z = (int)((long)fz & 255);
            x -= fx; y -= fy; z -= fz;
            double u = Fade(x), v = Fade(y), w = Fade(z);
            int A = P[X] + Y, AA = P[A] + Z, AB = P[A + 1] + Z, B = P[X + 1] + Y, BA = P[B] + Z, BB = P[B + 1] + Z;
            return Lerp(w,
                Lerp(v, Lerp(u, Grad(P[AA], x, y, z), Grad(P[BA], x - 1, y, z)),
                        Lerp(u, Grad(P[AB], x, y - 1, z), Grad(P[BB], x - 1, y - 1, z))),
                Lerp(v, Lerp(u, Grad(P[AA + 1], x, y, z - 1), Grad(P[BA + 1], x - 1, y, z - 1)),
                        Lerp(u, Grad(P[AB + 1], x, y - 1, z - 1), Grad(P[BB + 1], x - 1, y - 1, z - 1))));
        }

        static double Fade(double t) => t * t * t * (t * (t * 6 - 15) + 10);
        static double Lerp(double t, double a, double b) => a + t * (b - a);

        static double Grad(int hash, double x, double y, double z)
        {
            int h = hash & 15;
            double u = h < 8 ? x : y, v = h < 4 ? y : h == 12 || h == 14 ? x : z;
            return ((h & 1) == 0 ? u : -u) + ((h & 2) == 0 ? v : -v);
        }
    }
}
