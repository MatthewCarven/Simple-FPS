using System.Diagnostics;
using SimpleFps.Core.Bake;
using SimpleFps.Core.Bsp;
using SimpleFps.Core.Textures;
using Xunit.Abstractions;

namespace SimpleFps.Core.Tests;

/// <summary>
/// Bakes every texture and sky in the real content folder. Content isn't in the repo, so on a fresh
/// clone this reports that it had nothing to check and passes without proving anything.
/// </summary>
public class TextureCollectionTests(ITestOutputHelper output)
{
    /// <summary>
    /// A sky's faces may change across an edge by up to this much more than they do one pixel in.
    /// Turning or swapping a face makes the change across its edges several times bigger.
    /// </summary>
    private static bool JoinsUp(double across, double inside) => across <= inside * 1.5 + 3;

    /// <summary>
    /// Some skies don't join because of their pictures, not their layout: in 2026-11 a collage made
    /// not to (teehee_), a bottom that doesn't match its walls (hell1), damaged .tga files
    /// (mountainlakes). A layout mistake would break nearly every sky, so a few are allowed.
    /// </summary>
    private const double AllowedRoughShare = 0.05;

    private static string? FindContent()
    {
        for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            string content = Path.Combine(dir.FullName, "content");
            if (Directory.Exists(Path.Combine(content, "maps")))
            {
                return content;
            }
        }

