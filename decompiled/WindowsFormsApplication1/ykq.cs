using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Data.Common;
using System.Data.SqlClient;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Ports;
using System.Management;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;
using System.Windows.Forms.Layout;

namespace WindowsFormsApplication1;

internal static class ykq
{
	public static SerialPort uart = new SerialPort();

	public static bool Debug_enable = true;

	public static string AESkey;

	private static string connStr = "server=192.168.1.60;database=AIS20141219145250;uid=sql;pwd=iton1234;";

	private const int EM_SETCUEBANNER = 5377;

	private static float X;

	private static float Y;

	public static void debug_output(string failReason)
	{
		//IL_001b: Unknown result type (might be due to invalid IL or missing references)
		if (Debug_enable)
		{
			MessageBox.Show("您设置的参数错误,或者您的操作有误，请检查，失败原因如下:\r\n" + failReason, "错误警告", (MessageBoxButtons)0, (MessageBoxIcon)16);
		}
	}

	public static string[] readFile(string FilePath)
	{
		try
		{
			if (File.Exists(FilePath))
			{
				return File.ReadAllLines(FilePath, Encoding.Default);
			}
			return null;
		}
		catch (Exception ex)
		{
			debug_output(ex.ToString());
			return null;
		}
	}

	public static string openFile(string FilePath)
	{
		try
		{
			if (File.Exists(FilePath))
			{
				File.OpenRead(FilePath);
				return "ok";
			}
			return null;
		}
		catch (Exception ex)
		{
			debug_output(ex.ToString());
			return null;
		}
	}

	public static bool writeFile(string FilePath, string write_str)
	{
		try
		{
			if (File.Exists(FilePath))
			{
				File.Delete(FilePath);
			}
			FileStream fileStream = new FileStream(FilePath, FileMode.CreateNew, FileAccess.Write);
			StreamWriter streamWriter = new StreamWriter(fileStream, Encoding.Default);
			streamWriter.Write(write_str);
			streamWriter.Close();
			fileStream.Close();
			return true;
		}
		catch
		{
			return false;
		}
	}

	public static bool ReadBinFile(string FilePathAndName, ref byte[] backBytes, ref uint bin_size)
	{
		//IL_0060: Unknown result type (might be due to invalid IL or missing references)
		//IL_0042: Unknown result type (might be due to invalid IL or missing references)
		try
		{
			bin_size = 0u;
			if (File.Exists(FilePathAndName))
			{
				FileStream fileStream = new FileStream(FilePathAndName, FileMode.Open, FileAccess.Read);
				BinaryReader binaryReader = new BinaryReader(fileStream);
				backBytes = binaryReader.ReadBytes((int)(bin_size = (uint)fileStream.Length));
				binaryReader.Close();
				fileStream.Close();
				return true;
			}
			MessageBox.Show("文件路径不对或者文件不存在");
			return false;
		}
		catch (Exception ex)
		{
			bin_size = 0u;
			MessageBox.Show("读文件失败！原因：" + ex.ToString());
			return false;
		}
	}

	public static string WriteBinFlie(string FilePathAndName, byte[] srcBytes, int len)
	{
		FileStream fileStream = null;
		BinaryWriter binaryWriter = null;
		try
		{
			if (File.Exists(FilePathAndName))
			{
				File.Delete(FilePathAndName);
			}
			fileStream = new FileStream(FilePathAndName, FileMode.Create, FileAccess.Write);
			binaryWriter = new BinaryWriter(fileStream);
			for (uint num = 0u; num < len; num++)
			{
				binaryWriter.Write(srcBytes[num]);
			}
			binaryWriter.Close();
			fileStream.Close();
			binaryWriter = null;
			fileStream = null;
			return null;
		}
		catch (Exception ex)
		{
			return ex.Message + "\r\n在写入文件的过程中，发生了异常！";
		}
	}

	private static string getMacAddrLocal()
	{
		//IL_0007: Unknown result type (might be due to invalid IL or missing references)
		//IL_001f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0025: Expected O, but got Unknown
		string text = null;
		ManagementObjectEnumerator enumerator = new ManagementClass("Win32_NetworkAdapterConfiguration").GetInstances().GetEnumerator();
		try
		{
			while (enumerator.MoveNext())
			{
				ManagementObject val = (ManagementObject)enumerator.Current;
				if ((bool)((ManagementBaseObject)val)["IPEnabled"])
				{
					text += ((ManagementBaseObject)val)["MacAddress"].ToString();
					break;
				}
			}
		}
		finally
		{
			((IDisposable)enumerator)?.Dispose();
		}
		return text;
	}

