# Web2EXE Wizard Implementation Report

## Summary

Web2EXE Wizard now creates two outputs after application publishing:

- A portable ZIP in `Template/GeneratedApp/portable`.
- A custom C# Windows installer named `[AppName]-Setup.exe` in the same directory.

No Inno Setup, `ISCC.exe`, WiX, or other external installer software is used.

## Files Created

- `Launcher/WindowsInstallerBuilder.cs`
- `Launcher/PortablePackageBuilder.cs`
- `report.md`

## Files Modified

- `Launcher/Form1.cs`
  - Integrated portable ZIP creation.
  - Integrated custom Windows installer creation.
  - Added installer success and failure data to `BuildData`.
  - Reports the portable ZIP and installer paths separately.
  - Keeps the portable ZIP if installer generation fails.
  - Opens the shared portable-output folder.
- `README.md`
  - Removed Inno Setup requirements and instructions.
  - Documents portable ZIP extraction and execution.
  - Documents the WebView2 Runtime and .NET SDK requirements.

## Portable ZIP Builder

`PortablePackageBuilder`:

- Validates the publish directory and requires at least one file.
- Creates `Template/GeneratedApp/portable` if needed.
- Creates a Windows-safe filename using the application name and version.
- Adds the `-Portable.zip` suffix.
- Packages published files at the ZIP root with original relative folder structure.
- Never overwrites an existing ZIP.
- Reopens and verifies the archive after creation.
- Removes temporary or incomplete archive files after failures.

## Windows Installer Builder

`WindowsInstallerBuilder`:

- Validates the publish directory and output directory.
- Creates a payload ZIP containing the published application.
- Builds a temporary self-contained .NET installer project using `dotnet publish`.
- Embeds the payload and installed-file manifest in the installer executable.
- Writes `[AppName]-Setup.exe` to the portable output directory.
- Builds without Inno Setup, WiX, or external installer dependencies.

## Installation Behavior

The generated installer:

- Uses a per-user installation location:

```text
%LOCALAPPDATA%\Programs\[AppName]
```

- Does not require administrator privileges.
- Safely extracts files and rejects unsafe archive paths.
- Copies published application files into the installation directory.
- Creates an application and uninstall shortcut in the Start Menu.
- Offers an optional Desktop shortcut.
- Registers the application under:

```text
HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\[AppName]
```

- Creates `Uninstall.exe` inside the installation directory.
- Registers the executable for the application's DisplayIcon and uninstall command.
- Displays clear success or error dialogs.

## Upgrade and Uninstall Behavior

On reinstall or upgrade:

- Application files are copied over the existing installation.
- The installer removes obsolete managed files that are not in the new manifest.
- Existing Start Menu and Desktop shortcuts are removed and recreated.
- The HKCU registry entry is replaced with the current application registration.
- Files outside the managed manifest remain untouched.

Uninstall behavior:

- Removes Start Menu and Desktop shortcuts.
- Removes the HKCU uninstall registry entry.
- Deletes files recorded in the installed-file manifest.
- Does not delete unrelated user files.
- Removes only empty installation directories.

## Build Workflow

After `dotnet publish` succeeds:

1. Generate `update.json`.
2. Create the portable ZIP.
3. Create the Windows installer EXE.
4. Report or open the shared output folder.

If installer generation fails:

- The portable ZIP remains successfully created.
- The failure is reported separately in the status output.
- The installer failure does not overwrite or delete the ZIP.

## Update Manifest

`GenerateUpdateJson()` continues to use its existing download URL.

The existing URL is installer-oriented and has not been changed to a ZIP or fabricated URL. A real hosted portable ZIP or installer download location must be added later.

## Build Status

No build was completed because `Launcher/obj/project.assets.json` is missing.

The build returned:

```text
NETSDK1004: Assets file 'Launcher/obj/project.assets.json' not found.
```

This requires an approved NuGet restore before a successful project build can be attempted. No restore or dependency modification was performed.

## Final MVP Build and Verification

### Restore and Release Build

- `dotnet restore Launcher/Launcher.csproj` succeeded.
- `dotnet build Launcher/Launcher.csproj --configuration Release` succeeded with 0 errors.
- The first Release build reported 92 warnings, mainly existing nullable and platform-analysis warnings.
- The project assets file is now present at `Launcher/obj/project.assets.json`.

### Packaging Tests Performed

- Verified portable ZIP creation for a temporary publish directory.
- Verified ZIP entries were at the archive root with the original relative structure.
- Verified extraction of the tested ZIP structure.
- Verified Windows installer EXE generation from a temporary published application.
- Verified generated installer size and successful temporary installer build.
- Verified no Inno Setup or `ISCC.exe` references remain in active source or documentation.

### Runtime Installation Tests Performed

A temporary application named `Verify App` was installed and tested on Windows:

- Installation directory existed under `%LOCALAPPDATA%\Programs\Verify App`.
- HKCU uninstall registry entry was created with the expected display name, version, install location, icon, and uninstall command.
- Start Menu application and uninstall shortcuts were created.
- A two-shortcut Start Menu folder was confirmed after reinstall.
- The optional Desktop shortcut was verified using Windows UI Automation.
- Reinstalling/upgrading replaced the installation and preserved only the managed test files.
- The generated uninstaller returned exit code 0.
- The Start Menu folder and Desktop shortcut were removed after uninstall.
- The HKCU uninstall registry entry was removed after uninstall.
- Temporary install and registry test artifacts were cleaned up.

An additional temporary application named `Verify Cleanup` confirmed that registry and Start Menu cleanup occurred, but Windows retained the uninstaller executable briefly while it self-removed; the remaining test directory was cleaned manually during verification.

### Remaining Limitations

- The generated installer is framework-dependent and requires a compatible .NET runtime unless deployment changes to self-contained output.
- The temporary installer builder requires a working .NET SDK at wizard-generation time.
- Upgrade behavior is based on the installed-file manifest and does not preserve files outside that manifest.
- The uninstaller cannot guarantee immediate removal of its own executable or locked files on every Windows state.
- The update manifest download URL remains unchanged and is not a verified portable ZIP URL.
- No automated test project was added; the runtime checks were manual smoke tests.

## Inno Setup Status

The source and documentation contain no remaining Inno Setup, `ISCC.exe`, `setup.iss`, `GenerateInstallerScript`, `BuildInstaller`, or `IsInnoSetupInstalled` references.
