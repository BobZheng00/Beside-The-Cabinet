using System.Runtime.InteropServices;
using Windows.Media;
using Windows.Media.Capture;
using Windows.Media.Devices;
using Windows.Media.Effects;
using InsertCoin.Mind;

namespace InsertCoin.Game.App;

/// <summary>
/// The microphone as a call opens it: Windows' default microphone for calls, in communications mode, so that where it
/// has Windows 11's Voice Clarity, the companion's own voice, background noise and the room's echo are taken out
/// before the app hears anything. It opts out of Windows' ducking, so other apps' sound keeps its volume while it is
/// open. It gives 16 kHz mono as it comes in, and keeps nothing itself.
/// </summary>
/// <remarks>Which microphone is <see cref="Client"/>'s choice (the place for #342's choice of microphone).</remarks>
public sealed class Microphone : IDisposable
{
    private IAudioClient2? _client;
    private IAudioCaptureClient? _capture;
    private IAudioSessionControl2? _session;

    /// <summary>
    /// Whether Windows cancels echo on this microphone for calls (Voice Clarity, or the maker's own), so she can hear
    /// the player over her own voice. It reads Windows' list of the microphone's effects: nothing is opened.
    /// </summary>
    public static bool CancelsEcho()
    {
        try
        {
            var id = MediaDevice.GetDefaultAudioCaptureId(AudioDeviceRole.Communications);
            return !string.IsNullOrEmpty(id)
                && AudioEffectsManager.CreateAudioCaptureEffectsManager(id, MediaCategory.Communications, AudioProcessing.Default)
                    .GetAudioCaptureEffects().Any(e => e.AudioEffectType == AudioEffectType.AcousticEchoCancellation);
        }
        catch (Exception)
        {
            return false;                                   // no microphone, or Windows can't say: as if none
        }
    }

    /// <summary>
    /// Calls <paramref name="then"/> (on another thread) whenever Windows' default microphone for calls changes.
    /// shortcut: never unsubscribed, as she listens for the app's whole run; return a handle if that changes.
    /// </summary>
    public static void Changed(Action then) =>
        MediaDevice.DefaultAudioCaptureDeviceChanged += (_, e) =>
        {
            if (e.Role == AudioDeviceRole.Communications)
                then();
        };