        return null;
    }

    private static IEnumerable<Q2Bsp> Quake2Maps(string content)
    {
        foreach (string file in Directory.EnumerateFiles(Path.Combine(content, "maps"), "*.bsp"))
        {
            byte[] data = File.ReadAllBytes(file);
            if (BspFormat.Identify(data) == BspKind.Quake2)
            {
                yield return Q2Bsp.Read(data);
            }
        }
    }

    [Fact]
    public void EveryTextureTheMapsAskForBakesAndReadsBackExactly()
    {
        string? content = FindContent();
        if (content is null)
        {
            output.WriteLine("No content folder, so nothing was checked.");
            return;
        }

        var names = new SortedSet<string>(StringComparer.Ordinal);
        foreach (Q2Bsp bsp in Quake2Maps(content))
        {
            foreach (MeshBatch batch in MapMesher.Build(bsp).Models.SelectMany(m => m))
            {
                if ((batch.Flags & SurfaceFlags.Sky) == 0)
                {
                    names.Add(batch.Texture);
                }
            }
        }

        string cacheFolder = Directory.CreateTempSubdirectory("simplefps-alltex-").FullName;
        try
        {
            var cache = new TextureCache(content, cacheFolder);
            var index = new TextureIndex(Path.Combine(content, "textures"));
            Palette palette = Palette.Load(Path.Combine(content, "palette.act"));
            var failures = new List<string>();
            int originals = 0, highRes = 0;
            long originalBytes = 0, highResBytes = 0;
            var clock = Stopwatch.StartNew();

            foreach (string name in names)
            {
                (BakedTexture? original, bool baked) = cache.Wall(name, TextureSet.Original);
                if (original is null)
                {
                    continue;
                }

                originals++;
                originalBytes += CheckReadsBack(cache, name, TextureSet.Original, original, baked, failures);

                // What the game showed before 3b: the .wal straight through the palette.
                byte[] expected = WalTexture.Load(index.Find(name)!).ToRgba(palette);
                if (!original.Images[0].Rgba.AsSpan(0, expected.Length).SequenceEqual(expected))
                {
                    failures.Add($"{name}: baked pixels differ from the .wal's");
                }

                (BakedTexture? high, bool highBaked) = cache.Wall(name, TextureSet.HighRes);
                if (high is null)
                {
                    continue;
                }

                highRes++;
                highResBytes += CheckReadsBack(cache, name, TextureSet.HighRes, high, highBaked, failures);
                BakedImage picture = high.Images[0];
                if ((high.Width, high.Height) != (original.Width, original.Height)
                    || (long)picture.Width * original.Height != (long)picture.Height * original.Width)
                {
                    failures.Add($"{name}: high-res {picture.Width} x {picture.Height} doesn't keep the .wal's {original.Width} x {original.Height}");
                }
            }

            output.WriteLine($"{names.Count} texture names, {originals} with a .wal, {names.Count - originals} drawn as the stand-in ({cache.Problems.Count} of those have a .wal that won't read)");
            output.WriteLine($"{highRes} from the high-res pack, {cache.OddShapes.Count} left out for their shape");
            output.WriteLine($"bakes: {originalBytes / 1e6:F0} MB of originals, {highResBytes / 1e6:F0} MB of high-res, {clock.Elapsed.TotalSeconds:F1} s");
            foreach (string line in cache.OddShapes.Concat(cache.Problems).Concat(failures.Take(20)))
            {
                output.WriteLine($"  {line}");
            }

            Assert.Empty(failures);
        }
        finally
        {
            Directory.Delete(cacheFolder, recursive: true);
        }
    }

    [Fact]
    public void EverySkyBakesAndItsFacesJoinUp()
    {
        string? content = FindContent();
        if (content is null)
        {
            output.WriteLine("No content folder, so nothing was checked.");
            return;
        }

        // The sky every loadable map asks for, from its override's entities when it has one.
        var mapCache = new MapCache(Path.Combine(content, "maps"), Path.Combine(Path.GetTempPath(), "unused"));
        var mapsBySky = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (string map in mapCache.Names())
        {
            MapCache.Source source = mapCache.Find(map);
            IReadOnlyList<BspEntity> entities = source.Override is { } file && MapOverride.Read(file).Entities is { } text
                ? BspEntity.Parse(text)
                : Q2Bsp.Read(File.ReadAllBytes(source.BspPath)).Entities;
            string sky = MapSky.From(entities).Name;
            if (!mapsBySky.TryGetValue(sky, out List<string>? maps))
            {
                mapsBySky[sky] = maps = [];
            }

            maps.Add(map);
        }

        // Plus every other sky box in the folders, named or not.
        var skies = new SortedSet<string>(mapsBySky.Keys, StringComparer.Ordinal);
        foreach (string folder in new[] { Path.Combine(content, "env"), Path.Combine(content, "q2textures-master", "pak96_env", "env") })
        {
            foreach (string file in Directory.EnumerateFiles(folder, "*rt.*", SearchOption.AllDirectories))
            {
                string relative = Path.GetRelativePath(folder, file).Replace('\\', '/').ToLowerInvariant();
                if (relative.EndsWith("rt.tga") || relative.EndsWith("rt.pcx") || relative.EndsWith("rt.png"))
                {
                    skies.Add(relative[..^"rt.tga".Length]);
                }
            }
        }

        string cacheFolder = Directory.CreateTempSubdirectory("simplefps-allsky-").FullName;
        try
        {
            var cache = new TextureCache(content, cacheFolder);
            var failures = new List<string>();
            var rough = new List<string>();
            var seams = new List<(string Sky, double Across, double Inside)>();
            var found = new HashSet<string>(StringComparer.Ordinal);
            int highRes = 0;
            var clock = Stopwatch.StartNew();

            foreach (string sky in skies)
            {
                foreach (TextureSet set in new[] { TextureSet.Original, TextureSet.HighRes })
                {
                    (BakedTexture? box, bool baked) = cache.Sky(sky, set);
                    if (box is null)
                    {
                        continue;
                    }

                    if (set == TextureSet.Original)
                    {
                        found.Add(sky);
                    }
                    else
                    {
                        highRes++;
                    }

                    CheckReadsBack(cache, sky, set, box, baked, failures);

                    RgbaImage[] faces = box.Images.Select(f => new RgbaImage(f.Width, f.Height, f.Rgba)).ToArray();
                    (double across, double inside) = SkySeams.Measure(faces);
                    string label = set == TextureSet.HighRes ? $"{sky} (high-res)" : sky;
                    seams.Add((label, across, inside));
                    if (!JoinsUp(across, inside))
                    {
                        rough.Add($"{label}: {across:F1} across its edges against {inside:F1} inside");
                    }
                }
            }

            var missing = mapsBySky.Where(m => !found.Contains(m.Key)).ToList();
            output.WriteLine($"{mapsBySky.Values.Sum(m => m.Count)} maps name {mapsBySky.Count} skies; {missing.Sum(m => m.Value.Count)} maps' skies have no pictures:");
            foreach ((string sky, List<string> maps) in missing)
            {
                output.WriteLine($"  {sky}: {string.Join(", ", maps)}");
            }

            output.WriteLine($"{skies.Count} skies in all: {found.Count} baked from env, {highRes} from the pack, in {clock.Elapsed.TotalSeconds:F1} s");
            double medianRatio = seams.Select(s => s.Across / Math.Max(s.Inside, 0.5)).Order().ElementAt(seams.Count / 2);
            output.WriteLine($"{rough.Count} of {seams.Count} sky boxes don't join; across their edges they change {medianRatio:F2} times as much as inside, at the median:");
            foreach (string line in rough.Concat(cache.Problems).Concat(failures))
            {
                output.WriteLine($"  {line}");
            }

            Assert.Empty(failures);
            Assert.True(rough.Count <= seams.Count * AllowedRoughShare, $"{rough.Count} of {seams.Count} skies don't join up");
        }
        finally
        {
            Directory.Delete(cacheFolder, recursive: true);
        }
    }

    /// <summary>
    /// Asking again must come from the bake on disk, unchanged. Deletes the bake afterwards so the
    /// whole collection never sits in the temp folder at once. Returns the bake's size.
    /// </summary>
    private static long CheckReadsBack(TextureCache cache, string name, TextureSet set, BakedTexture first, bool baked, List<string> failures)
    {
        bool sky = first.Images.Count == 6;
        string path = cache.BakePath(sky ? "skies" : "textures", set, name, sky ? TextureCache.SkyExtension : TextureCache.TextureExtension);
        (BakedTexture? again, bool bakedAgain) = sky ? cache.Sky(name, set) : cache.Wall(name, set);
        if (!baked || bakedAgain || again is null || !File.Exists(path))
        {
            failures.Add($"{name} ({set}): baked {baked}, then baked again {bakedAgain}");
            return 0;
        }

        bool same = (again.Width, again.Height, again.Source, again.Images.Count) == (first.Width, first.Height, first.Source, first.Images.Count)
            && again.Images.Zip(first.Images).All(p => p.First.Levels == p.Second.Levels && p.First.Rgba.AsSpan().SequenceEqual(p.Second.Rgba));
        if (!same)
        {
            failures.Add($"{name} ({set}): reads back differently from how it was baked");
        }

        long size = new FileInfo(path).Length;
        File.Delete(path);
        return size;
    }
}
