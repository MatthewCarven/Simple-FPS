using System.Numerics;

namespace SimpleFps.Core.Sounds;

/// <summary>
/// How loud each looping sound is for one listener. A loop the listener can't hear (see
/// <see cref="Hearing"/>) is silent, and the rest fade with distance as loops did in the
/// original. Copies of one sound add up, but never past full volume, since the original mixed
/// every copy of a loop into one channel and capped it there. (It capped each ear on its own;
/// this caps the total, which can only be quieter where copies sit on both sides of you.)
/// </summary>
public sealed class LoopMix(Hearing hearing)
{
    private readonly Dictionary<int, (Vector3 Origin, int[] Clusters)> _spots = [];
    private readonly Dictionary<string, float> _totals = [];

    /// <summary>
    /// Fills <paramref name="gains"/> with a volume from 0 to 1 for each loop, in order. Call
    /// <see cref="Hearing.Listen"/> for the listener first.
    /// </summary>
    public void Mix(IReadOnlyList<LoopingSound> loops, Span<float> gains)
    {
        _totals.Clear();
        for (int i = 0; i < loops.Count; i++)
        {
            LoopingSound loop = loops[i];
            gains[i] = 0f;
            if (!loop.On || !hearing.CanHearLoop(loop.Origin, Clusters(i, loop.Origin)))
            {
                continue;
            }

            float gain = SoundFalloff.LoopGain(Vector3.Distance(loop.Origin, hearing.Listener));
            gains[i] = gain;
            _totals[loop.Sound] = _totals.GetValueOrDefault(loop.Sound) + gain;
        }

        for (int i = 0; i < loops.Count; i++)
        {
            if (gains[i] > 0f && _totals[loops[i].Sound] is var total and > 1f)
            {
                gains[i] /= total;
            }
        }
    }

    /// <summary>A loop's clusters, worked out again only when it moves.</summary>
    private int[] Clusters(int loop, Vector3 origin)
    {
        if (!_spots.TryGetValue(loop, out var spot) || spot.Origin != origin)
        {
            spot = (origin, hearing.SpotClusters(origin));
            _spots[loop] = spot;
        }

        return spot.Clusters;
    }
}
