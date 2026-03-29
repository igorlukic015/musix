using System.Runtime.InteropServices;

namespace Musix.Audio.Interop;

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

// No [ComImport] — this interface is implemented by managed code (CompletionHandler).
// ActivateCompleted receives the async operation as IntPtr to avoid CLR QI marshaling;
// GetActivateResult is called directly via vtable in the implementation.
[Guid("41D949AB-9862-444A-80F6-C261334DA5EB")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IActivateAudioInterfaceCompletionHandler
{
    [PreserveSig] int ActivateCompleted(IntPtr activateOperation);
}
