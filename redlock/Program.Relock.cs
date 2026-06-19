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
		       Registry.LocalMachine.OpenSubKey("SYSTEM\\CurrentControlSet\\services\\sppsvc", true))
		{
			sppsvcConfig.SetValue("Start", 4, RegistryValueKind.DWord);
		}

		Console.WriteLine("[i] Cleaning up product policies");
		using (var productOptions =
		       Registry.LocalMachine.OpenSubKey("SYSTEM\\CurrentControlSet\\Control\\ProductOptions", true))
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
		       Registry.LocalMachine.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Explorer", true))
		{
			registryKey3.DeleteValue("RPEnabled", false);
			registryKey3.DeleteValue("RPInstalled", false);
			registryKey3.DeleteValue("RPStore", false);
			registryKey3.DeleteValue("RPVersion", false);
		}

		using (var registryKey4 =
		       Registry.LocalMachine.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Explorer\\Advanced",
			       true))
		{
			registryKey4.DeleteValue("SHSXSWasEnabled", false);
		}

		try
		{
			using (var registryKey5 =
			       Registry.LocalMachine.OpenSubKey(
				       "SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\GRE_Initialize", true))
			{
				registryKey5.DeleteValue("RemoteFontBootCacheFlags", false);
			}
		}
		catch
		{
		}

		try
		{
			using (var registryKey6 =
			       Registry.LocalMachine.OpenSubKey(
				       "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Applets\\Paint\\Capabilities", true))
			{
				registryKey6.DeleteValue("CLSID", false);
			}
		}
		catch
		{
		}

		try
		{
			using (var registryKey7 =
			       Registry.LocalMachine.OpenSubKey(
				       "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Explorer\\AutoplayHandlers", true))
			{
				registryKey7.DeleteValue("ShowFlyout", false);
			}
		}
		catch
		{
		}

		try
		{
			using (var registryKey8 =
			       Registry.LocalMachine.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\TaskUI", true))
			{
				registryKey8.DeleteValue("TaskUIEnabled", false);
				registryKey8.DeleteValue("TaskUIRefreshEnabled", false);
				registryKey8.DeleteValue("TaskUIOnImmersive", false);
			}
		}
		catch
		{
		}

		try
		{
			using (var registryKey9 =
			       Registry.ClassesRoot.OpenSubKey("CLSID\\{4F12FF5D-D319-4A79-8380-9CC80384DC08}", true))
			{
				registryKey9.DeleteValue("AppID", false);
			}
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
			"SOFTWARE\\Microsoft\\SystemCertificates\\ROOT\\Certificates\\7721AC1150970D0B6A4B47AAEA73770712C907C5",
			false);
		AttemptMIEUninstall();
		Console.WriteLine("[i] Unregistering Immersive Browser");
		using (var registryKey10 = Registry.LocalMachine.OpenSubKey("Software\\RegisteredApplications", true))
		{
			registryKey10.DeleteValue("Immersive Browser", false);
		}

		Registry.LocalMachine.DeleteSubKeyTree(
			"SOFTWARE\\Microsoft\\Active Setup\\Installed Components\\{8E7E60C6-4CE5-476D-9E31-FD450F3F792F}",
			false);
		using (var registryKey11 =
		       Registry.LocalMachine.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Explorer", true))
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
		var subKeyNames = Registry.Users.GetSubKeyNames();
		foreach (var text in subKeyNames)
		{
			Console.WriteLine(" -> SID {0}", text);
			try
			{
				using (var registryKey =
				       Registry.Users.OpenSubKey(
					       Path.Combine(text, "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Explorer"), true))
				{
					registryKey.DeleteValue("RPEnabled", false);
					registryKey.DeleteValue("RPInstalled", false);
				}

				using (var registryKey2 = Registry.Users.OpenSubKey(
					       Path.Combine(text, "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Explorer\\Advanced"),
					       true))
				{
					registryKey2.DeleteValue("SHSXSWasEnabled", false);
				}

				using (var registryKey3 =
				       Registry.Users.OpenSubKey(Path.Combine(text, "Control Panel\\Desktop"), true))
				{
					registryKey3.DeleteValue("FastWallpaperRendering", false);
				}
			}
			catch
			{
			}
		}

		string[] subKeyNames2;
		using (var registryKey4 =
		       Registry.LocalMachine.OpenSubKey("SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\ProfileList"))
		{
			subKeyNames2 = registryKey4.GetSubKeyNames();
		}

		PrivilegeUtil.AdjustPrivilege("SeBackupPrivilege", true);
		PrivilegeUtil.AdjustPrivilege("SeRestorePrivilege", true);
		foreach (var text2 in subKeyNames2)
			if (!subKeyNames.Contains(text2))
				using (var registryKey5 = Registry.LocalMachine.OpenSubKey(
					       Path.Combine("SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\ProfileList", text2)))
				{
					if (registryKey5.GetValueNames().Contains("ProfileImagePath"))
					{
						Console.WriteLine(" -> SID {0}", text2);
						var text3 = (string)registryKey5.GetValue("ProfileImagePath");
						if (NativeMethods.RegLoadKey(2147483651U, text2, Path.Combine(text3, "NTuser.dat")) == 0)
							try
							{
								using (var registryKey6 = Registry.Users.OpenSubKey(
									       Path.Combine(text2,
										       "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Explorer"), true))
								{
									registryKey6.DeleteValue("RPEnabled", false);
									registryKey6.DeleteValue("RPInstalled", false);
								}

								using (var registryKey7 = Registry.Users.OpenSubKey(
									       Path.Combine(text2,
										       "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Explorer\\Advanced"),
									       true))
								{
									registryKey7.DeleteValue("SHSXSWasEnabled", false);
								}

								using (var registryKey8 =
								       Registry.Users.OpenSubKey(Path.Combine(text2, "Control Panel\\Desktop"),
									       true))
								{
									registryKey8.DeleteValue("FastWallpaperRendering", false);
								}
							}
							finally
							{
								NativeMethods.RegUnLoadKey(2147483651U, text2);
							}
					}
				}

		Console.WriteLine(" -> Default user");
		var text4 = new DirectoryInfo(Environment.GetFolderPath(Environment.SpecialFolder.CommonDocuments)).Parent
			.Parent.FullName + "\\Default\\NTuser.dat";
		if (NativeMethods.RegLoadKey(2147483651U, "Default", text4) == 0)
			try
			{
				using (var registryKey9 = Registry.Users.OpenSubKey(
					       Path.Combine("Default", "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Explorer"), true))
				{
					registryKey9.DeleteValue("RPEnabled", false);
					registryKey9.DeleteValue("RPInstalled", false);
				}

				using (var registryKey10 = Registry.Users.OpenSubKey(
					       Path.Combine("Default",
						       "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Explorer\\Advanced"), true))
				{
					registryKey10.DeleteValue("SHSXSWasEnabled", false);
				}

				using (var registryKey11 =
				       Registry.Users.OpenSubKey(Path.Combine("Default", "Control Panel\\Desktop"), true))
				{
					registryKey11.DeleteValue("FastWallpaperRendering", false);
				}
			}
			finally
			{
				NativeMethods.RegUnLoadKey(2147483651U, "Default");
			}
	}

	private static void AttemptMIEUninstall()
	{
		var files = Directory.GetFiles(
			Environment.GetFolderPath(Environment.SpecialFolder.Windows) + "\\servicing\\Packages",
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