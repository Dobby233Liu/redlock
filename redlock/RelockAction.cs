using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.Win32;

namespace redlock;

internal class RelockAction : BaseAction
{
	private void DeleteWithAttrCheck(string filePath)
	{
		if (!File.Exists(filePath)) return;

		var info = new FileInfo(filePath);
		if (info.Attributes.HasFlag(FileAttributes.ReadOnly))
			info.Attributes &= ~FileAttributes.ReadOnly;

		File.Delete(filePath);
	}

	internal void Perform()
	{
		SetUpProductPolicies();

		RemoveHKLMValues();
		RemoveHKCUValues();

#if FIX_MALFORMED_TWINUI
		var origTWinUiPath = GetSystemFile("twinui.dll.orig");
		if (File.Exists(origTWinUiPath))
		{
			Console.WriteLine("[i] Reverting TWinUI to the original version");
			File.Copy(origTWinUiPath, GetSystemFile("twinui.dll"), true);
			DeleteWithAttrCheck(origTWinUiPath);
		}
#endif

		var shsxsPath = GetSystemFile("shsxs.dll");
		if (File.Exists(shsxsPath))
		{
			Console.WriteLine("[i] Removing SHSxS");
			DeleteWithAttrCheck(shsxsPath);
			if (Is64BitOperatingSystem)
				DeleteWithAttrCheck(GetSystemFile("shsxs.dll", true));
		}

		RevertDuiMuiPatches();

		var sysResetRedPillPath = Path.Combine(SystemDirectory, "SysResetRedPill.xml");
		if (File.Exists(sysResetRedPillPath))
		{
			Console.WriteLine("[i] Removing System Reset manifest");
			File.Delete(sysResetRedPillPath);
		}

		var redPillLogPath = Path.Combine(SystemDirectory, "redpill.log");
		if (File.Exists(redPillLogPath))
		{
			Console.WriteLine("[i] Removing Redpill setup log");
			File.Delete(redPillLogPath);
		}

		Console.WriteLine("[i] Removing Redpill certificates");
		Hklm.DeleteSubKeyTree(RegKeyConstants.RpCert, false);

		UnregisterMie();
	}

	private void SetUpProductPolicies()
	{
		DisableSpp();

		Console.WriteLine("[i] Cleaning up product policies");
		using var productOptions = Hklm.OpenSubKey(RegKeyConstants.ProductOptions, true);
		if (productOptions is null) return;

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

	private void RemoveHKLMValues()
	{
		Console.WriteLine("[i] Removing Redpill values (HKLM)");

		using (var explorerConfig = Hklm.OpenSubKey(RegKeyConstants.Explorer, true))
		{
			explorerConfig?.DeleteValue("RPEnabled", false);
			explorerConfig?.DeleteValue("RPInstalled", false);
			explorerConfig?.DeleteValue("RPStore", false);
			explorerConfig?.DeleteValue("RPVersion", false);
		}

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
	}

	private void RemoveHKCUValues()
	{
		Console.WriteLine("[i] Removing Redpill values (HKCU)");
		foreach (var userKey in RegistryUtil.ForEachUserHive())
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
		var mieManifest = SetupUtil.GetMieManifest(WindowsDirectory);
		if (mieManifest is null) return;

		Console.WriteLine("[i] Uninstalling Immersive Browser");
		var proc = Process.Start("dism.exe",
			"/online /NoRestart /Disable-Feature /FeatureName:Immersive-Browser /PackageName:" +
			Path.GetFileNameWithoutExtension(mieManifest));
		proc?.WaitForExit();
	}

	private void RevertDuiMuiPatches()
	{
		var muiFiles = GetMuiForFile(GetSystemFile("dui70.dll"));
		muiFiles = muiFiles.Concat(GetMuiForFile(GetSystemFile("dui70.dll", true)));

		foreach (var muiEntry in muiFiles)
		{
			string muiFile = muiEntry.Value, origMuiFile = muiFile + ".orig";
			if (!File.Exists(origMuiFile)) continue;
			File.Delete(muiFile);
			File.Move(origMuiFile, muiFile);
		}
	}
}