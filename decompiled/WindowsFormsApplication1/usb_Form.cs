using System;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32.SafeHandles;
using WindowsFormsApplication1.Properties;

namespace WindowsFormsApplication1;

public class usb_Form : Form
{
	private enum CMD_TYPE
	{
		ID_CMD,
		OTA_CMD,
		BREATH_CMD,
		MOUSE_INF_CMD,
		BTN_CFG_CMD,
		DPI_CFG_CMD,
		REPORT_RATE_CFG_CMD,
		DEBUG_REPORT_RATE_CMD
	}

	[Flags]
	private enum SENDTYPE
	{
		FEATURE = 1,
		OUTPUT = 2,
		WRITE = 4
	}

	private IntPtr INVALID_HANDLE_VALUE = new IntPtr(-1);

	private FileStream hidDevice;

	private byte[] enc_key = new byte[16];

	private byte[] code_bin = Resources.code_2M;

	private byte[] param = Resources.param_128K;

	private byte[] source_binchar = new byte[2097152];

	private uint source_bin_size;

	private uint bin_crc;

	private uint bin_fw_version;

	private bool Search_dongle_flag;

	private byte report_id = 6;

	private int InputReportByteLength = 65535;

	private int OutputReportByteLength = 65535;

	private int FeatureReportByteLength = 65535;

	private bool findDev_flag;

	private bool nowFindDev;

	private bool lastFindDev;

	private const int MAX_REPORT_DATA_NUM = 512;

	private byte[] sendReportDataBuf = new byte[512];

	private byte[] getReportDataBuf = new byte[512];

	private bool first_start_read;

	private int hReadDevHandle;

	private byte[] readFileDataBuf = new byte[512];

	private int readFileDataLength;

	private int hWriteDevHandle;

	private ushort ota_index;

	private byte start_ota_flag;

	private long ota_jindu;

	private byte[] sen = new byte[512];

	private DateTime otaStartTime;

	private bool get_fw_version;

	private IContainer components;

	private RichTextBox log_richTextBox;

	private PictureBox pictureBox1;

	private Button clearLog_button;

	private TextBox VID_textBox;

	private TextBox PID_textBox;

	private Label label2;

	private TextBox interface_textBox;

	private Label label3;

	private Label label4;

	private Label label5;

	private TextBox ReportID_textBox;

	private Label bin_inf_label;

	private Button OtaStart_button;

	private Label label40;

	private Label label6;

	private Button OtaStop_button;

	private TextBox filebinPath_textBox;

	private Button openBin_button;

	private Panel panel2;

	private MenuStrip menuStrip1;

	private ToolTip toolTip1;

	private ToolStripMenuItem advancedModeToolStripMenuItem;

	private StatusStrip statusStrip1;

	private ToolStripStatusLabel toolStripStatusLabel1;

	private ToolStripStatusLabel toolStripStatusLabel2;

	private ToolStripStatusLabel toolStripStatusLabel3;

	private ToolStripStatusLabel toolStripStatusLabel4;

	private ToolStripMenuItem v10ToolStripMenuItem;

	private ProgressBar progressBar1;

	private TextBox textBox1;

	private Button button2;

	private Label label7;

	private Panel panel4;

	private Label label8;

	private ToolStripStatusLabel toolStripStatusLabel5;

	private ToolStripStatusLabel toolStripStatusLabel6;

	private Label label9;

	private ToolStripMenuItem getFwVersionToolStripMenuItem;

	private Label ota_result_label;

	private Timer timer1;

	public usb_Form()
	{
		InitializeComponent();
	}

	public string get_string_from_utf8_math(byte[] encodedBytes, int index, int count)
	{
		return new UTF8Encoding().GetString(encodedBytes, index, count);
	}

	private bool load_param()
	{
		//IL_0346: Unknown result type (might be due to invalid IL or missing references)
		//IL_022d: Unknown result type (might be due to invalid IL or missing references)
		try
		{
			int num = 0;
			for (int i = 0; i < 16; i++)
			{
				enc_key[i] = param[48 + i];
			}
			((Control)VID_textBox).Text = get_string_from_utf8_math(param, 64, 4);
			((Control)PID_textBox).Text = get_string_from_utf8_math(param, 68, 4);
			((Control)ReportID_textBox).Text = get_string_from_utf8_math(param, 72, 2);
			for (int j = 0; j < 16 && param[80 + j] != byte.MaxValue; j++)
			{
				num++;
			}
			((Control)interface_textBox).Text = get_string_from_utf8_math(param, 80, num);
			num = 0;
			for (int k = 0; k < 255 && param[256 + k] != byte.MaxValue; k++)
			{
				num++;
			}
			((ToolStripItem)toolStripStatusLabel1).Text = "VID=" + ((Control)VID_textBox).Text;
			((ToolStripItem)toolStripStatusLabel2).Text = "PID=" + ((Control)PID_textBox).Text;
			((ToolStripItem)toolStripStatusLabel3).Text = "ReportID=" + ((Control)ReportID_textBox).Text;
			string text = "IID=";
			int num2 = ((Control)interface_textBox).Text.IndexOf("&");
			text = ((num2 < 0) ? ((Control)interface_textBox).Text : (((Control)interface_textBox).Text.Substring(0, num2) + "&&" + ((Control)interface_textBox).Text.Substring(num2 + 1, ((Control)interface_textBox).Text.Length - 1 - num2)));
			((ToolStripItem)toolStripStatusLabel4).Text = "IID=" + text;
			source_bin_size = (uint)(code_bin[48] * 256 * 256 * 256 + code_bin[49] * 256 * 256 + code_bin[50] * 256 + code_bin[51]);
			if (source_bin_size >= 2097152)
			{
				MessageBox.Show("bin 的大小错误！！！！！！");
				return false;
			}
			for (int l = 0; l < source_bin_size; l++)
			{
				source_binchar[l] = code_bin[256 + l];
			}
			for (int m = 0; m < 16; m++)
			{
				source_binchar[source_bin_size + m] = byte.MaxValue;
			}
			bin_crc = BitConverter.ToUInt32(source_binchar, (int)(source_bin_size - 4));
			bin_fw_version = BitConverter.ToUInt32(code_bin, 2);
			((Control)bin_inf_label).Text = "Firmware Verson=0x" + bin_fw_version.ToString("X2") + "\r\ncode_Size=" + source_bin_size + "(0x" + source_bin_size.ToString("X") + ")code_crc=" + bin_crc.ToString("X2");
			return true;
		}
		catch
		{
			MessageBox.Show("加载参数错误！！！！！");
			return false;
		}
	}

