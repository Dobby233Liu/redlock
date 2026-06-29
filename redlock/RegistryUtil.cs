using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace redlock;

internal static class RegistryUtil
{
	[DllImport("advapi32.dll", CharSet = CharSet.Unicode, EntryPoint = "RegLoadKeyW")]
	private static extern int RegLoadKey(uint hKey, [MarshalAs(UnmanagedType.LPWStr)] [Optional] string lpSubKey,
		[MarshalAs(UnmanagedType.LPWStr)] string lpFile);

	[DllImport("advapi32.dll", CharSet = CharSet.Unicode, EntryPoint = "RegUnLoadKeyW")]
	private static extern int RegUnLoadKey(uint hKey,
		[MarshalAs(UnmanagedType.LPWStr)] [Optional]
		string lpSubKey);

	internal static IEnumerable<RegistryKey> ForEachUser()
	{
		const uint hKeyUsersId = unchecked((uint)RegistryHive.Users);
		const string defaultSid = ".DEFAULT";
		const string ntUserDat = "NTUSER.DAT";

		var loadedProfileKeyNames = Registry.Users.GetSubKeyNames().ToList();
		Dictionary<string, string> unloadedProfileImagePaths = new();

		var defaultProfileLoaded = loadedProfileKeyNames.Contains(defaultSid);
		string? defaultProfileDir = null;

		using (var profileList =
		       Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList"))
		{
			if (profileList is not null)
			{
				var profileKeyNames = profileList.GetSubKeyNames();
				foreach (var profileKeyName in profileKeyNames.Except(loadedProfileKeyNames))
				{
					using var profileKey = profileList.OpenSubKey(profileKeyName);
					var profileImagePath = (string?)profileKey?.GetValue("ProfileImagePath");
					if (profileImagePath is not null)
						unloadedProfileImagePaths.Add(profileKeyName, profileImagePath);
				}

				// for some reason my Windows 11 host has HKEY_USERS\{mySid}_Classes, although that is
				// not typical I'm still adding a case for it
				foreach (var possiblyBogusSubKeyName in loadedProfileKeyNames.Except(profileKeyNames).ToArray())
				{
					if (possiblyBogusSubKeyName == defaultSid)
						continue;
					Console.WriteLine($@"  ! WARNING: Excluding HKEY_USERS\{possiblyBogusSubKeyName}");
					loadedProfileKeyNames.Remove(possiblyBogusSubKeyName);
				}

				if (!defaultProfileLoaded)
					if (profileList.GetValue("Default") is string defaultProfileDirTemp)
						defaultProfileDir = Environment.ExpandEnvironmentVariables(defaultProfileDirTemp);
			}
		}

		if (!defaultProfileLoaded)
		{
			if (defaultProfileDir is null)
			{
				defaultProfileDir =
					new DirectoryInfo(Environment.GetFolderPath(Environment.SpecialFolder.CommonDocuments))
						.Parent?.Parent?.FullName;
				if (defaultProfileDir is not null)
					defaultProfileDir += @"\Default";
			}

			if (defaultProfileDir is not null)
				unloadedProfileImagePaths.Add(defaultSid, defaultProfileDir);
		}

		void PrintSid(string sid)
		{
			if (sid == defaultSid)
				Console.WriteLine(" -> Default user");
			else
				Console.WriteLine(" -> SID {0}", sid);
		}

		foreach (var profileKey in loadedProfileKeyNames)
		{
			PrintSid(profileKey);
			using var userKey = Registry.Users.OpenSubKey(profileKey, true);
			yield return userKey;
		}

		PrivilegeUtil.AdjustPrivilege("SeBackupPrivilege", true);
		PrivilegeUtil.AdjustPrivilege("SeRestorePrivilege", true);
		foreach (var entry in unloadedProfileImagePaths)
		{
			var sid = entry.Key;
			PrintSid(sid);

			var userKeyName = $"_REDLOCK_{sid}_";
			var profileImagePath = entry.Value;
			var userHivePath = Path.Combine(profileImagePath, ntUserDat);
			var loadResult = RegLoadKey(hKeyUsersId, userKeyName, userHivePath);
			if (loadResult != 0)
			{
				Console.WriteLine($" ! Loading hive {userHivePath} failed with error {loadResult}");
				continue;
			}

			try
			{
				using var userKey = Registry.Users.OpenSubKey(userKeyName, true);
				yield return userKey;
			}
			finally
			{
				RegUnLoadKey(hKeyUsersId, userKeyName);
			}
		}
	}
}