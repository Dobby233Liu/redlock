using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Microsoft.Win32;
using redlock.Properties;
using UiFilePatchFlags = redlock.ResourcePatches.UiFilePatchFlags;

namespace redlock;

internal class UnlockAction : BaseAction
{
	internal bool NoPolicies { get; set; } = false;
	internal bool NoShsxs { get; set; } = false;
	internal bool QueueMie { get; set; } = false;
	
	internal void Perform()
	{
		if (!NoPolicies)
		{
			DisableSpp();

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

		PerformSmartTweaks();
		SetUpHKCUValues();

		if (!NoShsxs)
			DropShsxs();

		Directory.SetCurrentDirectory(Environment.SystemDirectory);
		using (var comp2Raw = new MemoryStream(Resources.comp2))
		{
			using (var comp2 = new GZipStream(comp2Raw, CompressionMode.Decompress))
			{
				var buf = new byte[1982];
				comp2.Read(buf, 0, buf.Length);
				if (!File.Exists("SysResetRedPill.xml"))
				{
					Console.WriteLine("[i] Writing System Reset manifest");
					File.WriteAllBytes("SysResetRedPill.xml", buf);
				}

				buf = new byte[595];
				comp2.Read(buf, 0, buf.Length);
				if (!File.Exists("redpill.log"))
				{
					Console.WriteLine("[i] Writing Redpill setup log");
					File.WriteAllBytes("redpill.log", buf);
				}

				buf = new byte[944];
				comp2.Read(buf, 0, buf.Length);
				Console.WriteLine("[i] Writing Redpill certificates");
				using (var rpCertEntry = Registry.LocalMachine.CreateSubKey(
					       @"SOFTWARE\Microsoft\SystemCertificates\ROOT\Certificates\7721AC1150970D0B6A4B47AAEA73770712C907C5",
					       true))
				{
					rpCertEntry.SetValue("Blob", buf, RegistryValueKind.Binary);
				}
			}
		}

		RegisterMie(QueueMie);
	}
	
	private void PerformSmartTweaks()
	{
		if (PatternFinder.FindPatternInFile(Environment.SystemDirectory + "\\WebcamUi.dll",
			    Encoding.Unicode.GetBytes("RemoteFontBootCacheFlags")) > 0L)
		{
			const string webcamEnablerConfigKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\GRE_Initialize";
			using var webcamEnablerConfig = Registry.LocalMachine.CreateSubKey(webcamEnablerConfigKey, true);
			webcamEnablerConfig.SetValue("RemoteFontBootCacheFlags", 0x100f, RegistryValueKind.DWord);
		}

		const string pdfReaderFeature1 = "{656CF76D-B764-4C23-9CDE-EDEB2514ECA0}";
		const string pdfReaderFeature2 = "{D3E34B21-9D75-101A-8C3D-00AA001A1652}";
		var pdfReaderFeaturesPresent = PatternFinder.FindPatternsInFile(
			Environment.SystemDirectory + "\\glcnd.exe", [
				Encoding.Unicode.GetBytes(pdfReaderFeature1),
				Encoding.Unicode.GetBytes(pdfReaderFeature2)
			]);
		const string pdfReaderConfigKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Applets\Paint\Capabilities";
		if (pdfReaderFeaturesPresent[0] > 0L)
		{
			using var pdfReaderConfig = Registry.LocalMachine.CreateSubKey(pdfReaderConfigKey, true);
			pdfReaderConfig.SetValue("CLSID", pdfReaderFeature1, RegistryValueKind.String);
		}
		else if (pdfReaderFeaturesPresent[1] > 0L)
		{
			using var pdfReaderConfig = Registry.LocalMachine.CreateSubKey(pdfReaderConfigKey, true);
			pdfReaderConfig.SetValue("CLSID", pdfReaderFeature2, RegistryValueKind.String);
		}

		if (PatternFinder.FindPatternInFile(Environment.SystemDirectory + "\\TaskUI.exe",
			    Encoding.Unicode.GetBytes("TaskUIEnabled")) > 0L)
		{
			const string taskUiConfigKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\TaskUI";
			using var taskUiConfig = Registry.LocalMachine.CreateSubKey(taskUiConfigKey, true);
			taskUiConfig.SetValue("TaskUIEnabled", 1, RegistryValueKind.DWord);
			taskUiConfig.SetValue("TaskUIRefreshEnabled", 1, RegistryValueKind.DWord);
			taskUiConfig.SetValue("TaskUIOnImmersive", 1, RegistryValueKind.DWord);
		}

		if (PatternFinder.FindPatternInFile(Environment.SystemDirectory + "\\ExplorerFrame.dll",
		    [0x45, 0xDA, 0x98, 0x91, 0xD5, 0xC7, 0xFF, 0x4E, 0xA7, 0x26, 0x78, 0xFC, 0x54, 0x7D, 0xFF, 0x53])
		    > 0L)
		{
			const string ribbonConfigKey = "CLSID\\{4F12FF5D-D319-4A79-8380-9CC80384DC08}";
			using var ribbonConfig = Registry.ClassesRoot.CreateSubKey(ribbonConfigKey, true);
			ribbonConfig.SetValue("AppID", "{9198DA45-C7D5-4EFF-A726-78FC547DFF53}", RegistryValueKind.String);
		}

		if (PatternFinder.FindPatternInFile(Environment.SystemDirectory + "\\twinui.dll",
			    Encoding.Unicode.GetBytes("ShowFlyout")) > 0L)
		{
			const string autoPlayConfigKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\AutoplayHandlers";
			using var autoPlayConfig = Registry.LocalMachine.CreateSubKey(autoPlayConfigKey, true);
			autoPlayConfig.SetValue("ShowFlyout", 1, RegistryValueKind.DWord);
		}
	}

	private void SetUpHKCUValues()
	{
		Console.WriteLine("[i] Setting up Redpill values (HKCU)");
		var fastWpRenderingAvailable = PatternFinder.FindPatternInFile(Environment.SystemDirectory + "\\themecpl.dll",
			Encoding.Unicode.GetBytes("FastWallpaperRendering")) > 0L;
		foreach (var userKey in RegistryUtil.OpenUserHives())
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
		}
	}
	
