using System;
using System.Linq;
using Microsoft.Win32;

namespace redlock;

public class BaseAction
{
	protected void DisableSpp()
	{
		Console.WriteLine("[i] Disabling Software Protection Service");
		using var sppsvcConfig =
			Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\services\sppsvc", true);
		sppsvcConfig.SetValue("Start", 4, RegistryValueKind.DWord);
	}
}