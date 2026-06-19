using System;
using System.Media;
using System.Runtime.InteropServices;

namespace redlock;

internal class CliUtil
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
		while (true)
		{
			Console.Write($"{question} [Y/N] ");
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
	private static extern uint GetConsoleProcessList([In, Out] uint[] lpdwProcessList, uint dwProcessCount);
	
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
		return !IsConsoleWindowOurs() && IsConsoleWindowActive();
	}

	internal static void Pause()
	{
		Console.WriteLine("Press any key to continue...");
		Console.ReadKey(true);
	}
}