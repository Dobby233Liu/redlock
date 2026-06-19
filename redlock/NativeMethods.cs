using System;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

// ReSharper disable InconsistentNaming

namespace redlock;

internal static class NativeMethods
{
	[DllImport("advapi32.dll", CharSet = CharSet.Unicode, EntryPoint = "RegLoadKeyW", SetLastError = true)]
	internal static extern int RegLoadKey(uint hKey, [MarshalAs(UnmanagedType.LPWStr)] [Optional] string lpSubKey,
		[MarshalAs(UnmanagedType.LPWStr)] string lpFile);

	[DllImport("advapi32.dll", CharSet = CharSet.Unicode, EntryPoint = "RegUnLoadKeyW", SetLastError = true)]
	internal static extern int RegUnLoadKey(uint hKey,
		[MarshalAs(UnmanagedType.LPWStr)] [Optional]
		string lpSubKey);

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "LoadLibraryExW", SetLastError = true)]
	internal static extern SafeLibraryHandle LoadLibraryEx(string lpFileName, IntPtr hFile, uint dwFlags);

	[DllImport("advapi32.dll", CharSet = CharSet.Unicode, EntryPoint = "InitiateSystemShutdownW")]
	internal static extern int InitiateSystemShutdown(IntPtr lpMachineName, IntPtr lpMessage, int dwTimeout,
		bool bForceAppsClosed, bool bRebootAfterShutdown);

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