using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;
using redlock.Properties;

namespace redlock;

internal static partial class Program
{
	private const ushort EnUsLcid = 1033;
	
	private static byte[] LoadResource(NativeMethods.SafeLibraryHandle lib, IntPtr res)
	{
		var dataSize = ResNative.SizeofResource(lib, res);
		var data = new byte[dataSize];
		Marshal.Copy(ResNative.LoadResource(lib, res), data, 0, dataSize);
		return data;
	}
	
	private static void ConformAccentResources(string shsxsPath, string? shsxsPathWoW, string twinUiPath)
	{
		Console.WriteLine("[i] Conforming accent resources");
		
		bool twinRes4807Present;
		using (var twinUi = ResNative.LoadLibraryEx(twinUiPath, IntPtr.Zero,
			       ResNative.DONT_RESOLVE_DLL_REFERENCES | ResNative.LOAD_LIBRARY_AS_DATAFILE))
		{
			if (twinUi.IsInvalid) return;
			twinRes4807Present = ResNative.FindResourceEx(twinUi, new IntPtr(2), new IntPtr(4807),
				                 EnUsLcid) == IntPtr.Zero;
		}

		byte[] res5231PatchData;
		byte[] res5232PatchData;
		if (twinRes4807Present)
		{
			res5231PatchData = new byte[53352];
			res5232PatchData = new byte[36398];
			
			using var memoryStream = new MemoryStream(Resources.comp4);
			using var gzipStream = new GZipStream(memoryStream, CompressionMode.Decompress);
			gzipStream.Read(res5231PatchData, 0, res5231PatchData.Length);
			gzipStream.Read(res5232PatchData, 0, res5232PatchData.Length);
		}
		else
		{
			if (GetBuildNumber() >= 8102 || NativeMethods.GetImmersiveColorSetCount() == 1) return;
			
			using (var shsxs = ResNative.LoadLibraryEx(shsxsPath, IntPtr.Zero,
				       ResNative.DONT_RESOLVE_DLL_REFERENCES | ResNative.LOAD_LIBRARY_AS_DATAFILE))
			{
				if (shsxs.IsInvalid) return;
				var res5234 = ResNative.FindResourceEx(shsxs, "PNG", new IntPtr(5234), EnUsLcid);
				res5231PatchData = LoadResource(shsxs, res5234);
			}
			res5232PatchData = res5231PatchData;
		}

		void PatchShsxs(string path)
		{
			using var resUpdater = ResNative.BeginUpdateResource(path, false);
			ResNative.UpdateResource(resUpdater, "PNG", new IntPtr(5231), EnUsLcid,
				res5231PatchData, (uint)res5231PatchData.Length);
			ResNative.UpdateResource(resUpdater, "PNG", new IntPtr(5232), EnUsLcid,
				res5232PatchData, (uint)res5232PatchData.Length);
		}

		PatchShsxs(shsxsPath);
		if (shsxsPathWoW != null) PatchShsxs(shsxsPathWoW);
	}

