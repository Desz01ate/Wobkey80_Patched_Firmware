using System;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace WindowsFormsApplication1;

internal static class Program
{
	private const int WS_HIDE = 0;

	private const int WS_SHOWNORMAL = 1;

	private const int WS_SHOWMIN = 2;

	private const int WS_SHOWMAX = 3;

	[DllImport("User32.dll")]
	private static extern bool ShowWindowAsync(IntPtr hWnd, int cmdShow);

	[DllImport("User32.dll")]
	private static extern bool SetForegroundWindow(IntPtr hWnd);

	[STAThread]
	private static void Main(string[] args)
	{
		//IL_002a: Unknown result type (might be due to invalid IL or missing references)
		Process runningInstance = GetRunningInstance();
		if (runningInstance == null)
		{
			Application.EnableVisualStyles();
			Application.SetCompatibleTextRenderingDefault(false);
			Application.Run((Form)(object)new usb_Form());
		}
		else
		{
			HandleRunningInstance(runningInstance);
			MessageBox.Show("已经打开，请查看托盘");
		}
	}

	public static Process GetRunningInstance()
	{
		Process currentProcess = Process.GetCurrentProcess();
		Process[] processesByName = Process.GetProcessesByName(currentProcess.ProcessName);
		foreach (Process process in processesByName)
		{
			if (process.Id != currentProcess.Id && Assembly.GetExecutingAssembly().Location.Replace("/", "\\") == currentProcess.MainModule.FileName)
			{
				return process;
			}
		}
		return null;
	}

	public static void HandleRunningInstance(Process instance)
	{
		ShowWindowAsync(instance.MainWindowHandle, 3);
		SetForegroundWindow(instance.MainWindowHandle);
	}
}