    /// <summary>Opens the microphone; throws when it can't (none, or not allowed).</summary>
    public Microphone()
    {
        try
        {
            _client = Client();
            var properties = new AudioClientProperties { Size = (uint)Marshal.SizeOf<AudioClientProperties>(), Category = CategoryCommunications };
            _client.SetClientProperties(ref properties);    // before Initialize: what makes it a call's stream
            var format = new WaveFormat
            {
                Tag = 3, Channels = 1, Rate = Hearing.SampleRate, BytesPerSecond = Hearing.SampleRate * 4,
                BlockAlign = 4, Bits = 32,                  // 32-bit float, mono: Windows converts from the microphone's own
            };
            _client.Initialize(0, AutoConvertPcm | SrcDefaultQuality, 10_000_000, 0, ref format, IntPtr.Zero);   // shared, a second's buffer
            var captureId = typeof(IAudioCaptureClient).GUID;
            _client.GetService(ref captureId, out var capture);
            _capture = (IAudioCaptureClient)capture;
            var sessionId = new Guid("F4B1A599-7266-4319-A8CA-E70ACB11E8CD");     // IAudioSessionControl
            _client.GetService(ref sessionId, out var session);
            _session = (IAudioSessionControl2)session;
            _session.SetDuckingPreference(true);           // before it starts: other apps keep their volume
            _client.Start();
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    /// <summary>What it has heard since it was last read, 16 kHz mono.</summary>
    public float[] Read()
    {
        if (_capture is null)
            return [];
        var heard = new List<float>();
        while (true)
        {
            _capture.GetNextPacketSize(out var packet);
            if (packet == 0)
                return [.. heard];
            _capture.GetBuffer(out var data, out var frames, out var flags, out _, out _);
            var samples = new float[frames];
            if ((flags & BufferSilent) == 0)
                Marshal.Copy(data, samples, 0, (int)frames);
            _capture.ReleaseBuffer(frames);
            heard.AddRange(samples);
        }
    }

    /// <summary>Closes the microphone.</summary>
    public void Dispose()
    {
        try
        {
            _client?.Stop();
        }
        catch (COMException)
        {
            // never started, or the device went: closing goes on
        }
        foreach (var com in new object?[] { _session, _capture, _client })
            if (com is not null)
                Marshal.ReleaseComObject(com);
        _session = null;
        _capture = null;
        _client = null;
    }

    /// <summary>The microphone she hears through, Windows' default for calls, ready to be set up.</summary>
    private static IAudioClient2 Client()
    {
        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
        IMMDevice? device = null;
        try
        {
            enumerator.GetDefaultAudioEndpoint(DataFlowCapture, RoleCommunications, out device);
            var iid = typeof(IAudioClient2).GUID;
            device.Activate(ref iid, ClsctxAll, IntPtr.Zero, out var client);
            return (IAudioClient2)client;
        }
        finally
        {
            if (device is not null)
                Marshal.ReleaseComObject(device);
            Marshal.ReleaseComObject(enumerator);
        }
    }

    // ---- Windows' Core Audio (mmdeviceapi.h, audioclient.h, audiopolicy.h) ----------------------------------------------

    private const int DataFlowCapture = 1, RoleCommunications = 2, ClsctxAll = 0x17, CategoryCommunications = 3;
    private const uint AutoConvertPcm = 0x80000000, SrcDefaultQuality = 0x08000000, BufferSilent = 0x2;

    [StructLayout(LayoutKind.Sequential, Pack = 2)]
    private struct WaveFormat
    {
        public ushort Tag, Channels;
        public int Rate, BytesPerSecond;
        public ushort BlockAlign, Bits, Extra;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AudioClientProperties
    {
        public uint Size;
        public int Offload, Category, Options;
    }

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumerator
    {
    }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        void EnumAudioEndpoints();
        void GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        void Activate(ref Guid iid, int context, IntPtr parameters, [MarshalAs(UnmanagedType.IUnknown)] out object client);
    }

    [ComImport, Guid("726778CD-F60A-4EDA-82DE-E47610CD78AA"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioClient2
    {
        void Initialize(int shareMode, uint flags, long bufferDuration, long periodicity, ref WaveFormat format, IntPtr session);
        void GetBufferSize();
        void GetStreamLatency();
        void GetCurrentPadding();
        void IsFormatSupported();
        void GetMixFormat();
        void GetDevicePeriod();
        void Start();
        void Stop();
        void Reset();
        void SetEventHandle();
        void GetService(ref Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object service);
        void IsOffloadCapable();
        void SetClientProperties(ref AudioClientProperties properties);
    }

    [ComImport, Guid("C8ADBD64-E71E-48A0-A4DE-185C395CD317"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioCaptureClient
    {
        void GetBuffer(out IntPtr data, out uint frames, out uint flags, out ulong devicePosition, out ulong counterPosition);
        void ReleaseBuffer(uint frames);
        void GetNextPacketSize(out uint frames);
    }

    [ComImport, Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionControl2
    {
        void GetState();
        void GetDisplayName();
        void SetDisplayName();
        void GetIconPath();
        void SetIconPath();
        void GetGroupingParam();
        void SetGroupingParam();
        void RegisterAudioSessionNotification();
        void UnregisterAudioSessionNotification();
        void GetSessionIdentifier();
        void GetSessionInstanceIdentifier();
        void GetProcessId();
        void IsSystemSoundsSession();
        void SetDuckingPreference([MarshalAs(UnmanagedType.Bool)] bool optOut);
    }
}