	private void usb_Form_Load(object sender, EventArgs e)
	{
		((Control)this).Height = 240;
		((Control)ota_result_label).Text = "";
		load_param();
		USB_SearchDevice_ykq();
		report_id = Convert.ToByte(((Control)ReportID_textBox).Text.Trim());
	}

	private void usb_Form_FormClosing(object sender, FormClosingEventArgs e)
	{
		quitToolStripMenuItem_Click(null, null);
	}

	private void quitToolStripMenuItem_Click(object sender, EventArgs e)
	{
		CloseDevice();
		((Component)this).Dispose();
		((Form)this).Close();
	}

	private void advancedModeToolStripMenuItem_Click(object sender, EventArgs e)
	{
		((Control)this).Height = 240;
		if (!((Control)panel4).Visible)
		{
			((Control)panel4).Visible = true;
			((Control)this).Height = 600;
		}
		else
		{
			((Control)panel4).Visible = false;
			((Control)panel2).Visible = false;
			((Control)textBox1).Text = "";
		}
	}

	private void button2_Click(object sender, EventArgs e)
	{
		//IL_0029: Unknown result type (might be due to invalid IL or missing references)
		if (((Control)textBox1).Text == "telink")
		{
			((Control)panel2).Visible = true;
		}
		else
		{
			MessageBox.Show("Wrong password！");
		}
	}

	private void clearLog_button_Click(object sender, EventArgs e)
	{
		((Control)log_richTextBox).Text = "";
	}

	private void print_ykq(string log, Color c)
	{
		((Control)this).Invoke((Delegate)(EventHandler)delegate
		{
			((TextBoxBase)log_richTextBox).SelectedText = string.Empty;
			log_richTextBox.SelectionColor = c;
			((TextBoxBase)log_richTextBox).AppendText(log);
			((TextBoxBase)log_richTextBox).ScrollToCaret();
		});
	}

	private void outputFindDeviceInfo_ykq()
	{
		if (findDev_flag)
		{
			((Control)this).Invoke((Delegate)(EventHandler)delegate
			{
				//IL_0015: Unknown result type (might be due to invalid IL or missing references)
				//IL_001f: Expected O, but got Unknown
				pictureBox1.Image = (Image)Resources.ResourceManager.GetObject("right");
				print_ykq(" Device Connected\r\n", Color.Red);
				OtaStop_button_Click(null, null);
				timer1.Stop();
			});
			return;
		}
		((Control)pictureBox1).Invoke((Delegate)(EventHandler)delegate
		{
			//IL_0015: Unknown result type (might be due to invalid IL or missing references)
			//IL_001f: Expected O, but got Unknown
			pictureBox1.Image = (Image)Resources.ResourceManager.GetObject("wrong");
			print_ykq(" Device Disconnect\r\n", Color.Red);
			OtaStop_button_Click(null, null);
			((Control)OtaStart_button).Enabled = false;
			timer1.Start();
		});
	}

	private void ReportID_textBox_TextChanged(object sender, EventArgs e)
	{
		report_id = Convert.ToByte(((Control)ReportID_textBox).Text.Trim());
		sendReportDataBuf[0] = report_id;
	}

	public void CloseDevice()
	{
		if (findDev_flag)
		{
			findDev_flag = false;
			hidDevice.Close();
		}
	}

	private void BeginAsyncRead()
	{
		byte[] array = new byte[InputReportByteLength];
		hidDevice.BeginRead(array, 0, InputReportByteLength, ReadCompleted, array);
	}

	private void ReadCompleted(IAsyncResult iResult)
	{
		byte[] array = (byte[])iResult.AsyncState;
		try
		{
			hidDevice.EndRead(iResult);
			for (int i = 0; i < array.Length; i++)
			{
				readFileDataBuf[i] = array[i];
			}
			((Control)this).Invoke((Delegate)(EventHandler)delegate
			{
				Read_File_Data_Process_YKQ();
			});
			if (findDev_flag)
			{
				BeginAsyncRead();
			}
		}
		catch
		{
			EventArgs e = new EventArgs();
			CloseDevice();
			OnDeviceRemoved(e);
		}
	}

	private void OnDeviceRemoved(EventArgs e)
	{
		((Control)this).Invoke((Delegate)(EventHandler)delegate
		{
			USB_SearchDevice_ykq();
		});
	}

	private bool USB_SearchDevice_ykq()
	{
		if (findDev_flag)
		{
			return findDev_flag;
		}
		string allDevPath = "";
		string allHandleDevPath = "";
		string searchHandleDevPath = "";
		string hidp_caps = "";
		string value = ((Control)VID_textBox).Text.Trim().ToUpper();
		string value2 = ((Control)PID_textBox).Text.Trim().ToUpper();
		string value3 = ((Control)interface_textBox).Text.Trim().ToUpper();
		hWriteDevHandle = -1;
		hReadDevHandle = -1;
		InputReportByteLength = 0;
		OutputReportByteLength = 0;
		FeatureReportByteLength = 0;
		ushort vid = Convert.ToUInt16(value, 16);
		ushort pid = Convert.ToUInt16(value2, 16);
		ushort usagePage = Convert.ToUInt16(value3, 16);
		findDev_flag = MyUsbAPI.findDevice(vid, pid, usagePage, 0, ref hReadDevHandle, ref hWriteDevHandle, ref InputReportByteLength, ref OutputReportByteLength, ref FeatureReportByteLength, ref hidp_caps, ref allDevPath, ref allHandleDevPath, ref searchHandleDevPath);
		if (findDev_flag)
		{
			hidDevice = new FileStream(new SafeFileHandle((IntPtr)hReadDevHandle, ownsHandle: false), FileAccess.ReadWrite, InputReportByteLength, isAsync: true);
			BeginAsyncRead();
			print_ykq("allDevPath\r\n", Color.Black);
			print_ykq(allDevPath, Color.Black);
			print_ykq("allHandleDevPath\r\n", Color.Black);
			print_ykq(allHandleDevPath, Color.Black);
			print_ykq("searchHandleDevPath\r\n", Color.Black);
			print_ykq(searchHandleDevPath + "\r\n\r\n", Color.Black);
			print_ykq("hReadHandle=" + hReadDevHandle + "\r\n", Color.Black);
			print_ykq("hWriteHandle=" + hWriteDevHandle + "\r\n", Color.Black);
			print_ykq("\r\n InputReportByteLength=" + InputReportByteLength, Color.Red);
			print_ykq("\r\n OutputReportByteLength=" + OutputReportByteLength, Color.Red);
			print_ykq("\r\n FeatureReportByteLength=" + FeatureReportByteLength, Color.Red);
			print_ykq("\r\n hidp_caps=" + hidp_caps, Color.Red);
		}
		outputFindDeviceInfo_ykq();
		return findDev_flag;
	}

