# Files FastSearch

**An unofficial community fork of [Files](https://github.com/files-community/Files) with instant, indexed filename search powered by [Find My Files](https://github.com/P4suta/find-my-files).**

> [!IMPORTANT]
> Files FastSearch is **not** an official Files release and is not affiliated with, endorsed by or supported by the Files team or the Find My Files author. Please report problems with this build [here](https://github.com/ITheFallenI/files-fastsearch/issues), not to either upstream project.

**[Download the Windows 11 x64 preview EXE](https://github.com/ITheFallenI/files-fastsearch/releases/download/v4.3.2.0/FilesFastSearch-4.3.2.0-x64-Setup.exe)** · [Release notes and certificate requirements](https://github.com/ITheFallenI/files-fastsearch/releases/tag/v4.3.2.0)

Files FastSearch keeps the Files interface, tabs, thumbnails, previews and file operations exactly as they are, and replaces the slow folder walk behind Files' search box with a native NTFS index. Results appear while you type, Everything-style, without Voidtools Everything or Windows Search indexing.

## Features

- **Instant filename and folder search** in the search box and its suggestions, from any folder, drive, library or Home.
- **Native NTFS index** built by reading the Master File Table directly (seconds per volume) and kept current from the USN change journal, so new, deleted and renamed files show up straight away.
- **Every local NTFS drive** is indexed; the index is saved and reused across restarts.
- **Normal Files results**: icons, thumbnails, sorting, grouping, context menus and file operations work on search results just as they do elsewhere.
- **Automatic fallback** to the original Files search for anything the index doesn't cover: network and removable drives, FAT/exFAT/ReFS volumes, archives, tag searches (`tag:`), Windows Search syntax (`kind:`, `$...`), and drives that are still being indexed.
- **On-demand service**: the index runs locally and the service stops itself after its configured idle period when no clients remain. Memory and scan time depend on the drive and its files.
- **Installs beside official Files**: its own package identity, settings, app name, `files-fastsearch:` link protocol and `files-fastsearch.exe` alias.

### Search syntax

Plain text matches anywhere in a name, as in Files (`report.pdf` matches `report-final.pdf`). The index also understands Find My Files operators:

| Example | Meaning |
| --- | --- |
| `budget 2026` | names containing both words |
| `invoice \| receipt` | either word |
| `!draft` | names without "draft" |
| `*.mp4` | wildcards over the whole name |
| `"annual report"` | an exact phrase |

Queries containing `:` or starting with `$` are sent to the original Files search, so Windows Search (AQS) and tag queries keep working unchanged.

## Screenshots

_Screenshots will be added with the first release._

## Installing

> [!NOTE]
> The first download is an experimental, self-signed preview for Windows 11 x64 (build 22000 or later). Windows may show an unknown-publisher warning. It has been tested on the maintainer's PC, but not on a clean Windows installation. SignPath signing is not yet approved or configured.

1. Download `FilesFastSearch-4.3.2.0-x64-Setup.exe` from the [preview release](https://github.com/ITheFallenI/files-fastsearch/releases/tag/v4.3.2.0) and run it. The EXE includes the full MSIX app and Microsoft Windows App Runtime. You can inspect [the installation script](scripts/preview-installer/Install.ps1) first.
2. Read the certificate prompt. If you trust this project, type `TRUST AND INSTALL` and approve the Windows administrator prompt. This adds the preview's public signing certificate to **Local Computer → Trusted People**, trusting that signing certificate for all users, then installs the app for your account. It does not add a trusted root. The expected certificate thumbprint is `0BB11DF21C43A636B0A8BEABB9546476DA0543F0`. Press Enter to cancel if you do not wish to trust it. Keep a copy of your settings before testing a preview; the installer also backs up existing FastSearch settings under `%LOCALAPPDATA%\FilesFastSearch\SettingsBackups`.
3. Open **Settings → Advanced → Fast search** in Files FastSearch and select **Turn on fast search**. Windows asks once for administrator approval to register the indexing service; after that, Files FastSearch runs without elevation.
4. Indexing takes a few seconds to a minute per drive. Until a drive is ready, searches on it use the regular Files search.

Installing FastSearch leaves your default file manager unchanged. To open folders and **Win + E** in FastSearch, enable **Settings → Advanced → Experimental feature flags → Set Files as the default file manager**. This optional setting applies to your Windows account and asks for Windows administrator approval using your current account. It backs up the previous folder-opening commands and keeps its signed native launcher under `%LOCALAPPDATA%\FilesFastSearch\ShellIntegration`, separate from official Files.

Turn the toggle off to restore the previous handler (normally Explorer). Restoration only changes registrations still owned by FastSearch; a newer choice of file manager is preserved. Backups remain in the `Backups` folder beside the launcher. If the app is uninstalled while enabled, the retained launcher restores the previous registration on the next folder launch. A manual recovery command is also available:

```powershell
& "$env:LOCALAPPDATA\FilesFastSearch\ShellIntegration\Files.FastSearch.Launcher.exe" --restore-fastsearch
```

Open/Save dialogs keep their existing Windows behaviour. Special shell locations can still open in Explorer.

### Uninstalling

1. If enabled, turn off **Set Files as the default file manager** to restore your previous handler. Then, in **Settings → Advanced → Fast search**, select **Remove indexing service** (optionally deleting the saved index).
2. Uninstall Files FastSearch from **Settings → Apps** like any other app.
3. If you trusted the preview certificate, open `certlm.msc` → **Trusted People → Certificates**, verify its thumbprint above, and remove that certificate when you no longer need any builds signed with it. This needs administrator approval.

You can also remove the service from an administrator terminal with `fmf-service uninstall --purge-data`.

## How it works

```
Files FastSearch (WinUI 3, unelevated)
  └─ FolderSearch ──► Files.App.FastSearch ──named pipe──► fmf-service (Rust, LocalSystem, on demand)
        │                (Find My Files' own C# client)       └─ $MFT scan, USN journal, in-memory index
        └─ original Files search (fallback)
```

- `external/find-my-files` is a pinned fork of Find My Files with Windows build 22000 sharing and failed-volume recovery fixes.
- `src/Files.App.FastSearch` compiles Find My Files' own named-pipe client and service-control code straight from the submodule, adding only a small adapter for Files. The client checks that the pipe belongs to the registered service before trusting it, and the service only accepts the user who installed it. See the [Find My Files security model](https://github.com/P4suta/find-my-files/blob/main/docs/SECURITY.md).
- `src/Files.App/Utils/Storage/Search/FolderSearch.cs` asks the index first and falls back to the original search.

## Building from source

Requirements: Windows 11 x64, Visual Studio 2026 with the **WinUI application development** and **Desktop development with C++** workloads, the .NET 10 SDK, and Rust (stable, `x86_64-pc-windows-msvc`).

```powershell
git clone --recurse-submodules https://github.com/ITheFallenI/files-fastsearch
cd files-fastsearch

# Indexing engine and service
Push-Location external/find-my-files/engine
cargo build --locked --release -p fmf-service
Pop-Location

# Files FastSearch (Dev configuration, installs as "Files - Dev")
msbuild -restore src/Files.App/Files.App.csproj -p:Configuration=Debug -p:Platform=x64 -v:quiet -clp:ErrorsOnly

# Adapter tests
dotnet run --project tests/Files.App.FastSearch.Tests -p:Platform=x64
```

Run `.github/scripts/Deploy-DevPackage.ps1` to build and install "Files - Dev": it stages the files that only MSIX packaging adds (Files.App.Server, Assets) into the output folder, which a Visual Studio deploy of Files.App otherwise starts without. After that, debug **Files.App** from Visual Studio as usual. Development builds use the upstream "Files - Dev" identity, which also installs beside official Files.

### Updating from upstream

```powershell
git remote add upstream https://github.com/files-community/Files
git fetch upstream
git merge upstream/main

# Find My Files
git -C external/find-my-files fetch
git -C external/find-my-files checkout <tag or commit>
git add external/find-my-files
```

The integration is deliberately small (a new project, one search hook, and identity settings) so upstream merges stay easy.

## Known limitations

- Only fixed NTFS drives are indexed. Other locations fall back to the regular, slower search.
- Index results include name, path, size and date modified. Date created and the hidden-file dimming are not shown for indexed results.
- Very broad searches show the first 20,000 matches.
- With **Show hidden items** on, hidden and protected system files are both included.
- Searching inside folders reached through junctions or volume mount points only returns what is on the drive the path starts on.
- Explorer replacement is optional and uses per-user registry overrides. Open/Save dialog replacement remains unavailable in this fork.

## Credits and licences

- **[Files](https://github.com/files-community/Files)** by the Files Community, licensed under the [MIT License](../LICENSE-MIT), with some files under the [Mozilla Public License 2.0](../LICENSE-MPL).
- **[Find My Files](https://github.com/P4suta/find-my-files)** by P4suta, licensed under the [Apache License 2.0](https://github.com/P4suta/find-my-files/blob/main/LICENSE). The pinned submodule includes local Windows build 22000 sharing and failed-volume recovery changes; see [third-party notices](NOTICE.md).

## Privacy and code signing

The filename index and search queries stay on this computer. See the [privacy policy](FASTSEARCH_PRIVACY.md) for update checks and other network features inherited from Files.

The [code signing policy](FASTSEARCH_CODE_SIGNING.md) explains local test signing and the pending SignPath application. There is no publicly trusted signed FastSearch release yet.
- Third-party components and their licences are listed in [NOTICE.md](NOTICE.md).

If you like Files, please consider [supporting the Files team](https://files.community/sponsor). Files FastSearch exists because of their work.
