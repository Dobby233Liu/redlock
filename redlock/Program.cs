using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using redlock.ArgumentParser;

namespace redlock;

internal static class Program
{
	private class Arguments : ArgumentsBase
	{
		public Arguments()
		{
		}

		public Arguments(string[] args) : base(args)
		{
		}

		[Option("audit")] [OptionStoreTrue] public bool UnlockInAudit { get; set; }
		[Option("auditu")] [OptionStoreTrue] public bool RelockInAudit { get; set; }

		[Option("noshsxs")] [OptionStoreTrue] public bool NoShsxs { get; set; }
		[Option("nopol")] [OptionStoreTrue] public bool NoPolicies { get; set; }

		[Option("queuemie")] [OptionStoreTrue] public bool QueueMie { get; set; }
	}

	private static void Main(string[] argArray)
	{
		var entryAssembly = Assembly.GetEntryAssembly();
		if (entryAssembly is not null)
		{
			var versionInfo = FileVersionInfo.GetVersionInfo(entryAssembly.Location);
			Console.Title = $"{versionInfo.ProductName} v{versionInfo.ProductVersion}";
		}

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
			new UnlockAction
			{
				NoPolicies = args.NoPolicies,
				NoShsxs = args.NoShsxs,
				QueueMie = args.QueueMie
			}.Perform();
			RebootToSystem();
		}
		else if (args.RelockInAudit)
		{
			new RelockAction().Perform();
			RebootToSystem();
		}
		else
		{
			ModePrompt();
		}
	}

	private static void ModePrompt()
	{
		var oldColor = Console.ForegroundColor;
		Console.ForegroundColor = ConsoleColor.Cyan;
		Console.Write("///");
		Console.ForegroundColor = ConsoleColor.White;
		Console.Write(" Mode Selection ");
		Console.ForegroundColor = ConsoleColor.Cyan;
		Console.WriteLine("//");
		Console.ForegroundColor = oldColor;

		Console.WriteLine("1) Install Redpill");
		Console.WriteLine("2) Install Redpill without SHSxS");
		Console.WriteLine("3) Install Redpill without modifying policies");
		Console.WriteLine("4) Uninstall Redpill");
		Console.WriteLine("5) Exit\n");

		var selection = CliUtil.GetInt("Select a mode", 1, 5);
		if (selection == 5) return;

		var args = new Arguments();
		if (selection == 4)
			args.RelockInAudit = true;
		else
			args.UnlockInAudit = true;
		if (selection == 2) args.NoShsxs = true;
		if (selection == 3) args.NoPolicies = true;
		RebootToAudit(args);
	}

	private static void RebootToAudit(Arguments args)
	{
		var entryPath = GetTempEntryPath(out var abort);
		if (abort || entryPath is null)
		{
			if (entryPath is null)
			{
				Console.WriteLine("Don't know where the entry assembly is, cannot continue");
				if (CliUtil.ShouldPauseBeforeExit())
					CliUtil.Pause();
			}

			Environment.Exit(1);
			return;
		}

		var windowsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

		using var setupConfig = Registry.LocalMachine.CreateSubKey(RegKeyConstants.Setup, true);
		var oldSetupType = (int)setupConfig.GetValue("SetupType", 2);
		if (oldSetupType == 2 && SetupUtil.GetMieManifest(windowsDir) is not null)
		{
			Console.WriteLine("[!] Installation may take longer than expected due to Windows servicing");
			if (!CliUtil.Question("Would you like to proceed?"))
				return;
			args.QueueMie = true;
		}

		var oldCmdLine = (string)setupConfig.GetValue("CmdLine");
		var cmdLine = entryPath + " " + args.Build();
		setupConfig.SetValue("SetupTypeBak", oldSetupType, RegistryValueKind.DWord);
		setupConfig.SetValue("CmdLineBak", oldCmdLine, RegistryValueKind.String);
		setupConfig.SetValue("SetupType", 1, RegistryValueKind.DWord);
		setupConfig.SetValue("CmdLine", cmdLine, RegistryValueKind.String);
		setupConfig.Close();

		Console.WriteLine("[i] Rebooting into Setup Mode");
		PrivilegeUtil.AdjustPrivilege("SeShutdownPrivilege", true);

		// ReSharper disable once InconsistentNaming
		const int EWX_REBOOT = 0x02;
		// ReSharper disable once InconsistentNaming
		const uint SHTDN_REASON_MAJOR_OPERATINGSYSTEM = 0x00020000;
		// ReSharper disable once InconsistentNaming
		const uint SHTDN_REASON_MINOR_RECONFIG = 0x00000004;
		// ReSharper disable once InconsistentNaming
		const uint SHTDN_REASON_FLAG_PLANNED = 0x80000000;

		[DllImport("user32.dll", CharSet = CharSet.Unicode)]
		static extern bool ExitWindowsEx(uint uFlags, int dwReason);

		ExitWindowsEx(EWX_REBOOT, unchecked((int)(
			SHTDN_REASON_MAJOR_OPERATINGSYSTEM | SHTDN_REASON_MINOR_RECONFIG
			                                   | SHTDN_REASON_FLAG_PLANNED)));
	}

	private static string? GetTempEntryPath(out bool abort)
	{
		abort = false;

		var entry = Assembly.GetEntryAssembly();
		if (entry is null)
			return null;
		var entryPath = entry.Location;

		var tempDir = @"%SystemRoot%\Temp";
		using (var systemEnvVars = Registry.LocalMachine.OpenSubKey(RegKeyConstants.SysEnviron))
		{
			if (systemEnvVars is not null)
				tempDir = (string)systemEnvVars.GetValue("TEMP", tempDir);
		}

		tempDir = Environment.ExpandEnvironmentVariables(tempDir);

		var systemDir = Environment.SystemDirectory;

		bool IsInDirectory(string child, string parent)
		{
			var childPath = Path.GetFullPath(child);
			var parentPath = Path.GetFullPath(parent);

			var trailingSeparator = Path.DirectorySeparatorChar;
			if (parentPath.Length > 0 && parentPath[parentPath.Length - 1] != trailingSeparator)
				parentPath += trailingSeparator;

			return childPath.StartsWith(parentPath, StringComparison.OrdinalIgnoreCase);
		}

		if (!IsInDirectory(tempDir, Path.GetPathRoot(systemDir)))
		{
			Console.WriteLine($"[!] Temp directory ({tempDir}) is not under system drive");
			goto tempFail;
		}

		var entryPathTemp = Path.Combine(tempDir, $"{Path.GetRandomFileName()}.exe");
		try
		{
			File.Copy(entryPath, entryPathTemp, true);
		}
		catch (Exception ex)
		{
			Console.WriteLine("[!] Couldn't copy myself to the system temp directory:");
			Console.WriteLine($"{ex.Message}\n{ex.StackTrace}\n");
			goto tempFail;
		}

		entryPath = entryPathTemp;
		SetupUtil.QueueSetupCompleteAction(@$"del {CliUtil.EscapeCmdParameter(entryPath)}", systemDir);
		return entryPath;

		tempFail:
		Console.WriteLine("[!] Please make sure you're running this program from the system drive");
		Console.WriteLine($"My current path is {entryPath}");
		if (CliUtil.Question("Continue?"))
			return entryPath;
		abort = true;
		return null;
	}

	private static void RebootToSystem()
	{
		Console.WriteLine("[i] Preparing to exit Setup Mode");

		using (var setupConfig = Registry.LocalMachine.OpenSubKey(RegKeyConstants.Setup, true))
		{
			if (setupConfig is not null)
			{
				var oldSetupType = (int)setupConfig.GetValue("SetupTypeBak", 2);
				setupConfig.SetValue("SetupType", oldSetupType, RegistryValueKind.DWord);
				setupConfig.DeleteValue("SetupTypeBak", false);

				var oldCmdLine = (string?)setupConfig.GetValue("CmdLineBak");
				if (oldCmdLine is not null)
				{
					setupConfig.SetValue("CmdLine", oldCmdLine, RegistryValueKind.String);
					setupConfig.DeleteValue("CmdLineBak", false);
				}
			}
		}

		Environment.Exit(Environment.ExitCode);
	}
}