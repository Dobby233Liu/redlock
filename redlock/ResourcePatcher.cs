using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace redlock;

internal class ResourcePatcher
{
	private readonly BaseAction _action;
	
	private const ushort EnUsLcid = 1033;
	
	private static readonly IntPtr ResType2 = new(2);
	private static readonly IntPtr TwinResId4807 = new(4807);
	private static readonly IntPtr SxsResId5231 = new(5231);
	private static readonly IntPtr SxsResId5232 = new(5232);
	private static readonly IntPtr SxsResId5234 = new(5234);
	
	private static readonly IntPtr[] SxsUiFileIds = 
	[
		new(3520), new(3521), new(3522), new(3523), new(17502), new(17542), new(17549), new(17563), new(17576),
		new(17578), new(17582)
	];
	
	private const int TouchSwitchStripPortionLength = 10194; // HARDCODED
	
	private static readonly IntPtr ResType6 = new(6);
	private static readonly IntPtr DuiResId7 = new(7);
	private const int DuiRes7PossiblePadSize = 16;
	private static readonly IntPtr DuiResId8 = new(8);
	private static readonly IntPtr DuiResId9 = new(9);
	private static readonly IntPtr NullHandle = IntPtr.Zero;

	internal ResourcePatcher(BaseAction action)
	{
		_action = action;
	}
	
	private byte[] LoadResource(SafeLibraryHandle resLib, IntPtr resId)
	{
		var data = new byte[Native.SizeofResource(resLib, resId)];
		Marshal.Copy(Native.LoadResource(resLib, resId), data, 0, data.Length);
		return data;
	}
	
	public void ConformAccentResources(string shsxsPath, string? shsxsPathWoW, string twinUiPath)
	{
		Console.WriteLine("[i] Conforming accent resources");
		
		bool twinRes4807Exists;
		using (var twinUi = Native.LoadLibraryEx(twinUiPath, IntPtr.Zero,
			       Native.DONT_RESOLVE_DLL_REFERENCES | Native.LOAD_LIBRARY_AS_DATAFILE))
		{
			if (twinUi.IsInvalid) return;
			twinRes4807Exists = 
				Native.FindResourceEx(twinUi, ResType2, TwinResId4807, EnUsLcid) == IntPtr.Zero;
		}
		
		byte[] res5231SubstData;
		byte[] res5232SubstData;
		if (twinRes4807Exists)
		{
			using var comp4 = new BlobPacks.Comp4();
			res5231SubstData = comp4.Read(comp4.SxsRes5231).Data;
			res5232SubstData = comp4.Read(comp4.SxsRes5232).Data;
		}
		else
		{
			if (_action.GetBuildNumber() >= 8102 || _action.GetImmersiveColorSetCount() == 1) return;
			
			using (var shsxs = Native.LoadLibraryEx(shsxsPath, IntPtr.Zero,
				       Native.DONT_RESOLVE_DLL_REFERENCES | Native.LOAD_LIBRARY_AS_DATAFILE))
			{
				if (shsxs.IsInvalid) return;
				var res5234 = Native.FindResourceEx(shsxs, "PNG", SxsResId5234, EnUsLcid);
				res5231SubstData = LoadResource(shsxs, res5234);
			}
			res5232SubstData = res5231SubstData;
		}

		void PatchShsxs(string path)
		{
			using var resUpdater = Native.BeginUpdateResource(path, false);
			Native.UpdateResource(resUpdater, "PNG", SxsResId5231, EnUsLcid,
				res5231SubstData, (uint)res5231SubstData.Length);
			Native.UpdateResource(resUpdater, "PNG", SxsResId5232, EnUsLcid,
				res5232SubstData, (uint)res5232SubstData.Length);
		}

		PatchShsxs(shsxsPath);
		if (shsxsPathWoW != null) PatchShsxs(shsxsPathWoW);
	}

