using Musix.Audio.Interop;
using NAudio.Wave;
using System.Runtime.InteropServices;

namespace Musix.Audio;

// Captures PCM audio produced by a specific process using WASAPI process loopback.
//
// Process loopback taps into the audio stream a process sends to the speakers without
// affecting playback and without requiring a "stereo mix" virtual device. It was added
// in Windows 10 21H2 (build 19044) via ActivateAudioInterfaceAsync with the virtual
// device path "VAD\Process_Loopback".
//
// Lifecycle:
//   1. InitializeAsync(pid)  — activate WASAPI, negotiate format, create event handle
//   2. StartCapture()        — start the audio engine, spin up the capture thread
//   3. (DataAvailable fires for every audio packet)
//   4. StopCapture()         — signal the thread to exit, drain, stop the engine
//   5. Dispose()             — release all Win32 / COM resources
internal sealed class ProcessLoopbackCapture : IDisposable
{
    private IAudioClient? _audioClient;
    private IAudioCaptureClient? _captureClient;
    private WaveFormat? _waveFormat;
    private Thread? _captureThread;
    private IntPtr _eventHandle;
    private volatile bool _isCapturing;

    // Throws before InitializeAsync has completed — callers must await initialization first.
    public WaveFormat WaveFormat => _waveFormat ?? throw new InvalidOperationException("Not initialized.");

    // Raised on the capture thread for every audio packet WASAPI delivers.
    // WaveInEventArgs.BytesRecorded is always > 0; the buffer may be zeroed if the
    // process was silent (AUDCLNT_BUFFERFLAGS_SILENT) but the event fires regardless.
    public event EventHandler<WaveInEventArgs>? DataAvailable;