	private void Read_File_Data_Process_YKQ()
	{
		//IL_014d: Unknown result type (might be due to invalid IL or missing references)
		try
		{
			bool flag = true;
			if (readFileDataBuf[0] == 5 && readFileDataBuf[1] == 2 && readFileDataBuf[2] == 3 && readFileDataBuf[3] == 0 && readFileDataBuf[4] == 6 && readFileDataBuf[5] == byte.MaxValue)
			{
				if (readFileDataBuf[6] == 0)
				{
					((Control)ota_result_label).Text = " OTA Success";
					((Control)ota_result_label).ForeColor = Color.Green;
				}
				else
				{
					flag = false;
					string text = "OTA Fail=" + readFileDataBuf[6];
					((Control)ota_result_label).Text = text;
					((Control)ota_result_label).ForeColor = Color.Red;
				}
			}
			else if (get_fw_version && readFileDataBuf[0] == 5 && readFileDataBuf[1] == 1 && readFileDataBuf[2] == 8 && readFileDataBuf[3] == 0)
			{
				MessageBox.Show(string.Concat("Version=0x" + BitConverter.ToUInt32(readFileDataBuf, 4).ToString("X2") + "\r\n", "CRC=0x", BitConverter.ToUInt32(readFileDataBuf, 8).ToString("X2")), "USB Device Firmware Inforamtion", (MessageBoxButtons)0, (MessageBoxIcon)64);
				get_fw_version = false;
			}
			if (start_ota_flag != 0)
			{
				_ = readFileDataBuf[0];
				_ = 5;
			}
			if (!flag)
			{
				OtaStop_button_Click(null, null);
			}
			else if (start_ota_flag != 0)
			{
				ota_write();
			}
		}
		catch
		{
			print_ykq("error", Color.Red);
		}
	}

	private bool user_sendDataToDevice(SENDTYPE sendType, byte[] buf, int len, ref int errNum, ref string errReason)
	{
		//IL_000d: Unknown result type (might be due to invalid IL or missing references)
		if (!findDev_flag)
		{
			MessageBox.Show("device disconnect");
			print_ykq("\r\n device disconnect", Color.Red);
			return false;
		}
		int num = 0;
		Array.Clear(sendReportDataBuf, 0, OutputReportByteLength);
		sendReportDataBuf[0] = report_id;
		for (int i = 0; i < len; i++)
		{
			sendReportDataBuf[i + 1] = buf[i];
		}
		bool flag = false;
		switch (sendType)
		{
		case SENDTYPE.FEATURE:
			num = FeatureReportByteLength;
			flag = MyUsbAPI.HidD_SetFeature(new IntPtr(hWriteDevHandle), sendReportDataBuf, num);
			if (!flag)
			{
				errNum = MyUsbAPI.GetLastError();
				errReason = MyUsbAPI.GetSysErrMsg(errNum);
				print_ykq("\r\n SetFeatureReport Fail->[" + errNum + "]" + errReason, Color.Red);
			}
			else
			{
				print_ykq("SetFeatureReport success", Color.Green);
			}
			break;
		case SENDTYPE.OUTPUT:
			num = OutputReportByteLength;
			flag = MyUsbAPI.HidD_SetOutputReport((IntPtr)hWriteDevHandle, sendReportDataBuf, num);
			if (!flag)
			{
				errNum = MyUsbAPI.GetLastError();
				errReason = MyUsbAPI.GetSysErrMsg(errNum);
				print_ykq("\r\n SetOutputReport Fail->[" + errNum + "]" + errReason, Color.Red);
			}
			else
			{
				print_ykq("\r\n SetOutputReport Success", Color.Green);
			}
			break;
		case SENDTYPE.WRITE:
			hidDevice.Write(sendReportDataBuf, 0, OutputReportByteLength);
			break;
		}
		if (flag)
		{
			string text = "\r\n";
			text += ykq.HexByteToHexString(sendReportDataBuf, num, " ");
			print_ykq(text, Color.Green);
		}
		return flag;
	}

	private ushort crc16(byte[] pD, int len)
	{
		ushort[] array = new ushort[2] { 0, 40961 };
		ushort num = ushort.MaxValue;
		int num2 = 0;
		for (int num3 = len; num3 > 0; num3--)
		{
			ushort num4 = pD[num2];
			num2++;
			for (int i = 0; i < 8; i++)
			{
				num = (ushort)((num >> 1) ^ array[(num ^ num4) & 1]);
				num4 >>= 1;
			}
		}
		return num;
	}

	private void ota_write()
	{
		bool flag = false;
		byte[] array = new byte[60];
		for (int i = 0; i < OutputReportByteLength; i++)
		{
			sen[i] = byte.MaxValue;
		}
		int num = 3;
		sen[0] = 2;
		if (start_ota_flag == 1)
		{
			sen[1] = 2;
			sen[2] = 0;
			sen[num] = 1;
			sen[num + 1] = byte.MaxValue;
			ota_index = 0;
			start_ota_flag = 2;
			((Control)label6).Text = "";
			otaStartTime = DateTime.Now;
			flag = true;
		}
		else if (start_ota_flag == 2)
		{
			flag = true;
			sen[1] = 0;
			sen[2] = 0;
			((Control)OtaStart_button).Enabled = false;
			byte[] array2 = new byte[18];
			for (int j = 0; j < 3; j++)
			{
				array[20 * j] = (byte)(ota_index & 0xFF);
				array[20 * j + 1] = (byte)((ota_index >> 8) & 0xFF);
				array2[0] = array[20 * j];
				array2[1] = array[20 * j + 1];
				for (int k = 0; k < 16; k++)
				{
					array[20 * j + k + 2] = source_binchar[ota_index * 16 + k];
					array2[k + 2] = source_binchar[ota_index * 16 + k];
				}
				ushort num2 = crc16(array2, 18);
				array[20 * j + 18] = (byte)(num2 & 0xFF);
				array[20 * j + 19] = (byte)((num2 >> 8) & 0xFF);
				ota_index++;
				if (ota_index * 16 >= source_bin_size + 16)
				{
					ota_index--;
					break;
				}
				sen[1] = (byte)(20 * j + 20);
			}
			for (int l = 0; l < sen[1]; l++)
			{
				sen[num + l] = array[l];
			}
			ota_jindu = ota_index * 16 * 100 / source_bin_size;
			if (ota_jindu > 100)
			{
				ota_jindu = 100L;
			}
			((Control)label40).Text = ota_jindu + "%";
			progressBar1.Value = (int)ota_jindu;
			if (sen[1] == 0)
			{
				((Control)label6).Text = (DateTime.Now - otaStartTime).TotalSeconds.ToString() ?? "";
				sen[1] = 6;
				sen[2] = 0;
				sen[num] = 2;
				sen[num + 1] = byte.MaxValue;
				ushort num3 = (ushort)(ota_index - 1);
				sen[num + 2] = (byte)(num3 & 0xFF);
				sen[num + 3] = (byte)((num3 >> 8) & 0xFF);
				ushort num4 = (ushort)(65535 - num3 + 1);
				sen[num + 4] = (byte)(num4 & 0xFF);
				sen[num + 5] = (byte)((num4 >> 8) & 0xFF);
				start_ota_flag = 0;
				ota_index = 0;
				OtaStop_button_Click(null, null);
			}
		}
		if (flag)
		{
			int errNum = 0;
			string errReason = "";
			user_sendDataToDevice(SENDTYPE.WRITE, sen, OutputReportByteLength, ref errNum, ref errReason);
		}
	}

