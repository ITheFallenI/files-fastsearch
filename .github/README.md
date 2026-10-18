# Files FastSearch

**An unofficial community fork of [Files](https://github.com/files-community/Files) with instant, indexed filename search powered by [Find My Files](https://github.com/P4suta/find-my-files).**

> [!IMPORTANT]
> Files FastSearch is **not** an official Files release and is not affiliated with, endorsed by or supported by the Files team or the Find My Files author. Please report problems with this build [here](https://github.com/ITheFallenI/files-fastsearch/issues), not to either upstream project.

Files FastSearch keeps the Files interface, tabs, thumbnails, previews and file operations exactly as they are, and replaces the slow folder walk behind Files' search box with a native NTFS index. Results appear while you type, Everything-style, without Voidtools Everything or Windows Search indexing.

## Features

- **Instant filename and folder search** in the search box and its suggestions, from any folder, drive, library or Home.
- **Native NTFS index** built by reading the Master File Table directly (seconds per volume) and kept current from the USN change journal, so new, deleted and renamed files show up straight away.
- **Every local NTFS drive** is indexed; the index is saved and reused across restarts.
- **Normal Files results**: icons, thumbnails, sorting, grouping, context menus and file operations work on search results just as they do elsewhere.
- **Automatic fallback** to the original Files search for anything the index doesn't cover: network and removable drives, FAT/exFAT/ReFS volumes, archives, tag searches (`tag:`), Windows Search syntax (`kind:`, `$...`), and drives that are still being indexed.
- **Low overhead**: the index uses roughly 100 MB of RAM per million files, runs as an on-demand service, and stops itself a few minutes after the last Files window closes.
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
> There is no release yet. This section describes how installation will work once the first release is published.

1. Download `FilesFastSearch.msixbundle` from the [latest release](https://github.com/ITheFallenI/files-fastsearch/releases/latest) and open it.
2. Open **Settings → Advanced → Fast search** in Files FastSearch and select **Turn on fast search**. Windows asks once for administrator approval to register the indexing service; after that, Files FastSearch runs without elevation.
3. Indexing takes a few seconds to a minute per drive. Until a drive is ready, searches on it use the regular Files search.

Official Files, if installed, is not changed in any way. Files FastSearch does not offer to replace File Explorer, so it never takes over or removes that setting from official Files.

### Uninstalling

1. In **Settings → Advanced → Fast search**, select **Remove indexing service** (optionally deleting the saved index).
2. Uninstall Files FastSearch from **Settings → Apps** like any other app.

You can also remove the service from an administrator terminal with `fmf-service uninstall --purge-data`.

## How it works

```
Files FastSearch (WinUI 3, unelevated)
  └─ FolderSearch ──► Files.App.FastSearch ──named pipe──► fmf-service (Rust, LocalSystem, on demand)
        │                (Find My Files' own C# client)       └─ $MFT scan, USN journal, in-memory index
        └─ original Files search (fallback)
```

- `external/find-my-files` is a pinned git submodule of Find My Files. Its engine and service are built unchanged.
- `src/Files.App.FastSearch` compiles Find My Files' own named-pipe client and service-control code straight from the submodule, adding only a small adapter for Files. The client checks that the pipe belongs to the registered service before trusting it, and the service only accepts the user who installed it. See the [Find My Files security model](https://github.com/P4suta/find-my-files/blob/main/docs/SECURITY.md).
- `src/Files.App/Utils/Storage/Search/FolderSearch.cs` asks the index first and falls back to the original search.

## Building from source

Requirements: Windows 11 x64, Visual Studio 2026 with the **WinUI application development** and **Desktop development with C++** workloads, the .NET 10 SDK, and Rust (stable, `x86_64-pc-windows-msvc`).

```powershell
git clone --recurse-submodules https://github.com/ITheFallenI/files-fastsearch
cd files-fastsearch

# Indexing engine and service
cargo build --locked --release --manifest-path external/find-my-files/engine/Cargo.toml -p fmf-service

# Files FastSearch (Dev configuration, installs as "Files - Dev")
msbuild -restore src/Files.App/Files.App.csproj -p:Configuration=Debug -p:Platform=x64 -v:quiet -clp:ErrorsOnly

# Adapter tests
dotnet run --project tests/Files.App.FastSearch.Tests -p:Platform=x64
```

Deploy and debug the **Files.App** project from Visual Studio as usual. Development builds use the upstream "Files - Dev" identity, which also installs beside official Files.

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
- Files' "Replace File Explorer" and "Replace Open File dialog" options are hidden, because they are shared with official Files.

## Credits and licences

- **[Files](https://github.com/files-community/Files)** by the Files Community, licensed under the [MIT License](../LICENSE-MIT), with some files under the [Mozilla Public License 2.0](../LICENSE-MPL).
- **[Find My Files](https://github.com/P4suta/find-my-files)** by P4suta, licensed under the [Apache License 2.0](https://github.com/P4suta/find-my-files/blob/main/LICENSE). It is included unmodified as a submodule.
- Third-party components and their licences are listed in [NOTICE.md](NOTICE.md).

If you like Files, please consider [supporting the Files team](https://files.community/sponsor). Files FastSearch exists because of their work.
