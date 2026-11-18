using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;

namespace Launcher;

public static class WindowsInstallerBuilder
{
    public static string Create(
        string publishedApplicationDirectory,
        string outputDirectory,
        string applicationName,
        string version)
    {
        if (string.IsNullOrWhiteSpace(publishedApplicationDirectory))
            throw new ArgumentException("The published application directory is required.", nameof(publishedApplicationDirectory));

        if (!Directory.Exists(publishedApplicationDirectory))
            throw new DirectoryNotFoundException($"Published application directory not found: {publishedApplicationDirectory}");

        string[] publishedFiles = Directory.GetFiles(publishedApplicationDirectory, "*", SearchOption.AllDirectories);
        if (publishedFiles.Length == 0)
            throw new IOException($"Published application directory contains no files: {publishedApplicationDirectory}");

        if (string.IsNullOrWhiteSpace(outputDirectory))
            throw new ArgumentException("The installer output directory is required.", nameof(outputDirectory));

        Directory.CreateDirectory(outputDirectory);

        string safeApplicationName = MakeSafeFileName(applicationName, "Application", 180);
        string finalInstallerPath = Path.Combine(outputDirectory, $"{safeApplicationName}-Setup.exe");

        string temporaryRoot = Path.Combine(
            Path.GetTempPath(),
            "Web2EXE-Wizard",
            "Installer-" + Guid.NewGuid().ToString("N"));
        string temporaryInstallerProject = Path.Combine(temporaryRoot, "Project");
        string temporaryPayloadZip = Path.Combine(temporaryRoot, "Payload.zip");

        try
        {
            Directory.CreateDirectory(temporaryInstallerProject);

            ZipFile.CreateFromDirectory(
                publishedApplicationDirectory,
                temporaryPayloadZip,
                CompressionLevel.Optimal,
                false);

            string[] relativeManifest = publishedFiles
                .Select(file => Path.GetRelativePath(publishedApplicationDirectory, file).Replace('\\', '/'))
                .OrderBy(file => file, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            File.WriteAllLines(
                Path.Combine(temporaryInstallerProject, "InstalledFiles.txt"),
                relativeManifest);

            File.Copy(
                temporaryPayloadZip,
                Path.Combine(temporaryInstallerProject, "Payload.zip"),
                true);

            File.WriteAllText(
                Path.Combine(temporaryInstallerProject, "WindowsInstaller.csproj"),
                InstallerProjectXml);

            File.WriteAllText(
                Path.Combine(temporaryInstallerProject, "Program.cs"),
                CreateInstallerSource(applicationName, version));

            string temporaryPublishDirectory = Path.Combine(temporaryRoot, "Publish");
            ProcessStartInfo publishInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $@"publish ""{Path.Combine(temporaryInstallerProject, "WindowsInstaller.csproj")}"" -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o ""{temporaryPublishDirectory}""",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using Process? publishProcess = Process.Start(publishInfo);
            if (publishProcess == null)
                throw new InvalidOperationException("Could not start dotnet to build the Windows installer.");

            string publishOutput = publishProcess.StandardOutput.ReadToEnd();
            string publishError = publishProcess.StandardError.ReadToEnd();
            publishProcess.WaitForExit();

            if (publishProcess.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"Windows installer build failed: {publishError}{Environment.NewLine}{publishOutput}");
            }

            if (!Directory.Exists(temporaryPublishDirectory))
                throw new IOException("dotnet publish did not create the temporary installer output directory.");

            string? generatedInstaller = Directory
                .GetFiles(temporaryPublishDirectory, "*.exe", SearchOption.AllDirectories)
                .FirstOrDefault(file =>
                    Path.GetFileName(file).Equals("WindowsInstaller.exe", StringComparison.OrdinalIgnoreCase));

            if (generatedInstaller == null)
            {
                generatedInstaller = Directory
                    .GetFiles(temporaryPublishDirectory, "*.exe", SearchOption.AllDirectories)
                    .FirstOrDefault();
            }

            if (string.IsNullOrWhiteSpace(generatedInstaller) || !File.Exists(generatedInstaller))
                throw new IOException("dotnet publish did not produce a Windows installer executable.");

            if (new FileInfo(generatedInstaller).Length == 0)
                throw new IOException("Generated Windows installer executable is empty.");

            string temporaryFinalInstaller = finalInstallerPath + ".tmp";
            if (File.Exists(temporaryFinalInstaller))
                File.Delete(temporaryFinalInstaller);

            File.Copy(generatedInstaller, temporaryFinalInstaller, true);

            if (!File.Exists(temporaryFinalInstaller) || new FileInfo(temporaryFinalInstaller).Length == 0)
                throw new IOException("Generated Windows installer executable could not be verified.");

            File.Move(temporaryFinalInstaller, finalInstallerPath, true);

            if (!File.Exists(finalInstallerPath) || new FileInfo(finalInstallerPath).Length == 0)
                throw new IOException("Final Windows installer executable could not be verified.");

            return finalInstallerPath;
        }
        finally
        {
            try
            {
                if (Directory.Exists(temporaryRoot))
                    Directory.Delete(temporaryRoot, true);
            }
            catch
            {
                // Temporary installer build cleanup is best-effort.
            }
        }
    }