	private void DropShsxs()
	{
		var shsxsPath = Environment.SystemDirectory + "\\shsxs.dll";
		var twinUiPath = Environment.SystemDirectory + "\\twinui.dll";
		if (File.Exists(shsxsPath) || !File.Exists(twinUiPath)) return;
				
		var useAltInitLauncherDataLayer = true;
		var altInitLauncherDataLayerPatterns = PatternFinder.FindPatternsInFile(twinUiPath, [
			Encoding.ASCII.GetBytes("RP_GetLayoutManagerBandDependencies"),
			Encoding.ASCII.GetBytes("RP_InitLauncherDataLayer")
		], false);
		for (var j = 0; j < altInitLauncherDataLayerPatterns.Length; j++)
			if (altInitLauncherDataLayerPatterns[j] <= 0L)
			{
				useAltInitLauncherDataLayer = false;
				break;
			}

		var isOs64Bit = IntPtr.Size == 8;
		var shsxsPathWoW = shsxsPath;
		using (var comp1Raw = new MemoryStream(Resources.comp1))
		{
			using (var comp1 = new GZipStream(comp1Raw, CompressionMode.Decompress))
			{
				var shsxsData = new byte[2140160];
				comp1.Read(shsxsData, 0, shsxsData.Length);
				if (isOs64Bit)
				{
					if (useAltInitLauncherDataLayer)
					{
						shsxsData[20015] = 114;
						shsxsData[20040] = 48;
					}
					File.WriteAllBytes(shsxsPath, shsxsData);
							
					shsxsPathWoW = Environment.GetFolderPath(Environment.SpecialFolder.SystemX86) + "\\shsxs.dll";
				}

				shsxsData = new byte[2139648];
				comp1.Read(shsxsData, 0, shsxsData.Length);
				if (useAltInitLauncherDataLayer)
				{
					shsxsData[16095] = 114;
					shsxsData[16146] = 48;
				}
				File.WriteAllBytes(isOs64Bit ? shsxsPathWoW : shsxsPath, shsxsData);
			}
		}

		var oobeAccentSupportPatterns = PatternFinder.FindPatternsInFile(
			Environment.SystemDirectory + @"\oobe\msoobeplugins.dll", 
			[
				Encoding.Unicode.GetBytes("OOBEColorolorSet"),
				Encoding.Unicode.GetBytes("GradientColor")
			]);
		if (oobeAccentSupportPatterns[0] != 0L || oobeAccentSupportPatterns[1] != 0L)
			ResourcePatches.ConformAccentResources(shsxsPath, isOs64Bit ? shsxsPathWoW : null, twinUiPath);
				
		var rpVersion =
			CodeAnalysisUtil.GetRequiredRPVersion(@$"{Environment.SystemDirectory}\explorer.exe");
				
		if (rpVersion != 26)
		{
			using var explorerConfig =
				Registry.LocalMachine.OpenSubKey(
					@"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer", true);
			explorerConfig.SetValue("RPVersion", rpVersion, RegistryValueKind.DWord);
		}

		var uiFilePatchFlags = UiFilePatchFlags.None;
		if (rpVersion > 23)
		{
			var array5 = PatternFinder.FindPatternsInFile(Environment.SystemDirectory + "\\dui70.dll", [
				Encoding.Unicode.GetBytes("TouchEditInner"),
				Encoding.ASCII.GetBytes("ItemHeightInPopup"),
				Encoding.Unicode.GetBytes("TouchSelectPopupHost"),
				Encoding.Unicode.GetBytes("WrappingList"),
				Encoding.Unicode.GetBytes("TouchCarouselScrollBar"),
				Encoding.ASCII.GetBytes("TouchSwitch"),
				Encoding.ASCII.GetBytes("TouchEdit@")
			]);
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
			ResourcePatches.DoUiFilePatches(shsxsPath, uiFilePatchFlags);
			ResourcePatches.DoDuiMuiPatches(isOs64Bit);

			if (isOs64Bit)
			{
				Console.WriteLine("[i] Patching WoW SHSxS");
				ResourcePatches.DoUiFilePatches(shsxsPathWoW, uiFilePatchFlags);
			}
		}
	}
	
