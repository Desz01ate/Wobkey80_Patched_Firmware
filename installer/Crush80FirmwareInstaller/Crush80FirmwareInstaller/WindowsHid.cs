using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Crush80FirmwareInstaller;

public sealed record HidDeviceInfo(
    string Path,
    ushort VendorId,
    ushort ProductId,
    ushort UsagePage,
    ushort Usage,
    int InputReportLength,
    int OutputReportLength);

public static class HidDeviceEnumerator
{
    public static HidDeviceInfo? Find(DeviceTarget target) => Enumerate()
        .FirstOrDefault(device =>
            device.VendorId == target.ParsedVendorId &&
            device.ProductId == target.ParsedProductId &&
            device.UsagePage == target.ParsedUsagePage);

    public static IReadOnlyList<HidDeviceInfo> Enumerate()
    {
        var devices = new List<HidDeviceInfo>();
        NativeMethods.HidD_GetHidGuid(out var hidGuid);
        var deviceInfoSet = NativeMethods.SetupDiGetClassDevs(
            ref hidGuid,
            IntPtr.Zero,
            IntPtr.Zero,
            NativeMethods.DigcfPresent | NativeMethods.DigcfDeviceInterface);

        if (deviceInfoSet == NativeMethods.InvalidHandleValue)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to enumerate HID devices.");
        }

        try
        {
            for (uint index = 0; ; index++)
            {
                var interfaceData = new NativeMethods.SpDeviceInterfaceData
                {
                    Size = Marshal.SizeOf<NativeMethods.SpDeviceInterfaceData>()
                };

                if (!NativeMethods.SetupDiEnumDeviceInterfaces(deviceInfoSet, IntPtr.Zero, ref hidGuid, index, ref interfaceData))
                {
                    var error = Marshal.GetLastWin32Error();
                    if (error == NativeMethods.ErrorNoMoreItems)
                    {
                        break;
                    }

                    throw new Win32Exception(error, "Unable to enumerate a HID device interface.");
                }

                var path = GetDevicePath(deviceInfoSet, ref interfaceData);
                var device = Inspect(path);
                if (device is not null)
                {
                    devices.Add(device);
                }
            }
        }
        finally
        {
            NativeMethods.SetupDiDestroyDeviceInfoList(deviceInfoSet);
        }

        return devices;
    }

    private static string GetDevicePath(IntPtr deviceInfoSet, ref NativeMethods.SpDeviceInterfaceData interfaceData)
    {
        _ = NativeMethods.SetupDiGetDeviceInterfaceDetail(
            deviceInfoSet,
            ref interfaceData,
            IntPtr.Zero,
            0,
            out var requiredSize,
            IntPtr.Zero);

        var buffer = Marshal.AllocHGlobal(requiredSize);
        try
        {
            Marshal.WriteInt32(buffer, IntPtr.Size == 8 ? 8 : 6);
            if (!NativeMethods.SetupDiGetDeviceInterfaceDetail(
                    deviceInfoSet,
                    ref interfaceData,
                    buffer,
                    requiredSize,
                    out _,
                    IntPtr.Zero))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to read a HID device path.");
            }

            return Marshal.PtrToStringUni(IntPtr.Add(buffer, 4))
                   ?? throw new InvalidDataException("Windows returned an empty HID device path.");
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static HidDeviceInfo? Inspect(string path)
    {
        using var handle = NativeMethods.CreateFile(
            path,
            NativeMethods.GenericRead | NativeMethods.GenericWrite,
            NativeMethods.FileShareRead | NativeMethods.FileShareWrite,
            IntPtr.Zero,
            NativeMethods.OpenExisting,
            0,
            IntPtr.Zero);

        if (handle.IsInvalid)
        {
            return null;
        }

        var attributes = new NativeMethods.HiddAttributes { Size = Marshal.SizeOf<NativeMethods.HiddAttributes>() };
        if (!NativeMethods.HidD_GetAttributes(handle, ref attributes) ||
            !NativeMethods.HidD_GetPreparsedData(handle, out var preparsedData))
        {
            return null;
        }

        try
        {
            if (NativeMethods.HidP_GetCaps(preparsedData, out var caps) != NativeMethods.HidpStatusSuccess)
            {
                return null;
            }

            return new HidDeviceInfo(
                path,
                attributes.VendorId,
                attributes.ProductId,
                caps.UsagePage,
                caps.Usage,
                caps.InputReportByteLength,
                caps.OutputReportByteLength);
        }
        finally
        {
            NativeMethods.HidD_FreePreparsedData(preparsedData);
        }
    }
}

public sealed class HidDevice : IAsyncDisposable
{
    private readonly FileStream _stream;
    private readonly int _inputReportLength;
    private readonly byte _reportId;

    private HidDevice(FileStream stream, int inputReportLength, byte reportId)
    {
        _stream = stream;
        _inputReportLength = inputReportLength;
        _reportId = reportId;
    }

