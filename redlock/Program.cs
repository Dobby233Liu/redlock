using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;
using redlock.Properties;

namespace redlock
{
	internal class Program
	{
		private static void Main(string[] args)
		{
			for (var i = 0; i < args.Length; i++)
				args[i] = args[i].ToLower();
			
			if (args.Contains("audit"))
			{
				UnlockInAudit(args);
				return;
			}

			if (args.Contains("auditu"))
			{
				RelockInAudit();
				return;
			}

			StandardRun();
		}

		private static void StandardRun()
		{
			var oldColor = Console.ForegroundColor;
			Console.ForegroundColor = ConsoleColor.Cyan;
			Console.Write("//");
			Console.ForegroundColor = ConsoleColor.White;
			Console.Write("selection");
			Console.ForegroundColor = ConsoleColor.Cyan;
			Console.WriteLine("/");
			Console.ForegroundColor = oldColor;
			
			Console.WriteLine("1) Install Redpill");
			Console.WriteLine("2) Install Redpill excluding SHSxS");
			Console.WriteLine("3) Install Redpill excluding policies");
			Console.WriteLine("4) Uninstall Redpill");
			Console.WriteLine("5) Exit\n");
			// TODO: could copy ourselves to the system temp directory?
			Console.WriteLine("! Make sure you're running this program from the system drive before proceeding\n");
			
			var selection = 0;
			while (selection is < 1 or > 5)
			{
				Console.Write("Select a mode: ");
				int.TryParse(Console.ReadLine(), out selection);
			}
			if (selection == 5) return;
			
			var args = new List<string>();
			args.Add(selection == 4 ? "auditu" : "audit");
			if (selection == 2) args.Add("noshsxs");
			if (selection == 3) args.Add("nopol");
			
			using var setupConfig = Registry.LocalMachine.OpenSubKey("SYSTEM\\Setup", true);
			var oldSetupType = (int?)setupConfig.GetValue("SetupType");
			if (oldSetupType.GetValueOrDefault() == 2 &&
			    Directory.GetFiles(Environment.GetFolderPath(Environment.SpecialFolder.Windows) + @"\servicing\Packages",
					    "Microsoft-Windows-ImmersiveBrowser-Package~*~~*.mum").Length != 0)
			{
				if (MessageBox.Show(
					    "Rebooting from OOBE on this install may take longer than expected due to Windows servicing, would you like to proceed?",
					    string.Empty, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.No)
					return;
				args.Add("queuemie");
			}

			var oldCmdLine = (string)setupConfig.GetValue("CmdLine");
			var cmdLine = Assembly.GetEntryAssembly().Location + " " + string.Join(" ", args);
			setupConfig.SetValue("SetupTypeBak", oldSetupType, RegistryValueKind.DWord);
			setupConfig.SetValue("CmdLineBak", oldCmdLine, RegistryValueKind.String);
			setupConfig.SetValue("SetupType", 1, RegistryValueKind.DWord);
			setupConfig.SetValue("CmdLine", cmdLine, RegistryValueKind.String);
			setupConfig.Close();
			
			Console.WriteLine("[i] Rebooting into Setup Mode");
			NativeMethods.AdjustPrivilege("SeShutdownPrivilege", true);
			NativeMethods.InitiateSystemShutdown(IntPtr.Zero, IntPtr.Zero, 0, false, true);
		}

		private static void UnlockInAudit(string[] args)
		{
			if (!args.Contains("nopol"))
			{
				Console.WriteLine("[i] Disabling Software Protection Service");
				using (var sppsvcConfig =
				       Registry.LocalMachine.OpenSubKey("SYSTEM\\CurrentControlSet\\services\\sppsvc", true))
				{
					sppsvcConfig.SetValue("Start", 4, RegistryValueKind.DWord);
				}

				Console.WriteLine("[i] Installing product policies");
				using (var productOptions =
				       Registry.LocalMachine.OpenSubKey("SYSTEM\\CurrentControlSet\\Control\\ProductOptions", true))
				{
					var oldPolicy = (byte[])productOptions.GetValue("ProductPolicy");
					productOptions.SetValue("ProductPolicyBkp", oldPolicy, RegistryValueKind.Binary);
					
					var policy = ProductPolicy.Deserialize(oldPolicy);
					for (var i = 1; i <= 9; i++) policy.SetValue($"SLC-Component-RP-0{i}", 1, true);
					policy.SetValue("WSLicensingService-EnableLOBApps", 0);
					policy.SetValue("WinStoreUI-Enabled", 1);
					policy.SetValue("explorer-CanSuppressStartMenuOnLogin", 0);
					policy.SetValue("explorer-ClientLoginExperienceAllowed", 1);
					policy.SetValue("explorer-DefaultLauncherLayout", 0);
					policy.SetValue("Security-SPP-GenuineLocalStatus", 1, true);
					
					productOptions.SetValue("ProductPolicy",
						policy.Serialize().ToArray(), RegistryValueKind.Binary);
				}
			}

			Console.WriteLine("[i] Setting up Redpill values (HKLM)");
			using (var machineRpConfig =
			       Registry.LocalMachine.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Explorer", true))
			{
				machineRpConfig.SetValue("RPEnabled", 1, RegistryValueKind.DWord);
				machineRpConfig.SetValue("RPInstalled", 1, RegistryValueKind.DWord);
				machineRpConfig.SetValue("RPStore", 1, RegistryValueKind.DWord);
			}

			SetUpSmartTweaks();
			SetUpHKCUValues();
			
			if (!args.Contains("noshsxs"))
			{
				var wowBinsPresent = IntPtr.Size == 8;
				var text2 = Environment.SystemDirectory + "\\shsxs.dll";
				var text3 = Environment.SystemDirectory + "\\twinui.dll";
				if (!File.Exists(text2) && File.Exists(text3))
				{
					var text4 = text2;
					var flag4 = true;
					var array2 = FindPatternsInFile(text3, new[]
					{
						Encoding.ASCII.GetBytes("RP_GetLayoutManagerBandDependencies"),
						Encoding.ASCII.GetBytes("RP_InitLauncherDataLayer")
					}, false);
					for (var j = 0; j < array2.Length; j++)
						if (array2[j] <= 0L)
						{
							flag4 = false;
							break;
						}

					using (var memoryStream = new MemoryStream(Resources.comp1))
					{
						using (var gzipStream = new GZipStream(memoryStream, CompressionMode.Decompress))
						{
							var array3 = new byte[2140160];
							gzipStream.Read(array3, 0, array3.Length);
							if (wowBinsPresent)
							{
								if (flag4)
								{
									array3[20015] = 114;
									array3[20040] = 48;
								}

								File.WriteAllBytes(text2, array3);
								text4 = Environment.GetFolderPath(Environment.SpecialFolder.SystemX86) + "\\shsxs.dll";
							}

							array3 = new byte[2139648];
							gzipStream.Read(array3, 0, array3.Length);
							if (flag4)
							{
								array3[16095] = 114;
								array3[16146] = 48;
							}

							File.WriteAllBytes(wowBinsPresent ? text4 : text2, array3);
						}
					}

					var array4 = FindPatternsInFile(Environment.SystemDirectory + "\\oobe\\msoobeplugins.dll", new[]
					{
						Encoding.Unicode.GetBytes("OOBEColorolorSet"),
						Encoding.Unicode.GetBytes("GradientColor")
					});
					if (array4[0] != 0L || array4[1] != 0L) ConformAccentResources(text2, wowBinsPresent ? text4 : null, text3);
					var requiredRPVersion =
						GetRequiredRPVersion(Environment.GetEnvironmentVariable("WINDIR") + "\\explorer.exe");
					if (requiredRPVersion != 26)
						using (var registryKey4 =
						       Registry.LocalMachine.OpenSubKey(
							       "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Explorer", true))
						{
							registryKey4.SetValue("RPVersion", requiredRPVersion, RegistryValueKind.DWord);
						}

					var uiFilePatchFlags = UiFilePatchFlags.None;
					if (requiredRPVersion > 23)
					{
						var array5 = FindPatternsInFile(Environment.SystemDirectory + "\\dui70.dll", new[]
						{
							Encoding.Unicode.GetBytes("TouchEditInner"),
							Encoding.ASCII.GetBytes("ItemHeightInPopup"),
							Encoding.Unicode.GetBytes("TouchSelectPopupHost"),
							Encoding.Unicode.GetBytes("WrappingList"),
							Encoding.Unicode.GetBytes("TouchCarouselScrollBar"),
							Encoding.ASCII.GetBytes("TouchSwitch"),
							Encoding.ASCII.GetBytes("TouchEdit@")
						});
						if (array5[0] < 0L) uiFilePatchFlags |= UiFilePatchFlags.TouchEditInner;
						if (array5[1] < 0L) uiFilePatchFlags |= UiFilePatchFlags.ItemHeightInPopup;
						if (array5[2] < 0L) uiFilePatchFlags |= UiFilePatchFlags.TouchSelectPopup;
						if (array5[3] < 0L) uiFilePatchFlags |= UiFilePatchFlags.WrappingList;
						if (array5[4] < 0L) uiFilePatchFlags |= UiFilePatchFlags.TouchCarouselScrollBar;
						if (array5[5] < 0L) uiFilePatchFlags |= UiFilePatchFlags.TouchSwitch;
						if (array5[6] < 0L) uiFilePatchFlags |= UiFilePatchFlags.TouchEditDeprecated;
					}

					if (uiFilePatchFlags != UiFilePatchFlags.None)
					{
						Console.WriteLine("[i] Patching native SHSxS");
						DoUiFilePatches(text2, uiFilePatchFlags);
						DoDuiMuiPatches(wowBinsPresent);

						if (wowBinsPresent)
						{
							Console.WriteLine("[i] Patching WoW SHSxS");
							DoUiFilePatches(text4, uiFilePatchFlags);
						}
					}
				}
			}

			Directory.SetCurrentDirectory(Environment.SystemDirectory);
			using (var memoryStream2 = new MemoryStream(Resources.comp2))
			{
				using (var gzipStream2 = new GZipStream(memoryStream2, CompressionMode.Decompress))
				{
					var array6 = new byte[1982];
					gzipStream2.Read(array6, 0, array6.Length);
					if (!File.Exists("SysResetRedPill.xml"))
					{
						Console.WriteLine("[i] Writing System Reset manifest");
						File.WriteAllBytes("SysResetRedPill.xml", array6);
					}

					array6 = new byte[595];
					gzipStream2.Read(array6, 0, array6.Length);
					if (!File.Exists("redpill.log"))
					{
						Console.WriteLine("[i] Writing static Redpill setup log");
						File.WriteAllBytes("redpill.log", array6);
					}

					array6 = new byte[944];
					gzipStream2.Read(array6, 0, array6.Length);
					Console.WriteLine("[i] Writing Redpill certificates");
					Registry.LocalMachine.CreateSubKey(
						"SOFTWARE\\Microsoft\\SystemCertificates\\ROOT\\Certificates\\7721AC1150970D0B6A4B47AAEA73770712C907C5");
					using (var registryKey5 = Registry.LocalMachine.OpenSubKey(
						       "SOFTWARE\\Microsoft\\SystemCertificates\\ROOT\\Certificates\\7721AC1150970D0B6A4B47AAEA73770712C907C5",
						       true))
					{
						registryKey5.SetValue("Blob", array6, RegistryValueKind.Binary);
					}
				}
			}

			AttemptMIEInstall(args.Contains("queuemie"));
			Console.WriteLine("[i] Registering Immersive Browser");
			using (var registryKey6 = Registry.LocalMachine.OpenSubKey("Software\\RegisteredApplications", true))
			{
				registryKey6.SetValue("Immersive Browser", "SOFTWARE\\Microsoft\\Immersive Browser\\Capabilities",
					RegistryValueKind.String);
			}

			Registry.LocalMachine.CreateSubKey(
				"SOFTWARE\\Microsoft\\Active Setup\\Installed Components\\{8E7E60C6-4CE5-476D-9E31-FD450F3F792F}");
			using (var registryKey7 = Registry.LocalMachine.OpenSubKey(
				       "SOFTWARE\\Microsoft\\Active Setup\\Installed Components\\{8E7E60C6-4CE5-476D-9E31-FD450F3F792F}",
				       true))
			{
				registryKey7.SetValue("IsInstalled", 1, RegistryValueKind.DWord);
			}

			using (var registryKey8 =
			       Registry.LocalMachine.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Explorer", true))
			{
				registryKey8.SetValue("MIEInstallResult", 0, RegistryValueKind.DWord);
			}

			using (var registryKey9 = Registry.LocalMachine.OpenSubKey("SYSTEM\\Setup", true))
			{
				if (registryKey9.GetValueNames().Contains("SetupTypeBak"))
				{
					Console.WriteLine("[i] Preparing to reboot");
					var num = (int?)registryKey9.GetValue("SetupTypeBak");
					var text5 = (string)registryKey9.GetValue("CmdLineBak");
					registryKey9.SetValue("SetupType", num, RegistryValueKind.DWord);
					registryKey9.SetValue("CmdLine", text5, RegistryValueKind.String);
					registryKey9.DeleteValue("SetupTypeBak", false);
					registryKey9.DeleteValue("CmdLineBak", false);
				}
			}
		}

		private static void RelockInAudit()
		{
			Console.WriteLine("[i] Disabling Software Protection Service");
			using (var registryKey =
			       Registry.LocalMachine.OpenSubKey("SYSTEM\\CurrentControlSet\\services\\sppsvc", true))
			{
				registryKey.SetValue("Start", 4, RegistryValueKind.DWord);
			}

			Console.WriteLine("[i] Cleaning up product policies");
			using (var registryKey2 =
			       Registry.LocalMachine.OpenSubKey("SYSTEM\\CurrentControlSet\\Control\\ProductOptions", true))
			{
				if (registryKey2.GetValueNames().Contains("ProductPolicyBkp"))
				{
					registryKey2.SetValue("ProductPolicy", (byte[])registryKey2.GetValue("ProductPolicyBkp"),
						RegistryValueKind.Binary);
					registryKey2.DeleteValue("ProductPolicyBkp");
				}
				else
				{
					var array = (byte[])registryKey2.GetValue("ProductPolicy");
					registryKey2.SetValue("ProductPolicyBkp", array, RegistryValueKind.Binary);
					var productPolicy = ProductPolicy.Deserialize(array);
					for (var i = 1; i < 10; i++)
					{
						var text = string.Format("SLC-Component-RP-0{0}", i);
						if (productPolicy.Policies.ContainsKey(text)) productPolicy.Policies.Remove(text);
					}

					registryKey2.SetValue("ProductPolicy",
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

			NativeMethods.AdjustPrivilege("SeBackupPrivilege", true);
			NativeMethods.AdjustPrivilege("SeRestorePrivilege", true);
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

		private static void RevertDuiMuiPatches()
		{
			var currentUICulture = CultureInfo.CurrentUICulture;
			foreach (var text in new[]
			         {
				         Environment.SystemDirectory + "\\" + currentUICulture.Name + "\\dui70.dll.mui",
				         Environment.GetFolderPath(Environment.SpecialFolder.SystemX86) + "\\" + currentUICulture.Name +
				         "\\dui70.dll.mui"
			         })
			{
				var text2 = text + ".orig";
				if (File.Exists(text2))
				{
					File.Delete(text);
					File.Move(text2, text);
				}
			}
		}

		private static void SetUpSmartTweaks()
		{
			if (FindPatternInFile(Environment.SystemDirectory + "\\WebcamUi.dll",
				    Encoding.Unicode.GetBytes("RemoteFontBootCacheFlags")) > 0L)
			{
				var text = "SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\GRE_Initialize";
				Registry.LocalMachine.CreateSubKey(text);
				using (var registryKey = Registry.LocalMachine.OpenSubKey(text, true))
				{
					registryKey.SetValue("RemoteFontBootCacheFlags", 4111, RegistryValueKind.DWord);
				}
			}

			var array = FindPatternsInFile(Environment.SystemDirectory + "\\glcnd.exe", new[]
			{
				Encoding.Unicode.GetBytes("{656CF76D-B764-4C23-9CDE-EDEB2514ECA0}"),
				Encoding.Unicode.GetBytes("{D3E34B21-9D75-101A-8C3D-00AA001A1652}")
			});
			if (array[0] > 0L)
			{
				var text2 = "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Applets\\Paint\\Capabilities";
				Registry.LocalMachine.CreateSubKey(text2);
				using (var registryKey2 = Registry.LocalMachine.OpenSubKey(text2, true))
				{
					registryKey2.SetValue("CLSID", "{656CF76D-B764-4C23-9CDE-EDEB2514ECA0}", RegistryValueKind.String);
					goto IL_0142;
				}
			}

			if (array[1] > 0L)
			{
				var text3 = "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Applets\\Paint\\Capabilities";
				Registry.LocalMachine.CreateSubKey(text3);
				using (var registryKey3 = Registry.LocalMachine.OpenSubKey(text3, true))
				{
					registryKey3.SetValue("CLSID", "{D3E34B21-9D75-101A-8C3D-00AA001A1652}", RegistryValueKind.String);
				}
			}

			IL_0142:
			if (FindPatternInFile(Environment.SystemDirectory + "\\TaskUI.exe",
				    Encoding.Unicode.GetBytes("TaskUIEnabled")) > 0L)
			{
				var text4 = "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\TaskUI";
				Registry.LocalMachine.CreateSubKey(text4);
				using (var registryKey4 = Registry.LocalMachine.OpenSubKey(text4, true))
				{
					registryKey4.SetValue("TaskUIEnabled", 1, RegistryValueKind.DWord);
					registryKey4.SetValue("TaskUIRefreshEnabled", 1, RegistryValueKind.DWord);
					registryKey4.SetValue("TaskUIOnImmersive", 1, RegistryValueKind.DWord);
				}
			}

			if (FindPatternInFile(Environment.SystemDirectory + "\\ExplorerFrame.dll", new byte[]
			    {
				    69, 218, 152, 145, 213, 199, 255, 78, 167, 38,
				    120, 252, 84, 125, 255, 83
			    }) > 0L)
			{
				var text5 = "CLSID\\{4F12FF5D-D319-4A79-8380-9CC80384DC08}";
				Registry.ClassesRoot.CreateSubKey(text5);
				using (var registryKey5 = Registry.ClassesRoot.OpenSubKey(text5, true))
				{
					registryKey5.SetValue("AppID", "{9198DA45-C7D5-4EFF-A726-78FC547DFF53}", RegistryValueKind.String);
				}
			}

			if (FindPatternInFile(Environment.SystemDirectory + "\\twinui.dll",
				    Encoding.Unicode.GetBytes("ShowFlyout")) > 0L)
			{
				var text6 = "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Explorer\\AutoplayHandlers";
				Registry.LocalMachine.CreateSubKey(text6);
				using (var registryKey6 = Registry.LocalMachine.OpenSubKey(text6, true))
				{
					registryKey6.SetValue("ShowFlyout", 1, RegistryValueKind.DWord);
				}
			}
		}

		private static void AttemptMIEInstall(bool queue)
		{
			var files = Directory.GetFiles(
				Environment.GetFolderPath(Environment.SpecialFolder.Windows) + "\\servicing\\Packages",
				"Microsoft-Windows-ImmersiveBrowser-Package~*~~*.mum");
			if (files.Length != 0)
			{
				var text = "/online /NoRestart /Enable-Feature /FeatureName:Immersive-Browser /PackageName:" +
				           files[0].Split('\\').Last().Replace(".mum", "");
				if (queue)
				{
					Console.WriteLine("[i] Queuing Immersive Browser install");
					var text2 = Environment.GetEnvironmentVariable("WINDIR") + "\\Setup\\Scripts";
					if (!Directory.Exists(text2)) Directory.CreateDirectory(text2);
					File.WriteAllText(text2 + "\\SetupComplete.cmd", "dism.exe " + text);
					return;
				}

				Console.WriteLine("[i] Installing Immersive Browser");
				Process.Start("dism.exe", text).WaitForExit();
			}
		}

		private static void SetUpHKCUValues()
		{
			Console.WriteLine("[i] Setting up Redpill values (HKCU)");
			var flag = FindPatternInFile(Environment.SystemDirectory + "\\themecpl.dll",
				Encoding.Unicode.GetBytes("FastWallpaperRendering")) > 0L;
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
						registryKey.SetValue("RPEnabled", 1, RegistryValueKind.DWord);
						registryKey.SetValue("RPInstalled", 1, RegistryValueKind.DWord);
					}

					if (flag)
						using (var registryKey2 =
						       Registry.Users.OpenSubKey(Path.Combine(text, "Control Panel\\Desktop"), true))
						{
							registryKey2.SetValue("FastWallpaperRendering", 1, RegistryValueKind.DWord);
						}
				}
				catch
				{
				}
			}

			string[] subKeyNames2;
			using (var registryKey3 =
			       Registry.LocalMachine.OpenSubKey("SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\ProfileList"))
			{
				subKeyNames2 = registryKey3.GetSubKeyNames();
			}

			NativeMethods.AdjustPrivilege("SeBackupPrivilege", true);
			NativeMethods.AdjustPrivilege("SeRestorePrivilege", true);
			foreach (var text2 in subKeyNames2)
				if (!subKeyNames.Contains(text2))
					using (var registryKey4 = Registry.LocalMachine.OpenSubKey(
						       Path.Combine("SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\ProfileList", text2)))
					{
						if (registryKey4.GetValueNames().Contains("ProfileImagePath"))
						{
							Console.WriteLine(" -> SID {0}", text2);
							var text3 = (string)registryKey4.GetValue("ProfileImagePath");
							if (NativeMethods.RegLoadKey(2147483651U, text2, Path.Combine(text3, "NTuser.dat")) == 0)
								try
								{
									using (var registryKey5 = Registry.Users.OpenSubKey(
										       Path.Combine(text2,
											       "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Explorer"), true))
									{
										registryKey5.SetValue("RPEnabled", 1, RegistryValueKind.DWord);
										registryKey5.SetValue("RPInstalled", 1, RegistryValueKind.DWord);
									}

									if (flag)
										using (var registryKey6 =
										       Registry.Users.OpenSubKey(Path.Combine(text2, "Control Panel\\Desktop"),
											       true))
										{
											registryKey6.SetValue("FastWallpaperRendering", 1, RegistryValueKind.DWord);
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
					using (var userRpConfig = Registry.Users.OpenSubKey(
						       Path.Combine("Default", "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Explorer"), true))
					{
						userRpConfig.SetValue("RPEnabled", 1, RegistryValueKind.DWord);
						userRpConfig.SetValue("RPInstalled", 1, RegistryValueKind.DWord);
					}

					if (flag)
						using (var desktopConfig =
						       Registry.Users.OpenSubKey(Path.Combine("Default", "Control Panel\\Desktop"), true))
						{
							desktopConfig.SetValue("FastWallpaperRendering", 1, RegistryValueKind.DWord);
						}
				}
				finally
				{
					NativeMethods.RegUnLoadKey(2147483651U, "Default");
				}
		}

		private static void ConformAccentResources(string patchingNative, string patchingWoW, string twinGuidance)
		{
			Console.WriteLine("[i] Conforming accent resoruces");
			var intPtr = NativeMethods.LoadLibraryEx(twinGuidance, IntPtr.Zero, 3U);
			if (intPtr == IntPtr.Zero) return;
			var flag = NativeMethods.FindResourceEx(intPtr, new IntPtr(2), new IntPtr(4807), 1033) == IntPtr.Zero;
			NativeMethods.FreeLibrary(intPtr);
			byte[] array;
			byte[] array2;
			if (flag)
			{
				array = new byte[53352];
				array2 = new byte[36398];
				using (var memoryStream = new MemoryStream(Resources.comp4))
				{
					using (var gzipStream = new GZipStream(memoryStream, CompressionMode.Decompress))
					{
						gzipStream.Read(array, 0, array.Length);
						gzipStream.Read(array2, 0, array2.Length);
						goto IL_012A;
					}
				}
			}

			if (GetBuildNumber() >= 8102 || NativeMethods.GetImmersiveColorSetCount() == 1) return;
			intPtr = NativeMethods.LoadLibraryEx(patchingNative, IntPtr.Zero, 3U);
			if (intPtr == IntPtr.Zero) return;
			var intPtr2 = NativeMethods.FindResourceEx(intPtr, "PNG", new IntPtr(5234), 1033);
			var num = NativeMethods.SizeofResource(intPtr, intPtr2);
			array = new byte[num];
			Marshal.Copy(NativeMethods.LoadResource(intPtr, intPtr2), array, 0, num);
			NativeMethods.FreeLibrary(intPtr);
			array2 = array;
			IL_012A:
			var intPtr3 = NativeMethods.BeginUpdateResource(patchingNative, false);
			NativeMethods.UpdateResource(intPtr3, "PNG", new IntPtr(5231), 1033, array, (uint)array.Length);
			NativeMethods.UpdateResource(intPtr3, "PNG", new IntPtr(5232), 1033, array2, (uint)array2.Length);
			NativeMethods.EndUpdateResource(intPtr3, false);
			if (patchingWoW != null)
			{
				var intPtr4 = NativeMethods.BeginUpdateResource(patchingWoW, false);
				NativeMethods.UpdateResource(intPtr4, "PNG", new IntPtr(5231), 1033, array, (uint)array.Length);
				NativeMethods.UpdateResource(intPtr4, "PNG", new IntPtr(5232), 1033, array2, (uint)array2.Length);
				NativeMethods.EndUpdateResource(intPtr4, false);
			}
		}

		private static void DoUiFilePatches(string patchingTarget, UiFilePatchFlags patchFlags)
		{
			Console.WriteLine("[i] Patching with flags 0x{0:x}", (int)patchFlags);
			var intPtr = NativeMethods.LoadLibraryEx(patchingTarget, IntPtr.Zero, 3U);
			if (intPtr == IntPtr.Zero) return;
			var array = new[]
			{
				3520, 3521, 3522, 3523, 17502, 17542, 17549, 17563, 17576, 17578,
				17582
			};
			var array2 = new string[11];
			for (var i = 0; i < 11; i++)
			{
				var intPtr2 = NativeMethods.FindResourceEx(intPtr, "UIFILE", new IntPtr(array[i]), 1033);
				var num = NativeMethods.SizeofResource(intPtr, intPtr2);
				var array3 = new byte[num];
				Marshal.Copy(NativeMethods.LoadResource(intPtr, intPtr2), array3, 0, num);
				array2[i] = Encoding.ASCII.GetString(array3);
			}

			NativeMethods.FreeLibrary(intPtr);
			for (var j = 0; j < 11; j++)
			{
				if ((patchFlags & UiFilePatchFlags.TouchEditInner) == UiFilePatchFlags.TouchEditInner)
					array2[j] = array2[j].Replace("TouchEditInner", "TouchEdit");
				if ((patchFlags & UiFilePatchFlags.ItemHeightInPopup) == UiFilePatchFlags.ItemHeightInPopup)
				{
					array2[j] = array2[j].Replace(" itemheightinpopup=\"55rp\"", "");
					array2[j] = array2[j].Replace(" itemheightinpopup=\"40rp\"", "");
				}

				if ((patchFlags & UiFilePatchFlags.TouchSelectPopup) == UiFilePatchFlags.TouchSelectPopup)
					array2[j] = array2[j].Replace(
						"TouchSelectPopup visible=\"true\" accessible=\"true\" accrole=\"window\" background=\"ImmersiveControlDarkSelectBackgroundPressed\"/>",
						"if id=\"atom(TouchSelectPopup)\"><HWNDElement visible=\"true\" accessible=\"true\" accrole=\"list\"/></if> ");
				if ((patchFlags & UiFilePatchFlags.WrappingList) == UiFilePatchFlags.WrappingList)
					array2[j] = array2[j].Replace("WrappingList", "ItemList");
				if ((patchFlags & UiFilePatchFlags.TouchCarouselScrollBar) == UiFilePatchFlags.TouchCarouselScrollBar)
					array2[j] = array2[j].Replace("TouchCarouselScrollBar", "TouchScrollBar");
				if ((patchFlags & UiFilePatchFlags.TouchSwitch) == UiFilePatchFlags.TouchSwitch)
					for (var k = array2[j].IndexOf("<if class=\"DarkToggleClass\">", StringComparison.Ordinal);
					     k > 0;
					     k = array2[j].IndexOf("<if class=\"DarkToggleClass\">", StringComparison.Ordinal))
					{
						var num2 = k + 10194;
						array2[j] = array2[j].Substring(0, k) + array2[j].Substring(num2);
					}

				if ((patchFlags & UiFilePatchFlags.TouchEditDeprecated) == UiFilePatchFlags.TouchEditDeprecated)
					array2[j] = array2[j].Replace("<TouchEdit ", "<TouchEdit2 ");
			}

			var intPtr3 = NativeMethods.BeginUpdateResource(patchingTarget, false);
			for (var l = 0; l < 11; l++)
			{
				var bytes = Encoding.ASCII.GetBytes(array2[l]);
				NativeMethods.UpdateResource(intPtr3, "UIFILE", new IntPtr(array[l]), 1033, bytes, (uint)bytes.Length);
			}

			NativeMethods.EndUpdateResource(intPtr3, false);
		}

		private static void DoDuiMuiPatches(bool alsoPatchWow)
		{
			var array = new byte[246];
			var array2 = new byte[550];
			var array3 = new byte[260];
			using (var memoryStream = new MemoryStream(Resources.comp3))
			{
				using (var gzipStream = new GZipStream(memoryStream, CompressionMode.Decompress))
				{
					gzipStream.Read(array, 0, array.Length);
					gzipStream.Read(array2, 0, array2.Length);
					gzipStream.Read(array3, 0, array3.Length);
				}
			}

			var culture = CultureInfo.CurrentUICulture;
			string[] array4;
			if (alsoPatchWow)
				array4 = new[]
				{
					Environment.SystemDirectory + "\\" + culture.Name + "\\dui70.dll.mui",
					Environment.GetFolderPath(Environment.SpecialFolder.SystemX86) + "\\" + culture.Name +
					"\\dui70.dll.mui"
				};
			else
				array4 = new[] { Environment.SystemDirectory + "\\" + culture.Name + "\\dui70.dll.mui" };
			foreach (var text in array4)
			{
				var intPtr = NativeMethods.LoadLibraryEx(text, IntPtr.Zero, 3U);
				var intPtr2 = NativeMethods.FindResourceEx(intPtr, new IntPtr(6), new IntPtr(9),
					(ushort)culture.LCID);
				var flag = intPtr2 == IntPtr.Zero;
				intPtr2 = NativeMethods.FindResourceEx(intPtr, new IntPtr(6), new IntPtr(8),
					(ushort)culture.LCID);
				var flag2 = intPtr2 == IntPtr.Zero;
				intPtr2 = NativeMethods.FindResourceEx(intPtr, new IntPtr(6), new IntPtr(7),
					(ushort)culture.LCID);
				if (intPtr2 != IntPtr.Zero)
				{
					var num = NativeMethods.SizeofResource(intPtr, intPtr2);
					var array6 = new byte[num];
					Marshal.Copy(NativeMethods.LoadResource(intPtr, intPtr2), array6, 0, num);
					NativeMethods.FreeLibrary(intPtr);
					var flag3 = true;
					for (var j = num - 16; j < num; j++)
						if (array6[j] > 0)
						{
							flag3 = false;
							break;
						}

					if (array6[flag3 ? num - 18 : num - 2] == 37)
					{
						Array.Resize(ref array6, num + array.Length - (flag3 ? 16 : 0));
						Array.Copy(array, 0, array6, num - (flag3 ? 16 : 0), array.Length);
					}

					var intPtr3 = IntPtr.Zero;
					if (num != array6.Length)
					{
						if (intPtr3 == IntPtr.Zero) intPtr3 = GetResourceUpdaterForMUI(text);
						NativeMethods.UpdateResource(intPtr3, new IntPtr(6), new IntPtr(7),
							(ushort)culture.LCID, array6, (uint)array6.Length);
					}

					if (flag2)
					{
						if (intPtr3 == IntPtr.Zero) intPtr3 = GetResourceUpdaterForMUI(text);
						NativeMethods.UpdateResource(intPtr3, new IntPtr(6), new IntPtr(8),
							(ushort)culture.LCID, array2, (uint)array2.Length);
					}

					if (flag)
					{
						if (intPtr3 == IntPtr.Zero) intPtr3 = GetResourceUpdaterForMUI(text);
						NativeMethods.UpdateResource(intPtr3, new IntPtr(6), new IntPtr(9),
							(ushort)culture.LCID, array3, (uint)array3.Length);
					}

					if (intPtr3 != IntPtr.Zero)
					{
						NativeMethods.EndUpdateResource(intPtr3, false);
						RevertMuiWorkaround(text);
					}
				}
			}
		}

		private static IntPtr GetResourceUpdaterForMUI(string filePath)
		{
			File.Copy(filePath, filePath + ".orig", true);
			var num = FindPatternInFile(filePath, Encoding.Unicode.GetBytes("MUI"));
			using (var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Write))
			{
				fileStream.Seek(num, SeekOrigin.Begin);
				fileStream.WriteByte(65);
			}

			return NativeMethods.BeginUpdateResource(filePath, false);
		}

		private static void RevertMuiWorkaround(string filePath)
		{
			var num = FindPatternInFile(filePath, Encoding.Unicode.GetBytes("AUI"));
			using (var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Write))
			{
				fileStream.Seek(num, SeekOrigin.Begin);
				fileStream.WriteByte(77);
			}
		}

		private static int GetRequiredRPVersion(string filePath)
		{
			var num = int.MaxValue;
			using (var fileStream = new FileStream(filePath, FileMode.Open))
			{
				using (var binaryReader = new BinaryReader(fileStream))
				{
					binaryReader.BaseStream.Seek(60L, SeekOrigin.Begin);
					binaryReader.BaseStream.Seek(binaryReader.ReadInt32() + 4, SeekOrigin.Begin);
					var num2 = binaryReader.ReadUInt16();
					var flag = false;
					Console.Write(" -> Architecture: ");
					if (num2 == 34404)
					{
						Console.WriteLine("x64");
						flag = true;
					}
					else
					{
						if (num2 != 332)
						{
							Console.WriteLine("Unknown");
							return num;
						}

						Console.WriteLine("x86");
					}

					var num3 = binaryReader.ReadUInt16();
					binaryReader.BaseStream.Seek(flag ? 40L : 44L, SeekOrigin.Current);
					var num4 = flag ? binaryReader.ReadInt64() : binaryReader.ReadInt32();
					binaryReader.BaseStream.Seek(flag ? 208L : 192L, SeekOrigin.Current);
					var list = new List<PESectionInfo>();
					var num5 = 0;
					for (var i = 0; i < num3; i++)
					{
						var pesectionInfo = new PESectionInfo
						{
							SectionName = Encoding.ASCII.GetString(binaryReader.ReadBytes(8)).TrimEnd(new char[1]),
							VirtSize = binaryReader.ReadInt32(),
							VirtAddr = binaryReader.ReadInt32(),
							PhysSize = binaryReader.ReadInt32(),
							PhysAddr = binaryReader.ReadInt32()
						};
						if (num5 < 1 && pesectionInfo.SectionName == ".rsrc") num5 = pesectionInfo.PhysAddr;
						pesectionInfo.VirtOffset = pesectionInfo.VirtAddr - pesectionInfo.PhysAddr;
						list.Add(pesectionInfo);
						binaryReader.BaseStream.Seek(16L, SeekOrigin.Current);
					}

					var stringPhysAddr = FindPatternInFile(binaryReader, Encoding.ASCII.GetBytes("RP_VersionCheck"),
						true, list[0].PhysAddr, num5);
					if (stringPhysAddr > -1L)
					{
						var pesectionInfo2 = list.Where(x =>
							stringPhysAddr > x.PhysAddr && stringPhysAddr < x.PhysAddr + x.PhysSize).First();
						var num6 = stringPhysAddr + pesectionInfo2.VirtOffset;
						Console.WriteLine(" -> Found RP_VersionCheck at 0x{0:x} (virtual address 0x{1:x} in {2})",
							stringPhysAddr, num6, pesectionInfo2.SectionName);
						binaryReader.BaseStream.Seek(list[0].PhysAddr, SeekOrigin.Begin);
						var array = new byte[15];
						if (flag)
						{
							var flag2 = false;
							for (;;)
							{
								if (array[8] != 72 || array[9] != 141 || array[10] != 21)
								{
									Array.Copy(array, 1, array, 0, 14);
									try
									{
										array[array.Length - 1] = binaryReader.ReadByte();
									}
									catch
									{
										flag2 = true;
										goto IL_02BA;
									}

									continue;
								}

								IL_02BA:
								if (flag2) goto IL_0398;
								var num7 = (int)((num6 - (binaryReader.BaseStream.Position + list[0].VirtOffset)) &
								                 (long)(ulong)-1);
								var num8 = BitConverter.ToInt32(array, 11);
								if (num7 == num8) break;
								array[8] = 0;
							}

							Console.WriteLine(" -> Found matching lea rdx");
						}
						else
						{
							num6 += num4;
							var flag3 = false;
							while (array[10] != 104 || BitConverter.ToInt32(array, 11) != num6)
							{
								Array.Copy(array, 1, array, 0, 14);
								try
								{
									array[array.Length - 1] = binaryReader.ReadByte();
								}
								catch
								{
									flag3 = true;
									break;
								}
							}

							if (!flag3)
								Console.WriteLine(" -> Found matching push offset at 0x{0:x}",
									binaryReader.BaseStream.Position - 5L);
						}

						IL_0398:
						while ((array[10] != 131 || array[11] != 248) &&
						       (array[10] != 61 || array[13] != 1 || array[14] != 0))
						{
							Array.Copy(array, 1, array, 0, 14);
							try
							{
								array[array.Length - 1] = binaryReader.ReadByte();
							}
							catch
							{
								return num;
							}
						}

						if (array[14] == 0)
							num = BitConverter.ToInt32(array, 11);
						else
							num = array[12];
						Console.WriteLine(" -> Found cmp eax, {0:x} at 0x{1:x}", num,
							(int)binaryReader.BaseStream.Position - 5);
					}
					else
					{
						Console.WriteLine(" -> TWinUI doesn't contain RP_VersionCheck");
					}
				}
			}

			return num;
		}

		private static long FindPatternInFile(string filePath, byte[] bytePattern, bool getOffset = true,
			long minOffset = 0L, long maxOffset = 0L)
		{
			if (!File.Exists(filePath)) return -1L;
			long num;
			using (var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
			{
				using (var binaryReader = new BinaryReader(fileStream))
				{
					num = FindPatternsInFile(binaryReader, new[] { bytePattern }, getOffset, minOffset, maxOffset)[0];
				}
			}

			return num;
		}

		private static long FindPatternInFile(BinaryReader binReader, byte[] bytePattern, bool getOffset = true,
			long minOffset = 0L, long maxOffset = 0L)
		{
			return FindPatternsInFile(binReader, new[] { bytePattern }, getOffset, minOffset, maxOffset)[0];
		}

		private static long[] FindPatternsInFile(string filePath, byte[][] bytePatterns, bool getOffset = true,
			long minOffset = 0L, long maxOffset = 0L)
		{
			if (!File.Exists(filePath))
			{
				var array = new long[bytePatterns.Length];
				for (var i = 0; i < array.Length; i++) array[i] = -1L;
				return array;
			}

			long[] array2;
			using (var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
			{
				using (var binaryReader = new BinaryReader(fileStream))
				{
					array2 = FindPatternsInFile(binaryReader, bytePatterns, getOffset, minOffset, maxOffset);
				}
			}

			return array2;
		}

		private static long[] FindPatternsInFile(BinaryReader binReader, byte[][] bytePatterns, bool getOffset = true,
			long minOffset = 0L, long maxOffset = 0L)
		{
			var position = binReader.BaseStream.Position;
			var array = new string[bytePatterns.Length];
			for (var i = 0; i < bytePatterns.Length; i++) array[i] = BitConverter.ToString(bytePatterns[i]);
			var array2 = new long[bytePatterns.Length];
			for (var j = 0; j < array2.Length; j++) array2[j] = -1L;
			var array3 = new byte[4096];
			if (minOffset > 0L) binReader.BaseStream.Seek(minOffset, SeekOrigin.Begin);
			if (maxOffset < 1L) maxOffset = binReader.BaseStream.Length;
			while (binReader.BaseStream.Position < maxOffset)
			{
				if (binReader.BaseStream.Position > minOffset) Array.Copy(array3, 2048, array3, 0, 2048);
				var num = maxOffset - binReader.BaseStream.Position;
				var num2 = 2048;
				if (num < 2048L)
				{
					num2 = (int)num;
					Array.Resize(ref array3, 2048 + num2);
				}

				Array.Copy(binReader.ReadBytes(num2), 0, array3, 2048, num2);
				if (getOffset)
				{
					var flag = true;
					for (var k = 0; k < bytePatterns.Length; k++)
						if (array2[k] <= -1L)
						{
							var num3 = BitConverter.ToString(array3).IndexOf(array[k], StringComparison.Ordinal);
							if (num3 > -1)
								array2[k] = binReader.BaseStream.Position - array3.Length + (num3 + 1) / 3;
							else
								flag = false;
						}

					if (flag) break;
				}
				else
				{
					var flag2 = true;
					for (var l = 0; l < bytePatterns.Length; l++)
						if (array2[l] <= -1L)
						{
							if (BitConverter.ToString(array3).Contains(array[l]))
								array2[l] = 1L;
							else
								flag2 = false;
						}

					if (flag2) break;
				}
			}

			binReader.BaseStream.Seek(position, SeekOrigin.Begin);
			return array2;
		}

		private static int GetBuildNumber()
		{
			using (var currentVersion =
			       Registry.LocalMachine.OpenSubKey("SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion"))
			{
				return int.Parse((string)currentVersion.GetValue("CurrentBuild"));
			}
		}
	}
}