	private static string getCpuID()
	{
		//IL_0007: Unknown result type (might be due to invalid IL or missing references)
		//IL_001f: Unknown result type (might be due to invalid IL or missing references)
		try
		{
			string result = null;
			ManagementObjectEnumerator enumerator = new ManagementClass("Win32_Processor").GetInstances().GetEnumerator();
			try
			{
				while (enumerator.MoveNext())
				{
					result = ((ManagementBaseObject)(ManagementObject)enumerator.Current).Properties["ProcessorId"].Value.ToString();
				}
			}
			finally
			{
				((IDisposable)enumerator)?.Dispose();
			}
			return result;
		}
		catch
		{
			return null;
		}
	}

	private static string getDiskID()
	{
		//IL_000b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0023: Unknown result type (might be due to invalid IL or missing references)
		try
		{
			string result = "";
			ManagementObjectEnumerator enumerator = new ManagementClass("Win32_DiskDrive").GetInstances().GetEnumerator();
			try
			{
				while (enumerator.MoveNext())
				{
					result = ((ManagementBaseObject)(ManagementObject)enumerator.Current).Properties["Model"].Value.ToString();
				}
			}
			finally
			{
				((IDisposable)enumerator)?.Dispose();
			}
			return result;
		}
		catch
		{
			return "unkown";
		}
	}

	private static string getIpAddress()
	{
		//IL_000b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0023: Unknown result type (might be due to invalid IL or missing references)
		//IL_0029: Expected O, but got Unknown
		try
		{
			string result = "";
			ManagementObjectEnumerator enumerator = new ManagementClass("Win32_NetworkAdapterConfiguration").GetInstances().GetEnumerator();
			try
			{
				while (enumerator.MoveNext())
				{
					ManagementObject val = (ManagementObject)enumerator.Current;
					if (Convert.ToBoolean(((ManagementBaseObject)val)["IPEnabled"]))
					{
						result = ((Array)((ManagementBaseObject)val).Properties["IpAddress"].Value).GetValue(0).ToString();
						break;
					}
				}
			}
			finally
			{
				((IDisposable)enumerator)?.Dispose();
			}
			return result;
		}
		catch
		{
			return "unkown";
		}
	}

	private static string getUserName()
	{
		try
		{
			return Environment.UserName;
		}
		catch
		{
			return "unkown";
		}
	}

	private static string getComputerName()
	{
		try
		{
			return Environment.MachineName;
		}
		catch
		{
			return "unkown";
		}
	}

	private static string getSystemType()
	{
		//IL_000b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0023: Unknown result type (might be due to invalid IL or missing references)
		try
		{
			string result = "";
			ManagementObjectEnumerator enumerator = new ManagementClass("Win32_ComputerSystem").GetInstances().GetEnumerator();
			try
			{
				while (enumerator.MoveNext())
				{
					result = ((ManagementBaseObject)(ManagementObject)enumerator.Current)["SystemType"].ToString();
				}
			}
			finally
			{
				((IDisposable)enumerator)?.Dispose();
			}
			return result;
		}
		catch
		{
			return "unkown";
		}
	}

	private static string getTotalPhysicalMemory()
	{
		//IL_000b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0023: Unknown result type (might be due to invalid IL or missing references)
		try
		{
			string result = "";
			ManagementObjectEnumerator enumerator = new ManagementClass("Win32_ComputerSystem").GetInstances().GetEnumerator();
			try
			{
				while (enumerator.MoveNext())
				{
					result = ((ManagementBaseObject)(ManagementObject)enumerator.Current)["TotalPhysicalMemory"].ToString();
				}
			}
			finally
			{
				((IDisposable)enumerator)?.Dispose();
			}
			return result;
		}
		catch
		{
			return "unkown";
		}
	}

