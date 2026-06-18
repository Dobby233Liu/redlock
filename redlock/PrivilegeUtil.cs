using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

// ReSharper disable InconsistentNaming

namespace redlock;
internal class PrivilegeUtil
{
	[DllImport("kernel32.dll", SetLastError = true)]
	private static extern SafeProcessHandle OpenProcess(int dwDesiredAccess, int blnheritHandle, int dwAppProcessId);

	[DllImport("advapi32.dll", SetLastError = true)]
	private static extern int OpenProcessToken(SafeProcessHandle ProcessHandle, uint DesiredAccess, out SafeAccessTokenHandle TokenHandle);

	[DllImport("kernel32.dll", SetLastError = true)]
	private static extern int GetCurrentProcessId();

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
		
	[DllImport("advapi32.dll", CharSet = CharSet.Unicode, EntryPoint = "LookupPrivilegeValueW",
		SetLastError = true)]
	private static extern int LookupPrivilegeValue(int lpSystemName,
		[MarshalAs(UnmanagedType.LPWStr)] string lpName, ref LUID lpLuid);

	[DllImport("advapi32.dll", SetLastError = true)]
	private static extern int AdjustTokenPrivileges(SafeAccessTokenHandle TokenHandle, int DisableAllPriv,
		ref TOKEN_PRIVILEGES NewState, int BufferLength, int PreviousState, int ReturnLength);

	public static bool AdjustPrivilege(string name, bool enable)
	{
		using var proc = OpenProcess(2035711, 0, GetCurrentProcessId());
		if (proc.IsInvalid) return false;

		if (OpenProcessToken(proc, 56U, out var procToken) == 0)
			return false;

		using (procToken)
		{
			var luid = default(LUID);
			if (LookupPrivilegeValue(0, name, ref luid) == 0)
				return false;

			var tokenPrivileges = default(TOKEN_PRIVILEGES);
			tokenPrivileges.PrivilegeCount = 1;
			tokenPrivileges.Privileges.pLuid = luid;
			tokenPrivileges.Privileges.Attributes = enable ? 2 : 0;
				
			return AdjustTokenPrivileges(procToken, 0, ref tokenPrivileges, 0, 0, 0) != 0;
		}
	}
}