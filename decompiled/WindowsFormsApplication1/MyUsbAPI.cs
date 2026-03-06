using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace WindowsFormsApplication1;

internal class MyUsbAPI
{
	[Flags]
	private enum DIGCF
	{
		DIGCF_DEFAULT = 1,
		DIGCF_PRESENT = 2,
		DIGCF_ALLCLASSES = 4,
		DIGCF_PROFILE = 8,
		DIGCF_DEVICEINTERFACE = 0x10
	}

	private struct SP_DEVINFO_DATA
	{
		public static readonly SP_DEVINFO_DATA Empty = new SP_DEVINFO_DATA(Marshal.SizeOf(typeof(SP_DEVINFO_DATA)));

		public uint cbSize;

		public Guid ClassGuid;

		public uint DevInst;

		public IntPtr Reserved;

		private SP_DEVINFO_DATA(int size)
		{
			cbSize = (uint)size;
			ClassGuid = Guid.Empty;
			DevInst = 0u;
			Reserved = IntPtr.Zero;
		}
	}

	private struct SP_DEVICE_INTERFACE_DATA
	{
		public static readonly SP_DEVICE_INTERFACE_DATA Empty = new SP_DEVICE_INTERFACE_DATA(Marshal.SizeOf(typeof(SP_DEVICE_INTERFACE_DATA)));

		public int cbSize;

		public Guid InterfaceClassGuid;

		public uint Flags;

		public UIntPtr Reserved;

		private SP_DEVICE_INTERFACE_DATA(int size)
		{
			cbSize = size;
			InterfaceClassGuid = Guid.Empty;
			Flags = 0u;
			Reserved = UIntPtr.Zero;
		}
	}

	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
	private struct SP_DEVICE_INTERFACE_DETAIL_DATA
	{
		public uint cbSize;

		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
		public string DevicePath;
	}

	private static class DESIREDACCESS
	{
		public const uint GENERIC_READ = 2147483648u;

		public const uint GENERIC_WRITE = 1073741824u;

		public const uint GENERIC_EXECUTE = 536870912u;

		public const uint GENERIC_ALL = 268435456u;
	}

	private static class SHAREMODE
	{
		public const uint FILE_SHARE_READ = 1u;

		public const uint FILE_SHARE_WRITE = 2u;

		public const uint FILE_SHARE_DELETE = 4u;
	}

	private static class CREATIONDISPOSITION
	{
		public const uint CREATE_NEW = 1u;

		public const uint CREATE_ALWAYS = 2u;

		public const uint OPEN_EXISTING = 3u;

		public const uint OPEN_ALWAYS = 4u;

		public const uint TRUNCATE_EXISTING = 5u;
	}

	private static class FLAGSANDATTRIBUTES
	{
		public const uint FILE_FLAG_WRITE_THROUGH = 2147483648u;

		public const uint FILE_FLAG_OVERLAPPED = 1073741824u;

		public const uint FILE_FLAG_NO_BUFFERING = 536870912u;

		public const uint FILE_FLAG_RANDOM_ACCESS = 268435456u;

		public const uint FILE_FLAG_SEQUENTIAL_SCAN = 134217728u;

		public const uint FILE_FLAG_DELETE_ON_CLOSE = 67108864u;

		public const uint FILE_FLAG_BACKUP_SEMANTICS = 33554432u;

		public const uint FILE_FLAG_POSIX_SEMANTICS = 16777216u;

		public const uint FILE_FLAG_OPEN_REPARSE_POINT = 2097152u;

		public const uint FILE_FLAG_OPEN_NO_RECALL = 1048576u;

		public const uint FILE_FLAG_FIRST_PIPE_INSTANCE = 524288u;

		public const uint FILE_ATTRIBUTE_NORMAL = 128u;
	}

	public struct HIDP_CAPS
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

	public struct HIDD_ATTRIBUTES
	{
		public int Size;

		public ushort VendorID;

		public ushort ProductID;

		public short VersionNumber;
	}

	public struct OVERLAPPED
	{
		public int Internal;

		public int InternalHigh;

