using System;

namespace WindowsFormsApplication1;

public class report : EventArgs
{
	public readonly byte reportID;

	public readonly byte[] reportBuff;

	public report(byte id, byte[] arrayBuff)
	{
		reportID = id;
		reportBuff = arrayBuff;
	}
}
