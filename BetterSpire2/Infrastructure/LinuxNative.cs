using System.Runtime.InteropServices;

namespace BetterSpire2.Infrastructure;

internal static class LinuxNative
{
	[DllImport("libdl.so.2")]
	public static extern nint dlopen(string filename, int flags);

	[DllImport("libdl.so.2")]
	public static extern nint dlerror();
}