	private bool check_bin_is_ok()
	{
		//IL_0016: Unknown result type (might be due to invalid IL or missing references)
		if (bin_crc == uint.MaxValue || bin_crc == 0)
		{
			MessageBox.Show("Firmware CRC is wrong，illegal firmware");
			return false;
		}
		return true;
	}

	private void OtaStart_button_Click(object sender, EventArgs e)
	{
		if (check_bin_is_ok())
		{
			((Control)log_richTextBox).Text = "";
			ota_index = 0;
			start_ota_flag = 1;
			((Control)ota_result_label).Text = "";
			((Control)ota_result_label).ForeColor = Color.Red;
			ota_write();
		}
	}

	private void OtaStop_button_Click(object sender, EventArgs e)
	{
		start_ota_flag = 0;
		((Control)OtaStart_button).Enabled = true;
		print_ykq("--->ota stop\r\n", Color.Blue);
	}

	private void openBin_button_Click(object sender, EventArgs e)
	{
		//IL_0000: Unknown result type (might be due to invalid IL or missing references)
		//IL_0005: Unknown result type (might be due to invalid IL or missing references)
		//IL_0010: Unknown result type (might be due to invalid IL or missing references)
		//IL_0011: Unknown result type (might be due to invalid IL or missing references)
		//IL_014e: Unknown result type (might be due to invalid IL or missing references)
		try
		{
			OpenFileDialog val = new OpenFileDialog
			{
				Filter = "Bin File(*.bin)|*.bin"
			};
			((CommonDialog)val).ShowDialog();
			string fileName = ((FileDialog)val).FileName;
			if (!(fileName == ""))
			{
				((Control)filebinPath_textBox).Text = fileName;
				byte[] backBytes = new byte[524288];
				uint bin_size = 0u;
				ykq.ReadBinFile(fileName, ref backBytes, ref bin_size);
				source_bin_size = BitConverter.ToUInt32(backBytes, 24);
				bin_crc = BitConverter.ToUInt32(backBytes, (int)(source_bin_size - 4));
				for (int i = 0; i < source_bin_size; i++)
				{
					source_binchar[i] = backBytes[i];
				}
				for (int j = 0; j < 16; j++)
				{
					source_binchar[source_bin_size + j] = byte.MaxValue;
				}
				bin_fw_version = BitConverter.ToUInt32(backBytes, 2);
				((Control)bin_inf_label).Text = "Firmware Verson=0x" + bin_fw_version.ToString("X2") + "\r\ncode_Size=" + source_bin_size + "(0x" + source_bin_size.ToString("X") + ")code_crc=" + bin_crc.ToString("X2");
			}
		}
		catch
		{
			MessageBox.Show("telink bin File format error");
		}
	}

	private void getFwVersionToolStripMenuItem_Click(object sender, EventArgs e)
	{
		int errNum = 0;
		string errReason = "";
		for (int i = 0; i < OutputReportByteLength; i++)
		{
			sen[i] = byte.MaxValue;
		}
		sen[0] = 1;
		sen[1] = 0;
		sen[2] = 0;
		get_fw_version = true;
		user_sendDataToDevice(SENDTYPE.WRITE, sen, 32, ref errNum, ref errReason);
	}

	private void timer1_Tick(object sender, EventArgs e)
	{
		USB_SearchDevice_ykq();
	}

	private void button1_Click(object sender, EventArgs e)
	{
	}

	protected override void Dispose(bool disposing)
	{
		if (disposing && components != null)
		{
			components.Dispose();
		}
		((Form)this).Dispose(disposing);
	}

