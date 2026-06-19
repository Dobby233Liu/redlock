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
	
	/// <param name="action">action(userKey, sid)</param>
	internal static void ForEachUser(Action<RegistryKey, string> action)
	{
		const uint hKeyUsersId = unchecked((uint)RegistryHive.Users);
		const string defaultSid = ".DEFAULT";
		const string ntUserDat = "NTUSER.DAT";
	
		var loadedProfileKeyNames = Registry.Users.GetSubKeyNames().ToList();
		
		Dictionary<string, string> unloadedProfileImagePaths = new();
		string? usersDir = null;
		using (var profileList =
		       Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList"))
		{
			if (profileList is not null)
			{
				var listKeyNames = profileList.GetSubKeyNames();
				foreach (var profileKeyName in listKeyNames.Except(loadedProfileKeyNames))
				{
					using var profileKey = profileList.OpenSubKey(profileKeyName);
					var profileImagePath = (string?)profileKey?.GetValue("ProfileImagePath");
					if (profileImagePath is not null)
						unloadedProfileImagePaths.Add(profileKeyName, profileImagePath);
				}
				
				// for some reason my Windows 11 host has HKEY_USERS\{mySid}_Classes, although that is
				// not typical I'm still adding a case for it
				foreach (var possiblyBogusSubKeyName in loadedProfileKeyNames.Except(listKeyNames).ToArray())
				{
					if (possiblyBogusSubKeyName == defaultSid)
						continue;
					Console.WriteLine($@"  ! WARNING: Excluding HKEY_USERS\{possiblyBogusSubKeyName}");
					loadedProfileKeyNames.Remove(possiblyBogusSubKeyName);
				}
				
				usersDir = (string)profileList.GetValue("Default");
				if (usersDir is not null)
					usersDir = Environment.ExpandEnvironmentVariables(usersDir);
			}
		}
		if (usersDir is null)
		{
			usersDir = new DirectoryInfo(Environment.GetFolderPath(Environment.SpecialFolder.CommonDocuments))
				.Parent?.Parent?.FullName;
			if (usersDir is not null)
				usersDir += @"\Default";
		}
		if (usersDir is not null)
			unloadedProfileImagePaths.Add(defaultSid, usersDir);
		
		foreach (var profileKey in loadedProfileKeyNames)
		{
			Console.WriteLine(" -> SID {0}", profileKey);
			
			using var userKey = Registry.Users.OpenSubKey(profileKey, true);
			action(userKey, profileKey);
		}
		
		PrivilegeUtil.AdjustPrivilege("SeBackupPrivilege", true);
		PrivilegeUtil.AdjustPrivilege("SeRestorePrivilege", true);
		foreach (var entry in unloadedProfileImagePaths)
		{
			var sid = entry.Key;
			var profileImagePath = entry.Value;
			if (sid == defaultSid)
				Console.WriteLine(" -> Default user");
			else
				Console.WriteLine(" -> SID {0}", sid);
			
			var userKeyName = $"_REDLOCK_{sid}_";
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
				action(userKey, userKeyName);
			}
			finally
			{
				RegUnLoadKey(hKeyUsersId, userKeyName);
			}
		}
	}	
}