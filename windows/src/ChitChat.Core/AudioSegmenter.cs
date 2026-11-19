using ChitChat.Abstractions;

namespace ChitChat.Core;

/// <summary>Splits a recording into bounded, contiguous segments for speech recognition.</summary>
public static class AudioSegmenter
{
    /// <summary>Longest duration the encoder can represent.</summary>
    public const int EncoderCeilingSeconds = 400;
    /// <summary>Maximum duration sent to the encoder in one call.</summary>
    public const int MaxSegmentSeconds = 60;
    /// <summary>Time before the segment cap to look for a quiet cut.</summary>
    public const int SilenceSearchSeconds = 8;

    /// <summary>Finds split points without copying sample data.</summary>
    /// <param name="samples">The recording at <see cref="AudioChunk.SampleRate"/>.</param>
    public static IReadOnlyList<ReadOnlyMemory<float>> Split(ReadOnlyMemory<float> samples)
    {
        const int cap = MaxSegmentSeconds * AudioChunk.SampleRate;
        const int lookback = SilenceSearchSeconds * AudioChunk.SampleRate;
        var segments = new List<ReadOnlyMemory<float>>();

        while (samples.Length > cap)
        {
            var split = QuietestBoundary(samples.Span, cap - lookback, cap);
            segments.Add(samples[..split]);
            samples = samples[split..];
        }

        if (!samples.IsEmpty) segments.Add(samples);
        return segments;
    }

    private static int QuietestBoundary(ReadOnlySpan<float> samples, int first, int last)
    {
        const int width = AudioChunk.SampleRate / 10;
        var bestBoundary = last;
        var lowestEnergy = double.PositiveInfinity;
        for (var offset = last - width; offset >= first; offset -= width)
        {
            double energy = 0;
            foreach (var sample in samples.Slice(offset, width)) energy += sample * (double)sample;
            if (energy >= lowestEnergy) continue;

            lowestEnergy = energy;
            bestBoundary = offset + width / 2;
        }
        return bestBoundary;
    }
}
