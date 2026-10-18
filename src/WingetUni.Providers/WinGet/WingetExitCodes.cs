namespace WingetUni.Providers.WinGet;

/// <summary>
/// Translates the exit codes Winget returns into something a user can act on.
/// </summary>
/// <remarks>
/// Winget reports failures in the <c>0x8A15xxxx</c> range, and the raw numbers carry no meaning
/// on screen. Only codes that change what the user should do next are listed; anything else
/// falls back to the manager's own output, which is more useful than a bare number.
/// </remarks>
public static class WingetExitCodes
{
    public const int InvalidArguments = unchecked((int)0x8A150002);
    public const int DownloadFailed = unchecked((int)0x8A150008);
    public const int NoApplicableInstaller = unchecked((int)0x8A150010);
    public const int InstallerHashMismatch = unchecked((int)0x8A150011);
    public const int NoApplicationsFound = unchecked((int)0x8A150014);
    public const int MultipleApplicationsFound = unchecked((int)0x8A150016);
    public const int RequiresAdmin = unchecked((int)0x8A150019);
    public const int UpdateNotApplicable = unchecked((int)0x8A15002B);
    public const int InstallerSecurityCheckFailed = unchecked((int)0x8A15002D);
    public const int NoUninstallInformationFound = unchecked((int)0x8A15002F);
    public const int UninstallCommandFailed = unchecked((int)0x8A150030);
    public const int BlockedByPolicy = unchecked((int)0x8A15003A);
    public const int PackageAgreementsNotAccepted = unchecked((int)0x8A150041);
    public const int SourceAgreementsNotAccepted = unchecked((int)0x8A150046);
    public const int FailedToOpenAllSources = unchecked((int)0x8A15004B);
    public const int UpgradeVersionNotNewer = unchecked((int)0x8A15004F);
    public const int PortableInstallFailed = unchecked((int)0x8A150052);
    public const int InstallerProhibitsElevation = unchecked((int)0x8A150056);
    public const int PackageAlreadyInstalled = unchecked((int)0x8A150061);
    public const int MultipleInstallFailed = unchecked((int)0x8A150065);
    public const int MultipleUninstallFailed = unchecked((int)0x8A150066);
    public const int PackageIsPinned = unchecked((int)0x8A150068);
    public const int PackageInUse = unchecked((int)0x8A150101);
    public const int InstallInProgress = unchecked((int)0x8A150102);
    public const int FileInUse = unchecked((int)0x8A150103);
    public const int MissingDependency = unchecked((int)0x8A150104);
    public const int DiskFull = unchecked((int)0x8A150105);
    public const int InsufficientMemory = unchecked((int)0x8A150106);
    public const int NoNetwork = unchecked((int)0x8A150107);
    public const int ContactSupport = unchecked((int)0x8A150108);
    public const int RebootRequiredToFinish = unchecked((int)0x8A150109);
    public const int RebootRequiredToInstall = unchecked((int)0x8A15010A);
    public const int RebootInitiated = unchecked((int)0x8A15010B);
    public const int CancelledByUser = unchecked((int)0x8A15010C);
    public const int AlreadyInstalled = unchecked((int)0x8A15010D);
    public const int Downgrade = unchecked((int)0x8A15010E);
    public const int InstallBlockedByPolicy = unchecked((int)0x8A15010F);
    public const int DependencyInstallFailed = unchecked((int)0x8A150110);
    public const int UpgradeNotSupported = unchecked((int)0x8A150114);

    /// <summary>
    /// Describes what an exit code means, or null when the code has no prepared explanation
    /// and the manager's own output should be shown instead.
    /// </summary>
    public static string? Describe(int exitCode) => exitCode switch
    {
        InvalidArguments => "WinGet rejected the command line arguments for this operation.",

        DownloadFailed => "Downloading the installer failed.",
        NoApplicableInstaller => "None of the package's installers apply to this system.",
        InstallerHashMismatch =>
            "The downloaded installer did not match the hash in the package manifest, so it was not run.",
        InstallerSecurityCheckFailed => "The installer did not pass WinGet's security check.",

        NoApplicationsFound => "No package matched. It may have been renamed or removed from the source.",
        MultipleApplicationsFound => "Several packages matched. This operation needs an unambiguous package id.",

        RequiresAdmin =>
            "This operation needs administrator rights. Restart the application as administrator and try again.",
        InstallerProhibitsElevation =>
            "This installer refuses to run with administrator rights. Restart the application without elevation.",

        UpdateNotApplicable => "WinGet found no update to apply. The installed version may already be current.",
        UpgradeVersionNotNewer => "The offered version is not newer than the version already installed.",

        NoUninstallInformationFound => "No uninstall method was recorded for this package.",
        UninstallCommandFailed => "This package's uninstall program failed. Its own output is shown below.",

        BlockedByPolicy => "Group Policy blocks this operation on this machine.",
        InstallBlockedByPolicy => "Group Policy blocks this installation on this machine.",

        PackageAgreementsNotAccepted => "The package license had to be accepted before installing.",
        SourceAgreementsNotAccepted => "The source agreement had to be accepted before this operation.",
        FailedToOpenAllSources => "One or more package sources could not be opened.",

        PortableInstallFailed => "The portable package could not be installed.",
        PackageAlreadyInstalled => "A version of this package is already installed.",
        AlreadyInstalled => "Another version of this package is already installed.",
        Downgrade => "A newer version of this package is already installed.",

        PackageInUse => "The application is running. Close it and try again.",
        FileInUse => "A file in the package is in use. Close the application and try again.",
        InstallInProgress => "Another installation is already running. Wait for it to finish and try again.",

        MissingDependency => "A dependency required by this package is missing from the system.",
        DependencyInstallFailed => "A dependency of this package could not be installed.",
        DiskFull => "There is not enough free disk space.",
        InsufficientMemory => "There is not enough free memory. Close other applications and try again.",
        NoNetwork => "This package needs an internet connection.",

        PackageIsPinned => "The package is pinned. Remove the pin with WinGet to allow this upgrade.",

        RebootRequiredToFinish => "The package was installed. Restart the PC to finish.",
        RebootRequiredToInstall => "Installation failed. Restart the PC and try again.",
        RebootInitiated => "The PC will restart to finish the installation.",

        CancelledByUser => "The installation was cancelled.",
        UpgradeNotSupported => "This package's installer cannot upgrade an existing installation.",

        MultipleInstallFailed => "At least one of the requested packages failed to install.",
        MultipleUninstallFailed => "At least one of the requested packages failed to uninstall.",
        ContactSupport => "The installer reported an error. Contact the package's publisher for support.",

        _ => null,
    };

    /// <summary>True when restarting the application elevated is the likely way past the failure.</summary>
    public static bool NeedsElevation(int exitCode) => exitCode == RequiresAdmin;

    /// <summary>True when the failure is only cleared by restarting Windows.</summary>
    public static bool NeedsRestart(int exitCode) =>
        exitCode is RebootRequiredToInstall or RebootRequiredToFinish;
}
