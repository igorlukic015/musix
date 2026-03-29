using System.Runtime.InteropServices;
using NAudio.Wave;

namespace Musix;

internal sealed class ProcessLoopbackCapture : IDisposable
{
    private IAudioClient? _audioClient;
    private IAudioCaptureClient? _captureClient;
    private WaveFormat? _waveFormat;
    private Thread? _captureThread;
    private IntPtr _eventHandle;
    private volatile bool _isCapturing;

    public WaveFormat WaveFormat => _waveFormat ?? throw new InvalidOperationException("Not initialized.");

    public event EventHandler<WaveInEventArgs>? DataAvailable;

    public async Task InitializeAsync(int processId)
    {
        _audioClient = await ActivateProcessLoopbackAsync((uint)processId);

        int hr = _audioClient.GetMixFormat(out IntPtr mixFormatPtr);
        Marshal.ThrowExceptionForHR(hr);

        try
        {
            _waveFormat = WaveFormat.MarshalFromPtr(mixFormatPtr);

            _eventHandle = CreateEventW(IntPtr.Zero, false, false, null);
            if (_eventHandle == IntPtr.Zero)
            {
                throw new InvalidOperationException("Failed to create audio event handle.");
            }

            hr = _audioClient.Initialize(
                AudioClientShareMode.Shared,
                AudioClientStreamFlags.Loopback | AudioClientStreamFlags.EventCallback,
                2_000_000L,
                0,
                mixFormatPtr,
                IntPtr.Zero);
            Marshal.ThrowExceptionForHR(hr);
        }
        finally
        {
            Marshal.FreeCoTaskMem(mixFormatPtr);
        }

        hr = _audioClient.SetEventHandle(_eventHandle);
        Marshal.ThrowExceptionForHR(hr);

        Guid captureClientIid = new("C8ADBD64-E71E-48a0-A4DE-185C395CD317");
        hr = _audioClient.GetService(ref captureClientIid, out IntPtr captureClientPtr);
        Marshal.ThrowExceptionForHR(hr);

        _captureClient = (IAudioCaptureClient)Marshal.GetObjectForIUnknown(captureClientPtr);
        Marshal.Release(captureClientPtr);
    }

    public void StartCapture()
    {
        if (_audioClient is null || _captureClient is null)
        {
            throw new InvalidOperationException("Not initialized.");
        }

        _isCapturing = true;

        int hr = _audioClient.Start();
        Marshal.ThrowExceptionForHR(hr);

        _captureThread = new Thread(CaptureLoop) { IsBackground = true, Name = "AudioCapture" };
        _captureThread.Start();
    }

    public void StopCapture()
    {
        _isCapturing = false;
        _captureThread?.Join(TimeSpan.FromSeconds(2));
        _captureThread = null;
        _audioClient?.Stop();
    }

    private void CaptureLoop()
    {
        while (_isCapturing)
        {
            WaitForSingleObject(_eventHandle, 100);

            if (!_isCapturing)
            {
                break;
            }

            while (true)
            {
                int hr = _captureClient!.GetNextPacketSize(out uint packetSize);
                if (hr != 0 || packetSize == 0)
                {
                    break;
                }

                hr = _captureClient.GetBuffer(
                    out IntPtr dataPtr,
                    out uint numFrames,
                    out uint flags,
                    out ulong _,
                    out ulong _);

                if (hr != 0)
                {
                    break;
                }

                int byteCount = (int)(numFrames * _waveFormat!.BlockAlign);
                byte[] buffer = new byte[byteCount];

                const uint AudclntBufferFlagsSilent = 0x00000002;
                if ((flags & AudclntBufferFlagsSilent) == 0)
                {
                    Marshal.Copy(dataPtr, buffer, 0, byteCount);
                }

                _captureClient.ReleaseBuffer(numFrames);
                DataAvailable?.Invoke(this, new WaveInEventArgs(buffer, byteCount));
            }
        }
    }

    private static async Task<IAudioClient> ActivateProcessLoopbackAsync(uint processId)
    {
        AudioClientActivationParams activationParams = new()
        {
            ActivationType = AudioClientActivationType.ProcessLoopback,
            ProcessLoopbackParams = new AudioClientProcessLoopbackParams
            {
                TargetProcessId = processId,
                ProcessLoopbackMode = ProcessLoopbackMode.IncludeTargetProcessTree,
            }
        };

        GCHandle paramsHandle = GCHandle.Alloc(activationParams, GCHandleType.Pinned);
        try
        {
            PropVariantBlob propVariant = new()
            {
                Vt = 0x41, // VT_BLOB
                CbSize = (uint)Marshal.SizeOf<AudioClientActivationParams>(),
                PBlobData = paramsHandle.AddrOfPinnedObject(),
            };

            CompletionHandler completionHandler = new();
            Guid audioClientIid = new("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2");

            int hr = ActivateAudioInterfaceAsync(
                VirtualAudioDeviceProcessLoopback,
                ref audioClientIid,
                ref propVariant,
                completionHandler,
                out _);
            Marshal.ThrowExceptionForHR(hr);

            return await completionHandler.GetResultAsync();
        }
        finally
        {
            paramsHandle.Free();
        }
    }

    public void Dispose()
    {
        StopCapture();

        if (_eventHandle != IntPtr.Zero)
        {
            CloseHandle(_eventHandle);
            _eventHandle = IntPtr.Zero;
        }

        if (_captureClient is not null)
        {
            Marshal.ReleaseComObject(_captureClient);
            _captureClient = null;
        }

        if (_audioClient is not null)
        {
            Marshal.ReleaseComObject(_audioClient);
            _audioClient = null;
        }
    }

