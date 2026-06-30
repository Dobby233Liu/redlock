using System;
using Microsoft.Win32;

namespace redlock;

internal class BaseAction
{
	protected readonly RegistryKey Hklm = Registry.LocalMachine;
	protected readonly RegistryKey Hkcr = Registry.ClassesRoot;
	
	protected void DisableSpp()
	{
		Console.WriteLine("[i] Disabling Software Protection Service");
		using var sppSvcConfig = Hklm.OpenSubKey(RegKeyConstants.SppSvc, true);
		sppSvcConfig.SetValue("Start", 4, RegistryValueKind.DWord);
	}
}