	private static void DoUiFilePatches(string shsxsPath, UiFilePatchFlags patchFlags)
	{
		Console.WriteLine("[i] Patching with flags 0x{0:x}", (int)patchFlags);

		var uiFileIds = new[] { 3520, 3521, 3522, 3523, 17502, 17542, 17549, 17563, 17576, 17578, 17582 };
		var uiFiles = new string[uiFileIds.Length];
		using (var shsxs = ResNative.LoadLibraryEx(shsxsPath, IntPtr.Zero,
			       ResNative.DONT_RESOLVE_DLL_REFERENCES | ResNative.LOAD_LIBRARY_AS_DATAFILE))
		{
			if (shsxs.IsInvalid) return;
			for (var i = 0; i < uiFileIds.Length; i++)
			{
				var uiFileRes = ResNative.FindResourceEx(shsxs, "UIFILE", new IntPtr(uiFileIds[i]),
					EnUsLcid);
				uiFiles[i] = Encoding.ASCII.GetString(LoadResource(shsxs, uiFileRes));
			}
		}

		for (var i = 0; i < uiFiles.Length; i++)
		{
			if ((patchFlags & UiFilePatchFlags.TouchEditInner) == UiFilePatchFlags.TouchEditInner)
				uiFiles[i] = uiFiles[i].Replace("TouchEditInner", "TouchEdit");
			
			if ((patchFlags & UiFilePatchFlags.ItemHeightInPopup) == UiFilePatchFlags.ItemHeightInPopup)
			{
				uiFiles[i] = uiFiles[i].Replace(" itemheightinpopup=\"55rp\"", "");
				uiFiles[i] = uiFiles[i].Replace(" itemheightinpopup=\"40rp\"", "");
			}

			if ((patchFlags & UiFilePatchFlags.TouchSelectPopup) == UiFilePatchFlags.TouchSelectPopup)
				uiFiles[i] = uiFiles[i].Replace(
					"TouchSelectPopup visible=\"true\" accessible=\"true\" accrole=\"window\" background=\"ImmersiveControlDarkSelectBackgroundPressed\"/>",
					"if id=\"atom(TouchSelectPopup)\"><HWNDElement visible=\"true\" accessible=\"true\" accrole=\"list\"/></if> ");
			
			if ((patchFlags & UiFilePatchFlags.WrappingList) == UiFilePatchFlags.WrappingList)
				uiFiles[i] = uiFiles[i].Replace("WrappingList", "ItemList");
			
			if ((patchFlags & UiFilePatchFlags.TouchCarouselScrollBar) == UiFilePatchFlags.TouchCarouselScrollBar)
				uiFiles[i] = uiFiles[i].Replace("TouchCarouselScrollBar", "TouchScrollBar");
			
			if ((patchFlags & UiFilePatchFlags.TouchSwitch) == UiFilePatchFlags.TouchSwitch)
				for (var j = uiFiles[i].IndexOf("<if class=\"DarkToggleClass\">", StringComparison.Ordinal);
				     j > 0;
				     j = uiFiles[i].IndexOf("<if class=\"DarkToggleClass\">", StringComparison.Ordinal))
				{
					var afterIndex = j + 10194;
					uiFiles[i] = uiFiles[i].Substring(0, j) + uiFiles[i].Substring(afterIndex);
				}

			if ((patchFlags & UiFilePatchFlags.TouchEditDeprecated) == UiFilePatchFlags.TouchEditDeprecated)
				uiFiles[i] = uiFiles[i].Replace("<TouchEdit ", "<TouchEdit2 ");
		}

		using var resUpdater = ResNative.BeginUpdateResource(shsxsPath, false);
		for (var i = 0; i < uiFileIds.Length; i++)
		{
			var uiFileBytes = Encoding.ASCII.GetBytes(uiFiles[i]);
			ResNative.UpdateResource(resUpdater, "UIFILE", new IntPtr(uiFileIds[i]), EnUsLcid,
				uiFileBytes, (uint)uiFileBytes.Length);
		}
	}

	private static IEnumerable<KeyValuePair<int, string>> GetMuiFilesForFile(string baseFile)
	{
		baseFile = Path.GetFullPath(baseFile);
		if (!File.Exists(baseFile))
			yield break;
		
		var dir = Path.GetDirectoryName(baseFile) + Path.DirectorySeparatorChar;
		var muiName = $"{Path.GetFileName(baseFile)}.mui";
		
		bool TryMakeNewPath(string cultureId, out string path)
		{
			path = Path.Combine(dir, cultureId, muiName);
			return File.Exists(path); 
		}

		string path;
		var installedCultures = CultureInfo.GetCultures(CultureTypes.InstalledWin32Cultures);
		foreach (var culture in installedCultures)
		{
			if (culture.Equals(CultureInfo.InvariantCulture))
				continue;
			if (TryMakeNewPath(culture.Name, out path)
			    || TryMakeNewPath(culture.LCID.ToString(CultureInfo.InvariantCulture), out path))
				yield return new KeyValuePair<int, string>(culture.LCID, path);
		}
	}
	
