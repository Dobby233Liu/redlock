using System;
using System.Runtime.InteropServices;

namespace redlock
{
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
		internal static extern IntPtr LoadLibraryEx(string lpFileName, IntPtr hFile, uint dwFlags);

		[DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "FindResourceExW", SetLastError = true)]
		internal static extern IntPtr
			FindResourceEx(IntPtr hModule, IntPtr lpszType, IntPtr lpszName, ushort wLanguage);

		[DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "FindResourceExW", SetLastError = true)]
		internal static extern IntPtr
			FindResourceEx(IntPtr hModule, string lpszType, IntPtr lpszName, ushort wLanguage);

		[DllImport("kernel32.dll", SetLastError = true)]
		internal static extern int SizeofResource(IntPtr hInstance, IntPtr hResInfo);

		[DllImport("kernel32.dll", SetLastError = true)]
		internal static extern IntPtr LoadResource(IntPtr hModule, IntPtr hResData);

		[DllImport("kernel32.dll", SetLastError = true)]
		internal static extern bool FreeLibrary(IntPtr hModule);

		[DllImport("kernel32.dll", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Unicode,
			EntryPoint = "BeginUpdateResourceW", ExactSpelling = true, SetLastError = true)]
		internal static extern IntPtr BeginUpdateResource(string pFileName, bool bDeleteExistingResources);

		[DllImport("kernel32.dll", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Unicode,
			EntryPoint = "UpdateResourceW", ExactSpelling = true, SetLastError = true)]
		internal static extern bool UpdateResource(IntPtr hUpdate, IntPtr lpType, IntPtr lpName, ushort wLanguage,
			byte[] lpData, uint cbData);

		[DllImport("kernel32.dll", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Unicode,
			EntryPoint = "UpdateResourceW", ExactSpelling = true, SetLastError = true)]
		internal static extern bool UpdateResource(IntPtr hUpdate, string lpType, IntPtr lpName, ushort wLanguage,
			byte[] lpData, uint cbData);

		[DllImport("kernel32.dll", CallingConvention = CallingConvention.StdCall, EntryPoint = "EndUpdateResourceW",
			ExactSpelling = true, SetLastError = true)]
		internal static extern bool EndUpdateResource(IntPtr hUpdate, bool fDiscard);

		[DllImport("kernel32.dll", SetLastError = true)]
		internal static extern IntPtr OpenProcess(int dwDesiredAccess, int blnheritHandle, int dwAppProcessId);

		[DllImport("advapi32.dll", SetLastError = true)]
		internal static extern int OpenProcessToken(IntPtr ProcessHandle, uint DesiredAccess, ref IntPtr TokenHandle);

		[DllImport("kernel32.dll", SetLastError = true)]
		internal static extern int GetCurrentProcessId();

		[DllImport("kernel32.dll", SetLastError = true)]
		internal static extern int CloseHandle(IntPtr hObject);

		[StructLayout(LayoutKind.Sequential, Pack = 1)]
		internal struct LUID
		{
			public int LowPart;

			public int HighPart;
		}
	
		[StructLayout(LayoutKind.Sequential, Pack = 1)]
		internal struct LUID_AND_ATTRIBUTES
		{
			public LUID pLuid;

			public int Attributes;
		}
		
		[StructLayout(LayoutKind.Sequential, Pack = 1)]
		internal struct TOKEN_PRIVILEGES
		{
			public int PrivilegeCount;

			public LUID_AND_ATTRIBUTES Privileges;
		}
		
		[DllImport("advapi32.dll", CharSet = CharSet.Unicode, EntryPoint = "LookupPrivilegeValueW",
			SetLastError = true)]
		internal static extern int LookupPrivilegeValue(int lpSystemName,
			[MarshalAs(UnmanagedType.LPWStr)] string lpName, ref LUID lpLuid);

		[DllImport("advapi32.dll", SetLastError = true)]
		internal static extern int AdjustTokenPrivileges(IntPtr TokenHandle, int DisableAllPriv,
			ref TOKEN_PRIVILEGES NewState, int BufferLength, int PreviousState, int ReturnLength);

		[DllImport("advapi32.dll", CharSet = CharSet.Unicode, EntryPoint = "InitiateSystemShutdownW")]
		internal static extern int InitiateSystemShutdown(IntPtr lpMachineName, IntPtr lpMessage, int dwTimeout,
			bool bForceAppsClosed, bool bRebootAfterShutdown);

		[DllImport("uxtheme.dll", EntryPoint = "#94")]
		internal static extern int GetImmersiveColorSetCount();

		public static bool AdjustPrivilege(string PrivilegeName, bool Enable)
		{
			var zero = IntPtr.Zero;
			var luid = default(NativeMethods.LUID);
			var token_PRIVILEGES = default(NativeMethods.TOKEN_PRIVILEGES);
			var num = 56U;
			var intPtr = NativeMethods.OpenProcess(2035711, 0, NativeMethods.GetCurrentProcessId());
			if (intPtr == IntPtr.Zero) return false;
			if (NativeMethods.OpenProcessToken(intPtr, num, ref zero) == 0)
			{
				if (intPtr != IntPtr.Zero) NativeMethods.CloseHandle(intPtr);
				return false;
			}

			if (NativeMethods.LookupPrivilegeValue(0, PrivilegeName, ref luid) == 0)
			{
				if (zero != IntPtr.Zero) NativeMethods.CloseHandle(zero);
				if (intPtr != IntPtr.Zero) NativeMethods.CloseHandle(intPtr);
				return false;
			}

			token_PRIVILEGES.PrivilegeCount = 1;
			token_PRIVILEGES.Privileges.pLuid = luid;
			token_PRIVILEGES.Privileges.Attributes = Enable ? 2 : 0;
			var num2 = NativeMethods.AdjustTokenPrivileges(zero, 0, ref token_PRIVILEGES, 0, 0, 0);
			if (zero != IntPtr.Zero) NativeMethods.CloseHandle(zero);
			if (intPtr != IntPtr.Zero) NativeMethods.CloseHandle(intPtr);
			return num2 != 0;
		}
	}
}