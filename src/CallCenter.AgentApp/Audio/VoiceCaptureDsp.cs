using System.Runtime.InteropServices;
using System.Threading;

namespace CallCenter.AgentApp.Audio;

/// <summary>
/// The Windows voice capture DSP (<c>CLSID_CWMAudioAEC</c>, part of every
/// Windows since Vista) in its "source mode": it opens the default microphone
/// and listens to what the default speaker is playing, and gives back the
/// microphone with the speaker's sound taken out of it (A-87).
/// </summary>
/// <remarks>
/// <b>Why this one.</b> An echo canceller has to know exactly what reached the
/// speaker and when. Feeding it the customer's voice as it arrives from the
/// network is not that: the playback queue in front of the speaker grows and
/// shrinks during a call (150 ms to several seconds), and the canceller loses
/// track. The DSP reads the speaker itself, after that queue, so the delay it
/// has to cover is only the device's, and it follows the two devices' clocks
/// drifting apart. Nothing to install and no native library to ship.
///
/// <b>It only runs while the speaker is playing.</b> With no stream open on the
/// render device it delivers nothing at all, and after a while fails with
/// 0x87CC000A (found 1 Oct 2026). In a call the speaker is always playing — the
/// endpoint plays silence when the customer is quiet — but anything that
/// stopped it would silence the agent, so <see cref="EchoCancellingMicrophone"/>
/// watches for the output drying up.
///
/// <b>Threading.</b> The DSP is a COM object; it is created, run and released on
/// one thread of its own (MTA), which also polls it every 5 ms. Samples arrive
/// on that thread, as the plain microphone's arrive on NAudio's.
///
/// Deliberately free of SIPSorcery, so a test program can exercise it alone.
/// </remarks>
public sealed class VoiceCaptureDsp : IDisposable
{
    /// <summary>The rates the DSP can deliver, mono 16-bit.</summary>
    public static readonly int[] SupportedRates = [8000, 11025, 16000, 22050];

    /// <summary>How long Start waits for the DSP to come up before giving up.</summary>
    private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(5);

    private readonly int _sampleRate;
    private readonly int _mode;
    private readonly Action<short[]> _onSamples;
    private readonly Action<Exception> _onFailure;
    private readonly ManualResetEventSlim _started = new();
    private readonly Thread _thread;
    private volatile bool _stopping;
    private Exception? _startFailure;

    private VoiceCaptureDsp(int sampleRate, int mode, Action<short[]> onSamples, Action<Exception> onFailure)
    {
        _sampleRate = sampleRate;
        _mode = mode;
        _onSamples = onSamples;
        _onFailure = onFailure;
        _thread = new Thread(Run) { IsBackground = true, Name = "Echo cancellation" };
        _thread.SetApartmentState(ApartmentState.MTA);
    }

    /// <summary>The microphone's index among the active capture devices, as the DSP was given it.</summary>
    public int MicrophoneIndex { get; private set; } = -1;

    /// <summary>The speaker's index among the active render devices, as the DSP was given it.</summary>
    public int SpeakerIndex { get; private set; } = -1;

