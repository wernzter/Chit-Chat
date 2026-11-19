using ChitChat.Abstractions;
using ChitChat.Core;
using ChitChat.Dictionary;
using ChitChat.Testing;
using Shouldly;
using Xunit;

namespace ChitChat.CoreTests;

public sealed class DictationEngineTests
{
    [Fact]
    public async Task ARecordingEmitsAndInsertsOneCorrectedResult()
    {
        var trigger = new FakeHotkeySource();
        var delivery = new RecordingTextInjector();
        var recognition = new FakeTranscriber("Meet at juniper lab.");
        DictionaryEntry[] entries = [DictionaryEntry.Correction("juniper lab", "Juniper Labs"), DictionaryEntry.Term("Harbourview")];
        await using var engine = new DictationEngine(FakeAudioCapture.Tone(0.5), trigger, recognition,
            delivery, () => entries);

        var results = new List<DictationResult>();
        engine.Completed += (_, result) => results.Add(result);
        await PressAndReleaseAsync(engine, trigger);

        delivery.Injected.ShouldBe(["Meet at Juniper Labs."]);
        var completed = results.ShouldHaveSingleItem();
        completed.Text.ShouldBe(delivery.Injected[0]);
        completed.Corrections.ShouldHaveSingleItem().To.ShouldBe("Juniper Labs");
        recognition.LastBias.ShouldBe(["Juniper Labs", "Harbourview"]);
        engine.Level.ShouldBe(0);
    }

    [Theory]
    [InlineData("")]
    [InlineData("    ")]
    public async Task EmptyRecognitionDoesNotEmitOrInsert(string reply)
    {
        var trigger = new FakeHotkeySource();
        var delivery = new RecordingTextInjector();
        await using var engine = new DictationEngine(FakeAudioCapture.Tone(0.25), trigger,
            new FakeTranscriber(reply), delivery, () => []);

        var results = new List<DictationResult>();
        engine.Completed += (_, result) => results.Add(result);
        await PressAndReleaseAsync(engine, trigger);

        results.ShouldBeEmpty();
        delivery.Injected.ShouldBeEmpty();
    }

    [Fact]
    public async Task KeyUpWhileIdleDoesNotStartASession()
    {
        var trigger = new FakeHotkeySource();
        var delivery = new RecordingTextInjector();
        var recognition = new FakeTranscriber("Unexpected");
        await using var engine = new DictationEngine(FakeAudioCapture.Silence(0.25), trigger,
            recognition, delivery, () => []);

        trigger.Release();

        engine.State.ShouldBe(DictationState.Idle);
        recognition.SegmentLengths.ShouldBeEmpty();
        delivery.Injected.ShouldBeEmpty();
    }

    [Fact]
    public void CoreAssemblyReferencesOnlyPlatformNeutralServices()
    {
        typeof(DictationEngine).Assembly.GetReferencedAssemblies()
            .ShouldNotContain(dependency => dependency.Name!.StartsWith("ChitChat.Platform.", StringComparison.Ordinal));
    }

    private static async Task PressAndReleaseAsync(DictationEngine engine, FakeHotkeySource trigger)
    {
        var recorded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnChange(object? sender, EventArgs e)
        {
            if (engine.State == DictationState.Recording && engine.Level > 0)
                recorded.TrySetResult();
            if (engine.State == DictationState.Idle && recorded.Task.IsCompleted)
                finished.TrySetResult();
        }

        engine.Changed += OnChange;
        try
        {
            engine.State.ShouldBe(DictationState.Idle);
            trigger.Press();
            await recorded.Task.WaitAsync(TimeSpan.FromSeconds(3));
            trigger.Release();
            await finished.Task.WaitAsync(TimeSpan.FromSeconds(3));
        }
        finally
        {
            engine.Changed -= OnChange;
        }
    }
}

public sealed class AudioSegmenterTests
{
    [Theory]
    [InlineData(5)]
    [InlineData(AudioSegmenter.MaxSegmentSeconds)]
    public void AShortRecordingRemainsOneSliceOfTheOriginalMemory(int seconds)
    {
        var samples = new float[seconds * AudioChunk.SampleRate];
        var result = AudioSegmenter.Split(samples).ShouldHaveSingleItem();
        result.Length.ShouldBe(samples.Length);
        samples[0] = 0.31f;
        result.Span[0].ShouldBe(samples[0]);
    }

    [Fact]
    public void LongDictationKeepsItsSamplesInBoundedChunks()
    {
        var length = (AudioSegmenter.EncoderCeilingSeconds + 40) * AudioChunk.SampleRate;
        var segments = AudioSegmenter.Split(new float[length]);
        segments.Count.ShouldBeGreaterThan(1);
        segments.Sum(segment => segment.Length).ShouldBe(length);
        foreach (var segment in segments)
        {
            segment.Length.ShouldBeInRange(1, AudioSegmenter.MaxSegmentSeconds * AudioChunk.SampleRate);
        }
    }

    [Fact]
    public void CutPrefersAQuietIntervalNearTheBoundary()
    {
        var samples = new float[2 * AudioSegmenter.MaxSegmentSeconds * AudioChunk.SampleRate];
        Array.Fill(samples, 0.55f);
        var quietInterval = (AudioSegmenter.MaxSegmentSeconds - 4) * AudioChunk.SampleRate;
        samples.AsSpan(quietInterval, AudioChunk.SampleRate).Clear();
        AudioSegmenter.Split(samples)[0].Length.ShouldBeInRange(quietInterval, quietInterval + AudioChunk.SampleRate);
    }
}