    private const string VirtualAudioDeviceProcessLoopback = "VAD\\Process_Loopback";

    [DllImport("mmdevapi.dll", CharSet = CharSet.Unicode)]
    private static extern int ActivateAudioInterfaceAsync(
        string deviceInterfacePath,
        ref Guid riid,
        ref PropVariantBlob activationParams,
        IActivateAudioInterfaceCompletionHandler completionHandler,
        out IActivateAudioInterfaceAsyncOperation? activationOperation);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateEventW(
        IntPtr lpEventAttributes,
        bool bManualReset,
        bool bInitialState,
        string? lpName);

    [DllImport("kernel32.dll")]
    private static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr hObject);
}

// ── WASAPI interop types ──────────────────────────────────────────────────────

internal enum AudioClientShareMode
{
    Shared = 0,
    Exclusive = 1,
}

[Flags]
internal enum AudioClientStreamFlags : uint
{
    Loopback = 0x00020000,
    EventCallback = 0x00040000,
}

internal enum AudioClientActivationType
{
    Default = 0,
    ProcessLoopback = 1,
}

internal enum ProcessLoopbackMode
{
    IncludeTargetProcessTree = 0,
    ExcludeTargetProcessTree = 1,
}

[StructLayout(LayoutKind.Sequential)]
internal struct AudioClientProcessLoopbackParams
{
    public uint TargetProcessId;
    public ProcessLoopbackMode ProcessLoopbackMode;
}

[StructLayout(LayoutKind.Sequential)]
internal struct AudioClientActivationParams
{
    public AudioClientActivationType ActivationType;
    public AudioClientProcessLoopbackParams ProcessLoopbackParams;
}

// PROPVARIANT for VT_BLOB (0x41): header (8 bytes) + cbSize (4) + pad (4) + pBlobData (8).
[StructLayout(LayoutKind.Explicit, Size = 24)]
internal struct PropVariantBlob
{
    [FieldOffset(0)]  public ushort Vt;
    [FieldOffset(2)]  public ushort WReserved1;
    [FieldOffset(4)]  public ushort WReserved2;
    [FieldOffset(6)]  public ushort WReserved3;
    [FieldOffset(8)]  public uint   CbSize;
    [FieldOffset(16)] public IntPtr PBlobData;
}

[ComImport]
[Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioClient
{
    [PreserveSig] int Initialize(AudioClientShareMode shareMode, AudioClientStreamFlags streamFlags, long hnsBufferDuration, long hnsPeriodicity, IntPtr pFormat, IntPtr audioSessionGuid);
    [PreserveSig] int GetBufferSize(out uint pNumBufferFrames);
    [PreserveSig] int GetStreamLatency(out long phnsLatency);
    [PreserveSig] int GetCurrentPadding(out uint pNumPaddingFrames);
    [PreserveSig] int IsFormatSupported(AudioClientShareMode shareMode, IntPtr pFormat, out IntPtr ppClosestMatch);
    [PreserveSig] int GetMixFormat(out IntPtr ppDeviceFormat);
    [PreserveSig] int GetDevicePeriod(out long phnsDefaultDevicePeriod, out long phnsMinimumDevicePeriod);
    [PreserveSig] int Start();
    [PreserveSig] int Stop();
    [PreserveSig] int Reset();
    [PreserveSig] int SetEventHandle(IntPtr eventHandle);
    [PreserveSig] int GetService(ref Guid riid, out IntPtr ppv);
}

[ComImport]
[Guid("C8ADBD64-E71E-48a0-A4DE-185C395CD317")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioCaptureClient
{
    [PreserveSig] int GetBuffer(out IntPtr ppData, out uint pNumFramesAvailable, out uint pdwFlags, out ulong pu64DevicePosition, out ulong pu64QPCPosition);
    [PreserveSig] int ReleaseBuffer(uint numFramesRead);
    [PreserveSig] int GetNextPacketSize(out uint pNumFramesInNextPacket);
}

[ComImport]
[Guid("72A567CE-257E-4B10-BB6A-E44DCA2D1531")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IActivateAudioInterfaceAsyncOperation
{
    [PreserveSig] int GetActivateResult(out int activateResult, out IntPtr activatedInterface);
}

[Guid("41D949AB-9862-444A-80F6-C261334DA5EB")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IActivateAudioInterfaceCompletionHandler
{
    [PreserveSig] int ActivateCompleted(IActivateAudioInterfaceAsyncOperation activateOperation);
}

[ClassInterface(ClassInterfaceType.None)]
internal sealed class CompletionHandler : IActivateAudioInterfaceCompletionHandler
{
    private readonly TaskCompletionSource<IAudioClient> _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task<IAudioClient> GetResultAsync() => _tcs.Task;

    public int ActivateCompleted(IActivateAudioInterfaceAsyncOperation activateOperation)
    {
        int hr = activateOperation.GetActivateResult(out int activateResult, out IntPtr interfacePtr);

        if (hr != 0)
        {
            _tcs.TrySetException(
                Marshal.GetExceptionForHR(hr)
                ?? new InvalidOperationException($"GetActivateResult failed: 0x{hr:X8}"));
            return 0;
        }

        if (activateResult != 0)
        {
            _tcs.TrySetException(
                Marshal.GetExceptionForHR(activateResult)
                ?? new InvalidOperationException($"Audio activation failed: 0x{activateResult:X8}"));
            return 0;
        }

        IAudioClient audioClient = (IAudioClient)Marshal.GetObjectForIUnknown(interfacePtr);
        Marshal.Release(interfacePtr);
        _tcs.TrySetResult(audioClient);
        return 0;
    }
}
