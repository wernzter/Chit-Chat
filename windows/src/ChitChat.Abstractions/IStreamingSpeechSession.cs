namespace ChitChat.Abstractions;

/// <summary>A local speech recognizer for one recording, used serially by its owner.</summary>
public interface IStreamingSpeechSession : IDisposable
{
    /// <summary>Accepts normalized mono microphone audio and returns the current provisional transcript.</summary>
    /// <param name="samples">Audio sampled at <see cref="AudioChunk.SampleRate"/>.</param>
    string Feed(ReadOnlyMemory<float> samples);

    /// <summary>Flushes the recognizer and returns the complete transcript.</summary>
    string Finish();
}
