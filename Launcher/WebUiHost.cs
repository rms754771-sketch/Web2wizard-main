using System.Diagnostics;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace Launcher;

public partial class Form1
{
    private WebView2? webUi;
    private string outputDirectory = "";
    private readonly string settingsFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Web2Executable", "settings.json");
    private readonly string historyFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Web2Executable", "build-history.json");
    private static readonly JsonSerializerOptions HostJsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };

    private sealed class AppSettings { public string? OutputDirectory { get; set; } }
    private sealed class BuildHistoryItem
    {
        public string Name { get; set; } = "";
        public string Url { get; set; } = "";
        public string Version { get; set; } = "";
        public string Status { get; set; } = "";
        public string Installer { get; set; } = "";
        public string Time { get; set; } = "";
    }

    private void InitializeWebUi()
    {
        foreach (Control control in Controls) control.Visible = false;
        Text = "Web2EXE Wizard";
        ClientSize = new Size(1440, 900);
        MinimumSize = new Size(1024, 700);
        StartPosition = FormStartPosition.CenterScreen;
        outputDirectory = LoadSettings().OutputDirectory ?? templatePath;
        if (string.IsNullOrWhiteSpace(outputDirectory)) outputDirectory = templatePath;
        outputDirectory = Path.GetFullPath(outputDirectory);
        outputPath = Path.Combine(outputDirectory, "GeneratedApp");
        webUi = new WebView2 { Dock = DockStyle.Fill };
        Controls.Add(webUi);
        webUi.BringToFront();
        Shown += async (_, _) => await InitializeWebUiAsync();
    }

    private async Task InitializeWebUiAsync()
    {
        if (webUi == null) return;
        try
        {
            string webRoot = Path.Combine(AppContext.BaseDirectory, "wwwroot");
            if (!Directory.Exists(webRoot)) webRoot = Path.Combine(projectRoot, "wwwroot");
            if (!Directory.Exists(webRoot)) throw new DirectoryNotFoundException("The Web2EXE interface files were not found. Build the WebUI project first.");
            await webUi.EnsureCoreWebView2Async();
            webUi.CoreWebView2.SetVirtualHostNameToFolderMapping("appassets.web2exe", webRoot, CoreWebView2HostResourceAccessKind.DenyCors);
            webUi.CoreWebView2.Settings.AreDevToolsEnabled = false;
            webUi.CoreWebView2.Settings.IsStatusBarEnabled = false;
            webUi.CoreWebView2.WebMessageReceived += WebUi_WebMessageReceived;
            webUi.CoreWebView2.NavigationStarting += (_, e) =>
            {
                if (!Uri.TryCreate(e.Uri, UriKind.Absolute, out var navigationUri) ||
                    navigationUri.Scheme != Uri.UriSchemeHttps ||
                    !navigationUri.Host.Equals("appassets.web2exe", StringComparison.OrdinalIgnoreCase))
                    e.Cancel = true;
            };
            webUi.CoreWebView2.NewWindowRequested += (_, e) =>
            {
                e.Handled = true;
                if (Uri.TryCreate(e.Uri, UriKind.Absolute, out var externalUri) && (externalUri.Scheme == Uri.UriSchemeHttp || externalUri.Scheme == Uri.UriSchemeHttps))
                {
                    try { Process.Start(new ProcessStartInfo(externalUri.AbsoluteUri) { UseShellExecute = true }); } catch { }
                }
            };
            webUi.CoreWebView2.Navigate("https://appassets.web2exe/index.html");
        }
        catch (Exception ex)
        {
            MessageBox.Show("Web2EXE Wizard could not start its interface.\n\n" + ex.Message, "Web2EXE Wizard", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void WebUi_WebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (!Uri.TryCreate(e.Source, UriKind.Absolute, out var sourceUri) ||
            sourceUri.Scheme != Uri.UriSchemeHttps ||
            !sourceUri.Host.Equals("appassets.web2exe", StringComparison.OrdinalIgnoreCase))
            return;

        try
        {
            using JsonDocument message = JsonDocument.Parse(e.WebMessageAsJson);
            JsonElement root = message.RootElement;
            if (!root.TryGetProperty("type", out var typeValue)) return;
            switch (typeValue.GetString())
            {
                case "ready": SendHostState(); break;
                case "build": StartBuildFromWebUi(root); break;
                case "browseOutput": BrowseOutputDirectory(); break;
                case "saveSettings": SaveOutputSettings(root); break;
                case "openFolder": OpenOutputDirectory(); break;
                case "checkTools": SendToolStatus(); break;
            }
        }
        catch (Exception ex) { SendWebUiMessage(new { type = "buildError", message = "The desktop app could not process the request: " + ex.Message }); }
    }

    private void StartBuildFromWebUi(JsonElement root)
    {
        string appName = root.TryGetProperty("name", out var nameValue) ? nameValue.GetString()?.Trim() ?? "" : "";
        string websiteUrl = root.TryGetProperty("url", out var urlValue) ? urlValue.GetString()?.Trim() ?? "" : "";
        if (string.IsNullOrWhiteSpace(appName) || appName.Length > 100 || !Uri.TryCreate(websiteUrl, UriKind.Absolute, out var parsedUrl) ||
            (parsedUrl.Scheme != Uri.UriSchemeHttp && parsedUrl.Scheme != Uri.UriSchemeHttps))
        {
            SendWebUiMessage(new { type = "buildError", message = "Enter an application name (up to 100 characters) and a valid HTTP or HTTPS website URL." });
            return;
        }
        if (buildWorker.IsBusy) { SendWebUiMessage(new { type = "buildError", message = "A build is already running. Wait for it to finish." }); return; }
        txtAppName.Text = appName;
        txtUrl.Text = websiteUrl;
        btnGenerate_Click(this, EventArgs.Empty);
        if (buildWorker.IsBusy) SendWebUiMessage(new { type = "buildStarted" });
        else SendWebUiMessage(new { type = "buildError", message = "The build could not be started. Check the entered details." });
    }

    private void BrowseOutputDirectory()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Choose the folder for generated application files",
            UseDescriptionForTitle = true,
            SelectedPath = Directory.Exists(outputDirectory) ? outputDirectory : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            outputDirectory = dialog.SelectedPath;
            outputPath = Path.Combine(outputDirectory, "GeneratedApp");
            SaveSettings(new AppSettings { OutputDirectory = outputDirectory });
            SendWebUiMessage(new { type = "settingsSaved", outputDirectory });
            SendHostState();
        }
    }

    private void SaveOutputSettings(JsonElement root)
    {
        string selected = root.TryGetProperty("outputDirectory", out var value) ? value.GetString()?.Trim() ?? "" : "";
        if (string.IsNullOrWhiteSpace(selected)) { SendWebUiMessage(new { type = "buildError", message = "Choose a valid output directory." }); return; }
        try
        {
            outputDirectory = Path.GetFullPath(selected);
            Directory.CreateDirectory(outputDirectory);
            outputPath = Path.Combine(outputDirectory, "GeneratedApp");
            SaveSettings(new AppSettings { OutputDirectory = outputDirectory });
            SendWebUiMessage(new { type = "settingsSaved", outputDirectory });
            SendHostState();
        }
        catch (Exception ex) { SendWebUiMessage(new { type = "buildError", message = "Could not save the output directory: " + ex.Message }); }
    }

    private void OpenOutputDirectory()
    {
        string directory = Directory.Exists(Path.Combine(outputPath, "portable")) ? Path.Combine(outputPath, "portable") : outputPath;
        try
        {
            Directory.CreateDirectory(directory);
            Process.Start(new ProcessStartInfo("explorer.exe", "\"" + directory + "\"") { UseShellExecute = true });
        }
        catch (Exception ex) { SendWebUiMessage(new { type = "buildError", message = "Could not open the output folder: " + ex.Message }); }
    }

    private AppSettings LoadSettings()
    {
        try { if (File.Exists(settingsFile)) return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(settingsFile)) ?? new AppSettings(); }
        catch { }
        return new AppSettings();
    }

    private void SaveSettings(AppSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(settingsFile)!);
        File.WriteAllText(settingsFile, JsonSerializer.Serialize(settings, HostJsonOptions));
    }

    private List<BuildHistoryItem> LoadBuildHistory()
    {
        try { if (File.Exists(historyFile)) return JsonSerializer.Deserialize<List<BuildHistoryItem>>(File.ReadAllText(historyFile)) ?? new List<BuildHistoryItem>(); }
        catch { }
        return new List<BuildHistoryItem>();
    }

    private void SaveBuildHistory(List<BuildHistoryItem> history)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(historyFile)!);
        File.WriteAllText(historyFile, JsonSerializer.Serialize(history, HostJsonOptions));
    }

    private void HandleWebUiBuildCompleted(RunWorkerCompletedEventArgs e)
    {
        object? result = e.Error == null ? e.Result : null;
        var history = LoadBuildHistory();
        if (result is BuildData build)
        {
            bool zipExists = !string.IsNullOrWhiteSpace(build.PortableZipPath) && File.Exists(build.PortableZipPath);
            bool installerExists = !string.IsNullOrWhiteSpace(build.WindowsInstallerPath) && File.Exists(build.WindowsInstallerPath);
            history.Insert(0, new BuildHistoryItem { Name = build.AppName ?? "Application", Url = build.WebsiteUrl ?? "", Version = build.Version ?? "", Status = zipExists ? "Successful" : "Failed", Installer = installerExists ? "Created" : "Not created", Time = DateTime.Now.ToString("MMM d, yyyy h:mm tt") });
        }
        else if (e.Error != null || result is false)
        {
            history.Insert(0, new BuildHistoryItem { Name = txtAppName.Text, Url = txtUrl.Text, Status = "Failed", Installer = "Not created", Time = DateTime.Now.ToString("MMM d, yyyy h:mm tt") });
        }
        SaveBuildHistory(history.Take(100).ToList());
        SendWebUiMessage(new { type = "buildFinished", success = result is BuildData });
        SendHostState();
    }

    private void SendHostState()
    {
        var history = LoadBuildHistory();
        SendWebUiMessage(new { type = "hostState", state = new
        {
            connected = true,
            builds = history.Select(item => new { name = item.Name, url = item.Url, version = item.Version, status = item.Status, installer = item.Installer, time = item.Time }).ToArray(),
            outputDirectory,
            tools = GetToolStatus(),
            building = buildWorker.IsBusy
        }});
    }

    private object GetToolStatus()
    {
        bool dotnet = false;
        try
        {
            using var process = Process.Start(new ProcessStartInfo { FileName = "dotnet", Arguments = "--version", UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true });
            if (process != null && process.WaitForExit(3000)) dotnet = process.ExitCode == 0;
        }
        catch { }
        bool webView2Runtime;
        try { webView2Runtime = !string.IsNullOrWhiteSpace(CoreWebView2Environment.GetAvailableBrowserVersionString()); }
        catch { webView2Runtime = false; }
        return new { dotnet, webView2 = webView2Runtime };
    }

    private void SendToolStatus() => SendWebUiMessage(new { type = "toolsUpdated", tools = GetToolStatus() });

    private void PublishWebUiStatus(string message)
    {
        if (webUi?.CoreWebView2 == null) return;
        SendWebUiMessage(new { type = "buildStatus", message });
        if (message.StartsWith("❌", StringComparison.Ordinal)) SendWebUiMessage(new { type = "buildError", message });
    }

    private void PublishWebUiProgress(int percentage, string? message)
    {
        if (webUi?.CoreWebView2 == null) return;
        SendWebUiMessage(new { type = "buildProgress", percentage, message });
    }

    private void SendWebUiMessage(object message)
    {
        if (webUi?.CoreWebView2 == null || webUi.IsDisposed) return;
        void Post() { try { webUi.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(message, HostJsonOptions)); } catch { } }
        if (webUi.InvokeRequired) { try { webUi.BeginInvoke((Action)Post); } catch { } }
        else Post();
    }
}
