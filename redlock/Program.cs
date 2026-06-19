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

		[Option("audit"), OptionStoreTrue()] public bool UnlockInAudit { get; set; }
		[Option("auditu"), OptionStoreTrue()] public bool RelockInAudit { get; set; }

		[Option("noshsxs"), OptionStoreTrue()] public bool NoShsxs { get; set; }
		[Option("nopol"), OptionStoreTrue()] public bool NoPolicies { get; set; }

		[Option("queuemie"), OptionStoreTrue()] public bool QueueMie { get; set; }
	}
	
	private static void Main(string[] argArray)
	{
		Arguments args;
		try
		{
			args = new Arguments(argArray);
		}
		catch (ArgumentException ex)
		{
			Console.WriteLine(ex.Message);
			
			if (CliUtil.ShouldPauseBeforeExit())
				CliUtil.Pause();
			Environment.Exit(1);
			
			return;
		}

		if (args.UnlockInAudit)
		{
			UnlockInAudit(args);
			return;
		}

		if (args.RelockInAudit)
		{
			RelockInAudit();
			return;
		}

		StandardRun();
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

		var selection = CliUtil.GetInt("Select a mode", 1, 5);

		if (selection == 5) return;

		var args = new Arguments();
		if (selection == 4)
			args.RelockInAudit = true;
		else
			args.UnlockInAudit = true;
		if (selection == 2) args.NoShsxs = true;
		if (selection == 3) args.NoPolicies = true;

		using var setupConfig = Registry.LocalMachine.OpenSubKey("SYSTEM\\Setup", true);
		var oldSetupType = (int?)setupConfig.GetValue("SetupType");
		if (oldSetupType.GetValueOrDefault() == 2 &&
		    Directory.GetFiles(Environment.GetFolderPath(Environment.SpecialFolder.Windows) + @"\servicing\Packages",
			    "Microsoft-Windows-ImmersiveBrowser-Package~*~~*.mum").Length != 0)
		{
			Console.WriteLine("! Rebooting from OOBE on this install may take longer than expected due to Windows servicing");
			if (!CliUtil.Question("Would you like to proceed?"))
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