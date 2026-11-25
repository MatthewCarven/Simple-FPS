using SimpleFps.Core.Bake;
using SimpleFps.Core.Bsp;
using SimpleFps.Core.Collision;
using SimpleFps.Core.Sounds;
using Xunit.Abstractions;

namespace SimpleFps.Core.Tests;

/// <summary>
/// Runs the sound reader and bake over every real sound and map in the content folder. Content
/// isn't in the repo, so on a fresh clone these report that they had nothing to check and pass.
/// </summary>
public class SoundCollectionTests(ITestOutputHelper output)
{
    private static string? FindContent()
    {
        for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            string content = Path.Combine(dir.FullName, "content");
            if (Directory.Exists(Path.Combine(content, "maps")) && Directory.Exists(Path.Combine(content, "sound")))
            {
                return content;
            }
        }

        return null;
    }

    /// <summary>Every map's entities, from its override file where it has one.</summary>
    private static IEnumerable<(string Map, List<BspEntity> Entities)> EveryMapsEntities(string content)
    {
        var cache = new MapCache(Path.Combine(content, "maps"), Path.Combine(Path.GetTempPath(), "simplefps-unused"));
        foreach (string name in cache.Names())
        {
            MapCache.Source source = cache.Find(name);
            if (source.Override is not null && MapOverride.Read(source.Override).Entities is { } replacement)
            {
                yield return (name, BspEntity.Parse(replacement));
                continue;
            }

            using FileStream stream = File.OpenRead(source.BspPath);
            yield return (name, Q2Bsp.ReadEntities(stream));
        }
    }

    [Fact]
    public void EverySoundTheMapsNameBakesAndReadsBackOrIsRefusedForAReason()
    {
        string? content = FindContent();
        if (content is null)
        {
            output.WriteLine("No content folder, so nothing was checked.");
            return;
        }

        // Every sound any entity names: speakers, trains and one worldspawn.
        var named = new Dictionary<string, SortedSet<string>>();
        int maps = 0, speakers = 0, loopsOn = 0;
        foreach ((string map, List<BspEntity> entities) in EveryMapsEntities(content))
        {
            maps++;
            var events = new SoundEvents();
            speakers += TargetSpeakers.Start(entities, events).Count;
            loopsOn += events.Loops.Count(l => l.On);
            foreach (string noise in entities.Select(e => e["noise"]).OfType<string>())
            {
                string file = SoundNames.ToFile(noise);
                if (!named.TryGetValue(file, out SortedSet<string>? where))
                {
                    named[file] = where = [];
                }

                where.Add(map);
            }
        }

        string cacheFolder = Directory.CreateTempSubdirectory("simplefps-soundbakes-").FullName;
        try
        {
            var refused = new List<string>();
            var mismatches = new List<string>();
            int baked = 0, looped = 0;
            long bytes = 0;
            double seconds = 0;
            foreach ((string file, SortedSet<string> where) in named.OrderBy(n => n.Key, StringComparer.Ordinal))
            {
                SoundLoad first = new SoundCache(content, cacheFolder).Load(file);
                if (first.Sound is null)
                {
                    Assert.False(string.IsNullOrWhiteSpace(first.Refused), $"{file} was refused with no reason");
                    refused.Add($"{first.Refused} (in {string.Join(", ", where.Take(3))}{(where.Count > 3 ? ", ..." : "")})");
                    continue;
                }

                SoundLoad again = new SoundCache(content, cacheFolder).Load(file);
                PcmSound direct = WavReader.Read(File.ReadAllBytes(Path.Combine(content, "sound", file)));
                BakedSound sound = again.Sound!;
                if (!first.Baked || again.Baked || sound.SampleRate != direct.SampleRate || sound.LoopStart != direct.LoopStart
                    || !sound.Samples.AsSpan().SequenceEqual(direct.Samples) || !sound.Samples.AsSpan().SequenceEqual(first.Sound.Samples))
                {
                    mismatches.Add(file);
                }

                baked++;
                looped += sound.LoopStart >= 0 ? 1 : 0;
                bytes += new FileInfo(new SoundCache(content, cacheFolder).BakePath(file)).Length;
                seconds += sound.Seconds;
            }

            output.WriteLine($"{maps} maps name {named.Count} sounds; {speakers} speakers, {loopsOn} loops start on");
            output.WriteLine($"{baked} baked and read back ({looped} with a loop point), {seconds / 60:F1} minutes of sound, {bytes / 1e6:F1} MB of bakes; {refused.Count} refused:");
            foreach (string reason in refused)
            {
                output.WriteLine($"  {reason}");
            }

            Assert.True(mismatches.Count == 0, "Read back differently: " + string.Join(", ", mismatches.Take(20)));
            Assert.True(named.Count >= 200 && baked >= 200, $"Only {baked} of {named.Count} named sounds baked, so this proved little.");
        }
        finally
        {
            Directory.Delete(cacheFolder, recursive: true);
        }
    }

    [Fact]
    public void EveryWavInTheSoundFolderReadsOrIsRefusedForAReason()
    {
        string? content = FindContent();
        if (content is null)
        {
            output.WriteLine("No content folder, so nothing was checked.");
            return;
        }

        string folder = Path.Combine(content, "sound");
        var refusals = new SortedDictionary<string, List<string>>(StringComparer.Ordinal);
        var cutShort = new List<string>();
        int read = 0, looped = 0;
        foreach (string path in Directory.EnumerateFiles(folder, "*.wav", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(folder, path).Replace('\\', '/');
            try
            {
                PcmSound sound = WavReader.Read(File.ReadAllBytes(path));
                Assert.NotEmpty(sound.Samples);
                Assert.InRange(sound.LoopStart, -1, sound.Samples.Length - 1);
                read++;
                looped += sound.LoopStart >= 0 ? 1 : 0;
                if (sound.CutShort)
                {
                    cutShort.Add(relative);
                }
            }
            catch (InvalidDataException e)
            {
                if (!refusals.TryGetValue(e.Message, out List<string>? files))
                {
                    refusals[e.Message] = files = [];
                }

                files.Add(relative);
            }
        }

        int refused = refusals.Values.Sum(f => f.Count);
        output.WriteLine($"{read + refused} files: {read} read ({looped} with a loop point, {cutShort.Count} cut short: {string.Join(", ", cutShort)}), {refused} refused:");
        foreach ((string reason, List<string> files) in refusals)
        {
            output.WriteLine($"  {reason} {files.Count}: {string.Join(", ", files.Take(4))}{(files.Count > 4 ? ", ..." : "")}");
        }

        Assert.True(read >= 2860, $"Only {read} files read.");
        Assert.True(refused <= 20, $"{refused} files refused.");
    }

    [Fact]
    public void EveryMapsVisibilityUnpacksAndCoversItsLeaves()
    {
        string? content = FindContent();
        if (content is null)
        {
            output.WriteLine("No content folder, so nothing was checked.");
            return;
        }

        var failures = new List<string>();
        var none = new List<string>();
        int maps = 0, largest = 0, speakers = 0, buried = 0;
        string largestName = "";
        foreach (string file in Directory.EnumerateFiles(Path.Combine(content, "maps"), "*.bsp"))
        {
            byte[] data = File.ReadAllBytes(file);
            if (BspFormat.Identify(data) != BspKind.Quake2)
            {
                continue;
            }

            maps++;
            string name = Path.GetFileNameWithoutExtension(file);
            Q2Bsp bsp = Q2Bsp.Read(data);
            MapVisibility visibility = MapVisibility.Read(bsp.Visibility);
            int clusters = bsp.Leaves.Max(l => (int)l.Cluster) + 1;
            if (bsp.Visibility.Length == 0)
            {
                none.Add(name);
            }
            else if (visibility.ClusterCount == 0)
            {
                failures.Add($"{name}: its visibility data wouldn't unpack");
            }
            else if (visibility.ClusterCount < clusters)
            {
                failures.Add($"{name}: {visibility.ClusterCount} clusters of data for leaves in {clusters}");
            }

            if (visibility.ClusterCount > largest)
            {
                (largest, largestName) = (visibility.ClusterCount, name);
            }

            // Speakers the original could never have played, because their spot is inside a wall.
            var hearing = new Hearing(CollisionData.From(bsp), visibility);
            foreach (TargetSpeaker speaker in TargetSpeakers.Start(bsp.Entities, new SoundEvents()))
            {
                speakers++;
                buried += hearing.SpotClusters(speaker.Origin).Length == 0 ? 1 : 0;
            }
        }

        output.WriteLine($"{maps} maps, {maps - none.Count} with visibility data, largest {largestName} with {largest} clusters; none in {string.Join(", ", none)}");
        output.WriteLine($"{speakers} speakers in the .bsp files, {buried} buried in walls where nobody can hear them");
        Assert.True(failures.Count == 0, string.Join('\n', failures));
        Assert.True(maps >= 363, $"Only found {maps} maps.");
    }
}
