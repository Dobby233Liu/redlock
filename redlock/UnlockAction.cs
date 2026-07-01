using System;
using System.Collections.Generic;
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
			patchDuiMui = InstallShsxs();
		
		if (patchDuiMui)
			DoDuiMuiPatches(Is64BitOperatingSystem);

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
#if FIX_MALFORMED_TWINUI
		var tWinUiPath = GetSystemFile("twinui.dll");
		if (PatternFinder.FindPatternInFile(tWinUiPath,
				Encoding.Unicode.GetBytes("winmain(zachd)"), false) != PatternFinder.NoneFound)
		{
			Console.WriteLine("[i] Restoring non-private TWinUI from component store");
			File.Copy(tWinUiPath, tWinUiPath + ".orig", true);
			var sfc = Process.Start("sfc.exe", $"/scanfile={CliUtil.QuoteParameter(tWinUiPath)}");
			sfc?.WaitForExit();
		}
#endif
		
		if (PatternFinder.FindPatternInFile(GetSystemFile("WebcamUi.dll"),
			    Encoding.Unicode.GetBytes("RemoteFontBootCacheFlags")) != PatternFinder.NoneFound)
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
		if (pdfReaderFeaturesPresent[0] != PatternFinder.NoneFound)
		{
			using var pdfReaderConfig = Hklm.CreateSubKey(pdfReaderConfigKey, true);
			pdfReaderConfig.SetValue("CLSID", pdfReaderFeature1, RegistryValueKind.String);
		}
		else if (pdfReaderFeaturesPresent[1] != PatternFinder.NoneFound)
		{
			using var pdfReaderConfig = Hklm.CreateSubKey(pdfReaderConfigKey, true);
			pdfReaderConfig.SetValue("CLSID", pdfReaderFeature2, RegistryValueKind.String);
		}

		if (PatternFinder.FindPatternInFile(GetSystemFile("TaskUI.exe"),
			    Encoding.Unicode.GetBytes("TaskUIEnabled")) != PatternFinder.NoneFound)
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
		if (ribbonEnablementPatterns[0] != PatternFinder.NoneFound)
		{
			using var ribbonConfig = Hkcr.CreateSubKey(RegKeyConstants.RibbonClass, true);
			ribbonConfig.SetValue("AppID", ribbonAppId.ToString(), RegistryValueKind.String);
		}
		else if (ribbonEnablementPatterns[1] != PatternFinder.NoneFound)
		{
			using var explorerAdvConfig = Hklm.OpenSubKey(RegKeyConstants.ExplorerAdv, true);
			explorerAdvConfig?.SetValue("RibbonizeMePlease", 1, RegistryValueKind.DWord);
		}

		if (PatternFinder.FindPatternInFile(GetSystemFile("twinui.dll"),
			    Encoding.Unicode.GetBytes("ShowFlyout")) != PatternFinder.NoneFound)
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
			Encoding.Unicode.GetBytes("FastWallpaperRendering")) != PatternFinder.NoneFound;
		foreach (var userKey in RegistryUtil.ForEachUserHive())
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
	private bool InstallShsxs()
	{
		string shsxsPath = GetSystemFile("shsxs.dll"),
			tWinUiPath = GetSystemFile("twinui.dll");
		if (File.Exists(shsxsPath) || !File.Exists(tWinUiPath))
			return false;

		var useAltInitLauncherDataLayer = PatternFinder.FindPatternsInFile(tWinUiPath, [
			Encoding.ASCII.GetBytes("RP_GetLayoutManagerBandDependencies"),
			Encoding.ASCII.GetBytes("RP_InitLauncherDataLayer")
		], false).All(t => t != PatternFinder.NoneFound);

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
		
		var oobeHasAccentSupport = PatternFinder.FindPatternsInFile(
			GetSystemFile(@"oobe\msoobeplugins.dll"), 
			[
				Encoding.Unicode.GetBytes("OOBEColorolorSet"), // not a typo
				Encoding.Unicode.GetBytes("GradientColor")
			]).Any(i => i != PatternFinder.NoneFound);
		if (oobeHasAccentSupport)
			ConformAccentResources(shsxsPath, isOs64Bit ? shsxsPathWoW : null, tWinUiPath);
				
		var rpVersion =
			CodeAnalysisUtil.GetRequiredRPVersion(GetSystemFile("explorer.exe"));
				
		if (rpVersion != 26)
		{
			using var explorerConfig = Hklm.OpenSubKey(RegKeyConstants.Explorer, true);
			explorerConfig?.SetValue("RPVersion", rpVersion, RegistryValueKind.DWord);
		}

		var uiFileFlags = UiFilePatchFlags.None;
		if (rpVersion > 23)
		{
			Func<string, byte[]> u16 = Encoding.Unicode.GetBytes;
			Func<string, byte[]> ascii = Encoding.ASCII.GetBytes;
			KeyValuePair<byte[], UiFilePatchFlags>[] patternToFlag =
			[
				new(u16("TouchEditInner"), UiFilePatchFlags.TouchEditInner),
				new(ascii("ItemHeightInPopup"), UiFilePatchFlags.ItemHeightInPopup),
				new(u16("TouchSelectPopupHost"), UiFilePatchFlags.TouchSelectPopup),
				new(u16("WrappingList"), UiFilePatchFlags.WrappingList),
				new(u16("TouchCarouselScrollBar"), UiFilePatchFlags.TouchCarouselScrollBar),
				new(ascii("TouchSwitch"), UiFilePatchFlags.TouchSwitch),
				new(ascii("TouchEdit@"), UiFilePatchFlags.TouchEditDeprecated)
			];
			var found = PatternFinder.FindPatternsInFile(GetSystemFile("dui70.dll"),
					patternToFlag.Select(i => i.Key).ToArray(), false);
			uiFileFlags = patternToFlag
				.Where((_, i) => found[i] != PatternFinder.NoneFound)
				.Aggregate(uiFileFlags, (cur, i) => cur | i.Value);
		}

		if (uiFileFlags == UiFilePatchFlags.None)
			return false;
		
		Console.WriteLine("[i] Patching native SHSxS");
		DoUiFilePatches(shsxsPath, uiFileFlags);
		if (isOs64Bit)
		{
			Console.WriteLine("[i] Patching WoW SHSxS");
			DoUiFilePatches(shsxsPathWoW, uiFileFlags);
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