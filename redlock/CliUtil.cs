using System;
using System.Media;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace redlock;

internal static class CliUtil
{
	internal static void Beep()
	{
		SystemSounds.Beep.Play();
	}

	internal static int GetInt(string prompt, int min = -int.MaxValue, int max = int.MaxValue)
	{
		while (true)
		{
			Console.Write($"{prompt}: ");
			if (int.TryParse(Console.ReadLine(), out var selection)
			    && selection >= min && selection <= max)
				return selection;
			Beep();
		}
	}

	internal static bool Question(string question)
	{
		Console.Write($"{question} [Y/N] ");
		while (true)
		{
			var key = Console.ReadKey(true);
			if (key is { Modifiers: 0, Key: ConsoleKey.Y or ConsoleKey.N })
			{
				Console.WriteLine(key.KeyChar);
				return key.Key == ConsoleKey.Y;
			}

			Beep();
		}
	}

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetConsoleProcessList", SetLastError = true)]
	private static extern uint GetConsoleProcessList([In] [Out] uint[] lpdwProcessList, uint dwProcessCount);

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern uint GetCurrentProcessId();

	private static bool IsConsoleWindowOurs()
	{
		var processList = new uint[2];
		if (GetConsoleProcessList(processList, (uint)processList.Length) < processList.Length)
			return false;
		return processList[0] == GetCurrentProcessId();
	}

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern IntPtr GetConsoleWindow();

	[DllImport("user32.dll", CharSet = CharSet.Unicode)]
	private static extern bool IsWindowVisible(IntPtr hWnd);

	[DllImport("user32.dll", CharSet = CharSet.Unicode)]
	private static extern bool IsIconic(IntPtr hWnd);

	private static bool IsConsoleWindowActive()
	{
		var windowHandle = GetConsoleWindow();
		return windowHandle != IntPtr.Zero && IsWindowVisible(windowHandle) && !IsIconic(windowHandle);
	}

	internal static bool ShouldPauseBeforeExit()
	{
		return Environment.UserInteractive && !IsConsoleWindowOurs() && IsConsoleWindowActive();
	}

	internal static void Pause()
	{
		Console.WriteLine("Press any key to continue...");
		Console.ReadKey(true);
	}

	private static readonly Regex CmdSpecialCharacterCaretRegex = new(@"[&<>^|]");
	private static readonly Regex CmdSpecialCharacterNonquotedCaretRegex = new(@"[\\]");
	private static readonly Regex CmdSpecialCharacterDoubleCaretRegex = new(@"[!]");
	private static readonly Regex CmdSpecialCharacterPercentRegex = new(@"[%]");

	/// <summary>Escapes a string (badly) for use in a Command Prompt batch file as a parameter</summary>
	/// <remarks><see href="https://ss64.com/nt/syntax-esc.html"></see></remarks>
	/// <param name="param">This is assumed to be unescaped. Note that the inclusion of &quot; is problematic</param>
	/// <param name="delayedExpansion">Whether the shell is expected to be in the EnableDelayedExpansion mode</param>
	internal static string EscapeCmdParameter(string param, bool delayedExpansion = false)
	{
		var gettingQuoted = param.Contains(" ");
		param = CmdSpecialCharacterCaretRegex.Replace(param, match => $"^{match.Value}");
		if (!gettingQuoted)
			param = CmdSpecialCharacterNonquotedCaretRegex.Replace(param, match => $"^{match.Value}");
		if (delayedExpansion)
			param = CmdSpecialCharacterDoubleCaretRegex.Replace(param, match => $"^^{match.Value}");
		param = CmdSpecialCharacterPercentRegex.Replace(param, match => $"%{match.Value}");
		if (gettingQuoted)
			param = $"\"{param}\"";
		return param;
	}
}