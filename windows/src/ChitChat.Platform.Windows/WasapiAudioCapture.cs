using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using ChitChat.Abstractions;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace ChitChat.Platform.Windows;

/// <summary>Opens the selected WASAPI input at the device's shared format.</summary>
/// <param name="deviceId">An explicit endpoint or null for the system default.</param>
public sealed class WasapiAudioCapture(string? deviceId = null) : IAudioCapture
{
    private readonly Lock _gate = new();
    private AudioSession? _active;
    private bool _closed;
    private volatile bool _silent;

    /// <inheritdoc />
    public bool IsCapturing
    {
        get
        {
            lock (_gate) return _active?.IsRunning == true;
        }
    }

    /// <summary>Whether recent input consists of digital silence.</summary>
    public bool LooksLikeBlockedMicrophone => _silent;

    /// <inheritdoc />
    public async IAsyncEnumerable<AudioChunk> CaptureAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        AudioSession session;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_closed, this);
            if (_active is not null) throw new InvalidOperationException("The microphone is already open.");
            cancellationToken.ThrowIfCancellationRequested();
            session = new AudioSession(deviceId);
            _active = session;
            _silent = false;
        }

        try
        {
            session.Start();
            await foreach (var item in session.Read(cancellationToken).ConfigureAwait(false))
            {
                _silent = item.DigitalSilence;
                yield return new AudioChunk(item.Samples);
            }
        }
        finally
        {
            lock (_gate)
            {
                if (ReferenceEquals(_active, session))
                {
                    session.Dispose();
                    _active = null;
                }
            }
        }
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            _closed = true;
            _active?.Dispose();
            _active = null;
        }
        return ValueTask.CompletedTask;
    }

    private sealed class AudioSession : IDisposable
    {
        private readonly MMDevice _device;
        private readonly WasapiCapture _capture;
        private readonly BufferedWaveProvider _buffer;
        private readonly ISampleProvider _converter;
        private readonly Channel<(float[] Samples, bool DigitalSilence)> _samples =
            Channel.CreateUnbounded<(float[], bool)>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = true,
            });
        private readonly float[] _scratch = new float[4096];
        private int _silence;
        private volatile bool _running;

        internal bool IsRunning => _running;

        internal AudioSession(string? id)
        {
            using var endpoints = new MMDeviceEnumerator();
            _device = id is null ? endpoints.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Multimedia)
                                 : endpoints.GetDevice(id);
            try
            {
                _capture = new WasapiCapture(_device, useEventSync: true, audioBufferMillisecondsLength: 40);
            }
            catch
            {
                _device.Dispose();
                throw;
            }

            try
            {
                var input = _capture.WaveFormat;
                _buffer = new BufferedWaveProvider(input) { ReadFully = false };
                ISampleProvider signal = _buffer.ToSampleProvider();
                if (input.Channels > 1) signal = new MonoInput(signal);
                _converter = input.SampleRate == AudioChunk.SampleRate
                    ? signal : new WdlResamplingSampleProvider(signal, AudioChunk.SampleRate);
                _capture.DataAvailable += Receive;
                _capture.RecordingStopped += Stopped;
            }
            catch
            {
                _capture.Dispose();
                _device.Dispose();
                throw;
            }
        }

        internal void Start()
        {
            _running = true;
            _capture.StartRecording();
        }

        internal IAsyncEnumerable<(float[] Samples, bool DigitalSilence)> Read(CancellationToken cancellationToken) =>
            _samples.Reader.ReadAllAsync(cancellationToken);

        private void Receive(object? sender, WaveInEventArgs args)
        {
            _buffer.AddSamples(args.Buffer, 0, args.BytesRecorded);
            int count;
            while ((count = _converter.Read(_scratch, 0, _scratch.Length)) > 0)
            {
                var block = _scratch.AsSpan(0, count).ToArray();
                _silence = Array.TrueForAll(block, sample => sample == 0) ? _silence + count : 0;
                _samples.Writer.TryWrite((block, _silence >= AudioChunk.SampleRate * 3 / 2));
            }
        }

        private void Stopped(object? sender, StoppedEventArgs args)
        {
            _running = false;
            _samples.Writer.TryComplete(args.Exception);
        }

        public void Dispose()
        {
            _samples.Writer.TryComplete();
            _capture.DataAvailable -= Receive;
            try
            {
                _capture.StopRecording();
            }
            catch (COMException)
            {
                Trace.TraceWarning("The audio device was disconnected during recording.");
            }
            finally
            {
                _capture.RecordingStopped -= Stopped;
                _capture.Dispose();
                _device.Dispose();
                _running = false;
            }
        }
    }

    private sealed class MonoInput(ISampleProvider source) : ISampleProvider
    {
        private float[] _frames = [];
        private readonly int _channels = source.WaveFormat.Channels;

        public WaveFormat WaveFormat { get; } =
            WaveFormat.CreateIeeeFloatWaveFormat(source.WaveFormat.SampleRate, 1);

        public int Read(float[] buffer, int offset, int count)
        {
            var requested = count * _channels;
            if (_frames.Length < requested) _frames = new float[requested];
            var received = source.Read(_frames, 0, requested) / _channels;
            for (var frame = 0; frame < received; frame++)
            {
                var start = frame * _channels;
                float total = 0;
                for (var channel = 0; channel < _channels; channel++) total += _frames[start + channel];
                buffer[offset + frame] = total / _channels;
            }
            return received;
        }
    }
}