	internal void DoUiFilePatches(string shsxsPath, UiFilePatchFlags patchFlags)
	{
		Console.WriteLine("[i] Patching with flags 0x{0:x}", (int)patchFlags);

		var uiFiles = new string[SxsUiFileIds.Length];
		using (var shsxs = Native.LoadLibraryEx(shsxsPath, IntPtr.Zero,
			       Native.DONT_RESOLVE_DLL_REFERENCES | Native.LOAD_LIBRARY_AS_DATAFILE))
		{
			if (shsxs.IsInvalid) return;
			for (var i = 0; i < SxsUiFileIds.Length; i++)
			{
				var uiFileRes = Native.FindResourceEx(shsxs, "UIFILE", SxsUiFileIds[i], EnUsLcid);
				uiFiles[i] = Encoding.ASCII.GetString(LoadResource(shsxs, uiFileRes));
			}
		}

		for (var i = 0; i < uiFiles.Length; i++)
		{
			if (patchFlags.HasFlag(UiFilePatchFlags.TouchEditInner))
				uiFiles[i] = uiFiles[i].Replace("TouchEditInner", "TouchEdit");
			
			if (patchFlags.HasFlag(UiFilePatchFlags.ItemHeightInPopup))
			{
				uiFiles[i] = uiFiles[i].Replace(" itemheightinpopup=\"55rp\"", "");
				uiFiles[i] = uiFiles[i].Replace(" itemheightinpopup=\"40rp\"", "");
			}

			if (patchFlags.HasFlag(UiFilePatchFlags.TouchSelectPopup))
				uiFiles[i] = uiFiles[i].Replace(
					"TouchSelectPopup visible=\"true\" accessible=\"true\" accrole=\"window\" background=\"ImmersiveControlDarkSelectBackgroundPressed\"/>",
					"if id=\"atom(TouchSelectPopup)\"><HWNDElement visible=\"true\" accessible=\"true\" accrole=\"list\"/></if> ");
			
			if (patchFlags.HasFlag(UiFilePatchFlags.WrappingList))
				uiFiles[i] = uiFiles[i].Replace("WrappingList", "ItemList");
			
			if (patchFlags.HasFlag(UiFilePatchFlags.TouchCarouselScrollBar))
				uiFiles[i] = uiFiles[i].Replace("TouchCarouselScrollBar", "TouchScrollBar");
			
			if (patchFlags.HasFlag(UiFilePatchFlags.TouchSwitch))
				for (var j = uiFiles[i].IndexOf("<if class=\"DarkToggleClass\">", StringComparison.Ordinal);
				     j > 0;
				     j = uiFiles[i].IndexOf("<if class=\"DarkToggleClass\">", StringComparison.Ordinal))
				{
					var afterStripPortion = j + TouchSwitchStripPortionLength;
					uiFiles[i] = uiFiles[i].Substring(0, j) + uiFiles[i].Substring(afterStripPortion);
				}

			if (patchFlags.HasFlag(UiFilePatchFlags.TouchEditDeprecated))
				uiFiles[i] = uiFiles[i].Replace("<TouchEdit ", "<TouchEdit2 ");
		}

		using var resUpdater = Native.BeginUpdateResource(shsxsPath, false);
		for (var i = 0; i < SxsUiFileIds.Length; i++)
		{
			var uiFileBytes = Encoding.ASCII.GetBytes(uiFiles[i]);
			Native.UpdateResource(resUpdater, "UIFILE", SxsUiFileIds[i], EnUsLcid,
				uiFileBytes, (uint)uiFileBytes.Length);
		}
	}

	private IEnumerable<KeyValuePair<int, string>> GetMuiFilesForFile(string baseFile)
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

