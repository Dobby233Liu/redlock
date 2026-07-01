using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace redlock;

internal class BaseAction
{
	protected readonly RegistryKey Hklm = Registry.LocalMachine;
	protected readonly RegistryKey Hkcr = Registry.ClassesRoot;
	
	internal string SystemDirectory => Environment.SystemDirectory;
	
	internal string SystemX86Directory => Environment.GetFolderPath(Environment.SpecialFolder.SystemX86);
	
	internal string GetSystemFile(string relativePath, bool isWoW = false)
	{
		return Path.Combine(!isWoW ? SystemDirectory : SystemX86Directory, relativePath);
	}
	
	internal int GetBuildNumber()
	{
		using var currentVersion = Hklm.OpenSubKey(RegKeyConstants.CurrentVersion);
		if (currentVersion is null) return -1;
		return int.Parse((string)currentVersion.GetValue("CurrentBuild", "-1"));
	}
	
	internal void DisableSpp()
	{
		Console.WriteLine("[i] Disabling Software Protection Service");
		using var sppSvcConfig = Hklm.OpenSubKey(RegKeyConstants.SppSvc, true);
		sppSvcConfig?.SetValue("Start", 4, RegistryValueKind.DWord);
	}

	internal int GetImmersiveColorSetCount()
	{
		[DllImport("uxtheme.dll", EntryPoint = "#94")]
		static extern int _GetImmersiveColorSetCount();
		return _GetImmersiveColorSetCount();
	}
}