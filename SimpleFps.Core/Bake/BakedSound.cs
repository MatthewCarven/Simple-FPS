namespace SimpleFps.Core.Bake;

/// <summary>A sound in our own form: one channel of 16-bit samples, ready to play.</summary>
public sealed class BakedSound
{
    /// <summary>The sound's path under the sound folder, in lower case, such as <c>world/amb10.wav</c>.</summary>
    public required string Name { get; init; }

    /// <summary>SHA-256 of the file it was baked from.</summary>
    public required string Hash { get; init; }

    /// <summary>Where it came from, relative to the content folder, for the log.</summary>
    public required string Source { get; init; }

    public required int SampleRate { get; init; }

    /// <summary>The sample a looping play goes back to, from the file's cue point, or -1 if it has none.</summary>
    public required int LoopStart { get; init; }

    public required short[] Samples { get; init; }

    public double Seconds => (double)Samples.Length / SampleRate;
}