    public static HidDevice Open(HidDeviceInfo info, byte reportId)
    {
        var handle = NativeMethods.CreateFile(
            info.Path,
            NativeMethods.GenericRead | NativeMethods.GenericWrite,
            NativeMethods.FileShareRead | NativeMethods.FileShareWrite,
            IntPtr.Zero,
            NativeMethods.OpenExisting,
            NativeMethods.FileFlagOverlapped,
            IntPtr.Zero);

        if (handle.IsInvalid)
        {
            var error = Marshal.GetLastWin32Error();
            handle.Dispose();
            throw new Win32Exception(error, "Unable to open the keyboard OTA interface.");
        }

        try
        {
            var stream = new FileStream(handle, FileAccess.ReadWrite, Math.Max(info.InputReportLength, info.OutputReportLength), true);
            return new HidDevice(stream, info.InputReportLength, reportId);
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    public async Task WriteAsync(byte[] report, CancellationToken cancellationToken)
    {
        await _stream.WriteAsync(report, cancellationToken);
    }

    public async Task<byte[]?> ReadResponseAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);

        while (true)
        {
            var report = new byte[_inputReportLength];
            int bytesRead;
            try
            {
                bytesRead = await _stream.ReadAsync(report, timeoutSource.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return null;
            }

            if (bytesRead == 0)
            {
                throw new IOException("The keyboard OTA interface was disconnected.");
            }

            if (report[0] == _reportId)
            {
                return bytesRead == report.Length ? report : report[..bytesRead];
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _stream.DisposeAsync();
    }
}

internal static class NativeMethods
{
    internal const uint DigcfPresent = 0x00000002;
    internal const uint DigcfDeviceInterface = 0x00000010;
    internal const int ErrorNoMoreItems = 259;
    internal const uint GenericRead = 0x80000000;
    internal const uint GenericWrite = 0x40000000;
    internal const uint FileShareRead = 0x00000001;
    internal const uint FileShareWrite = 0x00000002;
    internal const uint OpenExisting = 3;
    internal const uint FileFlagOverlapped = 0x40000000;
    internal const uint HidpStatusSuccess = 0x00110000;
    internal static readonly IntPtr InvalidHandleValue = new(-1);

    [StructLayout(LayoutKind.Sequential)]
    internal struct SpDeviceInterfaceData
    {
        public int Size;
        public Guid InterfaceClassGuid;
        public int Flags;
        public IntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct HiddAttributes
    {
        public int Size;
        public ushort VendorId;
        public ushort ProductId;
        public ushort VersionNumber;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct HidpCaps
    {
        public ushort Usage;
        public ushort UsagePage;
        public ushort InputReportByteLength;
        public ushort OutputReportByteLength;
        public ushort FeatureReportByteLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)]
        public ushort[] Reserved;
        public ushort NumberLinkCollectionNodes;
        public ushort NumberInputButtonCaps;
        public ushort NumberInputValueCaps;
        public ushort NumberInputDataIndices;
        public ushort NumberOutputButtonCaps;
        public ushort NumberOutputValueCaps;
        public ushort NumberOutputDataIndices;
        public ushort NumberFeatureButtonCaps;
        public ushort NumberFeatureValueCaps;
        public ushort NumberFeatureDataIndices;
    }

    [DllImport("hid.dll")]
    internal static extern void HidD_GetHidGuid(out Guid hidGuid);

    [DllImport("setupapi.dll", SetLastError = true)]
    internal static extern IntPtr SetupDiGetClassDevs(
        ref Guid classGuid,
        IntPtr enumerator,
        IntPtr parentWindow,
        uint flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetupDiEnumDeviceInterfaces(
        IntPtr deviceInfoSet,
        IntPtr deviceInfoData,
        ref Guid interfaceClassGuid,
        uint memberIndex,
        ref SpDeviceInterfaceData deviceInterfaceData);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetupDiGetDeviceInterfaceDetail(
        IntPtr deviceInfoSet,
        ref SpDeviceInterfaceData deviceInterfaceData,
        IntPtr deviceInterfaceDetailData,
        int deviceInterfaceDetailDataSize,
        out int requiredSize,
        IntPtr deviceInfoData);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

    [DllImport("hid.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool HidD_GetAttributes(SafeFileHandle device, ref HiddAttributes attributes);

    [DllImport("hid.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool HidD_GetPreparsedData(SafeFileHandle device, out IntPtr preparsedData);

    [DllImport("hid.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool HidD_FreePreparsedData(IntPtr preparsedData);

    [DllImport("hid.dll")]
    internal static extern uint HidP_GetCaps(IntPtr preparsedData, out HidpCaps capabilities);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern SafeFileHandle CreateFile(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        IntPtr securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);
}
