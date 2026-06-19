using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.Win32;

namespace redlock;

internal static partial class Program
{
	private static void RelockInAudit()
	{
		Console.WriteLine("[i] Disabling Software Protection Service");
		using (var sppsvcConfig =
		       Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\services\sppsvc", true))
		{
			sppsvcConfig.SetValue("Start", 4, RegistryValueKind.DWord);
		}

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
		using (var registryKey3 =
		       Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer", true))
		{
			registryKey3.DeleteValue("RPEnabled", false);
			registryKey3.DeleteValue("RPInstalled", false);
			registryKey3.DeleteValue("RPStore", false);
			registryKey3.DeleteValue("RPVersion", false);
		}

		using (var registryKey4 =
		       Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Advanced",
			       true))
		{
			registryKey4.DeleteValue("SHSXSWasEnabled", false);
		}

		try
		{
			using var registryKey5 =
				Registry.LocalMachine.OpenSubKey(
					@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\GRE_Initialize", true);
			registryKey5.DeleteValue("RemoteFontBootCacheFlags", false);
		}
		catch
		{
		}

		try
		{
			using var registryKey6 =
				Registry.LocalMachine.OpenSubKey(
					@"SOFTWARE\Microsoft\Windows\CurrentVersion\Applets\Paint\Capabilities", true);
			registryKey6.DeleteValue("CLSID", false);
		}
		catch
		{
		}

		try
		{
			using var registryKey7 =
				Registry.LocalMachine.OpenSubKey(
					@"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\AutoplayHandlers", true);
			registryKey7.DeleteValue("ShowFlyout", false);
		}
		catch
		{
		}

		try
		{
			using var registryKey8 =
				Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\TaskUI", true);
			registryKey8.DeleteValue("TaskUIEnabled", false);
			registryKey8.DeleteValue("TaskUIRefreshEnabled", false);
			registryKey8.DeleteValue("TaskUIOnImmersive", false);
		}
		catch
		{
		}

		try
		{
			using var registryKey9 =
				Registry.ClassesRoot.OpenSubKey("CLSID\\{4F12FF5D-D319-4A79-8380-9CC80384DC08}", true);
			registryKey9.DeleteValue("AppID", false);
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
		AttemptMIEUninstall();
		Console.WriteLine("[i] Unregistering Immersive Browser");
		using (var registryKey10 = Registry.LocalMachine.OpenSubKey("Software\\RegisteredApplications", true))
		{
			registryKey10.DeleteValue("Immersive Browser", false);
		}

		Registry.LocalMachine.DeleteSubKeyTree(
			@"SOFTWARE\Microsoft\Active Setup\Installed Components\{8E7E60C6-4CE5-476D-9E31-FD450F3F792F}",
			false);
		using (var registryKey11 =
		       Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer", true))
		{
			registryKey11.DeleteValue("MIEInstallResult", false);
		}

		RevertDuiMuiPatches();
		using (var registryKey12 = Registry.LocalMachine.OpenSubKey("SYSTEM\\Setup", true))
		{
			if (registryKey12.GetValueNames().Contains("SetupTypeBak"))
			{
				Console.WriteLine("[i] Preparing to reboot");
				var num = (int?)registryKey12.GetValue("SetupTypeBak");
				var text2 = (string)registryKey12.GetValue("CmdLineBak");
				registryKey12.SetValue("SetupType", num, RegistryValueKind.DWord);
				registryKey12.SetValue("CmdLine", text2, RegistryValueKind.String);
				registryKey12.DeleteValue("SetupTypeBak", false);
				registryKey12.DeleteValue("CmdLineBak", false);
			}
		}
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
		RegistryUtil.ForEachUser((userKey, _) =>
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
		});
	}

	private static void AttemptMIEUninstall()
	{
		var files = Directory.GetFiles(
			Environment.GetFolderPath(Environment.SpecialFolder.Windows) + @"\servicing\Packages",
			"Microsoft-Windows-ImmersiveBrowser-Package~*~~*.mum");
		if (files.Length != 0)
		{
			Console.WriteLine("[i] Uninstalling Immersive Browser");
			Process.Start("dism.exe",
				"/online /NoRestart /Disable-Feature /FeatureName:Immersive-Browser /PackageName:" +
				files[0].Split('\\').Last().Replace(".mum", "")).WaitForExit();
		}
	}
}