using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Microsoft.Win32;
using redlock.Properties;

namespace redlock;

internal static partial class Program
{
	private static void UnlockInAudit(Arguments args)
	{
		if (!args.NoPolicies)
		{
			Console.WriteLine("[i] Disabling Software Protection Service");
			using (var sppsvcConfig =
			       Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\services\sppsvc", true))
			{
				sppsvcConfig.SetValue("Start", 4, RegistryValueKind.DWord);
			}

			Console.WriteLine("[i] Installing product policies");
			using (var productOptions =
			       Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\ProductOptions", true))
			{
				var oldPolicy = (byte[])productOptions.GetValue("ProductPolicy");
				productOptions.SetValue("ProductPolicyBkp", oldPolicy, RegistryValueKind.Binary);

				var policy = new ProductPolicy().Deserialize(oldPolicy);
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
		       Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer", true))
		{
			machineRpConfig.SetValue("RPEnabled", 1, RegistryValueKind.DWord);
			machineRpConfig.SetValue("RPInstalled", 1, RegistryValueKind.DWord);
			machineRpConfig.SetValue("RPStore", 1, RegistryValueKind.DWord);
		}

		SetUpSmartTweaks();
		SetUpHKCUValues();

		if (!args.NoShsxs)
		{
			var wowBinsPresent = IntPtr.Size == 8;
			var text2 = Environment.SystemDirectory + "\\shsxs.dll";
			var text3 = Environment.SystemDirectory + "\\twinui.dll";
			if (!File.Exists(text2) && File.Exists(text3))
			{
				var text4 = text2;
				var flag4 = true;
				var array2 = PatternFinder.FindPatternsInFile(text3, new[]
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

				var array4 = PatternFinder.FindPatternsInFile(Environment.SystemDirectory + @"\oobe\msoobeplugins.dll",
					new[]
					{
						Encoding.Unicode.GetBytes("OOBEColorolorSet"),
						Encoding.Unicode.GetBytes("GradientColor")
					});
				if (array4[0] != 0L || array4[1] != 0L)
					ConformAccentResources(text2, wowBinsPresent ? text4 : null, text3);
				var requiredRpVersion =
					GetRequiredRPVersion(Environment.GetEnvironmentVariable("WINDIR") + "\\explorer.exe");
				if (requiredRpVersion != 26)
				{
					using var registryKey4 =
						Registry.LocalMachine.OpenSubKey(
							@"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer", true);
					registryKey4.SetValue("RPVersion", requiredRpVersion, RegistryValueKind.DWord);
				}

				var uiFilePatchFlags = UiFilePatchFlags.None;
				if (requiredRpVersion > 23)
				{
					var array5 = PatternFinder.FindPatternsInFile(Environment.SystemDirectory + "\\dui70.dll", new[]
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
					@"SOFTWARE\Microsoft\SystemCertificates\ROOT\Certificates\7721AC1150970D0B6A4B47AAEA73770712C907C5");
				using (var registryKey5 = Registry.LocalMachine.OpenSubKey(
					       @"SOFTWARE\Microsoft\SystemCertificates\ROOT\Certificates\7721AC1150970D0B6A4B47AAEA73770712C907C5",
					       true))
				{
					registryKey5.SetValue("Blob", array6, RegistryValueKind.Binary);
				}
			}
		}

		AttemptMIEInstall(args.QueueMie);
		Console.WriteLine("[i] Registering Immersive Browser");
		using (var registryKey6 = Registry.LocalMachine.OpenSubKey("Software\\RegisteredApplications", true))
		{
			registryKey6.SetValue("Immersive Browser", @"SOFTWARE\Microsoft\Immersive Browser\Capabilities",
				RegistryValueKind.String);
		}

		Registry.LocalMachine.CreateSubKey(
			@"SOFTWARE\Microsoft\Active Setup\Installed Components\{8E7E60C6-4CE5-476D-9E31-FD450F3F792F}");
		using (var registryKey7 = Registry.LocalMachine.OpenSubKey(
			       @"SOFTWARE\Microsoft\Active Setup\Installed Components\{8E7E60C6-4CE5-476D-9E31-FD450F3F792F}",
			       true))
		{
			registryKey7.SetValue("IsInstalled", 1, RegistryValueKind.DWord);
		}

		using (var registryKey8 =
		       Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer", true))
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

	private static void SetUpSmartTweaks()
	{
		if (PatternFinder.FindPatternInFile(Environment.SystemDirectory + "\\WebcamUi.dll",
			    Encoding.Unicode.GetBytes("RemoteFontBootCacheFlags")) > 0L)
		{
			var text = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\GRE_Initialize";
			Registry.LocalMachine.CreateSubKey(text);
			using var registryKey = Registry.LocalMachine.OpenSubKey(text, true);
			registryKey.SetValue("RemoteFontBootCacheFlags", 4111, RegistryValueKind.DWord);
		}

		var array = PatternFinder.FindPatternsInFile(Environment.SystemDirectory + "\\glcnd.exe", new[]
		{
			Encoding.Unicode.GetBytes("{656CF76D-B764-4C23-9CDE-EDEB2514ECA0}"),
			Encoding.Unicode.GetBytes("{D3E34B21-9D75-101A-8C3D-00AA001A1652}")
		});
		
		if (array[0] > 0L)
		{
			var text2 = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Applets\Paint\Capabilities";
			Registry.LocalMachine.CreateSubKey(text2);
			using var registryKey2 = Registry.LocalMachine.OpenSubKey(text2, true);
			registryKey2.SetValue("CLSID", "{656CF76D-B764-4C23-9CDE-EDEB2514ECA0}", RegistryValueKind.String);
		}
		else if (array[1] > 0L)
		{
			var text3 = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Applets\Paint\Capabilities";
			Registry.LocalMachine.CreateSubKey(text3);
			using var registryKey3 = Registry.LocalMachine.OpenSubKey(text3, true);
			registryKey3.SetValue("CLSID", "{D3E34B21-9D75-101A-8C3D-00AA001A1652}", RegistryValueKind.String);
		}

		if (PatternFinder.FindPatternInFile(Environment.SystemDirectory + "\\TaskUI.exe",
			    Encoding.Unicode.GetBytes("TaskUIEnabled")) > 0L)
		{
			var text4 = @"SOFTWARE\Microsoft\Windows\CurrentVersion\TaskUI";
			Registry.LocalMachine.CreateSubKey(text4);
			using var registryKey4 = Registry.LocalMachine.OpenSubKey(text4, true);
			registryKey4.SetValue("TaskUIEnabled", 1, RegistryValueKind.DWord);
			registryKey4.SetValue("TaskUIRefreshEnabled", 1, RegistryValueKind.DWord);
			registryKey4.SetValue("TaskUIOnImmersive", 1, RegistryValueKind.DWord);
		}

		if (PatternFinder.FindPatternInFile(Environment.SystemDirectory + "\\ExplorerFrame.dll", new byte[]
		    {
			    69, 218, 152, 145, 213, 199, 255, 78, 167, 38,
			    120, 252, 84, 125, 255, 83
		    }) > 0L)
		{
			var text5 = "CLSID\\{4F12FF5D-D319-4A79-8380-9CC80384DC08}";
			Registry.ClassesRoot.CreateSubKey(text5);
			using var registryKey5 = Registry.ClassesRoot.OpenSubKey(text5, true);
			registryKey5.SetValue("AppID", "{9198DA45-C7D5-4EFF-A726-78FC547DFF53}", RegistryValueKind.String);
		}

		if (PatternFinder.FindPatternInFile(Environment.SystemDirectory + "\\twinui.dll",
			    Encoding.Unicode.GetBytes("ShowFlyout")) > 0L)
		{
			var text6 = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\AutoplayHandlers";
			Registry.LocalMachine.CreateSubKey(text6);
			using var registryKey6 = Registry.LocalMachine.OpenSubKey(text6, true);
			registryKey6.SetValue("ShowFlyout", 1, RegistryValueKind.DWord);
		}
	}

	private static void AttemptMIEInstall(bool queue)
	{
		var files = Directory.GetFiles(
			Environment.GetFolderPath(Environment.SpecialFolder.Windows) + @"\servicing\Packages",
			"Microsoft-Windows-ImmersiveBrowser-Package~*~~*.mum");
		if (files.Length != 0)
		{
			var text = "/online /NoRestart /Enable-Feature /FeatureName:Immersive-Browser /PackageName:" +
			           files[0].Split('\\').Last().Replace(".mum", "");
			if (queue)
			{
				Console.WriteLine("[i] Queuing Immersive Browser install");
				var text2 = Environment.GetEnvironmentVariable("WINDIR") + @"\Setup\Scripts";
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
		var fastWpRenderingAvailable = PatternFinder.FindPatternInFile(Environment.SystemDirectory + "\\themecpl.dll",
			Encoding.Unicode.GetBytes("FastWallpaperRendering")) > 0L;
		RegistryUtil.ForEachUser((userKey, _) =>
		{
			using (var userRpConfig = userKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer", true))
			{
				userRpConfig.SetValue("RPEnabled", 1, RegistryValueKind.DWord);
				userRpConfig.SetValue("RPInstalled", 1, RegistryValueKind.DWord);
			}

			if (fastWpRenderingAvailable)
			{
				using var desktopConfig = userKey.OpenSubKey("Control Panel\\Desktop", true);
				desktopConfig.SetValue("FastWallpaperRendering", 1, RegistryValueKind.DWord);
			}
		});
	}

	private static int GetRequiredRPVersion(string filePath)
	{
		var num = int.MaxValue;
		using var fileStream = new FileStream(filePath, FileMode.Open);
		using var binaryReader = new BinaryReader(fileStream);
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

		var stringPhysAddr = PatternFinder.FindPattern(binaryReader, Encoding.ASCII.GetBytes("RP_VersionCheck"),
			true, list[0].PhysAddr, num5);
		if (stringPhysAddr > -1L)
		{
			var pesectionInfo2 = list.First(x =>
				stringPhysAddr > x.PhysAddr && stringPhysAddr < x.PhysAddr + x.PhysSize);
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
					var num7 = (ulong)(num6 - (binaryReader.BaseStream.Position + list[0].VirtOffset)) &
					           0xFFFFFFFFFFFFFFFF;
					var num8 = (uint)BitConverter.ToInt32(array, 11);
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

		return num;
	}
}