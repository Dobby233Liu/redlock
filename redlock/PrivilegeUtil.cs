using System;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

// ReSharper disable InconsistentNaming

namespace redlock;

internal static class PrivilegeUtil
{
	private const int TOKEN_QUERY = 0x0008;
	private const int TOKEN_QUERY_SOURCE = 0x0010;
	private const int TOKEN_ADJUST_PRIVILEGES = 0x0020;
	private const int SE_PRIVILEGE_ENABLED = 0x02;

	[StructLayout(LayoutKind.Sequential, Pack = 1)]
	private struct LUID
	{
		public int LowPart;

		public int HighPart;
	}

	[StructLayout(LayoutKind.Sequential, Pack = 1)]
	private struct LUID_AND_ATTRIBUTES
	{
		public LUID pLuid;

		public int Attributes;
	}

	[StructLayout(LayoutKind.Sequential, Pack = 1)]
	private struct TOKEN_PRIVILEGES
	{
		public int PrivilegeCount;

		public LUID_AND_ATTRIBUTES Privileges;
	}

	[DllImport("kernel32.dll")]
	private static extern SafeProcessHandle GetCurrentProcess();

	[DllImport("advapi32.dll", SetLastError = true)]
	private static extern int OpenProcessToken(SafeProcessHandle ProcessHandle, uint DesiredAccess,
		out SafeAccessTokenHandle TokenHandle);

	[DllImport("advapi32.dll", CharSet = CharSet.Unicode, EntryPoint = "LookupPrivilegeValueW",
		SetLastError = true)]
	private static extern int LookupPrivilegeValue(IntPtr lpSystemName,
		[MarshalAs(UnmanagedType.LPWStr)] string lpName, ref LUID lpLuid);

	[DllImport("advapi32.dll", SetLastError = true)]
	private static extern int AdjustTokenPrivileges(SafeAccessTokenHandle TokenHandle, bool DisableAllPriv,
		ref TOKEN_PRIVILEGES NewState, int BufferLength, IntPtr PreviousState, IntPtr ReturnLength);

	internal static bool AdjustPrivilege(string name, bool enable)
	{
		using var proc = GetCurrentProcess();

		if (OpenProcessToken(proc, TOKEN_QUERY | TOKEN_QUERY_SOURCE | TOKEN_ADJUST_PRIVILEGES, out var procToken) == 0)
			return false;
		using (procToken)
		{
			var luid = default(LUID);
			if (LookupPrivilegeValue(IntPtr.Zero, name, ref luid) == 0)
				return false;

			var privileges = default(TOKEN_PRIVILEGES);
			privileges.PrivilegeCount = 1;
			privileges.Privileges.pLuid = luid;
			privileges.Privileges.Attributes = enable ? SE_PRIVILEGE_ENABLED : 0;

			return AdjustTokenPrivileges(procToken, false, ref privileges, 0, IntPtr.Zero, IntPtr.Zero) != 0;
		}
	}
}