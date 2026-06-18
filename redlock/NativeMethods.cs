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

	[DllImport("advapi32.dll", CharSet = CharSet.Unicode, EntryPoint = "InitiateSystemShutdownW")]
	internal static extern int InitiateSystemShutdown(IntPtr lpMachineName, IntPtr lpMessage, int dwTimeout,
		bool bForceAppsClosed, bool bRebootAfterShutdown);

	[DllImport("uxtheme.dll", EntryPoint = "#94")]
	internal static extern int GetImmersiveColorSetCount();
}