using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace redlock;

internal class BaseAction
{
	internal bool Is64BitOperatingSystem => Environment.Is64BitOperatingSystem;
	
	protected readonly RegistryKey Hklm = Registry.LocalMachine;
	
	protected readonly RegistryKey Hkcr = Registry.ClassesRoot;
	
	internal string SystemDirectory => Environment.SystemDirectory;
	
	internal string SystemX86Directory => Environment.GetFolderPath(Environment.SpecialFolder.SystemX86);
	
	internal string WindowsDirectory => Environment.GetFolderPath(Environment.SpecialFolder.Windows);
	
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

	internal IEnumerable<KeyValuePair<int, string>> GetMuiFilesForFile(string baseFile)
	{
		baseFile = Path.GetFullPath(baseFile);
		if (!File.Exists(baseFile))
			yield break;
		
		var dir = Path.GetDirectoryName(baseFile) + Path.DirectorySeparatorChar;
		var muiName = $"{Path.GetFileName(baseFile)}.mui";
		
		bool TryMakeNewPath(string cultureId, out string path)
		{
			path = Path.Combine(dir, cultureId, muiName);
			return File.Exists(path); 
		}

		var installedCultures = CultureInfo.GetCultures(CultureTypes.InstalledWin32Cultures);
		foreach (var culture in installedCultures)
		{
			if (culture.Equals(CultureInfo.InvariantCulture))
				continue;
			if (TryMakeNewPath(culture.Name, out var path)
			    || TryMakeNewPath(culture.LCID.ToString(CultureInfo.InvariantCulture), out path))
				yield return new KeyValuePair<int, string>(culture.LCID, path);
		}
	}
	
	internal int GetImmersiveColorSetCount()
	{
		[DllImport("uxtheme.dll", EntryPoint = "#94")]
		static extern int _GetImmersiveColorSetCount();
		return _GetImmersiveColorSetCount();
	}
}