using System.Numerics;
using SimpleFps.Core.Bake;
using SimpleFps.Core.Bsp;
using SimpleFps.Core.Collision;
using SimpleFps.Core.Movement;
using Xunit.Abstractions;

namespace SimpleFps.Core.Tests;

/// <summary>
/// Runs the reader over every real map in content/maps. Content isn't in the repo, so on a fresh
/// clone this reports that it had nothing to check and passes without proving anything.
/// </summary>
public class WholeCollectionTests(ITestOutputHelper output)
{
    /// <summary>A map unit is about an inch, so a strip under this can't be seen.</summary>
    private const float VisibleHeight = 1f;

    private static string? FindMaps()
    {
        for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            string maps = Path.Combine(dir.FullName, "content", "maps");
            if (Directory.Exists(maps))
            {
                return maps;
            }
        }

        return null;
    }

    private static IEnumerable<(string Name, Q2Bsp Bsp)> DeathmatchMaps(string folder)
    {
        foreach (string file in Directory.EnumerateFiles(folder, "*.bsp"))
        {
            byte[] data = File.ReadAllBytes(file);
            if (BspFormat.Identify(data) == BspKind.Quake2)
            {
                Q2Bsp bsp = Q2Bsp.Read(data);
                if (bsp.Entities.Any(e => e.ClassName == "info_player_deathmatch"))
                {
                    yield return (Path.GetFileName(file), bsp);
                }
            }
        }
    }

    /// <summary>The brushes the world's tree can reach, leaving out those of doors and lifts.</summary>
    private static ClipPlane[][] WorldBrushes(Q2Bsp bsp, Contents mask)
    {
        var found = new HashSet<int>();
        var pending = new Stack<int>();
        pending.Push(bsp.Models[0].HeadNode);
        while (pending.Count > 0)
        {
            int node = pending.Pop();
            if (node >= 0)
            {
                pending.Push(bsp.Nodes[node].FrontChild);
                pending.Push(bsp.Nodes[node].BackChild);
                continue;
            }

            BspLeaf leaf = bsp.Leaves[-1 - node];
            for (int i = 0; i < leaf.LeafBrushCount; i++)
            {
                found.Add(bsp.LeafBrushes[leaf.FirstLeafBrush + i]);
            }
        }

        return found
            .Where(b => ((Contents)bsp.Brushes[b].Contents & mask) != 0)
            .Select(b => Enumerable
                .Range(bsp.Brushes[b].FirstSide, bsp.Brushes[b].SideCount)
                .Select(s => new ClipPlane(bsp.Planes[bsp.BrushSides[s].PlaneIndex].Normal, bsp.Planes[bsp.BrushSides[s].PlaneIndex].Distance))
                .ToArray())
            .Where(sides => !BspCollision.HasNoVolume(sides))
            .ToArray();
    }

    [Fact]
    public void TheTreeFindsExactlyWhatCheckingEveryBrushFinds()
    {
        string? maps = FindMaps();
        if (maps is null)
        {
            output.WriteLine("No content folder, so nothing was checked.");
            return;
        }

        const int tracesPerMap = 150;
        var random = new Random(12345);
        var hull = (Mins: new Vector3(-16, -16, -24), Maxs: new Vector3(16, 16, 32));
        var mismatches = new List<string>();
        int mapCount = 0, hits = 0, total = 0;

        foreach ((string name, Q2Bsp bsp) in DeathmatchMaps(maps))
        {
            mapCount++;
            var world = new BspCollision(bsp);
            ClipPlane[][] brushes = WorldBrushes(bsp, Contents.PlayerSolid);
            Vector3[] spawns = bsp.Entities
                .Where(e => e.ClassName == "info_player_deathmatch")
                .Select(e => e.GetVector("origin"))
                .OfType<Vector3>()
                .ToArray();

            for (int i = 0; i < tracesPerMap && spawns.Length > 0; i++)
            {
                Vector3 start = spawns[random.Next(spawns.Length)] + Scatter(random, 300f);
                Vector3 end = start + Scatter(random, 500f);
                bool point = i % 3 == 0;
                Vector3 mins = point ? Vector3.Zero : hull.Mins;
                Vector3 maxs = point ? Vector3.Zero : hull.Maxs;

                TraceResult tree = world.Trace(start, end, mins, maxs, Contents.PlayerSolid);

                var every = new TraceResult { Fraction = 1f };
                foreach (ClipPlane[] brush in brushes)
                {
                    BrushClipper.Clip(brush, Contents.Solid, start, end, mins, maxs, ref every);
                }

                BrushClipper.Finish(ref every, start, end);

                total++;
                hits += tree.Hit ? 1 : 0;

                // Map compilers leave brushes buried inside other brushes out of the tree, so a
                // sweep starting inside solid rock can see fewer of them. Players never start there.
                bool fromOpenSpace = !every.StartSolid;
                if (tree.StartSolid != every.StartSolid
                    || tree.AllSolid != every.AllSolid
                    || (fromOpenSpace && MathF.Abs(tree.Fraction - every.Fraction) > 1e-5f))
                {
                    mismatches.Add($"{name}: {start} to {end}: tree {tree.Fraction} vs every {every.Fraction}, solid {tree.StartSolid} vs {every.StartSolid}");
                }
            }
        }

        output.WriteLine($"{mapCount} maps, {total:N0} traces, {hits:N0} hit something, {mismatches.Count} mismatches");
        Assert.True(mismatches.Count == 0, string.Join('\n', mismatches.Take(10)));
        Assert.True(hits > total / 10, $"Only {hits} of {total} traces hit anything, so this proved little.");
    }

    [Fact]
    public void PlayersCanChargeRoundEveryMapWithoutEndingUpInsideIt()
    {
        string? maps = FindMaps();
        if (maps is null)
        {
            output.WriteLine("No content folder, so nothing was checked.");
            return;
        }

        const int spawnsPerMap = 4;
        const int ticksPerSpawn = 400;
        var random = new Random(54321);
        var settings = new MoveSettings();
        var problems = new List<string>();
        var clock = new System.Diagnostics.Stopwatch();
        long ticks = 0, stuck = 0;
        int mapCount = 0, grounded = 0, spawnCount = 0;

        foreach ((string name, Q2Bsp bsp) in DeathmatchMaps(maps))
        {
            mapCount++;
            var world = new BspCollision(bsp);
            var move = new PlayerMove(world, settings);

            foreach (Vector3 spawn in bsp.Entities
                .Where(e => e.ClassName == "info_player_deathmatch")
                .Select(e => e.GetVector("origin"))
                .OfType<Vector3>()
                .Take(spawnsPerMap))
            {
                spawnCount++;
                PlayerState s = PlayerState.At(spawn + (Vector3.UnitZ * PlayerHull.SpawnLift));
                float yaw = random.NextSingle() * 360f;
                bool landedOnce = false;
                MoveCommand cmd = default;
                PlayerState previous = s;

                for (int tick = 0; tick < ticksPerSpawn; tick++)
                {
                    if (tick % 20 == 0)
                    {
                        yaw += (random.NextSingle() - 0.5f) * 180f;
                        cmd = new MoveCommand(
                            yaw,
                            (random.NextSingle() - 0.5f) * 90f,
                            random.Next(10) < 8 ? 1 : -1,
                            random.Next(3) - 1,
                            Jump: random.Next(3) > 0,
                            Crouch: random.Next(8) == 0);
                    }

                    clock.Start();
                    move.Step(ref s, cmd);
                    clock.Stop();
                    ticks++;
                    landedOnce |= s.OnGround;

                    if (!float.IsFinite(s.Position.X + s.Position.Y + s.Position.Z + s.Velocity.X + s.Velocity.Y + s.Velocity.Z))
                    {
                        problems.Add($"{name}: position went bad at tick {tick}");
                        break;
                    }

                    if (world.Trace(s.Position, s.Position, s.Mins, s.Maxs, Contents.PlayerSolid).StartSolid)
                    {
                        stuck++;
                        problems.Add($"{name} t{tick}: at {s.Position} from {previous.Position}, velocity {s.Velocity}, crouch {previous.Crouched}>{s.Crouched}, width {previous.HalfWidth}>{s.HalfWidth}");
                        break;
                    }

                    previous = s;
                }

                grounded += landedOnce ? 1 : 0;
            }
        }

        double microseconds = clock.Elapsed.TotalMilliseconds * 1000 / Math.Max(ticks, 1);
        output.WriteLine($"{mapCount} maps, {spawnCount} spawns, {ticks:N0} ticks, {stuck} stuck, {grounded} reached the ground, {microseconds:F1} microseconds a tick");
        Assert.True(problems.Count == 0, string.Join('\n', problems.Take(12)));
        Assert.True(grounded > spawnCount * 0.95, $"Only {grounded} of {spawnCount} spawns ever stood on anything.");
    }

    [Fact]
    public void EveryMapBakesAndReadsBackExactlyAsBuiltFromItsFiles()
    {
        string? maps = FindMaps();
        if (maps is null)
        {
            output.WriteLine("No content folder, so nothing was checked.");
            return;
        }

        // Only used to list and find maps. Bakes stay in memory, so nothing is written.
        var cache = new MapCache(maps, Path.Combine(Path.GetTempPath(), "simplefps-unused"));
        const int tracesPerMap = 40;
        var random = new Random(2468);
        var hull = (Mins: new Vector3(-16, -16, -24), Maxs: new Vector3(16, 16, 32));
        var index = new MapIndex();
        var failures = new List<string>();
        var bakeClock = new System.Diagnostics.Stopwatch();
        var readClock = new System.Diagnostics.Stopwatch();
        long totalBytes = 0, largestBytes = 0;
        int traces = 0, hits = 0, overrides = 0;
        string largest = "";

        IReadOnlyList<string> names = cache.Names();
        foreach (string name in names)
        {
            try
            {
                MapCache.Source source = cache.Find(name);
                byte[] file = File.ReadAllBytes(source.BspPath);

                bakeClock.Start();
                BakedMap baked = MapBaker.Bake(name, source.BspName, file, source.Override);
                bakeClock.Stop();

                using var stream = new MemoryStream();
                BakeFile.Write(stream, baked);
                totalBytes += stream.Length;
                if (stream.Length > largestBytes)
                {
                    (largestBytes, largest) = (stream.Length, name);
                }

                stream.Position = 0;
                readClock.Start();
                BakedMap read = BakeFile.Read(stream, baked.Info.Hash) ?? throw new InvalidDataException("read back as stale");
                readClock.Stop();

                // Straight from the files, the way the game loaded maps before the bake.
                Q2Bsp bsp = Q2Bsp.Read(file);
                List<BspEntity> entities = bsp.Entities;
                if (source.Override is not null)
                {
                    overrides++;
                    entities = BspEntity.Parse(MapOverride.Read(source.Override).Entities!);
                }

                Assert.Equal(baked.Info, read.Info);
                BakeAssert.SameGeometry(MapMesher.Build(bsp), read.Geometry);
                BakeAssert.SameCollision(CollisionData.From(bsp), read.Collision);
                BakeAssert.SameEntities(entities, read.Entities);

                var direct = new BspCollision(bsp);
                var fromBake = new BspCollision(read.Collision);
                Vector3[] spots = entities
                    .Where(e => e.ClassName.StartsWith("info_player", StringComparison.Ordinal))
                    .Select(e => e.GetVector("origin"))
                    .OfType<Vector3>()
                    .DefaultIfEmpty(Vector3.Zero)
                    .ToArray();
                for (int i = 0; i < tracesPerMap; i++)
                {
                    Vector3 start = spots[random.Next(spots.Length)] + Scatter(random, 300f);
                    Vector3 end = start + Scatter(random, 500f);
                    bool point = i % 3 == 0;
                    Vector3 mins = point ? Vector3.Zero : hull.Mins;
                    Vector3 maxs = point ? Vector3.Zero : hull.Maxs;
                    TraceResult want = direct.Trace(start, end, mins, maxs, Contents.PlayerSolid);
                    BakeAssert.SameTrace(want, fromBake.Trace(start, end, mins, maxs, Contents.PlayerSolid), $"{start} to {end}");
                    traces++;
                    hits += want.Hit ? 1 : 0;
                }

                index.Add(read.Info);
            }
            catch (Exception e) when (e is InvalidDataException or IOException or Xunit.Sdk.XunitException or IndexOutOfRangeException)
            {
                failures.Add($"{name}: {e.GetType().Name}: {e.Message}");
            }
        }

        IReadOnlyList<MapIndexEntry> entries = index.Maps;
        List<MapIndexEntry> copies = entries.Where(m => m.DuplicateOf is not null).ToList();
        output.WriteLine($"{names.Count} maps ({overrides} with an override), {totalBytes / 1e6:F0} MB of bakes, largest {largest} at {largestBytes / 1e6:F1} MB");
        output.WriteLine($"baking took {bakeClock.Elapsed.TotalSeconds:F1} s, reading back {readClock.Elapsed.TotalSeconds:F1} s; {traces:N0} traces, {hits:N0} hit something");
        output.WriteLine($"{entries.Count(m => m.Title is not null)} have a title, {entries.Count(m => m.Author is not null)} an author");
        output.WriteLine($"{copies.Count} copies: {string.Join(", ", copies.Select(m => $"{m.Name} = {m.DuplicateOf}"))}");

        Assert.True(failures.Count == 0, string.Join('\n', failures.Take(20)));
        Assert.True(names.Count >= 363, $"Only found {names.Count} maps.");
        Assert.True(hits > traces / 10, $"Only {hits} of {traces} traces hit anything, so this proved little.");
        if (names.Contains("q2pduel7") && names.Contains("q2rdm2"))
        {
            Assert.Equal("q2pduel7", entries.Single(m => m.Name == "q2rdm2").DuplicateOf);
        }
    }

    private static Vector3 Scatter(Random random, float size) => new(
        (float)(random.NextDouble() * 2 - 1) * size,
        (float)(random.NextDouble() * 2 - 1) * size,
        (float)(random.NextDouble() * 2 - 1) * size);

    [Fact]
    public void EveryQuake2MapReadsAndMeshesCleanly()
    {
        string? maps = FindMaps();
        if (maps is null)
        {
            output.WriteLine("No content folder, so nothing was checked.");
            return;
        }

        var failures = new List<string>();
        int checkedMaps = 0;
        long triangles = 0, hairlines = 0;
        var wrongWay = new List<(float Height, string Where)>();
        int largestAtlas = 0;

        foreach (string file in Directory.EnumerateFiles(maps, "*.bsp"))
        {
            byte[] data = File.ReadAllBytes(file);
            if (BspFormat.Identify(data) != BspKind.Quake2)
            {
                continue;
            }

            checkedMaps++;
            try
            {
                Q2Bsp bsp = Q2Bsp.Read(data);
                MapGeometry geometry = MapMesher.Build(bsp);
                largestAtlas = Math.Max(largestAtlas, Math.Max(geometry.Lightmap.Width, geometry.Lightmap.Height));

                if (bsp.Models.Length == 0 || bsp.Entities.Count == 0)
                {
                    failures.Add($"{Path.GetFileName(file)}: no world model or no entities");
                }

                foreach (MeshBatch batch in geometry.Models.SelectMany(m => m))
                {
                    if (batch.Vertices.Any(v => v.LightCoord.X is < 0 or > 1 || v.LightCoord.Y is < 0 or > 1))
                    {
                        failures.Add($"{Path.GetFileName(file)}: light coordinate outside the atlas on {batch.Texture}");
                    }

                    for (int i = 0; i < batch.Indices.Count; i += 3)
                    {
                        MapVertex a = batch.Vertices[batch.Indices[i]];
                        Vector3 b = batch.Vertices[batch.Indices[i + 1]].Position;
                        Vector3 c = batch.Vertices[batch.Indices[i + 2]].Position;
                        Vector3 cross = Vector3.Cross(b - a.Position, c - a.Position);
                        if (cross.LengthSquared() < 0.01f)
                        {
                            continue;
                        }

                        triangles++;
                        if (Vector3.Dot(cross, a.Normal) <= 0)
                        {
                            continue;
                        }

                        // Rounded coordinates leave long thin strips along straight edges. Those
                        // have no visible height. Anything taller would be a real hole or spill.
                        float longest = MathF.Max(Vector3.Distance(a.Position, b), MathF.Max(Vector3.Distance(b, c), Vector3.Distance(c, a.Position)));
                        float height = cross.Length() / longest;
                        if (height < VisibleHeight)
                        {
                            hairlines++;
                        }
                        else
                        {
                            wrongWay.Add((height, $"{Path.GetFileName(file)} {batch.Texture} height {height:F1} at {a.Position}"));
                        }
                    }
                }
            }
            catch (Exception e) when (e is InvalidDataException or IndexOutOfRangeException or ArgumentException)
            {
                failures.Add($"{Path.GetFileName(file)}: {e.GetType().Name}: {e.Message}");
            }
        }

        output.WriteLine($"{checkedMaps} maps, {triangles:N0} triangles, {hairlines:N0} reversed hairlines, {wrongWay.Count:N0} visibly reversed, largest atlas {largestAtlas}");
        foreach ((_, string where) in wrongWay.OrderByDescending(w => w.Height).Take(12))
        {
            output.WriteLine(where);
        }

        Assert.True(failures.Count == 0, string.Join('\n', failures.Take(30)));
        Assert.True(checkedMaps >= 363, $"Only found {checkedMaps} maps.");
        Assert.Empty(wrongWay);
    }
}
