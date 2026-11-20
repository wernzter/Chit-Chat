using System.Runtime.InteropServices;

namespace ChitChat.Platform.Windows;

internal static class RecordingCues
{
    private const int SampleRate = 24_000;
    private const uint AsyncMemoryWithoutFallback = 0x0007;
    // Windows reads these buffers asynchronously. Keep both pinned for the app's lifetime.
    private static readonly GCHandle StartCue = GCHandle.Alloc(CreateWave(105, 1320, 1060, 0.17), GCHandleType.Pinned);
    private static readonly GCHandle FinishCue = GCHandle.Alloc(CreateWave(75, 540, 300, 0.24), GCHandleType.Pinned);

    internal static void Play(bool starting) =>
        _ = PlaySound((starting ? StartCue : FinishCue).AddrOfPinnedObject(), 0, AsyncMemoryWithoutFallback);

    private static byte[] CreateWave(int milliseconds, double initialHz, double finalHz, double volume)
    {
        var samples = SampleRate * milliseconds / 1_000;
        var dataSize = samples * sizeof(short);
        using var stream = new MemoryStream(44 + dataSize);
        using var wave = new BinaryWriter(stream);
        wave.Write("RIFF"u8);
        wave.Write(36 + dataSize);
        wave.Write("WAVEfmt "u8);
        wave.Write(16);
        wave.Write((short)1);
        wave.Write((short)1);
        wave.Write(SampleRate);
        wave.Write(SampleRate * sizeof(short));
        wave.Write((short)sizeof(short));
        wave.Write((short)16);
        wave.Write("data"u8);
        wave.Write(dataSize);

        var phase = 0d;
        for (var sample = 0; sample < samples; sample++)
        {
            var progress = (double)sample / samples;
            var frequency = finalHz + (initialHz - finalHz) * Math.Exp(-5 * progress);
            phase += 2 * Math.PI * frequency / SampleRate;
            var gain = Math.Min(sample / 60d, 1) * Math.Pow(1 - progress, 3);
            wave.Write((short)(Math.Sin(phase) * gain * volume * short.MaxValue));
        }
        return stream.ToArray();
    }

    [DllImport("winmm.dll", EntryPoint = "PlaySoundW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PlaySound(nint sound, nint module, uint flags);
}
