# RBXDowngrader

A small native Windows launcher for downloading, keeping, and launching Roblox player versions by deployment hash.

## Features

- Accepts bare 16-character hashes or `version-...` identifiers
- Downloads official deployment packages from Roblox CDN endpoints
- Verifies package checksums and blocks unsafe archive paths
- Stores builds in `%APPDATA%\RBXDowngrader\robloxversions`
- Shows the five newest known Windows builds in a compact live picker
- Detects copied version hashes and private-server links
- Supports custom names and total installed disk usage
- Launches versions or removes them instantly with silent background cleanup
- Opens Roblox private-server share links in any selected installed version
- Includes current-user and all-user installation, custom install paths, Start Menu registration, and a custom uninstaller

The uninstaller removes both the selected program directory and `%APPDATA%\RBXDowngrader`. Selecting **Keep downloaded Roblox versions** preserves only the `robloxversions` folder.

## Build

Requirements: Windows 10 or 11, PowerShell, and the .NET 8 SDK.

```powershell
dotnet build RBXDowngrader.sln -m:1
dotnet run --project tests\RBXDowngrader.Core.Tests
.\build-installer.ps1
```

The packaged installer is written to `artifacts\RBXDowngraderSetup.exe`. It is self-contained and installs without administrator access. The local `artifacts` folder is intentionally ignored by Git; attach the EXE directly to a GitHub release.

The recent-build picker reads a five-item Windows history response from the public [RBXOffsets API](https://rbxoffsets.com/documents/api). Packages are still downloaded directly from Roblox deployment servers.

## Notes

Roblox can retire deployment files, reject outdated clients, or change its package format. A valid historical hash is therefore not a guarantee that the client can still connect. Old clients may also contain fixed security issues, so only use builds you trust.

RBXDowngrader is an independent project and is not affiliated with Roblox Corporation or Latte Softworks.