	private static void DoDuiMuiPatches(bool alsoPatchWow)
	{
		var res7PatchData = new byte[246];
		var res8PatchData = new byte[550];
		var res9PatchData = new byte[260];
		using (var memoryStream = new MemoryStream(Resources.comp3))
			using (var gzipStream = new GZipStream(memoryStream, CompressionMode.Decompress))
			{
				gzipStream.Read(res7PatchData, 0, res7PatchData.Length);
				gzipStream.Read(res8PatchData, 0, res8PatchData.Length);
				gzipStream.Read(res9PatchData, 0, res9PatchData.Length);
			}

		var muiFiles = GetMuiFilesForFile(@$"{Environment.SystemDirectory}\dui70.dll");
		if (alsoPatchWow)
			muiFiles = muiFiles.Concat(
				GetMuiFilesForFile(@$"{Environment.GetFolderPath(Environment.SpecialFolder.SystemX86)}\dui70.dll"));
		foreach (var muiEntry in muiFiles)
		{
			var lcid = (ushort)muiEntry.Key;
			var muiFile = muiEntry.Value;
			
			bool res8NotPresent;
			bool res9NotPresent;
			int res7OrigSize;
			byte[] res7Data;
			using (var mui = ResNative.LoadLibraryEx(muiFile, IntPtr.Zero,
				       ResNative.DONT_RESOLVE_DLL_REFERENCES | ResNative.LOAD_LIBRARY_AS_DATAFILE))
			{
				var res = ResNative.FindResourceEx(mui, new IntPtr(6), new IntPtr(8),
					lcid);
				res8NotPresent = res == IntPtr.Zero;
				res = ResNative.FindResourceEx(mui, new IntPtr(6), new IntPtr(9),
					lcid);
				res9NotPresent = res == IntPtr.Zero;
				
				res = ResNative.FindResourceEx(mui, new IntPtr(6), new IntPtr(7),
					lcid);
				if (res == IntPtr.Zero) continue;
				res7OrigSize = ResNative.SizeofResource(mui, res);
				res7Data = LoadResource(mui, res);
			}

			const int res7PossiblePadSize = 16; 
			var res7HasPadding = true;
			for (var i = res7OrigSize - res7PossiblePadSize; i < res7OrigSize; i++)
				if (res7Data[i] > 0)
				{
					res7HasPadding = false;
					break;
				}
			var res7RealPadSize = res7HasPadding ? res7PossiblePadSize : 0;
			
			if (res7Data[res7OrigSize - res7RealPadSize - 2] == 0x25)
			{
				Array.Resize(ref res7Data, res7OrigSize - res7RealPadSize + res7PatchData.Length);
				Array.Copy(res7PatchData, 0,
					res7Data, res7OrigSize - res7RealPadSize,
					res7PatchData.Length);
			}

			var resUpdater = new ResNative.SafeResourceUpdateHandle(new IntPtr(0));
			void UpdateResource(IntPtr lpType, IntPtr lpName, byte[] lpData)
			{
				if (resUpdater.IsInvalid) resUpdater = GetResourceUpdaterForMUI(muiFile);
				ResNative.UpdateResource(resUpdater, lpType, lpName,
					lcid, lpData, (uint)lpData.Length);
			}
			
			try
			{
				if (res7OrigSize != res7Data.Length)
					UpdateResource(new IntPtr(6), new IntPtr(7), res7Data);
				if (res8NotPresent)
					UpdateResource(new IntPtr(6), new IntPtr(8), res8PatchData);
				if (res9NotPresent)
					UpdateResource(new IntPtr(6), new IntPtr(9), res9PatchData);

				if (!resUpdater.IsInvalid)
				{
					resUpdater.Close();
					RevertMuiWorkaround(muiFile);
				}
			}
			finally
			{
				if (!resUpdater.IsClosed)
					resUpdater.Close();
			}
		}
	}

