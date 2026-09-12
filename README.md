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
- Creates optional per-version Desktop and Start Menu shortcuts that follow renames
- Launches versions or removes them instantly with silent background cleanup
- Opens Roblox private-server share links in any selected installed version
- Minimizes to a Restore/Exit tray icon after launching Roblox when enabled
- Offers persisted settings for Discord Rich Presence, tray behavior, and startup update checks
- Shows the current Roblox game in Discord Rich Presence when configured by the maintainer
- Offers user-initiated in-place updates from verified GitHub Release installer assets
- Includes current-user and all-user installation, custom install paths, Start Menu registration, and a custom uninstaller

The uninstaller removes both the selected program directory and `%APPDATA%\RBXDowngrader`. Selecting **Keep downloaded Roblox versions** preserves only the `robloxversions` folder.

## Build

Requirements: Windows 10 or 11, PowerShell, and the .NET 8 SDK.

```powershell
dotnet build RBXDowngrader.sln -m:1
dotnet test tests\RBXDowngrader.Core.Tests
.\build-installer.ps1
```

The packaging script writes `artifacts\RBXDowngraderSetup-win-x64.exe` (or the selected runtime identifier). The installer is self-contained and installs without administrator access. The local `artifacts` folder is intentionally ignored by Git; attach only the architecture-matching setup executable to a GitHub release. The updater extracts the application payload embedded inside that setup executable, so no separate update ZIP is produced or required.

Update downloads are checked against the size and SHA-256 digest supplied by GitHub before extraction. Updates replace only files listed in the packaged application-file manifest. Downloaded Roblox versions, custom names, caches, settings, and logs are not part of that manifest and remain untouched.

Roblox package MD5 values are used as the deployment manifest's integrity check over HTTPS. They are not digital signatures and do not independently prove package authenticity.

## Discord Rich Presence maintainer setup

The Discord application is configured once by the RBXDowngrader maintainer. End users do not need a Discord developer account or client ID.

1. Open [Discord Developer Applications](https://discord.com/developers/applications) and sign in.
2. Select **New Application**, name it `RBXDowngrader`, and create it.
3. Optional: open **Rich Presence** > **Art Assets** to add images for a future presence layout.
4. Open **General Information** and copy the **Application ID**.
5. Replace `REPLACE_WITH_DISCORD_APPLICATION_ID` in `src/RBXDowngrader.App/DiscordPresenceService.cs` with that Application ID, then rebuild the installer.

The setting is enabled by default. If the ID is not configured, Discord is closed, or Roblox logs are unavailable, Rich Presence stays inactive and writes only to the application log.

The recent-build picker reads a five-item Windows history response from the public [RBXOffsets API](https://rbxoffsets.com/documents/api). Packages are still downloaded directly from Roblox deployment servers.

## Notes

Roblox can retire deployment files, reject outdated clients, or change its package format. A valid historical hash is therefore not a guarantee that the client can still connect. Old clients may also contain fixed security issues, so only use builds you trust.

RBXDowngrader is an independent project and is not affiliated with Roblox Corporation or Latte Softworks.