	private void InitializeComponent()
	{
		//IL_001c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0026: Expected O, but got Unknown
		//IL_0027: Unknown result type (might be due to invalid IL or missing references)
		//IL_0031: Expected O, but got Unknown
		//IL_0032: Unknown result type (might be due to invalid IL or missing references)
		//IL_003c: Expected O, but got Unknown
		//IL_003d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0047: Expected O, but got Unknown
		//IL_0048: Unknown result type (might be due to invalid IL or missing references)
		//IL_0052: Expected O, but got Unknown
		//IL_0053: Unknown result type (might be due to invalid IL or missing references)
		//IL_005d: Expected O, but got Unknown
		//IL_005e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0068: Expected O, but got Unknown
		//IL_0069: Unknown result type (might be due to invalid IL or missing references)
		//IL_0073: Expected O, but got Unknown
		//IL_0074: Unknown result type (might be due to invalid IL or missing references)
		//IL_007e: Expected O, but got Unknown
		//IL_007f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0089: Expected O, but got Unknown
		//IL_008a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0094: Expected O, but got Unknown
		//IL_0095: Unknown result type (might be due to invalid IL or missing references)
		//IL_009f: Expected O, but got Unknown
		//IL_00a0: Unknown result type (might be due to invalid IL or missing references)
		//IL_00aa: Expected O, but got Unknown
		//IL_00ab: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b5: Expected O, but got Unknown
		//IL_00b6: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c0: Expected O, but got Unknown
		//IL_00c1: Unknown result type (might be due to invalid IL or missing references)
		//IL_00cb: Expected O, but got Unknown
		//IL_00cc: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d6: Expected O, but got Unknown
		//IL_00d7: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e1: Expected O, but got Unknown
		//IL_00e2: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ec: Expected O, but got Unknown
		//IL_00ed: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f7: Expected O, but got Unknown
		//IL_00f8: Unknown result type (might be due to invalid IL or missing references)
		//IL_0102: Expected O, but got Unknown
		//IL_0103: Unknown result type (might be due to invalid IL or missing references)
		//IL_010d: Expected O, but got Unknown
		//IL_010e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0118: Expected O, but got Unknown
		//IL_011f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0129: Expected O, but got Unknown
		//IL_012a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0134: Expected O, but got Unknown
		//IL_0135: Unknown result type (might be due to invalid IL or missing references)
		//IL_013f: Expected O, but got Unknown
		//IL_0140: Unknown result type (might be due to invalid IL or missing references)
		//IL_014a: Expected O, but got Unknown
		//IL_014b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0155: Expected O, but got Unknown
		//IL_0156: Unknown result type (might be due to invalid IL or missing references)
		//IL_0160: Expected O, but got Unknown
		//IL_0161: Unknown result type (might be due to invalid IL or missing references)
		//IL_016b: Expected O, but got Unknown
		//IL_016c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0176: Expected O, but got Unknown
		//IL_0177: Unknown result type (might be due to invalid IL or missing references)
		//IL_0181: Expected O, but got Unknown
		//IL_0182: Unknown result type (might be due to invalid IL or missing references)
		//IL_018c: Expected O, but got Unknown
		//IL_018d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0197: Expected O, but got Unknown
		//IL_0198: Unknown result type (might be due to invalid IL or missing references)
		//IL_01a2: Expected O, but got Unknown
		//IL_01a3: Unknown result type (might be due to invalid IL or missing references)
		//IL_01ad: Expected O, but got Unknown
		//IL_01ae: Unknown result type (might be due to invalid IL or missing references)
		//IL_01b8: Expected O, but got Unknown
		//IL_01b9: Unknown result type (might be due to invalid IL or missing references)
		//IL_01c3: Expected O, but got Unknown
		//IL_01c4: Unknown result type (might be due to invalid IL or missing references)
		//IL_01ce: Expected O, but got Unknown
		//IL_01d5: Unknown result type (might be due to invalid IL or missing references)
		//IL_01df: Expected O, but got Unknown
		//IL_0244: Unknown result type (might be due to invalid IL or missing references)
		//IL_02ca: Unknown result type (might be due to invalid IL or missing references)
		//IL_0353: Unknown result type (might be due to invalid IL or missing references)
		//IL_03d2: Unknown result type (might be due to invalid IL or missing references)
		//IL_045d: Unknown result type (might be due to invalid IL or missing references)
		//IL_04c6: Unknown result type (might be due to invalid IL or missing references)
		//IL_0554: Unknown result type (might be due to invalid IL or missing references)
		//IL_05cc: Unknown result type (might be due to invalid IL or missing references)
		//IL_0644: Unknown result type (might be due to invalid IL or missing references)
		//IL_06ad: Unknown result type (might be due to invalid IL or missing references)
		//IL_0732: Unknown result type (might be due to invalid IL or missing references)
		//IL_073c: Expected O, but got Unknown
		//IL_075d: Unknown result type (might be due to invalid IL or missing references)
		//IL_07c6: Unknown result type (might be due to invalid IL or missing references)
		//IL_0857: Unknown result type (might be due to invalid IL or missing references)
		//IL_0861: Expected O, but got Unknown
		//IL_0892: Unknown result type (might be due to invalid IL or missing references)
		//IL_0900: Unknown result type (might be due to invalid IL or missing references)
		//IL_090a: Expected O, but got Unknown
		//IL_093e: Unknown result type (might be due to invalid IL or missing references)
		//IL_09a7: Unknown result type (might be due to invalid IL or missing references)
		//IL_128a: Unknown result type (might be due to invalid IL or missing references)
		//IL_1294: Expected O, but got Unknown
		//IL_12bb: Unknown result type (might be due to invalid IL or missing references)
		//IL_133b: Unknown result type (might be due to invalid IL or missing references)
		//IL_13ac: Unknown result type (might be due to invalid IL or missing references)
		//IL_13b6: Expected O, but got Unknown
		//IL_1559: Unknown result type (might be due to invalid IL or missing references)
		//IL_1563: Expected O, but got Unknown
		//IL_1571: Unknown result type (might be due to invalid IL or missing references)
		//IL_15a0: Unknown result type (might be due to invalid IL or missing references)
		//IL_15aa: Expected O, but got Unknown
		components = new Container();
		ComponentResourceManager componentResourceManager = new ComponentResourceManager(typeof(usb_Form));
		log_richTextBox = new RichTextBox();
		clearLog_button = new Button();
		VID_textBox = new TextBox();
		PID_textBox = new TextBox();
		label2 = new Label();
		interface_textBox = new TextBox();
		label3 = new Label();
		label4 = new Label();
		label5 = new Label();
		ReportID_textBox = new TextBox();
		bin_inf_label = new Label();
		OtaStart_button = new Button();
		label40 = new Label();
		label6 = new Label();
		OtaStop_button = new Button();
		filebinPath_textBox = new TextBox();
		openBin_button = new Button();
		panel2 = new Panel();
		label8 = new Label();
		menuStrip1 = new MenuStrip();
		advancedModeToolStripMenuItem = new ToolStripMenuItem();
		v10ToolStripMenuItem = new ToolStripMenuItem();
		getFwVersionToolStripMenuItem = new ToolStripMenuItem();
		toolTip1 = new ToolTip(components);
		textBox1 = new TextBox();
		statusStrip1 = new StatusStrip();
		toolStripStatusLabel1 = new ToolStripStatusLabel();
		toolStripStatusLabel2 = new ToolStripStatusLabel();
		toolStripStatusLabel3 = new ToolStripStatusLabel();
		toolStripStatusLabel4 = new ToolStripStatusLabel();
		toolStripStatusLabel5 = new ToolStripStatusLabel();
		toolStripStatusLabel6 = new ToolStripStatusLabel();
		progressBar1 = new ProgressBar();
		button2 = new Button();
		label7 = new Label();
		panel4 = new Panel();
		pictureBox1 = new PictureBox();
		label9 = new Label();
		ota_result_label = new Label();
		timer1 = new Timer(components);
		((Control)panel2).SuspendLayout();
		((Control)menuStrip1).SuspendLayout();
		((Control)statusStrip1).SuspendLayout();
		((Control)panel4).SuspendLayout();
		((ISupportInitialize)pictureBox1).BeginInit();
		((Control)this).SuspendLayout();
		((Control)log_richTextBox).Anchor = (AnchorStyles)15;
		((Control)log_richTextBox).Location = new Point(11, 110);
		((Control)log_richTextBox).Margin = new Padding(4);
		((Control)log_richTextBox).Name = "log_richTextBox";
		((TextBoxBase)log_richTextBox).ReadOnly = true;
		((Control)log_richTextBox).Size = new Size(1190, 280);
		((Control)log_richTextBox).TabIndex = 3;
		((Control)log_richTextBox).Text = "";
		((Control)clearLog_button).Anchor = (AnchorStyles)9;
		((Control)clearLog_button).Location = new Point(1094, 4);
		((Control)clearLog_button).Margin = new Padding(4);
		((Control)clearLog_button).Name = "clearLog_button";
		((Control)clearLog_button).Size = new Size(100, 29);
		((Control)clearLog_button).TabIndex = 53;
		((Control)clearLog_button).Text = "clear";
		((ButtonBase)clearLog_button).UseVisualStyleBackColor = true;
		((Control)clearLog_button).Click += clearLog_button_Click;
		((Control)VID_textBox).Location = new Point(56, 17);
		((Control)VID_textBox).Margin = new Padding(4);
		((Control)VID_textBox).Name = "VID_textBox";
		((Control)VID_textBox).Size = new Size(88, 25);
		((Control)VID_textBox).TabIndex = 62;
		((Control)VID_textBox).Text = "248A";
		toolTip1.SetToolTip((Control)(object)VID_textBox, "和usb描述符有关，用bushound可以查看");
		((Control)PID_textBox).Location = new Point(208, 17);
		((Control)PID_textBox).Margin = new Padding(4);
		((Control)PID_textBox).Name = "PID_textBox";
		((Control)PID_textBox).Size = new Size(88, 25);
		((Control)PID_textBox).TabIndex = 64;
		((Control)PID_textBox).Text = "5b49";
		toolTip1.SetToolTip((Control)(object)PID_textBox, "和usb描述符有关，用bushound可以查看");
		((Control)label2).AutoSize = true;
		((Control)label2).Location = new Point(17, 20);
		((Control)label2).Margin = new Padding(4, 0, 4, 0);
		((Control)label2).Name = "label2";
		((Control)label2).Size = new Size(31, 15);
		((Control)label2).TabIndex = 63;
		((Control)label2).Text = "VID";
		((Control)interface_textBox).Location = new Point(587, 17);
		((Control)interface_textBox).Margin = new Padding(4);
		((Control)interface_textBox).Name = "interface_textBox";
		((Control)interface_textBox).Size = new Size(121, 25);
		((Control)interface_textBox).TabIndex = 66;
		((Control)interface_textBox).Text = "FFEF";
		toolTip1.SetToolTip((Control)(object)interface_textBox, "和usb描述符有关，用bushound可以查看");
		((Control)label3).AutoSize = true;
		((Control)label3).Location = new Point(160, 20);
		((Control)label3).Margin = new Padding(4, 0, 4, 0);
		((Control)label3).Name = "label3";
		((Control)label3).Size = new Size(31, 15);
		((Control)label3).TabIndex = 65;
		((Control)label3).Text = "PID";
		((Control)label4).AutoSize = true;
		((Control)label4).Location = new Point(500, 20);
		((Control)label4).Margin = new Padding(4, 0, 4, 0);
		((Control)label4).Name = "label4";
		((Control)label4).Size = new Size(79, 15);
		((Control)label4).TabIndex = 68;
		((Control)label4).Text = "UsagePage";
		((Control)label5).AutoSize = true;
		((Control)label5).Location = new Point(316, 20);
		((Control)label5).Margin = new Padding(4, 0, 4, 0);
		((Control)label5).Name = "label5";
		((Control)label5).Size = new Size(71, 15);
		((Control)label5).TabIndex = 71;
		((Control)label5).Text = "ReportID";
		((Control)ReportID_textBox).Location = new Point(405, 17);
		((Control)ReportID_textBox).Margin = new Padding(4);
		((Control)ReportID_textBox).Name = "ReportID_textBox";
		((Control)ReportID_textBox).Size = new Size(88, 25);
		((Control)ReportID_textBox).TabIndex = 70;
		((Control)ReportID_textBox).Text = "05";
		((Control)ReportID_textBox).TextChanged += ReportID_textBox_TextChanged;
		((Control)bin_inf_label).AutoSize = true;
		((Control)bin_inf_label).Font = new Font("宋体", 14.25f, (FontStyle)0, (GraphicsUnit)3, (byte)134);
		((Control)bin_inf_label).Location = new Point(435, 44);
		((Control)bin_inf_label).Margin = new Padding(4, 0, 4, 0);
		((Control)bin_inf_label).Name = "bin_inf_label";
		((Control)bin_inf_label).Size = new Size(70, 24);
		((Control)bin_inf_label).TabIndex = 74;
		((Control)bin_inf_label).Text = "Size=";
		((Control)OtaStart_button).Location = new Point(914, 102);
		((Control)OtaStart_button).Margin = new Padding(4);
		((Control)OtaStart_button).Name = "OtaStart_button";
		((Control)OtaStart_button).Size = new Size(85, 54);
		((Control)OtaStart_button).TabIndex = 77;
		((Control)OtaStart_button).Text = "Start";
		((ButtonBase)OtaStart_button).UseVisualStyleBackColor = true;
		((Control)OtaStart_button).Click += OtaStart_button_Click;
		((Control)label40).AutoSize = true;
		((Control)label40).Font = new Font("宋体", 24f, (FontStyle)1, (GraphicsUnit)3, (byte)134);
		((Control)label40).ForeColor = Color.Red;
		((Control)label40).Location = new Point(810, 112);
		((Control)label40).Margin = new Padding(4, 0, 4, 0);
		((Control)label40).Name = "label40";
		((Control)label40).Size = new Size(59, 40);
		((Control)label40).TabIndex = 78;
		((Control)label40).Text = "0%";
		((Control)label6).AutoSize = true;
		((Control)label6).Font = new Font("宋体", 12f, (FontStyle)1, (GraphicsUnit)3, (byte)134);
		((Control)label6).ForeColor = Color.Blue;
		((Control)label6).Location = new Point(151, 171);
		((Control)label6).Margin = new Padding(4, 0, 4, 0);
		((Control)label6).Name = "label6";
		((Control)label6).Size = new Size(20, 20);
		((Control)label6).TabIndex = 79;
		((Control)label6).Text = "0";
		((Control)OtaStop_button).Location = new Point(1019, 102);
		((Control)OtaStop_button).Margin = new Padding(4);
		((Control)OtaStop_button).Name = "OtaStop_button";
		((Control)OtaStop_button).Size = new Size(82, 54);
		((Control)OtaStop_button).TabIndex = 80;
		((Control)OtaStop_button).Text = "Stop ";
		((ButtonBase)OtaStop_button).UseVisualStyleBackColor = true;
		((Control)OtaStop_button).Click += OtaStop_button_Click;
		((Control)filebinPath_textBox).Location = new Point(93, 63);
		((Control)filebinPath_textBox).Name = "filebinPath_textBox";
		((Control)filebinPath_textBox).Size = new Size(578, 25);
		((Control)filebinPath_textBox).TabIndex = 83;
		((Control)openBin_button).Location = new Point(702, 55);
		((Control)openBin_button).Name = "openBin_button";
		((Control)openBin_button).Size = new Size(127, 36);
		((Control)openBin_button).TabIndex = 85;
		((Control)openBin_button).Text = "open bin";
		((ButtonBase)openBin_button).UseVisualStyleBackColor = true;
		((Control)openBin_button).Click += openBin_button_Click;
		((Control)panel2).Controls.Add((Control)(object)label8);
		((Control)panel2).Controls.Add((Control)(object)label2);
		((Control)panel2).Controls.Add((Control)(object)VID_textBox);
		((Control)panel2).Controls.Add((Control)(object)interface_textBox);
		((Control)panel2).Controls.Add((Control)(object)label3);
		((Control)panel2).Controls.Add((Control)(object)label4);
		((Control)panel2).Controls.Add((Control)(object)clearLog_button);
		((Control)panel2).Controls.Add((Control)(object)openBin_button);
		((Control)panel2).Controls.Add((Control)(object)PID_textBox);
		((Control)panel2).Controls.Add((Control)(object)filebinPath_textBox);
		((Control)panel2).Controls.Add((Control)(object)ReportID_textBox);
		((Control)panel2).Controls.Add((Control)(object)label5);
		((Control)panel2).Controls.Add((Control)(object)log_richTextBox);
		((Control)panel2).Location = new Point(12, 279);
		((Control)panel2).Name = "panel2";
		((Control)panel2).Size = new Size(1207, 405);
		((Control)panel2).TabIndex = 87;
		((Control)panel2).Visible = false;
		((Control)label8).AutoSize = true;
		((Control)label8).Location = new Point(11, 63);
		((Control)label8).Name = "label8";
		((Control)label8).Size = new Size(76, 15);
		((Control)label8).TabIndex = 96;
		((Control)label8).Text = "bin文件：";
		((ToolStrip)menuStrip1).ImageScalingSize = new Size(20, 20);
		((ToolStrip)menuStrip1).Items.AddRange((ToolStripItem[])(object)new ToolStripItem[3]
		{
			(ToolStripItem)advancedModeToolStripMenuItem,
			(ToolStripItem)v10ToolStripMenuItem,
			(ToolStripItem)getFwVersionToolStripMenuItem
		});
		((Control)menuStrip1).Location = new Point(0, 0);
		((Control)menuStrip1).Name = "menuStrip1";
		((Control)menuStrip1).Size = new Size(1232, 28);
		((Control)menuStrip1).TabIndex = 90;
		((Control)menuStrip1).Text = "menuStrip1";
		((ToolStripItem)advancedModeToolStripMenuItem).Name = "advancedModeToolStripMenuItem";
		((ToolStripItem)advancedModeToolStripMenuItem).Size = new Size(120, 24);
		((ToolStripItem)advancedModeToolStripMenuItem).Text = "Debug Mode";
		((ToolStripItem)advancedModeToolStripMenuItem).Click += advancedModeToolStripMenuItem_Click;
		((ToolStripItem)v10ToolStripMenuItem).Alignment = (ToolStripItemAlignment)1;
		((ToolStripItem)v10ToolStripMenuItem).BackColor = Color.Lime;
		((ToolStripItem)v10ToolStripMenuItem).Name = "v10ToolStripMenuItem";
		((ToolStripItem)v10ToolStripMenuItem).Size = new Size(66, 24);
		((ToolStripItem)v10ToolStripMenuItem).Text = "v5.0.2";
		((ToolStripItem)getFwVersionToolStripMenuItem).Name = "getFwVersionToolStripMenuItem";
		((ToolStripItem)getFwVersionToolStripMenuItem).Size = new Size(185, 24);
		((ToolStripItem)getFwVersionToolStripMenuItem).Text = "Get Device Fw Version";
		((ToolStripItem)getFwVersionToolStripMenuItem).Click += getFwVersionToolStripMenuItem_Click;
		((Control)textBox1).Location = new Point(63, 18);
		((Control)textBox1).Name = "textBox1";
		textBox1.PasswordChar = '*';
		((Control)textBox1).Size = new Size(124, 25);
		((Control)textBox1).TabIndex = 93;
		toolTip1.SetToolTip((Control)(object)textBox1, "telink");
		((ToolStrip)statusStrip1).ImageScalingSize = new Size(20, 20);
		((ToolStrip)statusStrip1).Items.AddRange((ToolStripItem[])(object)new ToolStripItem[6]
		{
			(ToolStripItem)toolStripStatusLabel1,
			(ToolStripItem)toolStripStatusLabel2,
			(ToolStripItem)toolStripStatusLabel3,
			(ToolStripItem)toolStripStatusLabel4,
			(ToolStripItem)toolStripStatusLabel5,
			(ToolStripItem)toolStripStatusLabel6
		});
		((Control)statusStrip1).Location = new Point(0, 698);
		((Control)statusStrip1).Name = "statusStrip1";
		((Control)statusStrip1).Size = new Size(1232, 30);
		((Control)statusStrip1).TabIndex = 91;
		((Control)statusStrip1).Text = "statusStrip1";
		toolStripStatusLabel1.BorderSides = (ToolStripStatusLabelBorderSides)4;
		((ToolStripItem)toolStripStatusLabel1).Name = "toolStripStatusLabel1";
		((ToolStripItem)toolStripStatusLabel1).Size = new Size(171, 24);
		((ToolStripItem)toolStripStatusLabel1).Text = "toolStripStatusLabel1";
		toolStripStatusLabel2.BorderSides = (ToolStripStatusLabelBorderSides)4;
		((ToolStripItem)toolStripStatusLabel2).Name = "toolStripStatusLabel2";
		((ToolStripItem)toolStripStatusLabel2).Size = new Size(171, 24);
		((ToolStripItem)toolStripStatusLabel2).Text = "toolStripStatusLabel2";
		toolStripStatusLabel3.BorderSides = (ToolStripStatusLabelBorderSides)4;
		((ToolStripItem)toolStripStatusLabel3).Name = "toolStripStatusLabel3";
		((ToolStripItem)toolStripStatusLabel3).Size = new Size(171, 24);
		((ToolStripItem)toolStripStatusLabel3).Text = "toolStripStatusLabel3";
		((ToolStripItem)toolStripStatusLabel4).Name = "toolStripStatusLabel4";
		((ToolStripItem)toolStripStatusLabel4).Size = new Size(0, 24);
		((ToolStripItem)toolStripStatusLabel5).Name = "toolStripStatusLabel5";
		((ToolStripItem)toolStripStatusLabel5).Size = new Size(150, 24);
		((ToolStripItem)toolStripStatusLabel5).Text = "UsagePage=0xFFEF";
		((ToolStripItem)toolStripStatusLabel6).Name = "toolStripStatusLabel6";
		((ToolStripItem)toolStripStatusLabel6).Size = new Size(100, 24);
		((ToolStripItem)toolStripStatusLabel6).Text = "Usage=0x00";
		((Control)progressBar1).Location = new Point(36, 114);
		((Control)progressBar1).Name = "progressBar1";
		((Control)progressBar1).Size = new Size(758, 42);
		((Control)progressBar1).TabIndex = 92;
		((Control)button2).Location = new Point(204, 13);
		((Control)button2).Name = "button2";
		((Control)button2).Size = new Size(179, 31);
		((Control)button2).TabIndex = 94;
		((Control)button2).Text = "show Debug surface";
		((ButtonBase)button2).UseVisualStyleBackColor = true;
		((Control)button2).Click += button2_Click;
		((Control)label7).AutoSize = true;
		((Control)label7).Location = new Point(3, 21);
		((Control)label7).Name = "label7";
		((Control)label7).Size = new Size(37, 15);
		((Control)label7).TabIndex = 95;
		((Control)label7).Text = "密码";
		((Control)panel4).Controls.Add((Control)(object)textBox1);
		((Control)panel4).Controls.Add((Control)(object)button2);
		((Control)panel4).Controls.Add((Control)(object)label7);
		((Control)panel4).Location = new Point(16, 201);
		((Control)panel4).Name = "panel4";
		((Control)panel4).Size = new Size(416, 51);
		((Control)panel4).TabIndex = 96;
		((Control)panel4).Visible = false;
		pictureBox1.ErrorImage = null;
		pictureBox1.Image = (Image)componentResourceManager.GetObject("pictureBox1.Image");
		pictureBox1.ImeMode = (ImeMode)0;
		((Control)pictureBox1).Location = new Point(23, 32);
		((Control)pictureBox1).Margin = new Padding(4);
		((Control)pictureBox1).Name = "pictureBox1";
		((Control)pictureBox1).Size = new Size(68, 70);
		pictureBox1.SizeMode = (PictureBoxSizeMode)4;
		pictureBox1.TabIndex = 52;
		pictureBox1.TabStop = false;
		((Control)label9).AutoSize = true;
		((Control)label9).Location = new Point(9, 171);
		((Control)label9).Margin = new Padding(4, 0, 4, 0);
		((Control)label9).Name = "label9";
		((Control)label9).Size = new Size(134, 15);
		((Control)label9).TabIndex = 97;
		((Control)label9).Text = "OTA Total Time：";
		((Control)ota_result_label).AutoSize = true;
		((Control)ota_result_label).Font = new Font("宋体", 15f, (FontStyle)1, (GraphicsUnit)3, (byte)134);
		((Control)ota_result_label).Location = new Point(326, 173);
		((Control)ota_result_label).Name = "ota_result_label";
		((Control)ota_result_label).Size = new Size(110, 25);
		((Control)ota_result_label).TabIndex = 98;
		((Control)ota_result_label).Text = "label10";
		timer1.Interval = 1000;
		timer1.Tick += timer1_Tick;
		((ContainerControl)this).AutoScaleDimensions = new SizeF(8f, 15f);
		((ContainerControl)this).AutoScaleMode = (AutoScaleMode)1;
		((Form)this).ClientSize = new Size(1232, 728);
		((Control)this).Controls.Add((Control)(object)ota_result_label);
		((Control)this).Controls.Add((Control)(object)label9);
		((Control)this).Controls.Add((Control)(object)panel4);
		((Control)this).Controls.Add((Control)(object)progressBar1);
		((Control)this).Controls.Add((Control)(object)OtaStop_button);
		((Control)this).Controls.Add((Control)(object)OtaStart_button);
		((Control)this).Controls.Add((Control)(object)statusStrip1);
		((Control)this).Controls.Add((Control)(object)menuStrip1);
		((Control)this).Controls.Add((Control)(object)label6);
		((Control)this).Controls.Add((Control)(object)label40);
		((Control)this).Controls.Add((Control)(object)panel2);
		((Control)this).Controls.Add((Control)(object)bin_inf_label);
		((Control)this).Controls.Add((Control)(object)pictureBox1);
		((Form)this).FormBorderStyle = (FormBorderStyle)3;
		((Form)this).Icon = (Icon)componentResourceManager.GetObject("$this.Icon");
		((Form)this).MainMenuStrip = menuStrip1;
		((Form)this).Margin = new Padding(4);
		((Form)this).MaximizeBox = false;
		((Control)this).Name = "usb_Form";
		((Control)this).Text = "mouse&keybaord usb ota";
		((Form)this).FormClosing += new FormClosingEventHandler(usb_Form_FormClosing);
		((Form)this).Load += usb_Form_Load;
		((Control)panel2).ResumeLayout(false);
		((Control)panel2).PerformLayout();
		((Control)menuStrip1).ResumeLayout(false);
		((Control)menuStrip1).PerformLayout();
		((Control)statusStrip1).ResumeLayout(false);
		((Control)statusStrip1).PerformLayout();
		((Control)panel4).ResumeLayout(false);
		((Control)panel4).PerformLayout();
		((ISupportInitialize)pictureBox1).EndInit();
		((Control)this).ResumeLayout(false);
		((Control)this).PerformLayout();
	}
}
