using System;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

// ReSharper disable InconsistentNaming

namespace redlock;

internal sealed class SafeLibraryHandle : SafeHandleZeroOrMinusOneIsInvalid
{
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