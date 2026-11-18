using System.IO.Compression;

namespace Launcher;

public static class PortablePackageBuilder
{
    public static string Create(string publishedApplicationDirectory, string outputDirectory, string applicationName, string version)
    {
        if (string.IsNullOrWhiteSpace(publishedApplicationDirectory))
            throw new ArgumentException("The published application directory is required.", nameof(publishedApplicationDirectory));

        if (!Directory.Exists(publishedApplicationDirectory))
            throw new DirectoryNotFoundException($"Published application directory not found: {publishedApplicationDirectory}");

        string[] publishedFiles;
        try
        {
            publishedFiles = Directory.GetFiles(publishedApplicationDirectory, "*", SearchOption.AllDirectories);
        }
        catch (Exception ex)
        {
            throw new IOException($"Could not inspect the published application directory: {publishedApplicationDirectory}", ex);
        }

        if (publishedFiles.Length == 0)
            throw new IOException($"Published application directory contains no files: {publishedApplicationDirectory}");

        if (string.IsNullOrWhiteSpace(outputDirectory))
            throw new ArgumentException("The portable package output directory is required.", nameof(outputDirectory));

        Directory.CreateDirectory(outputDirectory);

        string safeApplicationName = MakeSafeFileName(applicationName, "Application", 180);
        string safeVersion = MakeSafeFileName(version, "UnknownVersion", 50);
        string archivePath = Path.Combine(outputDirectory, $"{safeApplicationName}-{safeVersion}-Portable.zip");

        if (File.Exists(archivePath))
            throw new IOException($"Portable ZIP already exists and will not be overwritten: {archivePath}");

        string temporaryArchivePath = archivePath + ".tmp";
        bool finalArchiveCreated = false;

        try
        {
            if (File.Exists(temporaryArchivePath))
                File.Delete(temporaryArchivePath);

            using (var archive = ZipFile.Open(temporaryArchivePath, ZipArchiveMode.Create))
            {
                foreach (string filePath in publishedFiles)
                {
                    string fullFilePath = Path.GetFullPath(filePath);
                    string relativePath = Path.GetRelativePath(publishedApplicationDirectory, fullFilePath);

                    if (relativePath == ".." ||
                        relativePath.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
                        Path.IsPathRooted(relativePath))
                        continue;

                    archive.CreateEntryFromFile(
                        fullFilePath,
                        relativePath.Replace('\\', '/'),
                        CompressionLevel.Optimal);
                }
            }

            VerifyArchive(temporaryArchivePath, publishedApplicationDirectory, archivePath, temporaryArchivePath);

            File.Move(temporaryArchivePath, archivePath);
            finalArchiveCreated = true;

            VerifyArchive(archivePath, publishedApplicationDirectory, archivePath, temporaryArchivePath);
            return archivePath;
        }
        catch
        {
            TryDeleteFile(temporaryArchivePath);
            if (finalArchiveCreated)
                TryDeleteFile(archivePath);

            throw;
        }
    }

    private static void VerifyArchive(string archivePath, string publishedApplicationDirectory, string finalArchivePath, string temporaryArchivePath)
    {
        if (!File.Exists(archivePath) || new FileInfo(archivePath).Length == 0)
            throw new IOException($"Portable ZIP is missing or empty: {archivePath}");

        var expectedEntries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string filePath in Directory.GetFiles(publishedApplicationDirectory, "*", SearchOption.AllDirectories))
        {
            string relativePath = Path.GetRelativePath(publishedApplicationDirectory, filePath);
            expectedEntries.Add(relativePath.Replace('\\', '/'));
        }

        if (expectedEntries.Count == 0)
            throw new IOException($"Published application directory contains no files: {publishedApplicationDirectory}");

        using var archive = ZipFile.OpenRead(archivePath);
        var actualEntries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        string finalArchiveName = Path.GetFileName(finalArchivePath);
        string temporaryArchiveName = Path.GetFileName(temporaryArchivePath);

        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name))
                continue;

            string entryName = entry.FullName.Replace('\\', '/');

            if (entryName.Equals(finalArchiveName, StringComparison.OrdinalIgnoreCase) ||
                entryName.Equals(temporaryArchiveName, StringComparison.OrdinalIgnoreCase) ||
                entryName.EndsWith("/" + finalArchiveName, StringComparison.OrdinalIgnoreCase) ||
                entryName.EndsWith("/" + temporaryArchiveName, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"Portable ZIP contains itself or its temporary archive: {entryName}");
            }

            actualEntries.Add(entryName);
        }

        if (actualEntries.Count == 0)
            throw new InvalidDataException($"Portable ZIP contains no files: {archivePath}");

        foreach (string expectedEntry in expectedEntries)
        {
            if (!actualEntries.Contains(expectedEntry))
                throw new InvalidDataException($"Portable ZIP is missing published file '{expectedEntry}': {archivePath}");
        }
    }

    private static void TryDeleteFile(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
                File.Delete(filePath);
        }
        catch
        {
            // Preserve the original packaging exception.
        }
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