    /// <summary>
    /// Starts the DSP on the Windows default microphone and speaker and returns
    /// once it is delivering, or throws if it could not be started.
    /// </summary>
    /// <param name="sampleRate">One of <see cref="SupportedRates"/>.</param>
    /// <param name="onSamples">The cleaned microphone, in whatever lengths the DSP produces.</param>
    /// <param name="onFailure">Called once if the DSP stops working after it started.</param>
    /// <param name="cancelEcho">False runs the same capture without the canceller, for comparing.</param>
    public static VoiceCaptureDsp Start(
        int sampleRate, Action<short[]> onSamples, Action<Exception> onFailure, bool cancelEcho = true)
    {
        if (Array.IndexOf(SupportedRates, sampleRate) < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleRate), sampleRate, "The DSP cannot deliver this rate.");
        }

        var dsp = new VoiceCaptureDsp(
            sampleRate, cancelEcho ? SingleChannelAec : SingleChannelNsAgc, onSamples, onFailure);

        dsp._thread.Start();

        if (!dsp._started.Wait(StartTimeout))
        {
            dsp.Dispose();
            throw new TimeoutException("The echo canceller did not start in time.");
        }

        if (dsp._startFailure is { } failure)
        {
            dsp._thread.Join(StartTimeout);
            throw new InvalidOperationException("The echo canceller could not be started: " + failure.Message, failure);
        }

        return dsp;
    }

    public void Dispose()
    {
        _stopping = true;

        if (_thread.IsAlive && Thread.CurrentThread != _thread)
        {
            _thread.Join(StartTimeout);
        }

        _started.Dispose();
    }

    private void Run()
    {
        IMediaObject? dmo = null;
        MediaBuffer? buffer = null;
        var bufferPointer = IntPtr.Zero;
        var timer = IntPtr.Zero;
        var running = false;
        Exception? failure = null;

        try
        {
            SpeakerIndex = DefaultDeviceIndex(EDataFlow.Render);
            MicrophoneIndex = DefaultDeviceIndex(EDataFlow.Capture);

            dmo = (IMediaObject)Activator.CreateInstance(
                Type.GetTypeFromCLSID(ClsidVoiceCaptureDsp, throwOnError: true)!)!;
            var properties = (IPropertyStore)dmo;

            Set(properties, SystemModeKey, PropVariant.FromInt(_mode));
            Set(properties, DeviceIndexesKey, PropVariant.FromInt((SpeakerIndex << 16) | (MicrophoneIndex & 0xFFFF)));

            SetOutputType(dmo, _sampleRate);
            Check(dmo.AllocateStreamingResources(), "AllocateStreamingResources");

            // A second of audio: the DSP hands over at most a frame or two per
            // call, so this only has to be generous, not exact.
            buffer = new MediaBuffer(_sampleRate * 2);
            bufferPointer = Marshal.GetComInterfaceForObject(buffer, typeof(IMediaBuffer));
            timer = CreateTimer();

            running = true;
            _started.Set();

            var output = new DmoOutputDataBuffer { pBuffer = bufferPointer };

            while (!_stopping)
            {
                do
                {
                    buffer.Reset();
                    output.dwStatus = 0;

                    var hr = dmo.ProcessOutput(0, 1, ref output, out _);
                    Check(hr, "ProcessOutput");

                    if (buffer.Length >= 2)
                    {
                        var samples = new short[buffer.Length / 2];
                        Marshal.Copy(buffer.Data, samples, 0, samples.Length);
                        _onSamples(samples);
                    }
                }
                while (!_stopping && (output.dwStatus & DmoOutputDataBufferIncomplete) != 0);

                Wait(timer, PollInterval);
            }
        }
        catch (Exception ex)
        {
            if (!running)
            {
                _startFailure = ex;
                _started.Set();
            }
            else if (!_stopping)
            {
                failure = ex;
            }
        }
        finally
        {
            if (timer != IntPtr.Zero)
            {
                CloseHandle(timer);
            }

            if (dmo is not null)
            {
                try
                {
                    dmo.FreeStreamingResources();
                }
                catch
                {
                    // Closing; nothing useful left to do with a failure here.
                }

                Marshal.ReleaseComObject(dmo);
            }

            if (bufferPointer != IntPtr.Zero)
            {
                Marshal.Release(bufferPointer);
            }

            buffer?.Dispose();
        }

        // Only once the DSP has let go of the microphone: whoever is told is
        // about to open it themselves.
        if (failure is not null)
        {
            try
            {
                _onFailure(failure);
            }
            catch
            {
                // The caller's handler failing must not take this thread down
                // with an unhandled exception, which would end the app.
            }
        }
    }

    // ---- Device lookup -------------------------------------------------------

    /// <summary>
    /// Where the Windows default device for the flow sits among the active
    /// devices, which is how the DSP is told which devices to use. The default
    /// is the one the plain microphone and the speaker use (A-03 was removed:
    /// calls always use the Windows default).
    /// </summary>
    private static int DefaultDeviceIndex(EDataFlow flow)
    {
        var enumerator = (IMMDeviceEnumerator)Activator.CreateInstance(
            Type.GetTypeFromCLSID(ClsidMmDeviceEnumerator, throwOnError: true)!)!;

        try
        {
            Check(enumerator.GetDefaultAudioEndpoint(flow, ERole.Console, out var device), "GetDefaultAudioEndpoint");
            Check(device.GetId(out var defaultId), "GetId");
            Marshal.ReleaseComObject(device);

            Check(enumerator.EnumAudioEndpoints(flow, DeviceStateActive, out var devices), "EnumAudioEndpoints");

            try
            {
                Check(devices.GetCount(out var count), "GetCount");

                for (var i = 0u; i < count; i++)
                {
                    Check(devices.Item(i, out var candidate), "Item");
                    Check(candidate.GetId(out var id), "GetId");
                    Marshal.ReleaseComObject(candidate);

                    if (string.Equals(id, defaultId, StringComparison.OrdinalIgnoreCase))
                    {
                        return (int)i;
                    }
                }
            }
            finally
            {
                Marshal.ReleaseComObject(devices);
            }

            throw new InvalidOperationException($"The default {flow} device is not among the active ones.");
        }
        finally
        {
            Marshal.ReleaseComObject(enumerator);
        }
    }

    // ---- DSP set-up ----------------------------------------------------------

    private static void Set(IPropertyStore properties, PropertyKey key, PropVariant value) =>
        Check(properties.SetValue(ref key, ref value), $"SetValue({key.pid})");

    private static void SetOutputType(IMediaObject dmo, int sampleRate)
    {
        var format = new WaveFormatEx
        {
            wFormatTag = 1, // PCM
            nChannels = 1,
            nSamplesPerSec = (uint)sampleRate,
            wBitsPerSample = 16,
            nBlockAlign = 2,
            nAvgBytesPerSec = (uint)sampleRate * 2,
            cbSize = 0,
        };

        var formatPointer = Marshal.AllocCoTaskMem(Marshal.SizeOf<WaveFormatEx>());

        try
        {
            Marshal.StructureToPtr(format, formatPointer, false);

            var type = new DmoMediaType
            {
                majortype = MediaTypeAudio,
                subtype = MediaSubtypePcm,
                bFixedSizeSamples = 1,
                bTemporalCompression = 0,
                lSampleSize = 2,
                formattype = FormatWaveFormatEx,
                pUnk = IntPtr.Zero,
                cbFormat = (uint)Marshal.SizeOf<WaveFormatEx>(),
                pbFormat = formatPointer,
            };

            Check(dmo.SetOutputType(0, ref type, 0), "SetOutputType");
        }
        finally
        {
            Marshal.FreeCoTaskMem(formatPointer);
        }
    }

    private static void Check(int hr, string what)
    {
        if (hr < 0)
        {
            throw new COMException($"{what} failed (0x{hr:X8})", hr);
        }
    }

    // ---- Polling timer -------------------------------------------------------

    /// <summary>
    /// Every 5 ms. The ordinary timer runs at 15.6 ms on most laptops, which
    /// would send the customer the agent's voice in uneven lumps.
    /// </summary>
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(5);

    /// <summary>A high-resolution waitable timer, or none on a Windows too old to have one.</summary>
    private static IntPtr CreateTimer() =>
        CreateWaitableTimerExW(IntPtr.Zero, null, CreateWaitableTimerHighResolution, TimerAllAccess);

    private static void Wait(IntPtr timer, TimeSpan interval)
    {
        if (timer != IntPtr.Zero)
        {
            // Negative: relative to now, in 100 ns units.
            var due = -interval.Ticks;

            if (SetWaitableTimer(timer, ref due, 0, IntPtr.Zero, IntPtr.Zero, false))
            {
                WaitForSingleObject(timer, 1000);
                return;
            }
        }

        Thread.Sleep(interval);
    }

    // ---- Interop -------------------------------------------------------------

    private static readonly Guid ClsidVoiceCaptureDsp = new("745057C7-F353-4F2D-A7EE-58434477730E");
    private static readonly Guid ClsidMmDeviceEnumerator = new("BCDE0395-E52F-467C-8E3D-C4579291692E");
    private static readonly Guid MediaTypeAudio = new("73647561-0000-0010-8000-00AA00389B71");
    private static readonly Guid MediaSubtypePcm = new("00000001-0000-0010-8000-00AA00389B71");
    private static readonly Guid FormatWaveFormatEx = new("05589F81-C356-11CE-BF01-00AA0055595A");

    /// <summary>The DSP's property set (wmcodecdsp.h); the ids start at PID_FIRST_USABLE, 2.</summary>
    private static readonly Guid DspProperties = new("6F52C567-0360-4BD2-9617-CCBF1421C939");
    private static readonly PropertyKey SystemModeKey = new() { fmtid = DspProperties, pid = 2 };
    private static readonly PropertyKey DeviceIndexesKey = new() { fmtid = DspProperties, pid = 4 };

    /// <summary>AEC_SYSTEM_MODE: one microphone with echo cancellation.</summary>
    private const int SingleChannelAec = 0;

    /// <summary>AEC_SYSTEM_MODE: one microphone, noise suppression and gain only, no echo cancellation.</summary>
    private const int SingleChannelNsAgc = 5;

    private const uint DmoOutputDataBufferIncomplete = 0x01000000;
    private const uint DeviceStateActive = 0x1;
    private const uint CreateWaitableTimerHighResolution = 0x2;
    private const uint TimerAllAccess = 0x1F0003;

    private enum EDataFlow
    {
        Render = 0,
        Capture = 1,
    }

    private enum ERole
    {
        Console = 0,
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PropertyKey
    {
        public Guid fmtid;
        public uint pid;
    }

    /// <summary>A PROPVARIANT holding a VT_I4: the 8-byte header, then the value, 24 bytes in all on x64.</summary>
    [StructLayout(LayoutKind.Explicit, Size = 24)]
    private struct PropVariant
    {
        [FieldOffset(0)]
        public ushort vt;

        [FieldOffset(8)]
        public int intValue;

        public static PropVariant FromInt(int value) => new() { vt = 3, intValue = value };
    }

    [StructLayout(LayoutKind.Sequential, Pack = 2)]
    private struct WaveFormatEx
    {
        public ushort wFormatTag;
        public ushort nChannels;
        public uint nSamplesPerSec;
        public uint nAvgBytesPerSec;
        public ushort nBlockAlign;
        public ushort wBitsPerSample;
        public ushort cbSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DmoMediaType
    {
        public Guid majortype;
        public Guid subtype;
        public int bFixedSizeSamples;
        public int bTemporalCompression;
        public uint lSampleSize;
        public Guid formattype;
        public IntPtr pUnk;
        public uint cbFormat;
        public IntPtr pbFormat;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DmoOutputDataBuffer
    {
        public IntPtr pBuffer;
        public uint dwStatus;
        public long rtTimestamp;
        public long rtTimelength;
    }

    /// <summary>IMediaObject, in vtable order; only some methods are called.</summary>
    [ComImport]
    [Guid("D8AD0F58-5494-4102-97C5-EC798E59BCF4")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMediaObject
    {
        [PreserveSig] int GetStreamCount(out int inputs, out int outputs);
        [PreserveSig] int GetInputStreamInfo(int index, out int flags);
        [PreserveSig] int GetOutputStreamInfo(int index, out int flags);
        [PreserveSig] int GetInputType(int index, int typeIndex, IntPtr type);
        [PreserveSig] int GetOutputType(int index, int typeIndex, IntPtr type);
        [PreserveSig] int SetInputType(int index, IntPtr type, int flags);
        [PreserveSig] int SetOutputType(int index, ref DmoMediaType type, int flags);
        [PreserveSig] int GetInputCurrentType(int index, IntPtr type);
        [PreserveSig] int GetOutputCurrentType(int index, IntPtr type);
        [PreserveSig] int GetInputSizeInfo(int index, out int size, out int lookahead, out int alignment);
        [PreserveSig] int GetOutputSizeInfo(int index, out int size, out int alignment);
        [PreserveSig] int GetInputMaxLatency(int index, out long latency);
        [PreserveSig] int SetInputMaxLatency(int index, long latency);
        [PreserveSig] int Flush();
        [PreserveSig] int Discontinuity(int index);
        [PreserveSig] int AllocateStreamingResources();
        [PreserveSig] int FreeStreamingResources();
        [PreserveSig] int GetInputStatus(int index, out int flags);
        [PreserveSig] int ProcessInput(int index, IntPtr buffer, int flags, long time, long length);
        [PreserveSig] int ProcessOutput(int flags, int count, ref DmoOutputDataBuffer buffers, out int status);
        [PreserveSig] int Lock(int lockIt);
    }

    [ComImport]
    [Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetAt(uint index, out PropertyKey key);
        [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
        [PreserveSig] int SetValue(ref PropertyKey key, ref PropVariant value);
        [PreserveSig] int Commit();
    }

    [ComImport]
    [Guid("59EFF8B9-938C-4A26-82F2-95CB84CDC837")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMediaBuffer
    {
        [PreserveSig] int SetLength(int length);
        [PreserveSig] int GetMaxLength(out int maxLength);
        [PreserveSig] int GetBufferAndLength(out IntPtr buffer, out int length);
    }

    [ComImport]
    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(EDataFlow flow, uint stateMask, out IMMDeviceCollection devices);
        [PreserveSig] int GetDefaultAudioEndpoint(EDataFlow flow, ERole role, out IMMDevice device);
    }

    [ComImport]
    [Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceCollection
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int Item(uint index, out IMMDevice device);
    }

    [ComImport]
    [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int context, IntPtr parameters, out IntPtr result);
        [PreserveSig] int OpenPropertyStore(int access, out IntPtr properties);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetState(out int state);
    }

    /// <summary>The buffer the DSP writes into: unmanaged memory, so it never moves under it.</summary>
    [ComVisible(true)]
    [ClassInterface(ClassInterfaceType.None)]
    private sealed class MediaBuffer(int capacity) : IMediaBuffer, IDisposable
    {
        public IntPtr Data { get; private set; } = Marshal.AllocHGlobal(capacity);

        public int Length { get; private set; }

        public void Reset() => Length = 0;

        public int SetLength(int length)
        {
            if (length < 0 || length > capacity)
            {
                return unchecked((int)0x80070057); // E_INVALIDARG
            }

            Length = length;
            return 0;
        }

        public int GetMaxLength(out int maxLength)
        {
            maxLength = capacity;
            return 0;
        }

        public int GetBufferAndLength(out IntPtr buffer, out int length)
        {
            buffer = Data;
            length = Length;
            return 0;
        }

        public void Dispose()
        {
            if (Data != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(Data);
                Data = IntPtr.Zero;
            }
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWaitableTimerExW(IntPtr attributes, string? name, uint flags, uint access);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetWaitableTimer(
        IntPtr timer, ref long dueTime, int period, IntPtr completion, IntPtr argument, bool resume);

    [DllImport("kernel32.dll")]
    private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);
}