	private static ResNative.SafeResourceUpdateHandle GetResourceUpdaterForMUI(string filePath)
	{
		File.Copy(filePath, filePath + ".orig", true);
		
		var muiStrOfs = PatternFinder.FindPatternInFile(filePath, Encoding.Unicode.GetBytes("MUI"));
		using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Write))
		{
			stream.Seek(muiStrOfs, SeekOrigin.Begin);
			stream.WriteByte(65); // 'A'
		}

		return ResNative.BeginUpdateResource(filePath, false);
	}
	
	private static void RevertMuiWorkaround(string filePath)
	{
		var muiStrOfs = PatternFinder.FindPatternInFile(filePath, Encoding.Unicode.GetBytes("AUI"));
		using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Write);
		stream.Seek(muiStrOfs, SeekOrigin.Begin);
		stream.WriteByte(77); // 'M'
	}

	private static void RevertDuiMuiPatches()
	{
		var muiFiles = GetMuiFilesForFile(@$"{Environment.SystemDirectory}\dui70.dll");
		muiFiles = muiFiles.Concat(
			GetMuiFilesForFile(@$"{Environment.GetFolderPath(Environment.SpecialFolder.SystemX86)}\dui70.dll"));
		foreach (var muiEntry in muiFiles)
		{
			var muiFile = muiEntry.Value;
			var origMuiFile = muiFile + ".orig";
			if (!File.Exists(origMuiFile)) continue;
			File.Delete(muiFile);
			File.Move(origMuiFile, muiFile);
		}
	}
	
	private static int GetBuildNumber()
	{
		using var currentVersion =
			Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
		if (currentVersion is null)
			return -1;
		return int.Parse((string)currentVersion.GetValue("CurrentBuild", "-1"));
	}

	[Flags]
	private enum UiFilePatchFlags
	{
		None = 0,
		TouchEditInner = 1,
		ItemHeightInPopup = 2,
		TouchSelectPopup = 4,
		WrappingList = 8,
		TouchCarouselScrollBar = 16,
		TouchSwitch = 32,
		TouchEditDeprecated = 64
	}

	private static class ResNative
	{
		[DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "LoadLibraryExW", SetLastError = true)]
		internal static extern NativeMethods.SafeLibraryHandle LoadLibraryEx(string lpFileName, IntPtr hFile, uint dwFlags);

		// ReSharper disable once InconsistentNaming
		internal const int DONT_RESOLVE_DLL_REFERENCES = 0x00000001;
		// ReSharper disable once InconsistentNaming
		internal const int LOAD_LIBRARY_AS_DATAFILE = 0x00000002;
		
		[DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "FindResourceExW", SetLastError = true)]
		internal static extern IntPtr
			FindResourceEx(NativeMethods.SafeLibraryHandle hModule, IntPtr lpszType, IntPtr lpszName, ushort wLanguage);

		[DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "FindResourceExW", SetLastError = true)]
		internal static extern IntPtr
			FindResourceEx(NativeMethods.SafeLibraryHandle hModule, string lpszType, IntPtr lpszName, ushort wLanguage);

		[DllImport("kernel32.dll", SetLastError = true)]
		internal static extern int SizeofResource(NativeMethods.SafeLibraryHandle hInstance, IntPtr hResInfo);

		[DllImport("kernel32.dll", SetLastError = true)]
		internal static extern IntPtr LoadResource(NativeMethods.SafeLibraryHandle hModule, IntPtr hResData);

		[DllImport("kernel32.dll", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Unicode,
			EntryPoint = "BeginUpdateResourceW", ExactSpelling = true, SetLastError = true)]
		internal static extern SafeResourceUpdateHandle BeginUpdateResource(string pFileName,
			bool bDeleteExistingResources);

		[DllImport("kernel32.dll", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Unicode,
			EntryPoint = "UpdateResourceW", ExactSpelling = true, SetLastError = true)]
		internal static extern bool UpdateResource(SafeResourceUpdateHandle hUpdate, IntPtr lpType, IntPtr lpName,
			ushort wLanguage,
			byte[] lpData, uint cbData);

		[DllImport("kernel32.dll", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Unicode,
			EntryPoint = "UpdateResourceW", ExactSpelling = true, SetLastError = true)]
		internal static extern bool UpdateResource(SafeResourceUpdateHandle hUpdate, string lpType, IntPtr lpName,
			ushort wLanguage,
			byte[] lpData, uint cbData);

		internal sealed class SafeResourceUpdateHandle : SafeHandleZeroOrMinusOneIsInvalid
		{
			internal SafeResourceUpdateHandle()
				: base(true)
			{
			}

			internal SafeResourceUpdateHandle(IntPtr handle)
				: base(true)
			{
			}

			[DllImport("kernel32.dll", CallingConvention = CallingConvention.StdCall, EntryPoint = "EndUpdateResourceW",
				ExactSpelling = true, SetLastError = true)]
			private static extern bool EndUpdateResource(IntPtr hUpdate, bool fDiscard);

			protected override bool ReleaseHandle()
			{
				return EndUpdateResource(handle, false);
			}
		}
	}
}