	public static List<string> getIPconfig()
	{
		List<string> list = new List<string>();
		Process process = Process.Start(new ProcessStartInfo("ipconfig", "/all")
		{
			CreateNoWindow = true,
			UseShellExecute = false,
			RedirectStandardInput = true,
			RedirectStandardOutput = true,
			RedirectStandardError = true
		});
		StreamReader standardOutput = process.StandardOutput;
		string text = standardOutput.ReadLine();
		while (!standardOutput.EndOfStream)
		{
			if (!string.IsNullOrEmpty(text))
			{
				text = text.Trim();
				list.Add(text);
			}
			text = standardOutput.ReadLine();
		}
		process.WaitForExit();
		process.Close();
		standardOutput.Close();
		return list;
	}

	private static string getIPconfig1()
	{
		new List<string>();
		Process? process = Process.Start(new ProcessStartInfo("ipconfig", "/all")
		{
			CreateNoWindow = true,
			UseShellExecute = false,
			RedirectStandardInput = true,
			RedirectStandardOutput = true,
			RedirectStandardError = true
		});
		string result = process.StandardOutput.ReadToEnd();
		process.WaitForExit();
		process.Close();
		return result;
	}

	public static string getPcInformation()
	{
		string text = "";
		List<string> list = new List<string>();
		list = getIPconfig();
		for (int i = 0; i < list.Count; i++)
		{
			text = text + i + "->" + list[i] + "\r\n";
		}
		return text;
	}

	public static string md5_64_jiami(string str)
	{
		MD5 mD = MD5.Create();
		byte[] buffer = mD.ComputeHash(Encoding.UTF8.GetBytes(str));
		byte[] array = mD.ComputeHash(buffer);
		StringBuilder stringBuilder = new StringBuilder();
		for (int i = 0; i < array.Length; i++)
		{
			stringBuilder.Append(array[i].ToString("x2"));
		}
		return stringBuilder.ToString();
	}

	public static string getComputeMainInformation()
	{
		List<string> list = new List<string>();
		byte b = 0;
		string text = null;
		list = getIPconfig();
		for (int i = 0; i < list.Count; i++)
		{
			if (list[i].IndexOf("主机名") >= 0 && b == 0)
			{
				b = 1;
				text += list[i];
			}
			else if (list[i].IndexOf("以太网适配器") >= 0 && b == 1)
			{
				b = 2;
				text += list[i];
			}
			else if (list[i].IndexOf("物理地址") >= 0 && b == 2)
			{
				b = 3;
				text += list[i];
			}
			if (b == 3)
			{
				break;
			}
		}
		return text;
	}

	public static string get_miyao()
	{
		try
		{
			return md5_64_jiami(getComputeMainInformation() + AESkey);
		}
		catch (Exception ex)
		{
			debug_output(ex.ToString());
			return null;
		}
	}

	public static bool readConfig(ref string[] result, string FileName)
	{
		//IL_00f9: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d9: Unknown result type (might be due to invalid IL or missing references)
		//IL_0085: Unknown result type (might be due to invalid IL or missing references)
		//IL_005c: Unknown result type (might be due to invalid IL or missing references)
		try
		{
			string path = Environment.CurrentDirectory + "\\" + FileName;
			if (File.Exists(path))
			{
				string[] array = File.ReadAllLines(path, Encoding.Default);
				int num = 2;
				bool flag = false;
				for (int i = 0; i < array.Length; i++)
				{
					if (array[i].Substring(0, 4) == "配置结束")
					{
						num = i;
						flag = true;
						if (num >= 2)
						{
							break;
						}
						MessageBox.Show(FileName + "格式不对,行数小于3，请解压范本");
						return false;
					}
				}
				if (!flag)
				{
					MessageBox.Show(FileName + "格式不对,没有配置结束字符，请解压范本");
					return false;
				}
				result = new string[num];
				for (int j = 0; j < result.Length; j++)
				{
					result[j] = array[j].Substring(array[j].IndexOf("=") + 1);
				}
				return true;
			}
			MessageBox.Show(FileName + "配置文件不存在");
			return false;
		}
		catch (Exception ex)
		{
			debug_output(ex.ToString());
			MessageBox.Show(FileName + "2读取失败，格式不对，请解压范本");
			return false;
		}
	}

	public static bool openProcess_YKQ(string FileName, string FilePath, bool WaitForExit, Form frm)
	{
		//IL_0020: Unknown result type (might be due to invalid IL or missing references)
		try
		{
			string text = FilePath + "\\" + FileName;
			if (!File.Exists(text))
			{
				MessageBox.Show(FileName + "文件不存在");
				return false;
			}
			Process process = Process.Start(new ProcessStartInfo
			{
				FileName = text,
				Arguments = "",
				WindowStyle = ProcessWindowStyle.Normal
			});
			if (WaitForExit)
			{
				((Control)frm).Enabled = false;
				process.WaitForExit();
				((Control)frm).Enabled = true;
				((Control)frm).Focus();
			}
			return true;
		}
		catch (Exception ex)
		{
			debug_output(ex.ToString());
			return false;
		}
	}

