using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.Win32;

namespace redlock;

internal static partial class Program
{
	private class Arguments : ArgumentsBase
	{
		public Arguments() : base()
		{
		}

		public Arguments(string[] args) : base(args)
		{
		}
		
		[ArgumentName("audit")]
		public bool InstallAudit { get; set; }
		
		[ArgumentName("auditu")]
		public bool UninstallAudit { get; set; }
		
		[ArgumentName("noshsxs")]
		public bool NoShsxs { get; set; }
		
		[ArgumentName("nopol")]
		public bool NoPolicies { get; set; }
		
		[ArgumentName("queuemie")]
		public bool QueueMie { get; set; }
	}
	
	private static void Main(string[] argArray)
	{
		var args = new Arguments(argArray);

		if (args.InstallAudit)
		{
			UnlockInAudit(args);
			return;
		}

		if (args.UninstallAudit)
		{
			RelockInAudit();
			return;
		}

		StandardRun();
	}

	internal static bool Question(string question)
	{
		while (true)
		{
			Console.WriteLine($"{question} [Y/N] ");
			var key = Console.ReadKey(true);
			if (key is { Modifiers: 0, Key: ConsoleKey.Y or ConsoleKey.N })
			{
				Console.WriteLine(key.KeyChar);
				return key.Key == ConsoleKey.Y;
			}
			Console.Beep();
		}
	}
	
	private static void StandardRun()
	{
		var oldColor = Console.ForegroundColor;
		Console.ForegroundColor = ConsoleColor.Cyan;
		Console.Write("//");
		Console.ForegroundColor = ConsoleColor.White;
		Console.Write("selection");
		Console.ForegroundColor = ConsoleColor.Cyan;
		Console.WriteLine("/");
		Console.ForegroundColor = oldColor;

		Console.WriteLine("1) Install Redpill");
		Console.WriteLine("2) Install Redpill excluding SHSxS");
		Console.WriteLine("3) Install Redpill excluding policies");
		Console.WriteLine("4) Uninstall Redpill");
		Console.WriteLine("5) Exit\n");
		// TODO: could copy ourselves to the system temp directory?
		Console.WriteLine("! Make sure you're running this program from the system drive before proceeding\n");

		var selection = 0;
		while (selection is < 1 or > 5)
		{
			Console.Write("Select a mode: ");
			int.TryParse(Console.ReadLine(), out selection);
		}

		if (selection == 5) return;

		var args = new Arguments();
		if (selection == 4)
			args.UninstallAudit = true;
		else
			args.InstallAudit = true;
		if (selection == 2) args.NoShsxs = true;
		if (selection == 3) args.NoPolicies = true;

		using var setupConfig = Registry.LocalMachine.OpenSubKey("SYSTEM\\Setup", true);
		var oldSetupType = (int?)setupConfig.GetValue("SetupType");
		if (oldSetupType.GetValueOrDefault() == 2 &&
		    Directory.GetFiles(Environment.GetFolderPath(Environment.SpecialFolder.Windows) + @"\servicing\Packages",
			    "Microsoft-Windows-ImmersiveBrowser-Package~*~~*.mum").Length != 0)
		{
			Console.WriteLine("! Rebooting from OOBE on this install may take longer than expected due to Windows servicing");
			if (!Question("Would you like to proceed?"))
				return;
			args.QueueMie = true;
		}

		var oldCmdLine = (string)setupConfig.GetValue("CmdLine");
		var cmdLine = Assembly.GetEntryAssembly().Location + " " + args.Build();
		setupConfig.SetValue("SetupTypeBak", oldSetupType, RegistryValueKind.DWord);
		setupConfig.SetValue("CmdLineBak", oldCmdLine, RegistryValueKind.String);
		setupConfig.SetValue("SetupType", 1, RegistryValueKind.DWord);
		setupConfig.SetValue("CmdLine", cmdLine, RegistryValueKind.String);
		setupConfig.Close();

		Console.WriteLine("[i] Rebooting into Setup Mode");
		PrivilegeUtil.AdjustPrivilege("SeShutdownPrivilege", true);
		NativeMethods.InitiateSystemShutdown(IntPtr.Zero, IntPtr.Zero, 0, false, true);
	}
}