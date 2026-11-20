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
    public async Task UsesTheOriginalEditorAndPlaysTheFinishCueAfterFailedDelivery()
    {
        var trigger = new FakeHotkeySource();
        var defaultTarget = new RecordingTextInjector();
        var actions = new List<string>();
        await using var engine = new DictationEngine(FakeAudioCapture.Tone(0.25), trigger,
            new FakeTranscriber("send to joniper"), defaultTarget,
            () => [DictionaryEntry.Correction("joniper", "Juniper")], options: new DictationOptions
            {
                CaptureOutput = () => new DelegateOutput(text =>
                {
                    actions.Add(text);
                    return false;
                }),
                PlayCue = starting => actions.Add(starting ? "start" : "done"),
                PrepareText = (text, _) => Task.FromResult(TranscriptCleanup.Format(text)),
            });
        engine.DeliveryFailed += (_, text) => actions.Add("recovery " + text);

        await PressAndReleaseAsync(engine, trigger);

        defaultTarget.Injected.ShouldBeEmpty();
        actions.ShouldBe(["start", "Send to Juniper.", "recovery Send to Juniper.", "done"]);
    }

    [Fact]
    public async Task StreamingShowsProvisionalTextAndDeliversTheFinalTextOnce()
    {
        var trigger = new FakeHotkeySource();
        var delivery = new RecordingTextInjector();
        var offline = new FakeTranscriber();
        var live = new StreamingSpeech();
        string? preview = null;
        await using var engine = new DictationEngine(FakeAudioCapture.Tone(0.25), trigger, offline, delivery,
            () => [DictionaryEntry.Term("Juniper")], options: new DictationOptions
            {
                StartStreamingSpeech = hints =>
                {
                    hints.ShouldBe(["Juniper"]);
                    return live;
                },
            });
        engine.Changed += (_, _) => { if (engine.PreviewText is { } text) preview = text; };

        await PressAndReleaseAsync(engine, trigger, waitForPreview: true);

        preview.ShouldBe("Meet at");
        engine.PreviewText.ShouldBeNull();
        delivery.Injected.ShouldBe(["Meet at Juniper"]);
        offline.SegmentLengths.ShouldBeEmpty();
        live.Disposed.ShouldBeTrue();
    }

    [Fact]
    public async Task SetupReceivesThePhysicalShortcutWithoutStartingDictation()
    {
        var trigger = new FakeHotkeySource();
        await using var engine = new DictationEngine(FakeAudioCapture.Tone(0.25), trigger,
            new FakeTranscriber("after setup"), new RecordingTextInjector(), () => []);
        var presses = new List<bool>();
        using (engine.TryBeginSetup(presses.Add).ShouldNotBeNull())
        {
            trigger.Press();
            engine.TogglePushToTalk();
            trigger.Release();
            engine.State.ShouldBe(DictationState.Idle);
        }
        presses.ShouldBe([true, false]);

        await PressAndReleaseAsync(engine, trigger);
    }

    [Fact]
    public void CoreAssemblyReferencesOnlyPlatformNeutralServices()
    {
        typeof(DictationEngine).Assembly.GetReferencedAssemblies()
            .ShouldNotContain(dependency => dependency.Name!.StartsWith("ChitChat.Platform.", StringComparison.Ordinal));
    }

    private static async Task PressAndReleaseAsync(DictationEngine engine, FakeHotkeySource trigger,
                                                   bool waitForPreview = false)
    {
        var recorded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnChange(object? sender, EventArgs e)
        {
            if (engine.State == DictationState.Recording && engine.Level > 0 &&
                (!waitForPreview || engine.PreviewText is not null))
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

    private sealed class DelegateOutput(Func<string, bool> inject) : ITextInjector
    {
        public ValueTask<bool> InjectAsync(string text, CancellationToken cancellationToken) =>
            ValueTask.FromResult(inject(text));
    }

    private sealed class StreamingSpeech : IStreamingSpeechSession
    {
        public bool Disposed { get; private set; }
        public string Feed(ReadOnlyMemory<float> samples) => "Meet at";
        public string Finish() => "Meet at Juniper";
        public void Dispose() => Disposed = true;
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