	public static bool closeProcess_YKQ(string FileName)
	{
		//IL_0022: Unknown result type (might be due to invalid IL or missing references)
		Process[] processesByName = Process.GetProcessesByName(FileName);
		if (processesByName.Length != 0)
		{
			processesByName[0].Kill();
			processesByName[0].WaitForExit();
			return true;
		}
		MessageBox.Show("进程不存在");
		return false;
	}

	public static bool StartProcess_YKQ(string FileName, string FilePath, bool WaitForExit, Form frm)
	{
		//IL_0071: Unknown result type (might be due to invalid IL or missing references)
		//IL_0021: Unknown result type (might be due to invalid IL or missing references)
		//IL_0027: Invalid comparison between Unknown and I4
		//IL_0049: Unknown result type (might be due to invalid IL or missing references)
		try
		{
			if (Process.GetProcessesByName(Path.GetFileNameWithoutExtension(FileName)).Length != 0 && (int)MessageBox.Show(FileName + "已经打开了,你是否需要再打开一个应用程序", "确认信息", (MessageBoxButtons)4, (MessageBoxIcon)48) == 7)
			{
				return true;
			}
			if (!openProcess_YKQ(FileName, FilePath, WaitForExit, frm))
			{
				MessageBox.Show(FilePath + "\\" + FileName + " 文件不存在,请重新配置");
				return false;
			}
			return true;
		}
		catch (Exception ex)
		{
			debug_output(ex.ToString());
			MessageBox.Show("读取" + FileName + "文件错误");
			return false;
		}
	}

	public static int ExecuteNonQuery(string sql, params SqlParameter[] ps)
	{
		//IL_0005: Unknown result type (might be due to invalid IL or missing references)
		//IL_000b: Expected O, but got Unknown
		//IL_000d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0013: Expected O, but got Unknown
		SqlConnection val = new SqlConnection(connStr);
		try
		{
			SqlCommand val2 = new SqlCommand(sql, val);
			try
			{
				val2.Parameters.AddRange(ps);
				((DbConnection)(object)val).Open();
				return ((DbCommand)(object)val2).ExecuteNonQuery();
			}
			finally
			{
				((IDisposable)val2)?.Dispose();
			}
		}
		finally
		{
			((IDisposable)val)?.Dispose();
		}
	}

	public static object ExecuteScalar(string sql, params SqlParameter[] ps)
	{
		//IL_0005: Unknown result type (might be due to invalid IL or missing references)
		//IL_000b: Expected O, but got Unknown
		//IL_000d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0013: Expected O, but got Unknown
		SqlConnection val = new SqlConnection(connStr);
		try
		{
			SqlCommand val2 = new SqlCommand(sql, val);
			try
			{
				val2.Parameters.AddRange(ps);
				((DbConnection)(object)val).Open();
				return ((DbCommand)(object)val2).ExecuteScalar();
			}
			finally
			{
				((IDisposable)val2)?.Dispose();
			}
		}
		finally
		{
			((IDisposable)val)?.Dispose();
		}
	}

	public static SqlDataReader ExecuteReader(string sql, params SqlParameter[] ps)
	{
		//IL_0005: Unknown result type (might be due to invalid IL or missing references)
		//IL_000b: Expected O, but got Unknown
		//IL_000d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0013: Expected O, but got Unknown
		SqlConnection val = new SqlConnection(connStr);
		try
		{
			SqlCommand val2 = new SqlCommand(sql, val);
			try
			{
				val2.Parameters.AddRange(ps);
				((DbConnection)(object)val).Open();
				return val2.ExecuteReader(CommandBehavior.CloseConnection);
			}
			finally
			{
				((IDisposable)val2)?.Dispose();
			}
		}
		catch (Exception ex)
		{
			((Component)(object)val).Dispose();
			throw ex;
		}
	}