    private const string InstallerProjectXml = """
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net8.0-windows</TargetFramework>
    <UseWindowsForms>true</UseWindowsForms>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <AssemblyName>WindowsInstaller</AssemblyName>
  </PropertyGroup>
  <ItemGroup>
    <EmbeddedResource Include="Payload.zip" />
    <EmbeddedResource Include="InstalledFiles.txt" />
  </ItemGroup>
</Project>
""";

    private static string CreateInstallerSource(string applicationName, string version)
    {
        string appNameLiteral = JsonSerializer.Serialize(applicationName);
        string versionLiteral = JsonSerializer.Serialize(version);

        return $$"""
using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

namespace WindowsInstaller;

internal static class Program
{
    internal const string ApplicationName = {{appNameLiteral}};
    internal const string ApplicationVersion = {{versionLiteral}};

    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        try
        {
            if (args.Any(arg => string.Equals(arg, "--uninstall", StringComparison.OrdinalIgnoreCase)))
            {
                UninstallApplication();
                MessageBox.Show("Application files and shortcuts were uninstalled.", "Uninstall Complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else
            {
                Application.Run(new InstallerForm());
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Windows Installer Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    internal static string GetInstallDirectory()
    {
        string safeName = MakeSafeFolderName(ApplicationName);
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs",
            safeName);
    }

    private static string MakeSafeFolderName(string value)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        var result = value
            .Select(character => Array.IndexOf(invalid, character) >= 0 || char.IsControl(character) ? '-' : character)
            .ToArray();

        string safe = new string(result).Trim().Trim(' ', '.');
        return string.IsNullOrWhiteSpace(safe) ? "Application" : safe;
    }

    internal static void InstallApplication(bool createDesktopShortcut)
    {
        string installDirectory = GetInstallDirectory();
        ValidateInstallDirectory(installDirectory);
        Directory.CreateDirectory(installDirectory);

        string temporaryDirectory = Path.Combine(
            Path.GetTempPath(),
            "Web2EXE-Wizard",
            "Extract-" + Guid.NewGuid().ToString("N"));

        try
        {
            Directory.CreateDirectory(temporaryDirectory);

            using Stream payloadStream = GetEmbeddedResource(".Payload.zip")
                ?? throw new InvalidDataException("The installer payload is missing.");
            ExtractPayloadSafely(payloadStream, temporaryDirectory);

            string oldManifestPath = Path.Combine(installDirectory, ".web2exe-installed-files.txt");
            var oldFiles = File.Exists(oldManifestPath)
                ? ReadManifest(oldManifestPath)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            string[] newFiles = ReadManifestResource();
            var newFilesSet = new HashSet<string>(newFiles, StringComparer.OrdinalIgnoreCase);

            foreach (string staleFile in oldFiles.Where(file => !newFilesSet.Contains(file)))
            {
                string stalePath = ResolveRelativeFile(installDirectory, staleFile);
                if (File.Exists(stalePath))
                {
                    try { File.Delete(stalePath); }
                    catch { /* Leave locked or unavailable stale files in place. */ }
                }
            }

            foreach (string relativeFile in newFiles)
            {
                string sourcePath = ResolveRelativeFile(temporaryDirectory, relativeFile);
                if (!File.Exists(sourcePath))
                    continue;

                string destinationPath = ResolveRelativeFile(installDirectory, relativeFile);
                Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
                File.Copy(sourcePath, destinationPath, true);
            }

            File.WriteAllLines(oldManifestPath, newFiles);

            string currentExecutable = Environment.ProcessPath
                ?? Process.GetCurrentProcess().MainModule?.FileName
                ?? throw new InvalidOperationException("Could not determine the installer executable path.");

            string uninstallerPath = Path.Combine(installDirectory, "Uninstall.exe");
            File.Copy(currentExecutable, uninstallerPath, true);

            string mainExecutable = FindMainExecutable(installDirectory, newFiles);

            RegisterApplication(installDirectory, mainExecutable, uninstallerPath);

            RemoveStartMenuShortcut();
            CreateStartMenuShortcut(mainExecutable);

            RemoveDesktopShortcut();
            if (createDesktopShortcut)
                CreateDesktopShortcut(mainExecutable);
        }
        finally
        {
            try
            {
                if (Directory.Exists(temporaryDirectory))
                    Directory.Delete(temporaryDirectory, true);
            }
            catch { }
        }

        MessageBox.Show(
            $"{ApplicationName} was installed successfully.\n\nLocation:\n{installDirectory}",
            "Installation Complete",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private static void UninstallApplication()
    {
        string installDirectory = GetInstallDirectory();
        string manifestPath = Path.Combine(installDirectory, ".web2exe-installed-files.txt");

        RemoveStartMenuShortcut();
        RemoveDesktopShortcut();

        try
        {
            using RegistryKey? uninstallKey = Registry.CurrentUser.OpenSubKey(
                "Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall",
                true);

            uninstallKey?.DeleteSubKey(ApplicationName, false);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Could not remove the Windows uninstall registry entry.", ex);
        }

        if (Directory.Exists(installDirectory))
        {
            if (File.Exists(manifestPath))
            {
                foreach (string relativeFile in ReadManifest(manifestPath))
                {
                    string filePath = ResolveRelativeFile(installDirectory, relativeFile);
                    if (File.Exists(filePath))
                    {
                        try { File.Delete(filePath); }
                        catch { /* Files that are locked remain for the user to remove. */ }
                    }
                }

                try { File.Delete(manifestPath); }
                catch { }
            }

            RemoveEmptyDirectories(installDirectory);
        }

        try
        {
            string uninstallerPath = Path.Combine(installDirectory, "Uninstall.exe");
            if (File.Exists(uninstallerPath))
            {
                string cleanupScript = Path.Combine(
                    Path.GetTempPath(),
                    "Web2EXE-Wizard-cleanup-" + Guid.NewGuid().ToString("N") + ".cmd");

                File.WriteAllText(
                    cleanupScript,
                    "@echo off\r\n" +
                    "timeout /t 2 /nobreak >nul\r\n" +
                    $"del /f /q \"{uninstallerPath}\" >nul 2>&1\r\n" +
                    $"rmdir /s /q \"{installDirectory}\" >nul 2>&1\r\n" +
                    $"del /f /q \"{cleanupScript}\" >nul 2>&1\r\n",
                    Encoding.ASCII);

                ProcessStartInfo cleanup = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/d /c \"{cleanupScript}\"",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                Process.Start(cleanup);
            }
        }
        catch { }
    }

    private static void ValidateInstallDirectory(string installDirectory)
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localAppData))
            throw new InvalidOperationException("The local application data directory could not be determined.");

        string programsDirectory = Path.Combine(localAppData, "Programs");
        string fullInstall = Path.GetFullPath(installDirectory);
        string fullPrograms = Path.GetFullPath(programsDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

        if (!fullInstall.StartsWith(fullPrograms, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Refusing to install outside the per-user Programs directory.");

        string relative = Path.GetRelativePath(fullPrograms, fullInstall);
        if (relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new InvalidOperationException("Invalid application installation path.");
    }

    private static string ResolveRelativeFile(string rootDirectory, string relativeFile)
    {
        string normalized = relativeFile.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
        string fullRoot = Path.GetFullPath(rootDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string fullPath = Path.GetFullPath(Path.Combine(fullRoot, normalized));

        if (!fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Unsafe relative path in installer package: {relativeFile}");

        return fullPath;
    }

    private static void ExtractPayloadSafely(Stream payloadStream, string destinationDirectory)
    {
        using var archive = new ZipArchive(payloadStream, ZipArchiveMode.Read, false);
        string fullDestination = Path.GetFullPath(destinationDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name))
                continue;

            string entryName = entry.FullName.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
            if (entryName.Contains(".." + Path.DirectorySeparatorChar) ||
                entryName.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
                Path.IsPathRooted(entryName))
                throw new InvalidDataException($"Unsafe path in installer payload: {entry.FullName}");

            string destinationPath = Path.GetFullPath(Path.Combine(fullDestination, entryName));
            if (!destinationPath.StartsWith(fullDestination, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Unsafe path in installer payload: {entry.FullName}");

            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            using Stream source = entry.Open();
            using var destination = File.Create(destinationPath);
            source.CopyTo(destination);
        }
    }

    private static Stream? GetEmbeddedResource(string suffix)
    {
        Assembly assembly = Assembly.GetExecutingAssembly();
        string? resourceName = assembly
            .GetManifestResourceNames()
            .FirstOrDefault(name => name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));

        return string.IsNullOrWhiteSpace(resourceName)
            ? null
            : assembly.GetManifestResourceStream(resourceName);
    }

    private static string[] ReadManifestResource()
    {
        using Stream? stream = GetEmbeddedResource(".InstalledFiles.txt")
            ?? throw new InvalidDataException("The installer file manifest is missing.");
        using var reader = new StreamReader(stream);

        return reader.ReadToEnd()
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .ToArray();
    }

    private static HashSet<string> ReadManifest(string manifestPath)
    {
        return new HashSet<string>(
            File.ReadAllLines(manifestPath).Where(line => !string.IsNullOrWhiteSpace(line)).Select(line => line.Trim()),
            StringComparer.OrdinalIgnoreCase);
    }

    private static string FindMainExecutable(string installDirectory, string[] newFiles)
    {
        string namedExecutable = Path.Combine(installDirectory, ApplicationName + ".exe");
        if (File.Exists(namedExecutable))
            return namedExecutable;

        string? manifestExecutable = newFiles
            .Select(file => ResolveRelativeFile(installDirectory, file))
            .FirstOrDefault(file =>
                Path.GetExtension(file).Equals(".exe", StringComparison.OrdinalIgnoreCase) &&
                !Path.GetFileName(file).Equals("Uninstall.exe", StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(manifestExecutable) && File.Exists(manifestExecutable))
            return manifestExecutable;

        throw new InvalidOperationException("The published application executable could not be found.");
    }

    private static void RegisterApplication(string installDirectory, string mainExecutable, string uninstallerPath)
    {
        using RegistryKey uninstallRoot = Registry.CurrentUser.CreateSubKey(
            "Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\" + ApplicationName,
            true);

        uninstallRoot.SetValue("DisplayName", ApplicationName);
        uninstallRoot.SetValue("DisplayVersion", ApplicationVersion);
        uninstallRoot.SetValue("Publisher", "Sashwat");
        uninstallRoot.SetValue("InstallLocation", installDirectory);
        uninstallRoot.SetValue("DisplayIcon", mainExecutable);
        uninstallRoot.SetValue("UninstallString", $"\"{uninstallerPath}\" --uninstall");
        uninstallRoot.SetValue("QuietUninstallString", $"\"{uninstallerPath}\" --uninstall");
        uninstallRoot.SetValue("NoModify", 1, RegistryValueKind.DWord);
        uninstallRoot.SetValue("NoRepair", 1, RegistryValueKind.DWord);
    }

    private static string GetStartMenuFolder()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Programs),
            ApplicationName);
    }

    private static void CreateStartMenuShortcut(string targetExecutable)
    {
        string shortcutFolder = GetStartMenuFolder();
        Directory.CreateDirectory(shortcutFolder);
        CreateShellShortcut(Path.Combine(shortcutFolder, ApplicationName + ".lnk"), targetExecutable);

        CreateShellShortcut(
            Path.Combine(shortcutFolder, "Uninstall " + ApplicationName + ".lnk"),
            Path.Combine(GetInstallDirectory(), "Uninstall.exe"),
            "--uninstall",
            true);
    }

    private static void RemoveStartMenuShortcut()
    {
        string shortcutFolder = GetStartMenuFolder();
        if (Directory.Exists(shortcutFolder))
        {
            foreach (string shortcut in Directory.GetFiles(shortcutFolder, "*.lnk", SearchOption.AllDirectories))
            {
                try { File.Delete(shortcut); }
                catch { }
            }

            try { Directory.Delete(shortcutFolder, true); }
            catch { }
        }
    }

    private static void CreateDesktopShortcut(string targetExecutable)
    {
        string desktopDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        if (!string.IsNullOrWhiteSpace(desktopDirectory))
            CreateShellShortcut(Path.Combine(desktopDirectory, ApplicationName + ".lnk"), targetExecutable);
    }

    private static void RemoveDesktopShortcut()
    {
        string desktopDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        if (string.IsNullOrWhiteSpace(desktopDirectory))
            return;

        string shortcutPath = Path.Combine(desktopDirectory, ApplicationName + ".lnk");
        if (File.Exists(shortcutPath))
        {
            try { File.Delete(shortcutPath); }
            catch { }
        }
    }

    private static void CreateShellShortcut(string shortcutPath, string targetExecutable, string arguments = "", bool installArguments = false)
    {
        Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
        if (shellType == null)
            throw new InvalidOperationException("Windows Script Host is unavailable; could not create a shortcut.");

        dynamic shell = Activator.CreateInstance(shellType)
            ?? throw new InvalidOperationException("Could not start Windows Script Host.");
        dynamic shortcut = shell.CreateShortcut(shortcutPath);
        shortcut.TargetPath = targetExecutable;
        shortcut.Description = ApplicationName;
        shortcut.IconLocation = targetExecutable + ",0";

        if (installArguments)
            shortcut.Arguments = arguments;

        shortcut.Save();
    }

    private static void RemoveEmptyDirectories(string rootDirectory)
    {
        foreach (string directory in Directory.GetDirectories(rootDirectory, "*", SearchOption.AllDirectories)
                     .OrderByDescending(path => path.Length))
        {
            try
            {
                if (!Directory.EnumerateFileSystemEntries(directory).Any())
                    Directory.Delete(directory);
            }
            catch { }
        }

        try
        {
            if (Directory.Exists(rootDirectory) && !Directory.EnumerateFileSystemEntries(rootDirectory).Any())
                Directory.Delete(rootDirectory);
        }
        catch { }
    }
}

internal sealed class InstallerForm : Form
{
    private readonly CheckBox desktopShortcut = new CheckBox
    {
        Text = "Create Desktop shortcut",
        Checked = false,
        AutoSize = true,
        Location = new Point(24, 70)
    };

    private readonly Button installButton = new Button
    {
        Text = "Install",
        Size = new Size(120, 36),
        Location = new Point(24, 110)
    };

    public InstallerForm()
    {
        Text = "Install " + Program.ApplicationName;
        Width = 480;
        Height = 220;
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;

        Controls.Add(new Label
        {
            Text = "Install location:" + Environment.NewLine + Program.GetInstallDirectory(),
            Location = new Point(24, 24),
            Size = new Size(410, 40)
        });
        Controls.Add(desktopShortcut);
        Controls.Add(installButton);

        installButton.Click += (_, _) =>
        {
            installButton.Enabled = false;
            try
            {
                Program.InstallApplication(desktopShortcut.Checked);
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Installation Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                installButton.Enabled = true;
            }
        };
    }
}

[ComImport]
[Guid("00021401-0000-0000-C000-000000000046")]
internal class ShellLink
{
}

[ComImport]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("000214F9-0000-0000-C000-000000000046")]
internal interface IShellLinkW
{
    void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cch, IntPtr pfd, int fFlags);
    void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cch);
    void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
    void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cch);
    void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
    void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cch);
    void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
    void GetHotkey(out short pwHotkey);
    void SetHotkey(short wHotkey);
    void GetShowCmd(out int piShowCmd);
    void SetShowCmd(int iShowCmd);
    void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cch, out int piIcon);
    void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
    void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, int dwReserved);
    void Resolve(IntPtr hwnd, int fFlags);
    void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
}

[ComImport]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("0000010b-0000-0000-C000-000000000046")]
internal interface IPersistFile
{
    void GetClassID(out Guid pClassID);
    [PreserveSig]
    int IsDirty();
    void Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, int dwMode);
    void Save([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, [MarshalAs(UnmanagedType.Bool)] bool fRemember);
    void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string pszFileName);
    void GetCurFile([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFileName);
}
""";
    }

