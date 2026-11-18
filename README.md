# Web2EXE Wizard

Convert any website into a Windows desktop application.

## Features

- Convert websites into native Windows apps
- Automatic icon download from website favicon
- WebView2-based desktop applications
- Portable ZIP output
- Version management
- Auto-update manifest generation
- Custom application names

## Requirements

- Windows 10/11
- .NET 8 SDK for building applications
- Microsoft Edge WebView2 Runtime

WebView2 Runtime:
https://developer.microsoft.com/microsoft-edge/webview2/

## How to Use

1. Enter an application name.
2. Enter a website URL.
3. Wait for the icon preview to load.
4. Click Generate.
5. After generation completes, open:

Template/GeneratedApp/portable

The ZIP file is named using the application name and version, for example:

`MyApplication-1.0.1-Portable.zip`

## Run a Portable Application

1. Extract the ZIP archive to a folder.
2. Keep all extracted files together.
3. Run the generated application executable from the extracted folder.
4. The generated application will open the configured website in WebView2.

## Developer

Sashwat

GitHub:
https://github.com/oxkr981-pixel

## License

MIT
