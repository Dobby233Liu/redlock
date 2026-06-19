using System;
using System.IO;
using System.Globalization;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using redlock.Properties;

namespace redlock;

internal static partial class Program
{
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
	
	private static class ResNativeMethods
	{
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
			
			protected override bool ReleaseHandle() => EndUpdateResource(this.handle, false);
		}
		
		[DllImport("kernel32.dll", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Unicode,
			EntryPoint = "BeginUpdateResourceW", ExactSpelling = true, SetLastError = true)]
		internal static extern SafeResourceUpdateHandle BeginUpdateResource(string pFileName, bool bDeleteExistingResources);

		[DllImport("kernel32.dll", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Unicode,
			EntryPoint = "UpdateResourceW", ExactSpelling = true, SetLastError = true)]
		internal static extern bool UpdateResource(SafeResourceUpdateHandle hUpdate, IntPtr lpType, IntPtr lpName, ushort wLanguage,
			byte[] lpData, uint cbData);

		[DllImport("kernel32.dll", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Unicode,
			EntryPoint = "UpdateResourceW", ExactSpelling = true, SetLastError = true)]
		internal static extern bool UpdateResource(SafeResourceUpdateHandle hUpdate, string lpType, IntPtr lpName, ushort wLanguage,
			byte[] lpData, uint cbData);
	}
	
	private static void ConformAccentResources(string patchingNative, string patchingWoW, string twinGuidance)
	{
		Console.WriteLine("[i] Conforming accent resources");
		bool flag;
		using (var intPtr = NativeMethods.LoadLibraryEx(twinGuidance, IntPtr.Zero, 3U))
		{
			if (intPtr.IsInvalid) return;
			flag = ResNativeMethods.FindResourceEx(intPtr, new IntPtr(2), new IntPtr(4807), 1033) == IntPtr.Zero;
		}

		byte[] array;
		byte[] array2;
		if (flag)
		{
			array = new byte[53352];
			array2 = new byte[36398];
			using var memoryStream = new MemoryStream(Resources.comp4);
			using var gzipStream = new GZipStream(memoryStream, CompressionMode.Decompress);
			gzipStream.Read(array, 0, array.Length);
			gzipStream.Read(array2, 0, array2.Length);
		}
		else
		{
			if (GetBuildNumber() >= 8102 || NativeMethods.GetImmersiveColorSetCount() == 1) return;
			
			using (var intPtr = NativeMethods.LoadLibraryEx(patchingNative, IntPtr.Zero, 3U))
			{
				if (intPtr.IsInvalid) return;
				var intPtr2 = ResNativeMethods.FindResourceEx(intPtr, "PNG", new IntPtr(5234), 1033);
				var num = ResNativeMethods.SizeofResource(intPtr, intPtr2);
				array = new byte[num];
				Marshal.Copy(ResNativeMethods.LoadResource(intPtr, intPtr2), array, 0, num);
			}

			array2 = array;
		}

		using (var intPtr3 = ResNativeMethods.BeginUpdateResource(patchingNative, false))
		{
			ResNativeMethods.UpdateResource(intPtr3, "PNG", new IntPtr(5231), 1033, array, (uint)array.Length);
			ResNativeMethods.UpdateResource(intPtr3, "PNG", new IntPtr(5232), 1033, array2, (uint)array2.Length);
		}

		if (patchingWoW != null)
		{
			using var intPtr4 = ResNativeMethods.BeginUpdateResource(patchingWoW, false);
			ResNativeMethods.UpdateResource(intPtr4, "PNG", new IntPtr(5231), 1033, array, (uint)array.Length);
			ResNativeMethods.UpdateResource(intPtr4, "PNG", new IntPtr(5232), 1033, array2, (uint)array2.Length);
		}
	}

	private static void DoUiFilePatches(string patchingTarget, UiFilePatchFlags patchFlags)
	{
		Console.WriteLine("[i] Patching with flags 0x{0:x}", (int)patchFlags);
		
		var array = new[]
		{
			3520, 3521, 3522, 3523, 17502, 17542, 17549, 17563, 17576, 17578,
			17582
		};
		var array2 = new string[11];
		using (var intPtr = NativeMethods.LoadLibraryEx(patchingTarget, IntPtr.Zero, 3U))
		{
			if (intPtr.IsInvalid) return;
			for (var i = 0; i < 11; i++)
			{
				var intPtr2 = ResNativeMethods.FindResourceEx(intPtr, "UIFILE", new IntPtr(array[i]), 1033);
				var num = ResNativeMethods.SizeofResource(intPtr, intPtr2);
				var array3 = new byte[num];
				Marshal.Copy(ResNativeMethods.LoadResource(intPtr, intPtr2), array3, 0, num);
				array2[i] = Encoding.ASCII.GetString(array3);
			}
		}

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

		using var intPtr3 = ResNativeMethods.BeginUpdateResource(patchingTarget, false);
		for (var l = 0; l < 11; l++)
		{
			var bytes = Encoding.ASCII.GetBytes(array2[l]);
			ResNativeMethods.UpdateResource(intPtr3, "UIFILE", new IntPtr(array[l]), 1033, bytes, (uint)bytes.Length);
		}
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
			array4 =
			[
				Environment.SystemDirectory + "\\" + culture.Name + "\\dui70.dll.mui",
				Environment.GetFolderPath(Environment.SpecialFolder.SystemX86) + "\\" + culture.Name +
				"\\dui70.dll.mui"
			];
		else
			array4 = [Environment.SystemDirectory + "\\" + culture.Name + "\\dui70.dll.mui"];
		foreach (var text in array4)
		{
			bool flag;
			bool flag2;
			int num;
			byte[] array6;
			
			using (var intPtr = NativeMethods.LoadLibraryEx(text, IntPtr.Zero, 3U))
			{
				var intPtr2 = ResNativeMethods.FindResourceEx(intPtr, new IntPtr(6), new IntPtr(9),
					(ushort)culture.LCID);
				flag = intPtr2 == IntPtr.Zero;
				intPtr2 = ResNativeMethods.FindResourceEx(intPtr, new IntPtr(6), new IntPtr(8),
					(ushort)culture.LCID);
				flag2 = intPtr2 == IntPtr.Zero;
				intPtr2 = ResNativeMethods.FindResourceEx(intPtr, new IntPtr(6), new IntPtr(7),
					(ushort)culture.LCID);

				if (intPtr2 == IntPtr.Zero) continue;

				num = ResNativeMethods.SizeofResource(intPtr, intPtr2);
				array6 = new byte[num];
				Marshal.Copy(ResNativeMethods.LoadResource(intPtr, intPtr2), array6, 0, num);
			}

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

			var intPtr3 = new ResNativeMethods.SafeResourceUpdateHandle(new IntPtr(0));
			try
			{
				if (num != array6.Length)
				{
					if (intPtr3.IsInvalid) intPtr3 = GetResourceUpdaterForMUI(text);
					ResNativeMethods.UpdateResource(intPtr3, new IntPtr(6), new IntPtr(7),
						(ushort)culture.LCID, array6, (uint)array6.Length);
				}

				if (flag2)
				{
					if (intPtr3.IsInvalid) intPtr3 = GetResourceUpdaterForMUI(text);
					ResNativeMethods.UpdateResource(intPtr3, new IntPtr(6), new IntPtr(8),
						(ushort)culture.LCID, array2, (uint)array2.Length);
				}

				if (flag)
				{
					if (intPtr3.IsInvalid) intPtr3 = GetResourceUpdaterForMUI(text);
					ResNativeMethods.UpdateResource(intPtr3, new IntPtr(6), new IntPtr(9),
						(ushort)culture.LCID, array3, (uint)array3.Length);
				}

				if (!intPtr3.IsInvalid)
				{
					intPtr3.Close();
					RevertMuiWorkaround(text);
				}
			}
			finally
			{
				if (!intPtr3.IsClosed)
					intPtr3.Close();
			}
		}
	}

	private static void RevertDuiMuiPatches()
	{
		var currentUiCulture = CultureInfo.CurrentUICulture;
		foreach (var text in new[]
		         {
			         Environment.SystemDirectory + "\\" + currentUiCulture.Name + "\\dui70.dll.mui",
			         Environment.GetFolderPath(Environment.SpecialFolder.SystemX86) + "\\" + currentUiCulture.Name +
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

	private static ResNativeMethods.SafeResourceUpdateHandle GetResourceUpdaterForMUI(string filePath)
	{
		File.Copy(filePath, filePath + ".orig", true);
		var num = PatternFinder.FindPatternInFile(filePath, Encoding.Unicode.GetBytes("MUI"));
		using (var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Write))
		{
			fileStream.Seek(num, SeekOrigin.Begin);
			fileStream.WriteByte(65);
		}

		return ResNativeMethods.BeginUpdateResource(filePath, false);
	}

	private static void RevertMuiWorkaround(string filePath)
	{
		var num = PatternFinder.FindPatternInFile(filePath, Encoding.Unicode.GetBytes("AUI"));
		using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Write);
		fileStream.Seek(num, SeekOrigin.Begin);
		fileStream.WriteByte(77);
	}
}