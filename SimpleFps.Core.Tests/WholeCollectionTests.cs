using System.Numerics;
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
