using System.Numerics;
using SimpleFps.Core.Bsp;
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