	public static DataSet SqlDataAdapter(string sql, params SqlParameter[] ps)
	{
		//IL_000c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0012: Expected O, but got Unknown
		try
		{
			DataSet dataSet = new DataSet();
			SqlDataAdapter val = new SqlDataAdapter(sql, connStr);
			try
			{
				val.SelectCommand.Parameters.AddRange(ps);
				((DataAdapter)(object)val).Fill(dataSet);
			}
			finally
			{
				((IDisposable)val)?.Dispose();
			}
			return dataSet;
		}
		catch (Exception ex)
		{
			debug_output(ex.ToString());
			return null;
		}
	}

	[DllImport("user32.dll", CharSet = CharSet.Auto)]
	private static extern int SendMessage(IntPtr hWnd, int msg, int wParam, [MarshalAs(UnmanagedType.LPWStr)] string lParam);

	public static void SetWatermark(this TextBox textBox, string watermark)
	{
		SendMessage(((Control)textBox).Handle, 5377, 0, watermark);
	}

	public static void ClearWatermark(this TextBox textBox)
	{
		SendMessage(((Control)textBox).Handle, 5377, 0, string.Empty);
	}

	private static void setTag_YKQ(Control cons)
	{
		//IL_0017: Unknown result type (might be due to invalid IL or missing references)
		//IL_001d: Expected O, but got Unknown
		foreach (Control item in (ArrangedElementCollection)cons.Controls)
		{
			Control val = item;
			val.Tag = val.Width + ":" + val.Height + ":" + val.Left + ":" + val.Top + ":" + val.Font.Size;
			if (((ArrangedElementCollection)val.Controls).Count > 0)
			{
				setTag_YKQ(val);
			}
		}
	}

	private static void setControls_YKQ(float newx, float newy, Control cons)
	{
		//IL_0017: Unknown result type (might be due to invalid IL or missing references)
		//IL_001d: Expected O, but got Unknown
		//IL_00a1: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ac: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b1: Unknown result type (might be due to invalid IL or missing references)
		//IL_00bb: Expected O, but got Unknown
		foreach (Control item in (ArrangedElementCollection)cons.Controls)
		{
			Control val = item;
			string[] array = val.Tag.ToString().Split(new char[1] { ':' });
			float num = Convert.ToSingle(array[0]) * newx;
			val.Width = (int)num;
			num = Convert.ToSingle(array[1]) * newy;
			val.Height = (int)num;
			num = Convert.ToSingle(array[2]) * newx;
			val.Left = (int)num;
			num = Convert.ToSingle(array[3]) * newy;
			val.Top = (int)num;
			float num2 = Convert.ToSingle(array[4]) * newy;
			val.Font = new Font(val.Font.Name, num2, val.Font.Style, val.Font.Unit);
			if (((ArrangedElementCollection)val.Controls).Count > 0)
			{
				setControls_YKQ(newx, newy, val);
			}
		}
	}

	public static bool frm_load_YKQ(Form frm)
	{
		try
		{
			X = ((Control)frm).Width;
			Y = ((Control)frm).Height;
			setTag_YKQ((Control)(object)frm);
			return true;
		}
		catch (Exception ex)
		{
			debug_output(ex.ToString());
			return false;
		}
	}

	public static bool frm_Resize_YKQ(Form frm)
	{
		try
		{
			float newx = (float)((Control)frm).Width / X;
			float newy = (float)((Control)frm).Height / Y;
			setControls_YKQ(newx, newy, (Control)(object)frm);
			return true;
		}
		catch (Exception ex)
		{
			debug_output(ex.ToString());
			return false;
		}
	}

	public static byte[] HexStringToToHexByte(string hexString)
	{
		hexString = hexString.Replace(" ", "");
		if (hexString.Length % 2 != 0)
		{
			hexString += " ";
		}
		byte[] array = new byte[hexString.Length / 2];
		for (int i = 0; i < array.Length; i++)
		{
			array[i] = Convert.ToByte(hexString.Substring(i * 2, 2), 16);
		}
		return array;
	}

	public static string HexByteToHexString(byte[] hexBytes, int length, string space)
	{
		string text = "";
		for (int i = 0; i < length; i++)
		{
			text = text + hexBytes[i].ToString("X2") + space;
		}
		return text;
	}

	public static void math_cpy(byte[] s_buf, int s_position, ref byte[] r_buf, int r_positon, int length)
	{
		for (int i = 0; i < length; i++)
		{
			r_buf[r_positon + i] = s_buf[s_position + i];
		}
	}
}
