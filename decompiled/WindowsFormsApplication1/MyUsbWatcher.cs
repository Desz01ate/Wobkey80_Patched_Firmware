using System;
using System.Management;
using System.Threading;
using System.Windows.Forms;

namespace WindowsFormsApplication1;

internal class MyUsbWatcher
{
	private ManagementEventWatcher insertWatcher;

	private ManagementEventWatcher removeWatcher;

	public bool AddUSBEventWatcher(EventArrivedEventHandler usbInsertHandler, EventArrivedEventHandler usbRemoveHandler, TimeSpan withinInterval)
	{
		//IL_00a1: Unknown result type (might be due to invalid IL or missing references)
		//IL_0010: Unknown result type (might be due to invalid IL or missing references)
		//IL_0016: Expected O, but got Unknown
		//IL_0030: Unknown result type (might be due to invalid IL or missing references)
		//IL_0036: Expected O, but got Unknown
		//IL_0039: Unknown result type (might be due to invalid IL or missing references)
		//IL_0043: Expected O, but got Unknown
		//IL_0068: Unknown result type (might be due to invalid IL or missing references)
		//IL_006e: Expected O, but got Unknown
		//IL_0071: Unknown result type (might be due to invalid IL or missing references)
		//IL_007b: Expected O, but got Unknown
		_ = Thread.CurrentThread.ManagedThreadId;
		try
		{
			ManagementScope val = new ManagementScope("root\\CIMV2");
			val.Options.EnablePrivileges = true;
			if (usbInsertHandler != null)
			{
				WqlEventQuery val2 = new WqlEventQuery("__InstanceCreationEvent", withinInterval, "TargetInstance isa 'Win32_USBControllerDevice'");
				insertWatcher = new ManagementEventWatcher(val, (EventQuery)(object)val2);
				insertWatcher.EventArrived += usbInsertHandler;
				insertWatcher.Start();
			}
			if (usbRemoveHandler != null)
			{
				WqlEventQuery val3 = new WqlEventQuery("__InstanceDeletionEvent", withinInterval, "TargetInstance isa 'Win32_USBControllerDevice'");
				removeWatcher = new ManagementEventWatcher(val, (EventQuery)(object)val3);
				removeWatcher.EventArrived += usbRemoveHandler;
				removeWatcher.Start();
			}
			return true;
		}
		catch (Exception ex)
		{
			RemoveUSBEventWatcher();
			MessageBox.Show(ex.ToString());
			return false;
		}
	}

	public void RemoveUSBEventWatcher()
	{
		if (insertWatcher != null)
		{
			insertWatcher.Stop();
			insertWatcher = null;
		}
		if (removeWatcher != null)
		{
			removeWatcher.Stop();
			removeWatcher = null;
		}
	}

	public static USBControllerDevice[] WhoUSBControllerDevice(EventArrivedEventArgs e)
	{
		object obj = e.NewEvent["TargetInstance"];
		ManagementBaseObject val = (ManagementBaseObject)((obj is ManagementBaseObject) ? obj : null);
		if (val != null && val.ClassPath.ClassName == "Win32_USBControllerDevice")
		{
			string antecedent = (val["Antecedent"] as string).Replace("\"", string.Empty).Split(new char[1] { '=' })[1];
			string dependent = (val["Dependent"] as string).Replace("\"", string.Empty).Split(new char[1] { '=' })[1];
			return new USBControllerDevice[1]
			{
				new USBControllerDevice
				{
					Antecedent = antecedent,
					Dependent = dependent
				}
			};
		}
		return null;
	}
}
