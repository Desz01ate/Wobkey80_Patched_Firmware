using System.CodeDom.Compiler;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Resources;
using System.Runtime.CompilerServices;

namespace WindowsFormsApplication1.Properties;

[GeneratedCode("System.Resources.Tools.StronglyTypedResourceBuilder", "4.0.0.0")]
[DebuggerNonUserCode]
[CompilerGenerated]
internal class Resources
{
	private static ResourceManager resourceMan;

	private static CultureInfo resourceCulture;

	[EditorBrowsable(EditorBrowsableState.Advanced)]
	internal static ResourceManager ResourceManager
	{
		get
		{
			if (resourceMan == null)
			{
				resourceMan = new ResourceManager("WindowsFormsApplication1.Properties.Resources", typeof(Resources).Assembly);
			}
			return resourceMan;
		}
	}

	[EditorBrowsable(EditorBrowsableState.Advanced)]
	internal static CultureInfo Culture
	{
		get
		{
			return resourceCulture;
		}
		set
		{
			resourceCulture = value;
		}
	}

	internal static byte[] code_2M => (byte[])ResourceManager.GetObject("code_2M", resourceCulture);

	internal static byte[] param_128K => (byte[])ResourceManager.GetObject("param_128K", resourceCulture);

	internal static Bitmap right => (Bitmap)ResourceManager.GetObject("right", resourceCulture);

	internal static Bitmap wrong => (Bitmap)ResourceManager.GetObject("wrong", resourceCulture);

	internal Resources()
	{
	}
}
