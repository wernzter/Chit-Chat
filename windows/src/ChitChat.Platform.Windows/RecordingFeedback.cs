using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;

namespace ChitChat.Platform.Windows;

internal sealed class RecordingFeedback : IDisposable
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, MMDevice> _mutedDevices = new(StringComparer.Ordinal);
    private readonly Timer _refresh;
    private bool _disposed;

    internal RecordingFeedback()
    {
        MuteAvailableOutputs();
        // Include headphones or other outputs that become available mid-recording.
        _refresh = new Timer(_ => MuteAvailableOutputs(), null, 500, 500);
    }

    private void MuteAvailableOutputs()
    {
        lock (_gate)
        {
            if (_disposed) return;
            try
            {
                using var endpoints = new MMDeviceEnumerator();
                foreach (var device in endpoints.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
                {
                    var retain = false;
                    try
                    {
                        var id = device.ID;
                        if (_mutedDevices.ContainsKey(id) || device.AudioEndpointVolume.Mute) continue;
                        device.AudioEndpointVolume.Mute = true;
                        _mutedDevices.Add(id, device);
                        retain = true;
                    }
                    catch (Exception error) when (IsDeviceError(error))
                    {
                    }
                    finally { if (!retain) device.Dispose(); }
                }
            }
            catch (Exception error) when (IsDeviceError(error))
            {
            }
        }
    }

    public void Dispose()
    {
        _refresh.Dispose();
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var device in _mutedDevices.Values)
            {
                try
                {
                    if (device.AudioEndpointVolume.Mute) device.AudioEndpointVolume.Mute = false;
                }
                catch (Exception error) when (IsDeviceError(error))
                {
                }
                finally { device.Dispose(); }
            }
            _mutedDevices.Clear();
        }
    }

    private static bool IsDeviceError(Exception error) => error is COMException or InvalidOperationException;
}