	private void RegisterMie(bool queueInstallation)
	{
		AttemptMIEInstall(queueInstallation);
		Console.WriteLine("[i] Registering Immersive Browser");
		using (var appRegistry = Registry.LocalMachine.OpenSubKey("Software\\RegisteredApplications", true))
		{
			appRegistry.SetValue("Immersive Browser", @"SOFTWARE\Microsoft\Immersive Browser\Capabilities",
				RegistryValueKind.String);
		}
		using (var mieInstallConfig = Registry.LocalMachine.CreateSubKey(
			       @"SOFTWARE\Microsoft\Active Setup\Installed Components\{8E7E60C6-4CE5-476D-9E31-FD450F3F792F}", 
			       true))
		{
			mieInstallConfig.SetValue("IsInstalled", 1, RegistryValueKind.DWord);
		}
		using (var explorerConfig =
		       Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer", true))
		{
			explorerConfig.SetValue("MIEInstallResult", 0, RegistryValueKind.DWord);
		}
	}
	
	private void AttemptMIEInstall(bool queue)
	{
		var mieManifests = SetupUtil.GetMieManifests();
		if (mieManifests.Length == 0) return;
		
		var dismExe = "dism.exe";
		var args = "/online /NoRestart /Enable-Feature /FeatureName:Immersive-Browser /PackageName:" +
		           Path.GetFileNameWithoutExtension(mieManifests[0]);
		
		if (queue)
		{
			Console.WriteLine("[i] Queuing Immersive Browser install");
			SetupUtil.QueueSetupCompleteAction($"{dismExe} {args}");
			return;
		}

		Console.WriteLine("[i] Installing Immersive Browser");
		Process.Start(dismExe, args)?.WaitForExit();
	}
}