namespace redlock;

internal static class RegKeyConstants
{
	internal const string SysEnviron = @"SYSTEM\CurrentControlSet\Control\Session Manager\Environment";
	internal const string Setup = @"SYSTEM\Setup";
	internal const string SppSvc = @"SYSTEM\CurrentControlSet\services\sppsvc";
	internal const string ProductOptions = @"SYSTEM\CurrentControlSet\Control\ProductOptions";
	internal const string Explorer = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer";

	internal const string RpCert = @"SOFTWARE\Microsoft\SystemCertificates\ROOT\Certificates\"
	                               + "7721AC1150970D0B6A4B47AAEA73770712C907C5";

	internal const string WebcamEnablement = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\GRE_Initialize";
	internal const string PdfReaderCap = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Applets\Paint\Capabilities";
	internal const string TaskUi = @"SOFTWARE\Microsoft\Windows\CurrentVersion\TaskUI";
	internal const string RibbonClass = @"CLSID\{4F12FF5D-D319-4A79-8380-9CC80384DC08}";
	internal const string AutoPlayHandlers = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\AutoplayHandlers";
	internal const string Desktop = @"Control Panel\Desktop";
	internal const string Apps = @"Software\RegisteredApplications";

	internal const string MieSetupData = @"SOFTWARE\Microsoft\Active Setup\Installed Components\"
	                                     + "{8E7E60C6-4CE5-476D-9E31-FD450F3F792F}";

	internal const string MieCap = @"SOFTWARE\Microsoft\Immersive Browser\Capabilities";
	internal const string ExplorerAdv = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
	internal const string CurrentVersion = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion";
	internal const string ProfileList = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList";
}