		public int Offset;

		public int OffsetHigh;

		public IntPtr hEvent;
	}

	public struct SECURITY_ATTRIBUTES
	{
		public int nLength;

		public IntPtr lpSecurityDescriptor;

		public bool bInheritHandle;
	}

	private static Guid guidHID = Guid.Empty;

	public const int Infinite = -1;

	[DllImport("setupapi.dll", SetLastError = true)]
	private static extern IntPtr SetupDiCreateDeviceInfoList(ref Guid ClassGuid, IntPtr hwndParent);

	[DllImport("setupapi.dll", SetLastError = true)]
	private static extern IntPtr SetupDiCreateDeviceInfoList(IntPtr ClassGuid, IntPtr hwndParent);

	[DllImport("setupapi.dll", CharSet = CharSet.Auto)]
	private static extern IntPtr SetupDiGetClassDevsEx(ref Guid ClassGuid, [MarshalAs(UnmanagedType.LPTStr)] string Enumerator, IntPtr hwndParent, DIGCF Flags, IntPtr DeviceInfoSet, [MarshalAs(UnmanagedType.LPTStr)] string MachineName, IntPtr Reserved);

	[DllImport("setupapi.dll", CharSet = CharSet.Auto)]
	private static extern IntPtr SetupDiGetClassDevsEx(IntPtr ClassGuid, [MarshalAs(UnmanagedType.LPTStr)] string Enumerator, IntPtr hwndParent, DIGCF Flags, IntPtr DeviceInfoSet, [MarshalAs(UnmanagedType.LPTStr)] string MachineName, IntPtr Reserved);

	[DllImport("setupapi.dll", CharSet = CharSet.Auto, SetLastError = true)]
	private static extern bool SetupDiEnumDeviceInterfaces(IntPtr hDevInfo, ref SP_DEVINFO_DATA devInfo, ref Guid interfaceClassGuid, uint memberIndex, ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData);

	[DllImport("setupapi.dll", CharSet = CharSet.Auto, SetLastError = true)]
	private static extern bool SetupDiEnumDeviceInterfaces(IntPtr hDevInfo, [MarshalAs(UnmanagedType.AsAny)] object devInfo, ref Guid interfaceClassGuid, uint memberIndex, ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData);

	[DllImport("setupapi.dll", SetLastError = true)]
	private static extern bool SetupDiDestroyDeviceInfoList(IntPtr DeviceInfoSet);

	[DllImport("setupapi.dll", SetLastError = true)]
	private static extern bool SetupDiEnumDeviceInfo(IntPtr DeviceInfoSet, uint MemberIndex, ref SP_DEVINFO_DATA DeviceInfoData);

