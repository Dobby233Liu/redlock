using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.Win32;

namespace redlock;

internal class RelockAction : BaseAction
{
	internal void Perform()
	{
		DisableSpp();

		Console.WriteLine("[i] Cleaning up product policies");
		using (var productOptions = Hklm.OpenSubKey(RegKeyConstants.ProductOptions, true))
		{
			if (productOptions is not null)
				if (productOptions.GetValueNames().Contains("ProductPolicyBkp"))
				{
					productOptions.SetValue("ProductPolicy", (byte[])productOptions.GetValue("ProductPolicyBkp"),
						RegistryValueKind.Binary);
					productOptions.DeleteValue("ProductPolicyBkp");
				}
				else
				{
					var oldPolicy = (byte[])productOptions.GetValue("ProductPolicy");
					productOptions.SetValue("ProductPolicyBkp", oldPolicy, RegistryValueKind.Binary);

					var productPolicy = new ProductPolicy().Deserialize(oldPolicy);
					for (var i = 1; i <= 9; i++)
					{
						var key = $"SLC-Component-RP-0{i}";
						productPolicy.Policies.Remove(key);
					}

					productOptions.SetValue("ProductPolicy",
						productPolicy.Serialize().ToArray(), RegistryValueKind.Binary);
				}
		}

		Console.WriteLine("[i] Removing Redpill values (HKLM)");
		using (var explorerConfig = Hklm.OpenSubKey(RegKeyConstants.Explorer, true))
		{
			explorerConfig?.DeleteValue("RPEnabled", false);
			explorerConfig?.DeleteValue("RPInstalled", false);
			explorerConfig?.DeleteValue("RPStore", false);
			explorerConfig?.DeleteValue("RPVersion", false);
		}
		
#if TWINUI_FIX
		var origTwinUiPath = GetSystemFile("twinui.dll.orig");
		if (File.Exists(origTwinUiPath))
		{
			File.Copy(origTwinUiPath, GetSystemFile("twinui.dll"), true);
			DeleteWithAttrCheck(origTwinUiPath);
		}
#endif
		
		using (var explorerAdvConfig = Hklm.OpenSubKey(RegKeyConstants.ExplorerAdv, true))
		{
			explorerAdvConfig?.DeleteValue("SHSXSWasEnabled", false);
			explorerAdvConfig?.DeleteValue("RibbonizeMePlease", false);
		}

		using var webcamEnablerConfig = Hklm.OpenSubKey(RegKeyConstants.WebcamEnablement, true);
		webcamEnablerConfig?.DeleteValue("RemoteFontBootCacheFlags", false);

		using var pdfReaderConfig = Hklm.OpenSubKey(RegKeyConstants.PdfReaderCap, true);
		pdfReaderConfig?.DeleteValue("CLSID", false);
		
		using var taskUiConfig = Hklm.OpenSubKey(RegKeyConstants.TaskUi, true);
		taskUiConfig?.DeleteValue("TaskUIEnabled", false);
		taskUiConfig?.DeleteValue("TaskUIRefreshEnabled", false);
		taskUiConfig?.DeleteValue("TaskUIOnImmersive", false);

		using var ribbonConfig = Hkcr.OpenSubKey(RegKeyConstants.RibbonClass, true);
		ribbonConfig?.DeleteValue("AppID", false);
		
		using var autoPlayConfig = Hklm.OpenSubKey(RegKeyConstants.AutoPlayHandlers, true);
		autoPlayConfig?.DeleteValue("ShowFlyout", false);
		
		RemoveHKCUValues();
		
		Directory.SetCurrentDirectory(SystemDirectory);
		if (File.Exists("shsxs.dll"))
		{
			Console.WriteLine("[i] Removing SHSxS");
			DeleteWithAttrCheck("shsxs.dll");
			if (Is64BitOperatingSystem)
				DeleteWithAttrCheck(GetSystemFile("shsxs.dll", true));
		}
		if (File.Exists("SysResetRedPill.xml"))
		{
			Console.WriteLine("[i] Removing System Reset manifest");
			File.Delete("SysResetRedPill.xml");
		}
		if (File.Exists("redpill.log"))
		{
			Console.WriteLine("[i] Removing Redpill setup log");
			File.Delete("redpill.log");
		}

		Console.WriteLine("[i] Removing Redpill certificates");
		Hklm.DeleteSubKeyTree(
			@"SOFTWARE\Microsoft\SystemCertificates\ROOT\Certificates\7721AC1150970D0B6A4B47AAEA73770712C907C5",
			false);

		UnregisterMie();

		RevertDuiMuiPatches();
	}

	private void DeleteWithAttrCheck(string filePath)
	{
		if (!File.Exists(filePath)) return;
		var info = new FileInfo(filePath);
		if (info.Attributes.HasFlag(FileAttributes.ReadOnly))
			info.Attributes &= ~FileAttributes.ReadOnly;
		File.Delete(filePath);
	}

	private void RemoveHKCUValues()
	{
		Console.WriteLine("[i] Removing Redpill values (HKCU)");
		foreach (var userKey in RegistryUtil.OpenUserHives())
		{
			using (var rpConfig = userKey.OpenSubKey(RegKeyConstants.Explorer, true))
			{
				rpConfig?.DeleteValue("RPEnabled", false);
				rpConfig?.DeleteValue("RPInstalled", false);
			}

			using (var explorerAdvConfig = userKey.OpenSubKey(RegKeyConstants.ExplorerAdv, true))
			{
				explorerAdvConfig?.DeleteValue("SHSXSWasEnabled", false);
			}

			using (var desktopConfig = userKey.OpenSubKey(RegKeyConstants.Desktop, true))
			{
				desktopConfig?.DeleteValue("FastWallpaperRendering", false);
			}
		}
	}

	private void UnregisterMie()
	{
		AttemptMIEUninstall();
		Console.WriteLine("[i] Unregistering Immersive Browser");
		using (var appRegistry = Hklm.OpenSubKey(RegKeyConstants.Apps, true))
		{
			appRegistry?.DeleteValue("Immersive Browser", false);
		}
		Hklm.DeleteSubKeyTree(RegKeyConstants.MieSetupData, false);
		using (var explorerConfig = Hklm.OpenSubKey(RegKeyConstants.Explorer, true))
		{
			explorerConfig?.DeleteValue("MIEInstallResult", false);
		}
	}
	
	private void AttemptMIEUninstall()
	{
		var mieManifests = SetupUtil.GetMieManifests(WindowsDirectory);
		if (mieManifests.Length == 0) return;
		
		Console.WriteLine("[i] Uninstalling Immersive Browser");
		var proc = Process.Start("dism.exe",
			"/online /NoRestart /Disable-Feature /FeatureName:Immersive-Browser /PackageName:" +
			Path.GetFileNameWithoutExtension(mieManifests[0]));
		proc?.WaitForExit();
	}

	private void RevertDuiMuiPatches()
	{
		var muiFiles = GetMuiFilesForFile(GetSystemFile("dui70.dll"));
		muiFiles = muiFiles.Concat(GetMuiFilesForFile(GetSystemFile("dui70.dll", true)));
		foreach (var muiEntry in muiFiles)
		{
			string muiFile = muiEntry.Value, origMuiFile = muiFile + ".orig";
			if (!File.Exists(origMuiFile)) continue;
			File.Delete(muiFile);
			File.Move(origMuiFile, muiFile);
		}
	}
}