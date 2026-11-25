using System.Numerics;
using SimpleFps.Core.Bsp;
using SimpleFps.Core.Collision;

namespace SimpleFps.Core.Sounds;

/// <summary>
/// Which sounds reach a listener, decided from the map's tree and visibility data the way the
/// original game's server decided which sounds to send each player (Matthew chose this over
/// distance alone, 2026-11-25). A looping sound needs its spot to be in sight from around the
/// listener's eye and within 400 units. A one-off sound needs the listener to be where its spot
/// can be heard, unless its attenuation is none, which is heard everywhere.
/// </summary>
/// <remarks>
/// Closed doors also cut sounds off in the original, through the map's areas. Until doors exist
/// (milestone 4c) every area counts as joined.
/// </remarks>
public sealed class Hearing
{
    /// <summary>The original never sent a looping sound with no model of its own from farther than this.</summary>
    public const float LoopRange = 400f;

    /// <summary>What can be seen from anywhere in a box this far round the eye counts, so a wall right at your nose doesn't cut sounds out.</summary>
    private const float EyeReach = 8f;

    /// <summary>A spot is linked into the tree with a box this far round it, as the original linked a point.</summary>
    private const float SpotReach = 1f;

    private readonly CollisionData _map;
    private readonly MapVisibility _visibility;
    private readonly int _headNode;
    private readonly byte[] _seen;
    private readonly List<int> _leaves = [];
    private readonly Stack<int> _pending = new();
    private bool _seesAnything;
    private int _listenerCluster = -1;

    public Hearing(CollisionData map, MapVisibility visibility)
    {
        _map = map;
        _visibility = visibility;
        _headNode = map.Models.Length > 0 ? map.Models[0].HeadNode : 0;
        _seen = new byte[visibility.RowBytes];
    }

    /// <summary>Where the listener's ear is, from the last <see cref="Listen"/>.</summary>
    public Vector3 Listener { get; private set; }

    /// <summary>Moves the listener, and works out what can be seen from around their eye.</summary>
    public void Listen(Vector3 eye)
    {
        Listener = eye;
        _listenerCluster = ClusterAt(eye);
        _seen.AsSpan().Clear();
        _seesAnything = false;

        var reach = new Vector3(EyeReach);
        LeavesIn(eye - reach, eye + reach, _leaves);
        foreach (int leaf in _leaves)
        {
            int cluster = _map.Leaves[leaf].Cluster;
            _seesAnything |= cluster >= 0;
            _visibility.AddVisibleFrom(cluster, _seen);
        }
    }

    /// <summary>The clusters a sound's spot touches, leaving out solid. A spot buried in a wall touches none and is never heard, as in the original.</summary>
    public int[] SpotClusters(Vector3 origin)
    {
        var reach = new Vector3(SpotReach);
        LeavesIn(origin - reach, origin + reach, _leaves);
        return _leaves.Select(leaf => (int)_map.Leaves[leaf].Cluster).Where(c => c >= 0).Distinct().ToArray();
    }

    /// <summary>True if a looping sound at <paramref name="origin"/>, touching <paramref name="clusters"/>, reaches the listener.</summary>
    public bool CanHearLoop(Vector3 origin, int[] clusters)
    {
        if (Vector3.Distance(origin, Listener) > LoopRange || !_seesAnything)
        {
            return false;
        }

        if (_visibility.ClusterCount == 0)
        {
            return clusters.Length > 0;
        }

        foreach (int cluster in clusters)
        {
            if (cluster < _visibility.ClusterCount && (_seen[cluster >> 3] & (1 << (cluster & 7))) != 0)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>True if a one-off sound at <paramref name="origin"/> reaches the listener.</summary>
    public bool CanHearOnce(Vector3 origin, float attenuation) =>
        attenuation == SoundFalloff.None || _visibility.CanHear(ClusterAt(origin), _listenerCluster);

    /// <summary>The cluster of the leaf a point is in, or -1 for solid or outside the map.</summary>
    public int ClusterAt(Vector3 point)
    {
        int leaf = LeafAt(point);
        return leaf >= 0 ? _map.Leaves[leaf].Cluster : -1;
    }

    /// <summary>The leaf a point is in. A point on a plane goes to its front.</summary>
    public int LeafAt(Vector3 point)
    {
        if (_map.Nodes.Length == 0)
        {
            return _map.Leaves.Length > 0 ? 0 : -1;
        }

        int node = _headNode;
        while (node >= 0)
        {
            ref readonly BspNode n = ref _map.Nodes[node];
            ref readonly BspPlane plane = ref _map.Planes[n.PlaneIndex];
            node = Vector3.Dot(plane.Normal, point) - plane.Distance < 0 ? n.BackChild : n.FrontChild;
        }

        return -1 - node;
    }

    /// <summary>Every leaf a box touches, into <paramref name="leaves"/>, which is cleared first.</summary>
    public void LeavesIn(Vector3 mins, Vector3 maxs, List<int> leaves)
    {
        leaves.Clear();
        if (_map.Nodes.Length == 0)
        {
            if (_map.Leaves.Length > 0)
            {
                leaves.Add(0);
            }

            return;
        }

        _pending.Clear();
        _pending.Push(_headNode);
        while (_pending.Count > 0)
        {
            int node = _pending.Pop();
            if (node < 0)
            {
                leaves.Add(-1 - node);
                continue;
            }

            ref readonly BspNode n = ref _map.Nodes[node];
            ref readonly BspPlane plane = ref _map.Planes[n.PlaneIndex];
            (float far, float near) = Reach(plane.Normal, mins, maxs);
            if (far >= plane.Distance)
            {
                _pending.Push(n.FrontChild);
            }

            if (near < plane.Distance)
            {
                _pending.Push(n.BackChild);
            }
        }
    }

    /// <summary>How far along a plane's normal a box's farthest and nearest corners are.</summary>
    private static (float Far, float Near) Reach(Vector3 normal, Vector3 mins, Vector3 maxs)
    {
        Vector3 far = new(normal.X < 0 ? mins.X : maxs.X, normal.Y < 0 ? mins.Y : maxs.Y, normal.Z < 0 ? mins.Z : maxs.Z);
        Vector3 near = new(normal.X < 0 ? maxs.X : mins.X, normal.Y < 0 ? maxs.Y : mins.Y, normal.Z < 0 ? maxs.Z : mins.Z);
        return (Vector3.Dot(normal, far), Vector3.Dot(normal, near));
    }
}
