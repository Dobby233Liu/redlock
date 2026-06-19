using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;
using redlock.Properties;

namespace redlock;

internal static partial class Program
{
	private static void Main(string[] args)
	{
		for (var i = 0; i < args.Length; i++)
			args[i] = args[i].ToLower();
			
		if (args.Contains("audit"))
		{
			UnlockInAudit(args);
			return;
		}

		if (args.Contains("auditu"))
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
			
		var selection = 0;
		while (selection is < 1 or > 5)
		{
			Console.Write("Select a mode: ");
			int.TryParse(Console.ReadLine(), out selection);
		}
		if (selection == 5) return;
			
		var args = new List<string>();
		args.Add(selection == 4 ? "auditu" : "audit");
		if (selection == 2) args.Add("noshsxs");
		if (selection == 3) args.Add("nopol");
			
		using var setupConfig = Registry.LocalMachine.OpenSubKey("SYSTEM\\Setup", true);
		var oldSetupType = (int?)setupConfig.GetValue("SetupType");
		if (oldSetupType.GetValueOrDefault() == 2 &&
		    Directory.GetFiles(Environment.GetFolderPath(Environment.SpecialFolder.Windows) + @"\servicing\Packages",
			    "Microsoft-Windows-ImmersiveBrowser-Package~*~~*.mum").Length != 0)
		{
			if (MessageBox.Show(
				    "Rebooting from OOBE on this install may take longer than expected due to Windows servicing, would you like to proceed?",
				    string.Empty, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.No)
				return;
			args.Add("queuemie");
		}

		var oldCmdLine = (string)setupConfig.GetValue("CmdLine");
		var cmdLine = Assembly.GetEntryAssembly().Location + " " + string.Join(" ", args);
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