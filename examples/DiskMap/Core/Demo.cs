namespace DiskMap.Core;

/// <summary>
/// A deterministic home folder of about 40k items, for --demo, offscreen snapshots and view checks. It never reads
/// the disk; sizes follow a fixed generator so every run draws the same picture.
/// </summary>
public static class Demo
{
    public const string Root = "/Users/demo";
    const long Modified = 1_793_000_000;

    public static Item Home()
    {
        var random = new Generator(42);
        return Item.Folder("demo", 0, Modified,
        [
            Folder("Library", random,
            [
                Folder("Developer", random,
                [
                    Folder("Xcode", random, [Bulk("DerivedData", random, 900, 22_000_000), Bulk("Archives", random, 60, 90_000_000)]),
                    Folder("CoreSimulator", random, [Bulk("Devices", random, 1400, 9_000_000), Bulk("Caches", random, 300, 4_000_000)])
                ]),
                Folder("Caches", random, [Bulk("com.apple.Safari", random, 800, 600_000), Bulk("Homebrew", random, 120, 30_000_000), Bulk("pip", random, 400, 2_000_000)]),
                Folder("Containers", random, [Bulk("com.docker.docker", random, 8, 3_800_000_000), Bulk("com.apple.mail", random, 2400, 900_000)]),
                Bulk("Application Support", random, 1800, 3_000_000)
            ]),
            Folder(".cargo", random,
            [
                Folder("targets", random,
                [
                    Target("nsh-0455aaa1", random, 41_200_000_000),
                    Target("shared-deps", random, 26_700_000_000, release: 4_300_000_000, windows: 4_900_000_000),
                    Target("gpui-kit-af01a901", random, 6_100_000_000),
                    Target("nsh-compat-4bc5fe3c", random, 3_500_000_000),
                    Target("omamail-72e66117", random, 3_500_000_000),
                    Target("embed-test-0455aaa1", random, 2_700_000_000),
                    Target("nsh-compat-747a6703", random, 2_200_000_000),
                    Target("gpui-fast-61e5a6a3", random, 1_900_000_000)
                ]),
                Bulk("registry", random, 4200, 180_000)
            ]),
            Folder("Movies", random, [Bulk("Exports", random, 14, 2_400_000_000), Bulk("Screen Recordings", random, 40, 300_000_000)]),
            Folder("Music", random, [Bulk("Logic", random, 900, 12_000_000)]),
            Folder("Pictures", random, [Bulk("Photos Library.photoslibrary", random, 6000, 3_400_000)]),
            Folder("Documents", random, [Bulk("Invoices", random, 300, 240_000), Bulk("Taxes", random, 120, 900_000), Bulk("Notes", random, 2000, 12_000)]),
            Folder("Downloads", random, [Bulk("Installers", random, 22, 600_000_000), Bulk("Papers", random, 160, 3_000_000)]),
            Folder("projects", random,
            [
                Folder("kumiko", random, [Bulk("artifacts", random, 900, 3_000_000), Bulk("node_modules", random, 9000, 60_000), Bulk("src", random, 1200, 14_000)]),
                Folder("site", random, [Bulk("node_modules", random, 7000, 70_000), Bulk(".next", random, 1500, 400_000)])
            ]),
            Item.File(".zsh_history", 1_200_000, 1_150_000, Modified),
            Item.File(".DS_Store", 12_288, 10_244, Modified)
        ]);
    }

    /// <summary>A Cargo target folder: debug output, optionally a release build and a cross-compiled target.</summary>
    static Item Target(string name, Generator random, long debug, long release = 0, long windows = 0)
    {
        var parts = new List<Item> { Sized("debug", random, debug) };
        if (release > 0)
            parts.Add(Sized("release", random, release));
        if (windows > 0)
            parts.Add(Folder("x86_64-pc-windows-msvc", random, [Sized("debug", random, windows)]));
        parts.Add(Bulk("wasm32-unknown-unknown", random, 30, 9_000_000));
        return Folder(name, random, parts);
    }

    /// <summary>A build folder of about the given size, split like Cargo's deps, build and incremental folders.</summary>
    static Item Sized(string name, Generator random, long size) => Folder(name, random,
    [
        Bulk("deps", random, 600, size * 55 / 100 / 600),
        Bulk("incremental", random, 300, size * 30 / 100 / 300),
        Bulk("build", random, 400, size * 15 / 100 / 400)
    ]);

    static Item Folder(string name, Generator random, IEnumerable<Item> children) => Item.Folder(name, 4096, Modified - random.Next(9_000_000), children);

    /// <summary>A folder of generated files around a mean size, with a long tail of large ones.</summary>
    static Item Bulk(string name, Generator random, int count, long mean)
    {
        var files = new Item[count];
        for (int i = 0; i < count; i++)
        {
            var apparent = Math.Max(1, (long)(mean * random.Skewed()));
            var size = (apparent + 4095) / 4096 * 4096;
            files[i] = Item.File(FileName(name, i, random), size, apparent, Modified - random.Next(30_000_000));
        }
        return Folder(name, random, files);
    }

    static string FileName(string folder, int index, Generator random)
    {
        var stem = folder switch
        {
            "deps" => "lib" + Word(random) + "-" + random.Hex() + index + ".rlib",
            "incremental" => Word(random) + "-" + random.Hex() + index + ".o",
            "Exports" or "Screen Recordings" => "Recording " + (index + 1) + ".mov",
            "Photos Library.photoslibrary" => "IMG_" + (4000 + index) + ".heic",
            "node_modules" => Word(random) + "-" + index + ".js",
            _ => Word(random) + "-" + index + Extension(random)
        };
        return stem;
    }

    static string Word(Generator random) => random.Next(12) switch
    {
        0 => "serde",
        1 => "tokio",
        2 => "syn",
        3 => "regex",
        4 => "hyper",
        5 => "gpui",
        6 => "proc_macro2",
        7 => "wasm_bindgen",
        8 => "anyhow",
        9 => "rayon",
        10 => "objc2",
        _ => "taffy"
    };

    static string Extension(Generator random) => random.Next(6) switch
    {
        0 => ".json",
        1 => ".png",
        2 => ".pdf",
        3 => ".zip",
        4 => ".dat",
        _ => ".txt"
    };

    /// <summary>A small fixed linear congruential generator, so the demo never depends on runtime randomness.</summary>
    sealed class Generator(ulong state)
    {
        public long Next(long bound)
        {
            state = state * 6364136223846793005UL + 1442695040888963407UL;
            return (long)((state >> 33) % (ulong)Math.Max(1, bound));
        }

        /// <summary>Mostly near 1, sometimes up to 20 times larger.</summary>
        public double Skewed()
        {
            double u = (Next(1_000_000) + 1) / 1_000_001.0;
            return Math.Min(20, 0.5 / Math.Sqrt(u));
        }

        public string Hex() => Next(0xffffff).ToString("x6", System.Globalization.CultureInfo.InvariantCulture);
    }
}
