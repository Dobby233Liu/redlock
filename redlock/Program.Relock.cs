using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.Win32;

namespace redlock;

internal static partial class Program
{
	private static void Relock()
	{
		DisableSpp();

		Console.WriteLine("[i] Cleaning up product policies");
		using (var productOptions =
		       Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\ProductOptions", true))
		{
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
					if (productPolicy.Policies.ContainsKey(key)) productPolicy.Policies.Remove(key);
				}

				productOptions.SetValue("ProductPolicy",
					productPolicy.Serialize().ToArray(), RegistryValueKind.Binary);
			}
		}

		Console.WriteLine("[i] Removing Redpill values (HKLM)");
		using (var explorerConfig =
		       Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer", true))
		{
			explorerConfig.DeleteValue("RPEnabled", false);
			explorerConfig.DeleteValue("RPInstalled", false);
			explorerConfig.DeleteValue("RPStore", false);
			explorerConfig.DeleteValue("RPVersion", false);
		}

		using (var explorerAdvConfig =
		       Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Advanced",
			       true))
		{
			explorerAdvConfig.DeleteValue("SHSXSWasEnabled", false);
		}

		try
		{
			using var webcamEnablerConfig =
				Registry.LocalMachine.OpenSubKey(
					@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\GRE_Initialize", true);
			webcamEnablerConfig.DeleteValue("RemoteFontBootCacheFlags", false);
		}
		catch
		{
		}

		try
		{
			using var pdfReaderConfig =
				Registry.LocalMachine.OpenSubKey(
					@"SOFTWARE\Microsoft\Windows\CurrentVersion\Applets\Paint\Capabilities", true);
			pdfReaderConfig.DeleteValue("CLSID", false);
		}
		catch
		{
		}

		try
		{
			using var taskUiConfig =
				Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\TaskUI", true);
			taskUiConfig.DeleteValue("TaskUIEnabled", false);
			taskUiConfig.DeleteValue("TaskUIRefreshEnabled", false);
			taskUiConfig.DeleteValue("TaskUIOnImmersive", false);
		}
		catch
		{
		}

		try
		{
			using var ribbonConfig =
				Registry.ClassesRoot.OpenSubKey("CLSID\\{4F12FF5D-D319-4A79-8380-9CC80384DC08}", true);
			ribbonConfig.DeleteValue("AppID", false);
		}
		catch
		{
		}

		try
		{
			using var autoPlayConfig =
				Registry.LocalMachine.OpenSubKey(
					@"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\AutoplayHandlers", true);
			autoPlayConfig.DeleteValue("ShowFlyout", false);
		}
		catch
		{
		}
		
		RemoveHKCUValues();
		
		Directory.SetCurrentDirectory(Environment.SystemDirectory);
		if (File.Exists("shsxs.dll"))
		{
			Console.WriteLine("[i] Removing SHSxS");
			DeleteWithAttrCheck("shsxs.dll");
			File.Delete("shsxs.dll");
			if (IntPtr.Size == 8)
				DeleteWithAttrCheck(Environment.GetFolderPath(Environment.SpecialFolder.SystemX86) + "\\shsxs.dll");
		}
		if (File.Exists("SysResetRedPill.xml"))
		{
			Console.WriteLine("[i] Removing System Reset manifest");
			File.Delete("SysResetRedPill.xml");
		}
		if (File.Exists("redpill.log"))
		{
			Console.WriteLine("[i] Removing static Redpill setup log");
			File.Delete("redpill.log");
		}

		Console.WriteLine("[i] Removing Redpill certificates");
		Registry.LocalMachine.DeleteSubKeyTree(
			@"SOFTWARE\Microsoft\SystemCertificates\ROOT\Certificates\7721AC1150970D0B6A4B47AAEA73770712C907C5",
			false);

		UnregisterMie();

		ResourcePatches.RevertDuiMuiPatches();

		RebootToSystem();
	}

	private static void DeleteWithAttrCheck(string filePath)
	{
		if (File.Exists(filePath))
		{
			var fileInfo = new FileInfo(filePath);
			if ((fileInfo.Attributes & FileAttributes.ReadOnly) == FileAttributes.ReadOnly)
				fileInfo.Attributes &= ~FileAttributes.ReadOnly;
			File.Delete(filePath);
		}
	}

	private static void RemoveHKCUValues()
	{
		Console.WriteLine("[i] Removing Redpill values (HKCU)");
		foreach (var userKey in RegistryUtil.OpenUserHives())
		{
			using (var rpConfig = userKey.OpenSubKey(
				       @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer", true))
			{
				rpConfig.DeleteValue("RPEnabled", false);
				rpConfig.DeleteValue("RPInstalled", false);
			}

			using (var explorerAdvConfig = userKey.OpenSubKey(
				       @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Advanced", true))
			{
				explorerAdvConfig.DeleteValue("SHSXSWasEnabled", false);
			}

			using (var desktopConfig = userKey.OpenSubKey("Control Panel\\Desktop", true))
			{
				desktopConfig.DeleteValue("FastWallpaperRendering", false);
			}
		}
	}

	private static void UnregisterMie()
	{
		AttemptMIEUninstall();
		Console.WriteLine("[i] Unregistering Immersive Browser");
		using (var appRegistry = Registry.LocalMachine.OpenSubKey("Software\\RegisteredApplications", true))
		{
			appRegistry.DeleteValue("Immersive Browser", false);
		}
		Registry.LocalMachine.DeleteSubKeyTree(
			@"SOFTWARE\Microsoft\Active Setup\Installed Components\{8E7E60C6-4CE5-476D-9E31-FD450F3F792F}",
			false);
		using (var explorerConfig =
		       Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer", true))
		{
			explorerConfig.DeleteValue("MIEInstallResult", false);
		}
	}
	
	private static void AttemptMIEUninstall()
	{
		var mieManifests = Directory.GetFiles(
			Environment.GetFolderPath(Environment.SpecialFolder.Windows) + @"\servicing\Packages",
			"Microsoft-Windows-ImmersiveBrowser-Package~*~~*.mum");
		if (mieManifests.Length == 0) return;
		
		Console.WriteLine("[i] Uninstalling Immersive Browser");
		Process.Start("dism.exe",
			"/online /NoRestart /Disable-Feature /FeatureName:Immersive-Browser /PackageName:" +
			mieManifests[0].Split('\\').Last().Replace(".mum", "")).WaitForExit();
	}
}