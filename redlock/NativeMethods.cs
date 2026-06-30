using System;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

// ReSharper disable InconsistentNaming

namespace redlock;

internal static class NativeMethods
{
	internal const int EWX_REBOOT = 0x02;
	internal const uint SHTDN_REASON_MAJOR_OPERATINGSYSTEM = 0x00020000;
	internal const uint SHTDN_REASON_MINOR_RECONFIG = 0x00000004;
	internal const uint SHTDN_REASON_FLAG_PLANNED = 0x80000000;

	[DllImport("user32.dll", CharSet = CharSet.Unicode)]
	internal static extern bool ExitWindowsEx(uint uFlags, int dwReason);

	[DllImport("uxtheme.dll", EntryPoint = "#94")]
	internal static extern int GetImmersiveColorSetCount();

	internal sealed class SafeLibraryHandle : SafeHandleZeroOrMinusOneIsInvalid
	{
		internal SafeLibraryHandle()
			: base(true)
		{
		}

		internal SafeLibraryHandle(IntPtr handle)
			: base(true)
		{
			SetHandle(handle);
		}

		[DllImport("kernel32.dll", SetLastError = true)]
		private static extern bool FreeLibrary(IntPtr hModule);

		protected override bool ReleaseHandle()
		{
			return FreeLibrary(handle);
		}
	}
}