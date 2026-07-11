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
			Environment.ExitCode = 1;
			return;
		}

		if (args.UnlockInAudit || args.RelockInAudit)
		{
			try
			{
				if (args.UnlockInAudit)
					new UnlockOperation
					{
						NoPolicies = args.NoPolicies,
						NoShsxs = args.NoShsxs,
						QueueMie = args.QueueMie
					}.Perform();
				else if (args.RelockInAudit)
					new RelockOperation().Perform();
			}
			finally
			{
				RestoreSetupType();
			}
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
		if (selection != 4)
		{
			args.UnlockInAudit = true;
			args.NoShsxs = selection == 2;
			args.NoPolicies = selection == 3;
		}
		else
		{
			args.RelockInAudit = true;
		}
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

		using (var setupConfig = Registry.LocalMachine.CreateSubKey(RegKeyConstants.Setup, true))
		{
			var oldSetupType = (int?)setupConfig.GetValue("SetupType", 0);
			if (oldSetupType == 2)
			{
				var windowsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
				if (SetupUtil.GetMieManifest(windowsDir) is not null)
				{
					Console.WriteLine("[!] Rebooting from OOBE may take longer than expected due to Windows servicing");
					if (!CliUtil.Question("Would you like to proceed?"))
						return;
					args.QueueMie = true;
				}
			}

			if (oldSetupType is not null)
				setupConfig.SetValue("SetupTypeBak", oldSetupType, RegistryValueKind.DWord);
			setupConfig.SetValue("SetupType", 1, RegistryValueKind.DWord);

			var oldCmdLine = (string?)setupConfig.GetValue("CmdLine");
			if (oldCmdLine is not null)
				setupConfig.SetValue("CmdLineBak", oldCmdLine, RegistryValueKind.String);
			setupConfig.SetValue("CmdLine", $"{entryPath} ${args.Build()}", RegistryValueKind.String);
		}

		Console.WriteLine("[i] Rebooting into Setup Mode");
		RebootSystem();
	}

	private static void RebootSystem()
	{
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

		var tempDir = Environment.GetEnvironmentVariable("TEMP", EnvironmentVariableTarget.Machine);
		tempDir ??= @"%SystemRoot%\Temp";
		tempDir = Environment.ExpandEnvironmentVariables(tempDir);

		var systemDir = Environment.SystemDirectory;

		bool IsInDirectory(string child, string parent)
		{
			var parentPath = Path.GetFullPath(parent);
			if (parentPath.Length <= 0) return false;
			var childPath = Path.GetFullPath(child);

			var trailingSeparator = Path.DirectorySeparatorChar;
			if (parentPath[parentPath.Length - 1] != trailingSeparator)
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

	private static void RestoreSetupType()
	{
		using var setupConfig = Registry.LocalMachine.OpenSubKey(RegKeyConstants.Setup, true);
		if (setupConfig is null) return;
		
		if ((int)setupConfig.GetValue("SetupType", 0) == 0)
			return;
		Console.WriteLine("[i] Preparing to exit Setup Mode");

		var oldSetupType = (int)setupConfig.GetValue("SetupTypeBak", 0);
		setupConfig.SetValue("SetupType", oldSetupType, RegistryValueKind.DWord);
		setupConfig.DeleteValue("SetupTypeBak", false);

		var oldCmdLine = (string?)setupConfig.GetValue("CmdLineBak");
		if (oldCmdLine is null) return;
		setupConfig.SetValue("CmdLine", oldCmdLine, RegistryValueKind.String);
		setupConfig.DeleteValue("CmdLineBak", false);

		// we likely set up ourselves as the setup program, meaning normally exiting is enough
	}
}