		var installedCultures = CultureInfo.GetCultures(CultureTypes.InstalledWin32Cultures);
		foreach (var culture in installedCultures)
		{
			if (culture.Equals(CultureInfo.InvariantCulture))
				continue;
			if (TryMakeNewPath(culture.Name, out var path)
			    || TryMakeNewPath(culture.LCID.ToString(CultureInfo.InvariantCulture), out path))
				yield return new KeyValuePair<int, string>(culture.LCID, path);
		}
	}

	internal void DoDuiMuiPatches(bool alsoPatchWow)
	{
		byte[] res7PatchData;
		byte[] res8SubstData;
		byte[] res9SubstData;
		using (var comp3 = new BlobPacks.Comp3())
		{
			res7PatchData = comp3.Read(comp3.DuiRes7Patch).Data;
			res8SubstData = comp3.Read(comp3.DuiRes8).Data;
			res9SubstData = comp3.Read(comp3.DuiRes9).Data;
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
			using (var mui = Native.LoadLibraryEx(muiFile, IntPtr.Zero,
				       Native.DONT_RESOLVE_DLL_REFERENCES | Native.LOAD_LIBRARY_AS_DATAFILE))
			{
				var res = Native.FindResourceEx(mui, ResType6, DuiResId8, lcid);
				res8NotPresent = res == IntPtr.Zero;
				res = Native.FindResourceEx(mui, ResType6, DuiResId9, lcid);
				res9NotPresent = res == IntPtr.Zero;
				
				res = Native.FindResourceEx(mui, ResType6, DuiResId7, lcid);
				if (res == IntPtr.Zero) continue;
				res7OrigSize = Native.SizeofResource(mui, res);
				res7Data = LoadResource(mui, res);
			}

			var res7HasPadding = true;
			for (var i = res7OrigSize - DuiRes7PossiblePadSize; i < res7OrigSize; i++)
				if (res7Data[i] != 0)
				{
					res7HasPadding = false;
					break;
				}
			var res7RealPadSize = res7HasPadding ? DuiRes7PossiblePadSize : 0;
			
			if (res7Data[res7OrigSize - res7RealPadSize - 2] == 0x25)
			{
				Array.Resize(ref res7Data, res7OrigSize - res7RealPadSize + res7PatchData.Length);
				Array.Copy(res7PatchData, 0,
					res7Data, res7OrigSize - res7RealPadSize,
					res7PatchData.Length);
			}

			var resUpdater = new Native.SafeResourceUpdateHandle(NullHandle);
			void UpdateResource(IntPtr lpType, IntPtr lpName, byte[] lpData)
			{
				if (resUpdater.IsInvalid) resUpdater = GetResourceUpdaterForMUI(muiFile);
				Native.UpdateResource(resUpdater, lpType, lpName, lcid, lpData, (uint)lpData.Length);
			}
			
			try
			{
				if (res7OrigSize != res7Data.Length)
					UpdateResource(ResType6, DuiResId7, res7Data);
				if (res8NotPresent)
					UpdateResource(ResType6, DuiResId8, res8SubstData);
				if (res9NotPresent)
					UpdateResource(ResType6, DuiResId9, res9SubstData);

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

	private Native.SafeResourceUpdateHandle GetResourceUpdaterForMUI(string filePath)
	{
		File.Copy(filePath, filePath + ".orig", true);
		
		// https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-updateresourcea#remarks
		// so we make the system think this is not a MUI file
		var muiStrOfs = PatternFinder.FindPatternInFile(filePath, Encoding.Unicode.GetBytes("MUI"));
		using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Write))
		{
			stream.Seek(muiStrOfs, SeekOrigin.Begin);
			stream.WriteByte(65); // 'A'
		}

		return Native.BeginUpdateResource(filePath, false);
	}
	
	private void RevertMuiWorkaround(string filePath)
	{
		var muiStrOfs = PatternFinder.FindPatternInFile(filePath, Encoding.Unicode.GetBytes("AUI"));
		using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Write);
		stream.Seek(muiStrOfs, SeekOrigin.Begin);
		stream.WriteByte(77); // 'M'
	}

	internal void RevertDuiMuiPatches()
	{
		var muiFiles = GetMuiFilesForFile(_action.GetSystemFile("dui70.dll"));
		muiFiles = muiFiles.Concat(
			GetMuiFilesForFile(_action.GetSystemFile("dui70.dll", true)));
		foreach (var muiEntry in muiFiles)
		{
			var muiFile = muiEntry.Value;
			var origMuiFile = muiFile + ".orig";
			if (!File.Exists(origMuiFile)) continue;
			File.Delete(muiFile);
			File.Move(origMuiFile, muiFile);
		}
	}

	[Flags]
	internal enum UiFilePatchFlags
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

	private static class Native
	{
		[DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "LoadLibraryExW", SetLastError = true)]
		internal static extern SafeLibraryHandle LoadLibraryEx(string lpFileName, IntPtr hFile, uint dwFlags);

		// ReSharper disable once InconsistentNaming
		internal const int DONT_RESOLVE_DLL_REFERENCES = 0x00000001;
		// ReSharper disable once InconsistentNaming
		internal const int LOAD_LIBRARY_AS_DATAFILE = 0x00000002;
		
		[DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "FindResourceExW", SetLastError = true)]
		internal static extern IntPtr
			FindResourceEx(SafeLibraryHandle hModule, IntPtr lpszType, IntPtr lpszName, ushort wLanguage);

		[DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "FindResourceExW", SetLastError = true)]
		internal static extern IntPtr
			FindResourceEx(SafeLibraryHandle hModule, string lpszType, IntPtr lpszName, ushort wLanguage);

		[DllImport("kernel32.dll", SetLastError = true)]
		internal static extern int SizeofResource(SafeLibraryHandle hInstance, IntPtr hResInfo);

		[DllImport("kernel32.dll", SetLastError = true)]
		internal static extern IntPtr LoadResource(SafeLibraryHandle hModule, IntPtr hResData);

		[DllImport("kernel32.dll", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Unicode,
			EntryPoint = "BeginUpdateResourceW", ExactSpelling = true, SetLastError = true)]
		internal static extern SafeResourceUpdateHandle BeginUpdateResource(string pFileName,
			bool bDeleteExistingResources);

		[DllImport("kernel32.dll", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Unicode,
			EntryPoint = "UpdateResourceW", ExactSpelling = true, SetLastError = true)]
		internal static extern bool UpdateResource(SafeResourceUpdateHandle hUpdate,
			IntPtr lpType, IntPtr lpName, ushort wLanguage, byte[] lpData, uint cbData);

		[DllImport("kernel32.dll", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Unicode,
			EntryPoint = "UpdateResourceW", ExactSpelling = true, SetLastError = true)]
		internal static extern bool UpdateResource(SafeResourceUpdateHandle hUpdate,
			string lpType, IntPtr lpName, ushort wLanguage, byte[] lpData, uint cbData);

		internal sealed class SafeResourceUpdateHandle : SafeHandleZeroOrMinusOneIsInvalid
		{
			internal SafeResourceUpdateHandle(IntPtr handle)
				: base(true)
			{
				SetHandle(handle);
			}

			[DllImport("kernel32.dll", CallingConvention = CallingConvention.StdCall,
				EntryPoint = "EndUpdateResourceW", ExactSpelling = true, SetLastError = true)]
			private static extern bool EndUpdateResource(IntPtr hUpdate, bool fDiscard);

			protected override bool ReleaseHandle()
			{
				return EndUpdateResource(handle, false);
			}
		}
	}
}