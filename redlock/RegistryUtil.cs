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

	// TODO: offline support
	internal static IEnumerable<RegistryKey> OpenUserHives()
	{
		const uint hKeyUsersId = unchecked((uint)RegistryHive.Users);
		const string defaultSid = ".DEFAULT";
		const string ntUserDat = "NTUSER.DAT";

		var loadedProfileKeyNames = Registry.Users.GetSubKeyNames().ToList();
		Dictionary<string, string> unloadedProfileImagePaths = new();

		var defaultProfileLoaded = loadedProfileKeyNames.Contains(defaultSid);
		string? defaultProfileDir = null;

		using (var profileList = Registry.LocalMachine.OpenSubKey(RegKeyConstants.ProfileList))
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

				// skip usrclass.dat etc. (although that may not be loaded in setup mode)
				foreach (var possiblyBogusSubKeyName in loadedProfileKeyNames.Except(profileKeyNames).ToArray())
				{
					if (possiblyBogusSubKeyName == defaultSid)
						continue;
					Console.WriteLine($@"  ! WARNING: Excluding HKEY_USERS\{possiblyBogusSubKeyName}");
					loadedProfileKeyNames.Remove(possiblyBogusSubKeyName);
				}

				if (!defaultProfileLoaded
					&& profileList.GetValue("Default") is string defaultProfileDirTemp)
					defaultProfileDir = Environment.ExpandEnvironmentVariables(defaultProfileDirTemp);
			}
		}

		if (!defaultProfileLoaded && defaultProfileDir is not null)
			unloadedProfileImagePaths.Add(defaultSid, defaultProfileDir);

		void PrintSid(string sid)
		{
			if (sid == defaultSid)
				Console.WriteLine(" -> Default user");
			else
				Console.WriteLine(" -> SID {0}", sid);
		}

		foreach (var profileKey in loadedProfileKeyNames)
		{
			using var userKey = Registry.Users.OpenSubKey(profileKey, true);
			if (userKey is null) continue;
			PrintSid(profileKey);
			yield return userKey;
		}

		PrivilegeUtil.AdjustPrivilege("SeBackupPrivilege", true);
		PrivilegeUtil.AdjustPrivilege("SeRestorePrivilege", true);
		foreach (var entry in unloadedProfileImagePaths)
		{
			var sid = entry.Key;
			var userKeyName = $"_REDLOCK_{sid}_";
			var profileImagePath = entry.Value;
			var userHivePath = Path.Combine(profileImagePath, ntUserDat);
			var loadResult = RegLoadKey(hKeyUsersId, userKeyName, userHivePath);
			if (loadResult != 0)
			{
				Console.WriteLine($" ! Loading hive {userHivePath} failed with error 0x{loadResult:8x}");
				continue;
			}

			try
			{
				using var userKey = Registry.Users.OpenSubKey(userKeyName, true);
				if (userKey is null) continue;
				PrintSid(sid);
				yield return userKey;
			}
			finally
			{
				RegUnLoadKey(hKeyUsersId, userKeyName);
			}
		}
	}
}