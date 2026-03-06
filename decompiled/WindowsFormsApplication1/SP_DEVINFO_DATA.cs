using System;
using System.Runtime.InteropServices;

namespace WindowsFormsApplication1;

[StructLayout(LayoutKind.Sequential)]
public class SP_DEVINFO_DATA
{
	public int cbSize = Marshal.SizeOf(typeof(SP_DEVINFO_DATA));

	public Guid classGuid = Guid.Empty;

	public int devInst;

	public int reserved;
}
