# arcus

[日本語](README.md)

arcus adds a folder bar beside standard Windows Open and Save As dialogs. See open folders and favorites at a glance, then click a folder to navigate there.

**[Download for Windows (ZIP · Beta)](https://github.com/amae-yoshino/arcus/releases/download/v1.2.0-beta.1/arcus-1.2.0-beta.1-win-x64.zip)** · [Release notes](https://github.com/amae-yoshino/arcus/releases/tag/v1.2.0-beta.1)

For Windows 11 x64. Requires .NET 10 Windows Desktop Runtime x64. See [requirements and installation](#requirements-and-installation) below.

## Features

- A folder bar that follows supported file dialogs.
- Folder sources: Windows Explorer, Files and OneCommander.
- Favorites with editable labels, ordering and JSON import/export.
- English and Japanese UI. The default follows the Windows UI language, using English for languages other than Japanese. Choose Settings → Language to override it.
- Rounded UI, light/dark colors for the bar and settings, adjustable bar height and per-app exclusions.
- Optional startup when signing in to Windows.

## Requirements and installation

Windows 11 x64 and [.NET 10 Windows Desktop Runtime x64](https://dotnet.microsoft.com/en-us/download/dotnet/10.0). The runtime is not bundled. The normal app runs without administrator privileges. Core functionality does not require internet access.

Download `arcus-1.2.0-beta.1-win-x64.zip` using the link above, extract it, and run `arcus.exe`. Open Settings from the tray icon, then open a supported file dialog and click a folder on the bar. Complete the file selection or save operation in the original dialog. To build from source, use the commands below.

## Folder coverage and limitations

| Source | Scope |
|---|---|
| Windows Explorer | Paths exposed by Shell COM; complete tab coverage is not guaranteed |
| Files | Current folder plus saved tab/pane data; UI structure checked with Files 4.3 |
| OneCommander | Public CurrentPathGet field plus saved tab data; checked with version 3.108 |

**Saved data may be outdated, include closed tabs, or miss newly opened tabs. This is not guaranteed live enumeration of every open tab.** The list is collected once per dialog and stays fixed until it closes.

Unsupported/custom dialogs, elevated applications, different UI structures, UNC paths and display configurations may not work. After failed or timed-out navigation, check the dialog manually; the address text may have changed without confirmed navigation.

The main tray app supports English and Japanese. Windows common dialogs use the operating system's language.

## Settings and privacy

Settings and favorites: `%AppData%\arcus\settings.json`; backup: `settings.json.bak`. Diagnostic logs are stored in `logs` and `diagnostics` under the same directory. Logs may contain full paths, file names and window titles. There is no automatic redaction, encryption or log rotation. Redact information before sharing. See [SECURITY.md](SECURITY.md).

## Updating and removing

Exit arcus from the tray menu before replacing it with a new release. Settings remain in AppData. If the executable moves and startup is enabled, save Settings again to update the startup entry. Back up settings before migrating between beta versions.

To remove arcus, disable startup in Settings and save, exit the app, and delete its extracted folder. Delete `%AppData%\arcus` separately only if you also want to remove settings and logs.

## Building

Use .NET SDK 10.0.402 as specified in `global.json`. No additional NuGet packages are referenced.

```powershell
dotnet build arcus.sln -c Release -m:1 -nr:false -p:UseSharedCompilation=false
dotnet run --project tests/Arcus.Tests -c Release --no-build
```

Runtime-excluded single-file publish:

```powershell
dotnet publish src/arcus/arcus.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:UseSharedCompilation=false -m:1 -nr:false -o artifacts/publish
```

[Build details](docs/BUILD.md) and [architecture](docs/ARCHITECTURE.md) are currently documented in Japanese.

## Reporting issues

Include the arcus version, Windows build, target application, file manager/version, UI language and steps to reproduce. Never attach unredacted private logs. See SECURITY.md for private security reporting.

## License

[MIT License](LICENSE). Copyright (c) 2026 amae yoshino. See [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) for third-party code license notices.
