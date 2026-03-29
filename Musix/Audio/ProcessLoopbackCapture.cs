using Musix.Audio.Interop;
using NAudio.Wave;
using System.Runtime.InteropServices;

namespace Musix.Audio;

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

        // Process loopback virtual clients do not implement GetMixFormat (returns E_NOTIMPL).
        // Fetch the mix format from the default render endpoint instead.
        _waveFormat = GetDefaultRenderMixFormat();

        _eventHandle = CreateEventW(IntPtr.Zero, false, false, null);
        if (_eventHandle == IntPtr.Zero)
        {
            throw new InvalidOperationException("Failed to create audio event handle.");
        }

        int formatSize = Marshal.SizeOf(_waveFormat);
        IntPtr mixFormatPtr = Marshal.AllocHGlobal(formatSize);
        try
        {
            Marshal.StructureToPtr(_waveFormat, mixFormatPtr, false);

            int hr = _audioClient.Initialize(
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
            Marshal.FreeHGlobal(mixFormatPtr);
        }

        int setEventHr = _audioClient.SetEventHandle(_eventHandle);
        Marshal.ThrowExceptionForHR(setEventHr);

        Guid captureClientIid = new("C8ADBD64-E71E-48a0-A4DE-185C395CD317");
        int getServiceHr = _audioClient.GetService(ref captureClientIid, out IntPtr captureClientPtr);
        Marshal.ThrowExceptionForHR(getServiceHr);

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

    private static WaveFormat GetDefaultRenderMixFormat()
    {
        NAudio.CoreAudioApi.MMDeviceEnumerator enumerator = new();
        NAudio.CoreAudioApi.MMDevice device = enumerator.GetDefaultAudioEndpoint(
            NAudio.CoreAudioApi.DataFlow.Render,
            NAudio.CoreAudioApi.Role.Multimedia);
        return device.AudioClient.MixFormat;
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
        out IntPtr activationOperation);

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

    [ClassInterface(ClassInterfaceType.None)]
    private sealed class CompletionHandler : IActivateAudioInterfaceCompletionHandler
    {
        private readonly TaskCompletionSource<IAudioClient> _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<IAudioClient> GetResultAsync() => _tcs.Task;

        public int ActivateCompleted(IntPtr activateOperationPtr)
        {
            // Call GetActivateResult via vtable to avoid CLR COM marshaling.
            // IActivateAudioInterfaceAsyncOperation vtable: [0]=QI [1]=AddRef [2]=Release [3]=GetActivateResult
            IntPtr vtable = Marshal.ReadIntPtr(activateOperationPtr);
            IntPtr fnPtr = Marshal.ReadIntPtr(vtable, 3 * IntPtr.Size);
            GetActivateResultDelegate getActivateResult = Marshal.GetDelegateForFunctionPointer<GetActivateResultDelegate>(fnPtr);

            int hr = getActivateResult(activateOperationPtr, out int activateResult, out IntPtr interfacePtr);

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

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetActivateResultDelegate(IntPtr thisPtr, out int activateResult, out IntPtr activatedInterface);
    }
}
