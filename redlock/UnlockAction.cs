using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.Win32;

namespace redlock;

internal partial class UnlockAction : BaseAction
{
	internal bool NoPolicies { get; set; }
	internal bool NoShsxs { get; set; }
	internal bool QueueMie { get; set; }
	
	internal void Perform()
	{
		if (!NoPolicies)
		{
			DisableSpp();

			Console.WriteLine("[i] Installing product policies");
			using var productOptions =
				Hklm.OpenSubKey(RegKeyConstants.ProductOptions, true);
			if (productOptions is not null)
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
		
		using (var explorerConfig =
		       Hklm.OpenSubKey(RegKeyConstants.Explorer, true))
		{
			explorerConfig?.SetValue("RPEnabled", 1, RegistryValueKind.DWord);
			explorerConfig?.SetValue("RPInstalled", 1, RegistryValueKind.DWord);
			explorerConfig?.SetValue("RPStore", 1, RegistryValueKind.DWord);
		}

		PerformSmartTweaks();
		SetUpHKCUValues();

		var patchDuiMui = false;
		if (!NoShsxs)
			patchDuiMui = DropShsxs();
		
		if (patchDuiMui)
		{
			Console.WriteLine("[i] Patching dui70 resources");
			DoDuiMuiPatches(Is64BitOperatingSystem);
		}

		Directory.SetCurrentDirectory(SystemDirectory);
		using (var comp2 = new BlobPacks.Comp2())
		{
			var buf = comp2.Read(comp2.SysResetRedPill).Data;
			if (!File.Exists("SysResetRedPill.xml"))
			{
				Console.WriteLine("[i] Writing System Reset manifest");
				File.WriteAllBytes("SysResetRedPill.xml", buf);
			}

			buf = comp2.Read(comp2.RedpillLog).Data;
			if (!File.Exists("redpill.log"))
			{
				Console.WriteLine("[i] Writing Redpill setup log");
				File.WriteAllBytes("redpill.log", buf);
			}

			buf = comp2.Read(comp2.RedpillCerts).Data;
			Console.WriteLine("[i] Writing Redpill certificates");
			using (var rpCertEntry = Hklm.CreateSubKey(
				       RegKeyConstants.RpCert, true))
			{
				rpCertEntry.SetValue("Blob", buf, RegistryValueKind.Binary);
			}
		}

		RegisterMie(QueueMie);
	}
	
	private void PerformSmartTweaks()
	{
#if TWINUI_FIX
		var twinUiPath = GetSystemFile("twinui.dll");
		if (PatternFinder.FindPatternInFile(twinUiPath,
				Encoding.Unicode.GetBytes("winmain(zachd)")) > 0L)
		{
			Console.WriteLine("[i] Restoring non-private TWinUI from component store");
			File.Copy(twinUiPath, twinUiPath + ".orig", true);
			var sfc = Process.Start("sfc.exe", $"/scanfile={CliUtil.QuoteParameter(twinUiPath)}");
			sfc?.WaitForExit();
		}
#endif
		
		if (PatternFinder.FindPatternInFile(GetSystemFile("WebcamUi.dll"),
			    Encoding.Unicode.GetBytes("RemoteFontBootCacheFlags")) > 0L)
		{
			using var webcamEnablementConfig =
				Hklm.CreateSubKey(RegKeyConstants.WebcamEnablement, true);
			webcamEnablementConfig.SetValue("RemoteFontBootCacheFlags", 0x100f, RegistryValueKind.DWord);
		}

		const string pdfReaderFeature1 = "{656CF76D-B764-4C23-9CDE-EDEB2514ECA0}";
		const string pdfReaderFeature2 = "{D3E34B21-9D75-101A-8C3D-00AA001A1652}";
		var pdfReaderFeaturesPresent = PatternFinder.FindPatternsInFile(
			GetSystemFile("glcnd.exe"), [
				Encoding.Unicode.GetBytes(pdfReaderFeature1),
				Encoding.Unicode.GetBytes(pdfReaderFeature2)
			]);
		const string pdfReaderConfigKey = RegKeyConstants.PdfReaderCap;
		if (pdfReaderFeaturesPresent[0] > 0L)
		{
			using var pdfReaderConfig = Hklm.CreateSubKey(pdfReaderConfigKey, true);
			pdfReaderConfig.SetValue("CLSID", pdfReaderFeature1, RegistryValueKind.String);
		}
		else if (pdfReaderFeaturesPresent[1] > 0L)
		{
			using var pdfReaderConfig = Hklm.CreateSubKey(pdfReaderConfigKey, true);
			pdfReaderConfig.SetValue("CLSID", pdfReaderFeature2, RegistryValueKind.String);
		}

		if (PatternFinder.FindPatternInFile(GetSystemFile("TaskUI.exe"),
			    Encoding.Unicode.GetBytes("TaskUIEnabled")) > 0L)
		{
			using var taskUiConfig = Hklm.CreateSubKey(RegKeyConstants.TaskUi, true);
			taskUiConfig.SetValue("TaskUIEnabled", 1, RegistryValueKind.DWord);
			taskUiConfig.SetValue("TaskUIRefreshEnabled", 1, RegistryValueKind.DWord);
			taskUiConfig.SetValue("TaskUIOnImmersive", 1, RegistryValueKind.DWord);
		}

		Guid ribbonAppId = new("{9198DA45-C7D5-4EFF-A726-78FC547DFF53}");
		var ribbonEnablementPatterns = PatternFinder.FindPatternsInFile(
			GetSystemFile("ExplorerFrame.dll"), [
				ribbonAppId.ToByteArray(),
				Encoding.Unicode.GetBytes("RibbonizeMePlease")
			]);
		if (ribbonEnablementPatterns[0] > 0L)
		{
			using var ribbonConfig = Hkcr.CreateSubKey(RegKeyConstants.RibbonClass, true);
			ribbonConfig.SetValue("AppID", ribbonAppId.ToString(), RegistryValueKind.String);
		}
		else
		{
			using var explorerAdvConfig = Hklm.OpenSubKey(RegKeyConstants.ExplorerAdv, true);
			explorerAdvConfig?.SetValue("RibbonizeMePlease", 1, RegistryValueKind.DWord);
		}

		if (PatternFinder.FindPatternInFile(GetSystemFile("twinui.dll"),
			    Encoding.Unicode.GetBytes("ShowFlyout")) > 0L)
		{
			using var autoPlayConfig = Hklm.CreateSubKey(RegKeyConstants.AutoPlayHandlers, true);
			autoPlayConfig.SetValue("ShowFlyout", 1, RegistryValueKind.DWord);
		}
	}

	private void SetUpHKCUValues()
	{
		Console.WriteLine("[i] Setting up Redpill values (HKCU)");
		var fastWpRenderingAvailable = PatternFinder.FindPatternInFile(
			GetSystemFile("themecpl.dll"), 
			Encoding.Unicode.GetBytes("FastWallpaperRendering")) > 0L;
		foreach (var userKey in RegistryUtil.OpenUserHives())
		{
			using (var explorerConfig = userKey.OpenSubKey(RegKeyConstants.Explorer, true))
			{
				explorerConfig?.SetValue("RPEnabled", 1, RegistryValueKind.DWord);
				explorerConfig?.SetValue("RPInstalled", 1, RegistryValueKind.DWord);
			}

			if (fastWpRenderingAvailable)
			{
				using var desktopConfig = userKey.OpenSubKey(RegKeyConstants.Desktop, true);
				desktopConfig?.SetValue("FastWallpaperRendering", 1, RegistryValueKind.DWord);
			}
		}
	}
	
	/// <returns>Whether DUI patching is needed</returns>
	private bool DropShsxs()
	{
		string shsxsPath = GetSystemFile("shsxs.dll"),
			twinUiPath = GetSystemFile("twinui.dll");
		if (File.Exists(shsxsPath) || !File.Exists(twinUiPath))
			return false;

		var altInitLauncherDataLayerPatterns = PatternFinder.FindPatternsInFile(twinUiPath, [
			Encoding.ASCII.GetBytes("RP_GetLayoutManagerBandDependencies"),
			Encoding.ASCII.GetBytes("RP_InitLauncherDataLayer")
		], false);
		var useAltInitLauncherDataLayer = altInitLauncherDataLayerPatterns.All(t => t > 0L);

		var isOs64Bit = Is64BitOperatingSystem;
		var shsxsPathWoW = shsxsPath;
		using (var comp1 = new BlobPacks.Comp1())
		{
			var shsxsBlob = comp1.Read(comp1.ShsxsAmd64);
			if (isOs64Bit)
			{
				if (useAltInitLauncherDataLayer)
					shsxsBlob.ApplyPatch(shsxsBlob.AltInitLauncherDataLayerPatches);
				File.WriteAllBytes(shsxsPath, shsxsBlob.Data);
						
				shsxsPathWoW = GetSystemFile("shsxs.dll", true);
			}

			shsxsBlob = comp1.Read(comp1.ShsxsI386);
			if (useAltInitLauncherDataLayer)
				shsxsBlob.ApplyPatch(shsxsBlob.AltInitLauncherDataLayerPatches);
			File.WriteAllBytes(isOs64Bit ? shsxsPathWoW : shsxsPath, shsxsBlob.Data);
		}

		
		var oobeAccentSupportPatterns = PatternFinder.FindPatternsInFile(
			GetSystemFile(@"oobe\msoobeplugins.dll"), 
			[
				Encoding.Unicode.GetBytes("OOBEColorolorSet"), // not a typo
				Encoding.Unicode.GetBytes("GradientColor")
			]);
		if (oobeAccentSupportPatterns[0] != 0L || oobeAccentSupportPatterns[1] != 0L)
			ConformAccentResources(shsxsPath, isOs64Bit ? shsxsPathWoW : null, twinUiPath);
				
		var rpVersion =
			CodeAnalysisUtil.GetRequiredRPVersion(GetSystemFile("explorer.exe"));
				
		if (rpVersion != 26)
		{
			using var explorerConfig = Hklm.OpenSubKey(RegKeyConstants.Explorer, true);
			explorerConfig?.SetValue("RPVersion", rpVersion, RegistryValueKind.DWord);
		}

		var uiFilePatchFlags = UiFilePatchFlags.None;
		if (rpVersion > 23)
		{
			var array5 = PatternFinder.FindPatternsInFile(GetSystemFile("dui70.dll"), [
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

		if (uiFilePatchFlags == UiFilePatchFlags.None)
			return false;
			
		Console.WriteLine("[i] Patching native SHSxS");
		DoUiFilePatches(shsxsPath, uiFilePatchFlags);
		if (isOs64Bit)
		{
			Console.WriteLine("[i] Patching WoW SHSxS");
			DoUiFilePatches(shsxsPathWoW, uiFilePatchFlags);
		}

		return true;
	}
	
	private void RegisterMie(bool queueInstallation)
	{
		AttemptMIEInstall(queueInstallation);
		Console.WriteLine("[i] Registering Immersive Browser");
		using (var appRegistry = Hklm.OpenSubKey(RegKeyConstants.Apps, true))
		{
			appRegistry?.SetValue("Immersive Browser", RegKeyConstants.MieCap, RegistryValueKind.String);
		}
		using (var mieInstallConfig = Hklm.CreateSubKey(RegKeyConstants.MieSetupData, true))
		{
			mieInstallConfig.SetValue("IsInstalled", 1, RegistryValueKind.DWord);
		}
		using (var explorerConfig = Hklm.OpenSubKey(RegKeyConstants.Explorer, true))
		{
			explorerConfig?.SetValue("MIEInstallResult", 0, RegistryValueKind.DWord);
		}
	}
	
	private void AttemptMIEInstall(bool queue)
	{
		var mieManifests = SetupUtil.GetMieManifests(WindowsDirectory);
		if (mieManifests.Length == 0) return;
		
		var dismExe = "dism.exe";
		var args = "/online /NoRestart /Enable-Feature /FeatureName:Immersive-Browser /PackageName:" +
		           Path.GetFileNameWithoutExtension(mieManifests[0]);
		
		if (queue)
		{
			Console.WriteLine("[i] Queuing Immersive Browser install");
			SetupUtil.QueueSetupCompleteAction($"{dismExe} {args}", SystemDirectory);
			return;
		}

		Console.WriteLine("[i] Installing Immersive Browser");
		Process.Start(dismExe, args)?.WaitForExit();
	}
}