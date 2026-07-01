using System;
using System.IO;

namespace redlock;

internal static class SetupUtil
{
	internal static void QueueSetupCompleteAction(string cmdLine, string systemDir)
	{
		var scriptsPath = Path.Combine(systemDir, @$"Setup\Scripts");
		if (!Directory.Exists(scriptsPath))
			Directory.CreateDirectory(scriptsPath);
		File.AppendAllText(@$"{scriptsPath}\SetupComplete.cmd", $"\r\n{cmdLine}");
	}

	internal static string[] GetMieManifests(string windowsDir)
	{
		return Directory.GetFiles(Path.Combine(windowsDir, @"servicing\Packages"),
			"Microsoft-Windows-ImmersiveBrowser-Package~*~~*.mum");
	}
}