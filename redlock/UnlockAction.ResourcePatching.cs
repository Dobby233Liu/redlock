using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace redlock;

internal partial class UnlockAction
{
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
	
	private byte[] LoadResource(SafeLibraryHandle resLib, IntPtr resId)
	{
		var data = new byte[ResNative.SizeofResource(resLib, resId)];
		Marshal.Copy(ResNative.LoadResource(resLib, resId), data, 0, data.Length);
		return data;
	}
	
	internal void ConformAccentResources(string shsxsPath, string? shsxsPathWoW, string twinUiPath)
	{
		Console.WriteLine("[i] Conforming accent resources");
		
		bool twinRes4807Exists;
		using (var twinUi = ResNative.LoadLibraryEx(twinUiPath, IntPtr.Zero,
			       ResNative.DONT_RESOLVE_DLL_REFERENCES | ResNative.LOAD_LIBRARY_AS_DATAFILE))
		{
			if (twinUi.IsInvalid) return;
			twinRes4807Exists = 
				ResNative.FindResourceEx(twinUi, ResType2, TwinResId4807, EnUsLcid) == IntPtr.Zero;
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
			if (GetBuildNumber() >= 8102 || GetImmersiveColorSetCount() == 1) return;
			
			using (var shsxs = ResNative.LoadLibraryEx(shsxsPath, IntPtr.Zero,
				       ResNative.DONT_RESOLVE_DLL_REFERENCES | ResNative.LOAD_LIBRARY_AS_DATAFILE))
			{
				if (shsxs.IsInvalid) return;
				var res5234 = ResNative.FindResourceEx(shsxs, "PNG", SxsResId5234, EnUsLcid);
				res5231SubstData = LoadResource(shsxs, res5234);
			}
			res5232SubstData = res5231SubstData;
		}

		void PatchShsxs(string path)
		{
			using var resUpdater = ResNative.BeginUpdateResource(path, false);
			ResNative.UpdateResource(resUpdater, "PNG", SxsResId5231, EnUsLcid,
				res5231SubstData, (uint)res5231SubstData.Length);
			ResNative.UpdateResource(resUpdater, "PNG", SxsResId5232, EnUsLcid,
				res5232SubstData, (uint)res5232SubstData.Length);
		}

		PatchShsxs(shsxsPath);
		if (shsxsPathWoW != null) PatchShsxs(shsxsPathWoW);
	}

	internal void DoUiFilePatches(string shsxsPath, UiFilePatchFlags patchFlags)
	{
		Console.WriteLine("[i] Patching with flags 0x{0:x}", (int)patchFlags);

		var uiFiles = new string[SxsUiFileIds.Length];
		using (var shsxs = ResNative.LoadLibraryEx(shsxsPath, IntPtr.Zero,
			       ResNative.DONT_RESOLVE_DLL_REFERENCES | ResNative.LOAD_LIBRARY_AS_DATAFILE))
		{
			if (shsxs.IsInvalid) return;
			for (var i = 0; i < SxsUiFileIds.Length; i++)
			{
				var uiFileRes = ResNative.FindResourceEx(shsxs, "UIFILE", SxsUiFileIds[i], EnUsLcid);
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

		using var resUpdater = ResNative.BeginUpdateResource(shsxsPath, false);
		for (var i = 0; i < SxsUiFileIds.Length; i++)
		{
			var uiFileBytes = Encoding.ASCII.GetBytes(uiFiles[i]);
			ResNative.UpdateResource(resUpdater, "UIFILE", SxsUiFileIds[i], EnUsLcid,
				uiFileBytes, (uint)uiFileBytes.Length);
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
		
		var muiFiles = GetMuiFilesForFile(GetSystemFile("dui70.dll"));
		if (alsoPatchWow)
			muiFiles = muiFiles.Concat(GetMuiFilesForFile(GetSystemFile("dui70.dll", true)));
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
				var res = ResNative.FindResourceEx(mui, ResType6, DuiResId8, lcid);
				res8NotPresent = res == IntPtr.Zero;
				res = ResNative.FindResourceEx(mui, ResType6, DuiResId9, lcid);
				res9NotPresent = res == IntPtr.Zero;
				
				res = ResNative.FindResourceEx(mui, ResType6, DuiResId7, lcid);
				if (res == IntPtr.Zero) continue;
				res7OrigSize = ResNative.SizeofResource(mui, res);
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

			var resUpdater = new ResNative.SafeResourceUpdateHandle(NullHandle);
			void UpdateResource(IntPtr lpType, IntPtr lpName, byte[] lpData)
			{
				if (resUpdater.IsInvalid)
				{
					resUpdater.Close();
					resUpdater = new ResourceUpdaterMui(muiFile);
				}

				ResNative.UpdateResource(resUpdater, lpType, lpName, lcid, lpData, (uint)lpData.Length);
			}

			try
			{
				if (res7OrigSize != res7Data.Length)
					UpdateResource(ResType6, DuiResId7, res7Data);
				if (res8NotPresent)
					UpdateResource(ResType6, DuiResId8, res8SubstData);
				if (res9NotPresent)
					UpdateResource(ResType6, DuiResId9, res9SubstData);
			}
			finally
			{
				if (!resUpdater.IsClosed)
					resUpdater.Close();
			}
		}
	}

	private class ResourceUpdaterMui : ResNative.SafeResourceUpdateHandle
	{
		public readonly string FilePath;
		
		internal ResourceUpdaterMui(string muiFile) : base(NullHandle)
		{
			FilePath = muiFile;
			
			PerformMuiWorkaround();

			SetHandle(ResNative.BeginUpdateResourceRawPtr(FilePath, false));
			if (base.IsInvalid)
				RevertMuiWorkaround();
		}

		protected void PerformMuiWorkaround()
		{
			File.Copy(FilePath, FilePath + ".orig", true);
			
			// https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-updateresourcea#remarks
			// so we make the system think this is not a MUI file
			var muiStrOfs = PatternFinder.FindPatternInFile(FilePath,
				Encoding.Unicode.GetBytes("MUI"));
			using (var stream = new FileStream(FilePath, FileMode.Open, FileAccess.Write))
			{
				stream.Seek(muiStrOfs, SeekOrigin.Begin);
				stream.WriteByte(65); // 'A'
			}
		}
		
		protected void RevertMuiWorkaround()
		{
			var muiStrOfs = PatternFinder.FindPatternInFile(FilePath,
				Encoding.Unicode.GetBytes("AUI"));
			using var stream = new FileStream(FilePath, FileMode.Open, FileAccess.Write);
			stream.Seek(muiStrOfs, SeekOrigin.Begin);
			stream.WriteByte(77); // 'M'
		}
		
		protected override bool ReleaseHandle()
		{
			var result = base.ReleaseHandle();
			if (result)
				RevertMuiWorkaround();
			return result;
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

	protected static class ResNative
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
		internal static extern IntPtr BeginUpdateResourceRawPtr(string pFileName, bool bDeleteExistingResources);
		
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

		internal class SafeResourceUpdateHandle : SafeHandleZeroOrMinusOneIsInvalid
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