	[DllImport("setupapi.dll", CharSet = CharSet.Auto, SetLastError = true)]
	private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr hDevInfo, ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData, ref SP_DEVICE_INTERFACE_DETAIL_DATA deviceInterfaceDetailData, int deviceInterfaceDetailDataSize, out int requiredSize, ref SP_DEVINFO_DATA deviceInfoData);

	[DllImport("setupapi.dll", CharSet = CharSet.Auto, SetLastError = true)]
	private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr hDevInfo, ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData, ref SP_DEVICE_INTERFACE_DETAIL_DATA deviceInterfaceDetailData, int deviceInterfaceDetailDataSize, out int requiredSize, [MarshalAs(UnmanagedType.AsAny)] object deviceInfoData);

	[DllImport("setupapi.dll", CharSet = CharSet.Auto, SetLastError = true)]
	private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr hDevInfo, ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData, ref SP_DEVICE_INTERFACE_DETAIL_DATA deviceInterfaceDetailData, int deviceInterfaceDetailDataSize, IntPtr requiredSize, [MarshalAs(UnmanagedType.AsAny)] object deviceInfoData);

	[DllImport("hid.dll")]
	public static extern void HidD_GetHidGuid(ref Guid HidGuid);

	[DllImport("kernel32.dll", SetLastError = true)]
	protected static extern IntPtr CreateFile([MarshalAs(UnmanagedType.LPStr)] string strName, uint nAccess, uint nShareMode, IntPtr lpSecurity, uint nCreationFlags, uint nAttributes, IntPtr lpTemplate);

	[DllImport("hid.dll")]
	private static extern bool HidD_GetSerialNumberString(IntPtr hidDeviceObject, IntPtr buffer, int bufferLength);

	[DllImport("hid.dll", SetLastError = true)]
	private static extern int HidP_GetCaps(int pPHIDP_PREPARSED_DATA, ref HIDP_CAPS myPHIDP_CAPS);

	[DllImport("hid.dll", SetLastError = true)]
	private static extern int HidD_GetPreparsedData(IntPtr hObject, ref int pPHIDP_PREPARSED_DATA);

	[DllImport("hid.dll", SetLastError = true)]
	private static extern int HidD_FreePreparsedData(int HidParsedData);

	[DllImport("hid.dll", SetLastError = true)]
	private static extern int HidD_GetAttributes(IntPtr HidDevHandle, ref HIDD_ATTRIBUTES HIDAttrib);

	[DllImport("hid.dll", SetLastError = true)]
	public static extern bool HidD_SetOutputReport(IntPtr HidDeviceObject, byte[] lpReportBuffer, int ReportBufferLength);

	[DllImport("hid.dll", SetLastError = true)]
	public static extern bool HidD_SetFeature(IntPtr HidDeviceObject, byte[] lpReportBuffer, int ReportBufferLength);

	[DllImport("hid.dll", SetLastError = true)]
	public static extern bool HidD_GetFeature(IntPtr HidDeviceObject, byte[] lpReportBuffer, int ReportBufferLength);

	[DllImport("kernel32.dll", SetLastError = true)]
	public static extern bool WriteFile(int hFile, byte[] lpBuffer, int nNumberOfBytesToWrite, ref int lpNumberOfBytesWritten, int lpOverlapped);

	[DllImport("kernel32.dll", SetLastError = true)]
	public static extern bool WriteFile(int hFile, byte[] lpBuffer, int nNumberOfBytesToWrite, ref int lpNumberOfBytesWritten, ref OVERLAPPED lpOverlapped);

	[DllImport("kernel32.dll", SetLastError = true)]
	public static extern bool WriteFile(int hFile, byte[] lpBuffer, int nNumberOfBytesToWrite, int lpNumberOfBytesWritten, ref OVERLAPPED lpOverlapped);

	[DllImport("kernel32.dll", SetLastError = true)]
	public static extern bool ReadFile(int hFile, byte[] lpBuffer, int nNumberOfBytesToRead, ref int lpNumberOfBytesRead, ref OVERLAPPED lpOverlapped);

	[DllImport("Kernel32.dll", SetLastError = true)]
	public static extern int GetLastError();

	[DllImport("Kernel32.dll", SetLastError = true)]
	public static extern int SetLastError(int success);

	[DllImport("Kernel32.dll")]
	public static extern int FormatMessage(int flag, ref IntPtr source, int msgid, int langid, ref string buf, int size, ref IntPtr args);

	public static string GetSysErrMsg(int errCode)
	{
		IntPtr source = IntPtr.Zero;
		string buf = null;
		FormatMessage(4864, ref source, errCode, 0, ref buf, 255, ref source);
		return buf;
	}

	[DllImport("kernel32.dll")]
	public static extern int CloseHandle(int hObject);

	[DllImport("kernel32.dll")]
	public static extern int FindClose(int hObject);

	[DllImport("kernel32", CharSet = CharSet.Ansi, ExactSpelling = true, SetLastError = true)]
	public static extern int WaitForSingleObject(IntPtr hHandle, int dwMilliseconds);

	[DllImport("Kernel32.dll", SetLastError = true)]
	public static extern IntPtr CreateEvent(IntPtr lpEventAttributes, bool bManualReset, bool bInitialState, string lpName);

	[DllImport("kernel32.dll", SetLastError = true)]
	public static extern bool GetOverlappedResult(int handle, ref OVERLAPPED lpOverLapped, ref int readNumber, bool b);

	[DllImport("kernel32.dll", SetLastError = true)]
	public static extern bool ResetEvent(IntPtr handle);

	[DllImport("kernel32.dll", SetLastError = true)]
	public static extern bool SetEvent(IntPtr handle);

	public static string[] GetDevicePath(Guid setupClassGuid, Guid interfaceClassGuid, string Enumerator = null)
	{
		if (interfaceClassGuid == Guid.Empty)
		{
			return null;
		}
		IntPtr intPtr = ((!(setupClassGuid == Guid.Empty)) ? SetupDiCreateDeviceInfoList(ref setupClassGuid, IntPtr.Zero) : SetupDiCreateDeviceInfoList(IntPtr.Zero, IntPtr.Zero));
		if (intPtr == new IntPtr(-1))
		{
			return null;
		}
		IntPtr intPtr2 = SetupDiGetClassDevsEx(ref interfaceClassGuid, Enumerator, IntPtr.Zero, DIGCF.DIGCF_PRESENT | DIGCF.DIGCF_DEVICEINTERFACE, intPtr, null, IntPtr.Zero);
		if (intPtr2 == new IntPtr(-1))
		{
			return null;
		}
		List<string> list = new List<string>();
		uint num = 0u;
		SP_DEVICE_INTERFACE_DATA deviceInterfaceData = SP_DEVICE_INTERFACE_DATA.Empty;
		while (SetupDiEnumDeviceInterfaces(intPtr2, null, ref interfaceClassGuid, num++, ref deviceInterfaceData))
		{
			SP_DEVICE_INTERFACE_DETAIL_DATA deviceInterfaceDetailData = new SP_DEVICE_INTERFACE_DETAIL_DATA
			{
				cbSize = ((IntPtr.Size == 4) ? ((uint)(4 + Marshal.SystemDefaultCharSize)) : 8u)
			};
			if (SetupDiGetDeviceInterfaceDetail(intPtr2, ref deviceInterfaceData, ref deviceInterfaceDetailData, Marshal.SizeOf((object)deviceInterfaceDetailData), IntPtr.Zero, null))
			{
				list.Add(deviceInterfaceDetailData.DevicePath);
			}
		}
		SetupDiDestroyDeviceInfoList(intPtr2);
		if (list.Count == 0)
		{
			return null;
		}
		return list.ToArray();
	}

	public static string[] GetDevicePath(Guid interfaceClassGuid, string Enumerator = null)
	{
		return GetDevicePath(Guid.Empty, interfaceClassGuid, Enumerator);
	}

	public static bool findDevice(string vid, string pid, string inf, ref int hReadHandle, ref int hWriteHandle, ref int InputReportByteLength, ref int OutputReportByteLength, ref int FeatureReportByteLength, ref string hidp_caps, ref string allDevPath, ref string allHandleDevPath, ref string searchHandleDevPath)
	{
		bool result = false;
		HidD_GetHidGuid(ref guidHID);
		HIDD_ATTRIBUTES HIDAttrib = default(HIDD_ATTRIBUTES);
		string[] devicePath = GetDevicePath(guidHID);
		allHandleDevPath = "";
		allDevPath = "";
		if (devicePath == null)
		{
			return false;
		}
		string[] array = devicePath;
		foreach (string text in array)
		{
			IntPtr intPtr = CreateFile(text, 3221225472u, 3u, (IntPtr)0, 3u, 1073741824u, (IntPtr)0);
			IntPtr intPtr2 = new IntPtr(-1);
			if (intPtr != intPtr2)
			{
				allHandleDevPath = allHandleDevPath + text + "\r\n\r\n";
				IntPtr intPtr3 = Marshal.AllocHGlobal(512);
				HidD_GetAttributes(intPtr, ref HIDAttrib);
				HidD_GetSerialNumberString(intPtr, intPtr3, 512);
				Marshal.PtrToStringAuto(intPtr3);
				Marshal.FreeHGlobal(intPtr3);
				if (text.ToUpper().IndexOf("VID_" + vid) >= 0 && text.ToUpper().IndexOf("PID_" + pid) >= 0 && (inf == "" || text.ToUpper().IndexOf(inf) >= 0))
				{
					int pPHIDP_PREPARSED_DATA = 0;
					HIDP_CAPS myPHIDP_CAPS = default(HIDP_CAPS);
					HidD_GetPreparsedData(intPtr, ref pPHIDP_PREPARSED_DATA);
					HidP_GetCaps(pPHIDP_PREPARSED_DATA, ref myPHIDP_CAPS);
					HIDAttrib.Size = Marshal.SizeOf((object)HIDAttrib);
					hReadHandle = (int)intPtr;
					hWriteHandle = (int)intPtr;
					searchHandleDevPath = text;
					result = true;
					int pPHIDP_PREPARSED_DATA2 = -1;
					HidD_GetPreparsedData(intPtr, ref pPHIDP_PREPARSED_DATA2);
					HIDP_CAPS myPHIDP_CAPS2 = default(HIDP_CAPS);
					HidP_GetCaps(pPHIDP_PREPARSED_DATA2, ref myPHIDP_CAPS2);
					InputReportByteLength = myPHIDP_CAPS2.InputReportByteLength;
					OutputReportByteLength = myPHIDP_CAPS2.OutputReportByteLength;
					FeatureReportByteLength = myPHIDP_CAPS2.FeatureReportByteLength;
					hidp_caps = "UsagePage  " + myPHIDP_CAPS2.UsagePage.ToString("X2") + "\r\nUsage  " + myPHIDP_CAPS2.Usage.ToString("X2") + "\r\n";
					return result;
				}
			}
		}
		return result;
	}

	public static bool findDevice(ushort vid, ushort pid, ushort usagePage, ushort usage, ref int hReadHandle, ref int hWriteHandle, ref int InputReportByteLength, ref int OutputReportByteLength, ref int FeatureReportByteLength, ref string hidp_caps, ref string allDevPath, ref string allHandleDevPath, ref string searchHandleDevPath)
	{
		bool result = false;
		HidD_GetHidGuid(ref guidHID);
		HIDD_ATTRIBUTES HIDAttrib = default(HIDD_ATTRIBUTES);
		string[] devicePath = GetDevicePath(guidHID);
		allHandleDevPath = "";
		allDevPath = "";
		if (devicePath == null)
		{
			return false;
		}
		string[] array = devicePath;
		foreach (string text in array)
		{
			IntPtr intPtr = CreateFile(text, 3221225472u, 3u, (IntPtr)0, 3u, 1073741824u, (IntPtr)0);
			IntPtr intPtr2 = new IntPtr(-1);
			if (!(intPtr != intPtr2))
			{
				continue;
			}
			allHandleDevPath = allHandleDevPath + text + "\r\n\r\n";
			IntPtr intPtr3 = Marshal.AllocHGlobal(512);
			HidD_GetAttributes(intPtr, ref HIDAttrib);
			HidD_GetSerialNumberString(intPtr, intPtr3, 512);
			Marshal.PtrToStringAuto(intPtr3);
			Marshal.FreeHGlobal(intPtr3);
			if (HIDAttrib.VendorID == vid && HIDAttrib.ProductID == pid)
			{
				int pPHIDP_PREPARSED_DATA = 0;
				HIDP_CAPS myPHIDP_CAPS = default(HIDP_CAPS);
				HidD_GetPreparsedData(intPtr, ref pPHIDP_PREPARSED_DATA);
				HidP_GetCaps(pPHIDP_PREPARSED_DATA, ref myPHIDP_CAPS);
				HIDAttrib.Size = Marshal.SizeOf((object)HIDAttrib);
				if (myPHIDP_CAPS.UsagePage == usagePage)
				{
					hReadHandle = (int)intPtr;
					hWriteHandle = (int)intPtr;
					searchHandleDevPath = text;
					result = true;
					InputReportByteLength = myPHIDP_CAPS.InputReportByteLength;
					OutputReportByteLength = myPHIDP_CAPS.OutputReportByteLength;
					FeatureReportByteLength = myPHIDP_CAPS.FeatureReportByteLength;
					hidp_caps = "UsagePage  " + myPHIDP_CAPS.UsagePage.ToString("X2") + "\r\nUsage  " + myPHIDP_CAPS.Usage.ToString("X2") + "\r\n";
					return result;
				}
			}
		}
		return result;
	}

	public static bool findDevice1111(string vid, string pid, string inf, ref int hReadHandle, ref int hWriteHandle, ref int InputReportByteLength, ref int OutputReportByteLength, ref int FeatureReportByteLength, ref string hidp_caps, ref string allDevPath, ref string allHandleDevPath, ref string searchHandleDevPath)
	{
		bool result = false;
		HidD_GetHidGuid(ref guidHID);
		HIDD_ATTRIBUTES HIDAttrib = default(HIDD_ATTRIBUTES);
		string[] devicePath = GetDevicePath(guidHID);
		allHandleDevPath = "";
		allDevPath = "";
		string[] array = devicePath;
		foreach (string text in array)
		{
			allDevPath = allDevPath + text + "\r\n\r\n";
		}
		if (devicePath == null)
		{
			return false;
		}
		array = devicePath;
		foreach (string text2 in array)
		{
			IntPtr intPtr = CreateFile(text2, 0u, 3u, (IntPtr)0, 3u, 128u, (IntPtr)0);
			IntPtr intPtr2 = new IntPtr(-1);
			if (intPtr != intPtr2)
			{
				allHandleDevPath = allHandleDevPath + text2 + "\r\n\r\n";
				int pPHIDP_PREPARSED_DATA = 0;
				HIDP_CAPS myPHIDP_CAPS = default(HIDP_CAPS);
				HidD_GetPreparsedData(intPtr, ref pPHIDP_PREPARSED_DATA);
				HidP_GetCaps(pPHIDP_PREPARSED_DATA, ref myPHIDP_CAPS);
				HIDAttrib.Size = Marshal.SizeOf((object)HIDAttrib);
				HidD_GetAttributes(intPtr, ref HIDAttrib);
				if (text2.ToUpper().IndexOf("VID_" + vid) >= 0 && text2.ToUpper().IndexOf("PID_" + pid) >= 0 && (inf == "" || text2.ToUpper().IndexOf(inf) >= 0))
				{
					hReadHandle = (int)CreateFile(text2, 2147483648u, 3u, (IntPtr)0, 3u, 1073741952u, (IntPtr)0);
					hWriteHandle = (int)CreateFile(text2, 1073741824u, 3u, (IntPtr)0, 3u, 1073741952u, (IntPtr)0);
					searchHandleDevPath = text2;
					result = true;
					int pPHIDP_PREPARSED_DATA2 = -1;
					HidD_GetPreparsedData(intPtr, ref pPHIDP_PREPARSED_DATA2);
					HIDP_CAPS myPHIDP_CAPS2 = default(HIDP_CAPS);
					HidP_GetCaps(pPHIDP_PREPARSED_DATA2, ref myPHIDP_CAPS2);
					InputReportByteLength = myPHIDP_CAPS2.InputReportByteLength;
					OutputReportByteLength = myPHIDP_CAPS2.OutputReportByteLength;
					FeatureReportByteLength = myPHIDP_CAPS2.FeatureReportByteLength;
					hidp_caps = "UsagePage  " + myPHIDP_CAPS2.UsagePage.ToString("X2") + "\r\nUsage  " + myPHIDP_CAPS2.Usage.ToString("X2") + "\r\n";
					return result;
				}
			}
		}
		return result;
	}

	public static bool sendFeature(int HidDevHandle, byte[] reportData, int FeatureReportByteLength)
	{
		return HidD_SetFeature(new IntPtr(HidDevHandle), reportData, FeatureReportByteLength);
	}

	[DllImport("setupapi.dll", SetLastError = true)]
	private static extern IntPtr SetupDiGetClassDevs(ref Guid ClassGuid, uint Enumerator, IntPtr HwndParent, DIGCF Flags);
}
