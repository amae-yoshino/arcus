arcus
=====

A clickable folder bar for standard Windows Open and Save As dialogs.

Requires Windows 11 x64 and .NET 10 Windows Desktop Runtime x64.
The runtime is not bundled:
https://dotnet.microsoft.com/en-us/download/dotnet/10.0

Extract the ZIP and run arcus.exe. Open Settings from the rainbow tray icon.
Configure folder sources, favorites and Language (Windows Auto / Japanese / English).
Open a supported file dialog and click a folder on the bar. Complete the file
selection or save operation in the original dialog.

Sources: Windows Explorer, Files and OneCommander, with limited coverage.
The folder list is collected once per dialog and remains fixed until it closes.
Saved Files and OneCommander tabs may be outdated, include closed tabs or miss
new tabs. Complete live tab coverage and compatibility with all apps are not guaranteed.
After a navigation failure or timeout, inspect the address and actual folder manually.

Update: Exit arcus from the tray menu, extract the new version and run it.
Settings remain in %AppData%\arcus\settings.json. If the executable moves,
save Settings again when automatic startup is enabled.
Remove: Disable startup and save, exit arcus, then delete its extracted folder.
Delete %AppData%\arcus separately only if you also want to remove settings and logs.

Logs under %AppData%\arcus\logs may include full paths, file names and window
titles. No automatic redaction, encryption or log rotation is implemented.
Redact private information before sharing logs or screenshots.

MIT License. Copyright (c) 2026 amae yoshino.
See LICENSE and THIRD_PARTY_NOTICES.md.