    private static string MakeSafeFileName(string value, string fallback, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return fallback;

        char[] invalidCharacters = Path.GetInvalidFileNameChars();
        var result = new char[value.Length];

        for (int i = 0; i < value.Length; i++)
        {
            char character = value[i];
            result[i] = Array.IndexOf(invalidCharacters, character) >= 0 || char.IsControl(character)
                ? '-'
                : character;
        }

        string safeName = new string(result).Trim().Trim(' ', '.');
        if (string.IsNullOrWhiteSpace(safeName))
            return fallback;

        if (IsReservedWindowsName(safeName))
            safeName += "_App";

        if (safeName.Length > maximumLength)
            safeName = safeName.Substring(0, maximumLength).TrimEnd(' ', '.');

        return string.IsNullOrWhiteSpace(safeName) ? fallback : safeName;
    }

    private static bool IsReservedWindowsName(string name)
    {
        string baseName = name.Split('.')[0].Trim().ToUpperInvariant();
        if (baseName is "CON" or "NUL" or "AUX" or "PRN")
            return true;

        if (baseName.StartsWith("COM", StringComparison.Ordinal) && baseName.Length == 4 &&
            char.IsDigit(baseName[3]) && baseName[3] >= '1' && baseName[3] <= '9')
            return true;

        if (baseName.StartsWith("LPT", StringComparison.Ordinal) && baseName.Length == 4 &&
            char.IsDigit(baseName[3]) && baseName[3] >= '1' && baseName[3] <= '9')
            return true;

        return false;
    }
}