    // Activates a WASAPI process-loopback client for the given process ID and prepares
    // it for event-driven capture.
    //
    // Steps:
    //   1. Call ActivateAudioInterfaceAsync with a VT_BLOB PROPVARIANT describing the target PID.
    //      This is asynchronous — Windows calls back IActivateAudioInterfaceCompletionHandler
    //      on a thread-pool thread; CompletionHandler bridges that callback to a Task.
    //   2. Fetch the mix format from the default render endpoint.
    //      Process-loopback virtual clients return E_NOTIMPL from GetMixFormat, so we read
    //      the format from the speakers/headphones instead — whatever Windows is mixing to
    //      that endpoint is exactly what the loopback stream will produce.
    //   3. Create a Win32 auto-reset event. WASAPI signals it when new audio data is ready,
    //      which lets the capture thread sleep cheaply in WaitForSingleObject.
    //   4. Initialize the audio client in Shared + Loopback + EventCallback mode with a
    //      200 ms buffer (2,000,000 hns). The buffer just needs to be large enough that the
    //      capture thread drains it before it wraps.
    //   5. Attach the event handle to the audio client via SetEventHandle.
    //   6. Obtain the IAudioCaptureClient service interface used to pull packets in CaptureLoop.
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
                2_000_000L,    // 200 ms buffer expressed in 100-nanosecond units
                0,             // periodicity must be 0 in shared mode
                mixFormatPtr,
                IntPtr.Zero);  // null = default audio session GUID
            Marshal.ThrowExceptionForHR(hr);
        }
        finally
        {
            Marshal.FreeHGlobal(mixFormatPtr);
        }

        int setEventHr = _audioClient.SetEventHandle(_eventHandle);
        Marshal.ThrowExceptionForHR(setEventHr);

        // IID for IAudioCaptureClient — GetService is a generic factory; we hand it the
        // interface ID and it gives back a void* which we wrap via GetObjectForIUnknown.
        Guid captureClientIid = new("C8ADBD64-E71E-48a0-A4DE-185C395CD317");
        int getServiceHr = _audioClient.GetService(ref captureClientIid, out IntPtr captureClientPtr);
        Marshal.ThrowExceptionForHR(getServiceHr);

        _captureClient = (IAudioCaptureClient)Marshal.GetObjectForIUnknown(captureClientPtr);
        Marshal.Release(captureClientPtr); // GetObjectForIUnknown AddRef'd; release the raw pointer
    }

    // Starts the WASAPI audio engine and launches a background thread that drains packets.
    // Throws if InitializeAsync has not been called — both _audioClient and _captureClient
    // must be non-null before the engine can start.
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

    // Signals the capture thread to exit and waits up to 2 seconds for it to finish,
    // then stops the WASAPI engine. Safe to call before StartCapture (no-op).
    //
    // _isCapturing is volatile so the write is visible to the capture thread immediately
    // without needing a lock or memory barrier.
    public void StopCapture()
    {
        _isCapturing = false;
        _captureThread?.Join(TimeSpan.FromSeconds(2));
        _captureThread = null;
        _audioClient?.Stop();
    }

    // Runs on the dedicated capture thread. Waits for the WASAPI event, then drains
    // every available packet in a tight inner loop before waiting again.
    //
    // Outer loop: WaitForSingleObject sleeps until Windows signals _eventHandle (new data
    // is ready) or the 100 ms timeout elapses. The timeout is a safety net so the thread
    // can notice _isCapturing = false even if no more events arrive.
    //
    // Inner loop: GetNextPacketSize tells us how many frames are in the next packet (0 means
    // the queue is empty). GetBuffer returns a pointer directly into WASAPI's internal buffer
    // — we must call ReleaseBuffer before calling GetBuffer again. We copy the bytes out
    // first, then release.
    //
    // Silent-flag handling: when a process has no audio to output, WASAPI still delivers
    // packets flagged with AUDCLNT_BUFFERFLAGS_SILENT (0x2). We skip the Marshal.Copy in
    // that case and leave the byte array zeroed — callers still get the event (useful for
    // keeping a timeline), but we avoid a pointless copy.
    private void CaptureLoop()
    {
        while (_isCapturing)
        {
            WaitForSingleObject(_eventHandle, 100); // sleep until data ready or 100 ms timeout

            if (!_isCapturing)
            {
                break;
            }

            while (true)
            {
                int hr = _captureClient!.GetNextPacketSize(out uint packetSize);
                if (hr != 0 || packetSize == 0)
                {
                    break; // queue empty or error — go back to WaitForSingleObject
                }

                hr = _captureClient.GetBuffer(
                    out IntPtr dataPtr,
                    out uint numFrames,
                    out uint flags,
                    out ulong _,   // device position (not used)
                    out ulong _);  // QPC timestamp (not used)

                if (hr != 0)
                {
                    break;
                }

                int byteCount = (int)(numFrames * _waveFormat!.BlockAlign);
                byte[] buffer = new byte[byteCount];

                const uint AudclntBufferFlagsSilent = 0x00000002;
                if ((flags & AudclntBufferFlagsSilent) == 0)
                {
                    // Real audio data — copy from WASAPI's buffer into our managed array.
                    // dataPtr is only valid until ReleaseBuffer, so copy before releasing.
                    Marshal.Copy(dataPtr, buffer, 0, byteCount);
                }

                _captureClient.ReleaseBuffer(numFrames);
                DataAvailable?.Invoke(this, new WaveInEventArgs(buffer, byteCount));
            }
        }
    }

    // Returns the WAVEFORMATEX of the default render (speaker/headphone) endpoint.
    // Used instead of IAudioClient.GetMixFormat because the process-loopback virtual
    // device returns E_NOTIMPL for that call. The loopback stream is always in the same
    // format Windows is mixing to the render endpoint, so this is correct and equivalent.
    private static WaveFormat GetDefaultRenderMixFormat()
    {
        NAudio.CoreAudioApi.MMDeviceEnumerator enumerator = new();
        NAudio.CoreAudioApi.MMDevice device = enumerator.GetDefaultAudioEndpoint(
            NAudio.CoreAudioApi.DataFlow.Render,
            NAudio.CoreAudioApi.Role.Multimedia);
        return device.AudioClient.MixFormat;
    }

    // Calls ActivateAudioInterfaceAsync to get an IAudioClient targeting the given PID.
    //
    // ActivateAudioInterfaceAsync is asynchronous: it returns immediately and later calls
    // IActivateAudioInterfaceCompletionHandler.ActivateCompleted on a Windows thread-pool
    // thread. CompletionHandler bridges that callback to a TaskCompletionSource so we can
    // await it naturally.
    //
    // Memory layout requirements:
    //   - AudioClientActivationParams must be pinned in place for the entire call because
    //     it is passed by pointer inside a PROPVARIANT blob. If the GC moved it between
    //     the call and when Windows reads it, the pointer would dangle.
    //   - PropVariantBlob is a hand-laid-out struct matching the 24-byte PROPVARIANT layout
    //     for VT_BLOB (vt=0x41): a 4-byte cbSize and a pointer to the blob data at offset 16.
    private static async Task<IAudioClient> ActivateProcessLoopbackAsync(uint processId)
    {
        AudioClientActivationParams activationParams = new()
        {
            ActivationType = AudioClientActivationType.ProcessLoopback,
            ProcessLoopbackParams = new AudioClientProcessLoopbackParams
            {
                TargetProcessId = processId,
                // IncludeTargetProcessTree also captures child processes (e.g. browser tabs
                // that spawn renderer sub-processes).
                ProcessLoopbackMode = ProcessLoopbackMode.IncludeTargetProcessTree,
            }
        };

        // Pin the struct so its address stays stable across the async activation.
        GCHandle paramsHandle = GCHandle.Alloc(activationParams, GCHandleType.Pinned);
        try
        {
            PropVariantBlob propVariant = new()
            {
                Vt = 0x41, // VT_BLOB — tells Windows the activation data is a raw byte blob
                CbSize = (uint)Marshal.SizeOf<AudioClientActivationParams>(),
                PBlobData = paramsHandle.AddrOfPinnedObject(),
            };

            CompletionHandler completionHandler = new();
            Guid audioClientIid = new("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2"); // IID_IAudioClient

            int hr = ActivateAudioInterfaceAsync(
                VirtualAudioDeviceProcessLoopback, // special path that routes to the loopback engine
                ref audioClientIid,
                ref propVariant,
                completionHandler,
                out _); // IActivateAudioInterfaceAsyncOperation* — not needed; we use the callback
            Marshal.ThrowExceptionForHR(hr);

            return await completionHandler.GetResultAsync();
        }
        finally
        {
            paramsHandle.Free();
        }
    }

    // Stops capture, closes the Win32 event handle, and releases both COM objects.
    // COM objects must be released via Marshal.ReleaseComObject rather than just letting
    // them go out of scope so that their native reference counts drop immediately instead
    // of waiting for a GC finalizer.
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

    // The virtual device path that routes ActivateAudioInterfaceAsync to the process
    // loopback engine. This is a well-known constant; it is not a real device node.
    private const string VirtualAudioDeviceProcessLoopback = "VAD\\Process_Loopback";

    // mmdevapi.dll: activates a WASAPI audio interface asynchronously.
    // The activationParams PROPVARIANT carries the target PID in VT_BLOB form.
    // completionHandler is called back on a Windows thread-pool thread when done.
    [DllImport("mmdevapi.dll", CharSet = CharSet.Unicode)]
    private static extern int ActivateAudioInterfaceAsync(
        string deviceInterfacePath,
        ref Guid riid,
        ref PropVariantBlob activationParams,
        IActivateAudioInterfaceCompletionHandler completionHandler,
        out IntPtr activationOperation);

    // kernel32: creates a Win32 event object. We use an auto-reset event (bManualReset=false)
    // so Windows resets it automatically after WaitForSingleObject returns.
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateEventW(
        IntPtr lpEventAttributes, // null = default security
        bool bManualReset,
        bool bInitialState,
        string? lpName); // null = unnamed event

    // kernel32: blocks the calling thread until the object is signaled or the timeout elapses.
    // Return value WAIT_OBJECT_0 (0) means signaled; WAIT_TIMEOUT (0x102) means timed out.
    [DllImport("kernel32.dll")]
    private static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

    // kernel32: closes any Win32 kernel object handle (event, thread, file, etc.).
    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr hObject);

    // Bridges the native IActivateAudioInterfaceCompletionHandler callback to a Task.
    //
    // Why no [ComImport]: this is a managed object that implements a COM interface.
    // [ComImport] is for importing existing native COM classes; here we're exporting
    // a managed class to COM (the CLR generates a COM-Callable Wrapper automatically).
    //
    // [ClassInterface(None)]: tells the CLR not to auto-generate an IDispatch interface.
    // We only need the vtable for IActivateAudioInterfaceCompletionHandler.
    [ClassInterface(ClassInterfaceType.None)]
    private sealed class CompletionHandler : IActivateAudioInterfaceCompletionHandler
    {
        private readonly TaskCompletionSource<IAudioClient> _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

        // Returns the Task that resolves to the activated IAudioClient.
        // The caller awaits this; it completes when ActivateCompleted fires.
        public Task<IAudioClient> GetResultAsync() => _tcs.Task;

        // Called by Windows on a thread-pool thread when audio interface activation finishes.
        //
        // activateOperationPtr is a raw IActivateAudioInterfaceAsyncOperation* pointer.
        // We cannot use it through a [ComImport] interface because the CLR's QueryInterface
        // call on it fails — the native object doesn't support the CLR's expected QI path.
        // Instead we call GetActivateResult directly via vtable:
        //
        //   IUnknown vtable layout (all COM objects share this prefix):
        //     slot 0: QueryInterface
        //     slot 1: AddRef
        //     slot 2: Release
        //
        //   IActivateAudioInterfaceAsyncOperation adds one method after IUnknown:
        //     slot 3: GetActivateResult(out HRESULT, out IUnknown*)
        //
        // We read the vtable pointer from offset 0 of the object, then read the function
        // pointer at slot 3 (3 * pointer size bytes into the vtable), wrap it in a delegate
        // typed as stdcall, and invoke it directly.
        public int ActivateCompleted(IntPtr activateOperationPtr)
        {
            IntPtr vtable = Marshal.ReadIntPtr(activateOperationPtr);          // *obj → vtable ptr
            IntPtr fnPtr = Marshal.ReadIntPtr(vtable, 3 * IntPtr.Size);       // vtable[3] → GetActivateResult
            GetActivateResultDelegate getActivateResult =
                Marshal.GetDelegateForFunctionPointer<GetActivateResultDelegate>(fnPtr);

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
                // activateResult is the HRESULT from the actual device activation.
                // hr above is the HRESULT of the GetActivateResult call itself.
                _tcs.TrySetException(
                    Marshal.GetExceptionForHR(activateResult)
                    ?? new InvalidOperationException($"Audio activation failed: 0x{activateResult:X8}"));
                return 0;
            }

            // Wrap the raw IUnknown* into a managed RCW and immediately drop the raw ref
            // (GetObjectForIUnknown AddRef'd it; we don't want to keep two references).
            IAudioClient audioClient = (IAudioClient)Marshal.GetObjectForIUnknown(interfacePtr);
            Marshal.Release(interfacePtr);
            _tcs.TrySetResult(audioClient);
            return 0;
        }

        // Matches the native signature of IActivateAudioInterfaceAsyncOperation::GetActivateResult.
        // StdCall is the calling convention for all COM interface methods on Windows.
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetActivateResultDelegate(IntPtr thisPtr, out int activateResult, out IntPtr activatedInterface);
    }
}
