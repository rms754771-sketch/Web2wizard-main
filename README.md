# Web2EXE Wizard

Convert websites into Windows desktop applications using a React interface hosted by the existing C# WinForms application.

## Features

- Figma-designed React interface hosted by Microsoft WebView2
- Allowlisted WebView2 message bridge to the C# desktop host
- Automatic website favicon download
- WebView2-based Windows application generation
- Portable ZIP output
- Custom C# Windows installer EXE (no Inno Setup or WiX dependency)
- Version management and update manifest generation
- Local build history and output-directory preferences

## Requirements

- Windows 10/11
- .NET 8 SDK
- Node.js 22 or later and npm (used to compile the React interface during the Launcher build)
- Microsoft Edge WebView2 Runtime

WebView2 Runtime:
https://developer.microsoft.com/microsoft-edge/webview2/

## Build and run

Open Launcher/Launcher.csproj in Visual Studio 2026, restore NuGet packages, and build. The MSBuild BuildWebUi target installs the WebUI npm dependencies and compiles the React application into Launcher/wwwroot. The generated web assets are copied to the Launcher build and publish output directories.

Run the Launcher project. Its WebView2 window hosts the local React UI; C# remains responsible for privileged operations, including publishing, portable ZIP creation, custom installer generation, folder selection, and opening output folders.

## Create an application

1. Open Create Application.
2. Enter an application name and an HTTP or HTTPS website URL.
3. Select Create Application.
4. The existing C# build pipeline generates the Windows application, a portable ZIP, and a custom installer EXE. If installer generation fails, the successfully created ZIP is retained.
5. Open Build History or Settings to view build results and configure the output directory.

Generated files are stored under the selected output directory in GeneratedApp/portable.

## Run a portable application

1. Extract the portable ZIP archive to a folder.
2. Keep all extracted files together.
3. Run the generated application executable.
4. The generated application opens the configured website in WebView2.

## Developer

Sashwat

GitHub:
https://github.com/oxkr981-pixel

## License

MIT
