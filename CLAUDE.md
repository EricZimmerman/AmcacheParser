# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Overview

- **`Amcache`**: a `netstandard2.0` NuGet library (package id `Amcache`) that reads Amcache data. It opens hives with the Registry package; fixes to hive or transaction log handling belong in Registry, followed by a package bump here.
  - `AmcacheOld`: the old format (Windows 7/8): `Root\File` and `Root\Programs`.
  - `AmcacheNew`: the new format (Windows 10/11): the `Root\Inventory*` keys. `HasOldFormatEntries` is set when an upgraded hive also has the old keys (Issues repo #203); the CLI then reads them with `AmcacheOld` too.
  - `AmcacheNew.FromDatabases(folder)` (`AmcacheDatabases.cs`): the `Inventory*.db` SQLite databases that replace the Root keys from Windows 11 26H1 on. A row is an `InventoryItem` like a hive key: `ItemKey` is the key name, `LastModified` (a FILETIME) the last write time, the other columns the values, with numbers as the registry's decimal strings. The same `Add*` methods (the old hive code, unchanged) read both, so the output matches. `DriverTimeStamp` is a signed INTEGER in the database and an unsigned DWORD in the hive; it is converted back. The seven typed databases (`TypedDatabases`) fill the classes; every other database's table, and all the `Metadata_N` tables (merged), go to `OtherTables` as they are.
  - Databases are copied (with `-wal` and `-journal`) to a new folder in a temp folder and only the copy is opened. `FromDatabases(directory, tempDirectory)` takes that folder (`FromDatabases(directory)`, the 2026.10.1 API, uses the system's temp folder); the CLI passes `--temp`, or by default `Temp` next to AmcacheParser (`TempFolder.cs`; Eric, 2026-10-20: nothing in the user's %TEMP%), created when needed and removed when empty, and stops with exit code 1 if it can't be written to (no fallback: the user gives `--temp`). The same in WxTCmd and SQLECmd (Microsoft.Data.Sqlite, read by column name); nothing is written next to the evidence (a test checks hashes). `PRAGMA quick_check` failures are reported. A damaged copy can have a row (ItemKey) more than once, differing only in LastModified; the newest is used (more data is needed to say more).
- **`AmcacheParser`**: the command line tool (`net10.0` only; .NET Framework was dropped for the native SQLite library). `-f` is a hive, the folder it's in, or one of the databases. It says which kinds of data it found, then writes the hive's CSVs, `_OldFormat` CSVs for a mixed hive, and `_Databases` CSVs (the same ones, plus one per other table and `Metadata`).

Input may be corrupt or hostile.

## Behavior to keep

- CSV columns: `WriteNewFormat` pins every column with `Order(...)`: the columns of 2026.5 in their order, new ones at the end. Before, unindexed columns were placed automatically, and adding a property moved them. Every database column has a CSV column (unwanted ones are hidden in the Timeline Explorer layout instead).
- A date that doesn't parse (Discord writes `53/16/2024`; some MSI dates hold two dates) is left empty and counted (`InvalidDates`), instead of losing the entry.
- Times are UTC in the `--dt` format, including the FILETIMEs in the Metadata CSV (`lastFullSync`, `LastScanTime`, ...).
- Transaction logs: `Helper.LogFiles` (`.LOG`, `.LOG1`, `.LOG2`, any case, not empty). Dirty: sequence numbers differ, or the header checksum is wrong and there are logs.
- `--co` (Issues #259, off by default): the console messages also go to `<yyyyMMddHHmmss>_AmcacheParser_<version>_ConsoleLog.txt` in the `--csv` folder (`ConsoleLog.cs`; the time is UTC, while the CSV names use local time), template `[HH:mm:ss.fff LVL] {Message:l}` (not `:lj`, which put enum values in quotes), same level as the console. `DateTimeOffsetFormatter` keeps a format given in the template (it used to print `{Timestamp:HH:mm:ss.fff}` in `--dt`'s format).
- Exit codes (Eric, 2026-10-22, all CLI tools): 1 only when the tool fails (bad arguments, a missing -f, an export or temp folder that can't be written, a hive or database it can't access or read: `CouldNotProcess`, `DatabasesNotAccessed`, or a dirty hive without its logs); damaged hives and databases, entries that could not be parsed (`EntriesNotParsed`) and no data are listed and exit 0.

## Commands

The SDK is pinned in `global.json` (.NET 10). The library uses `LangVersion 10`. Tests target `net10.0` and use NUnit 5 + FluentAssertions 7 (pinned to `[7.2.2,8.0.0)`, since 8.x requires a commercial license).

Debug info: `Directory.Build.props` sets `DebugType embedded` (the pdbs are inside the assemblies: stack traces and Exceptionless reports have file and line numbers, and no .pdb file ships) and, for Release, `ContinuousIntegrationBuild` (source paths in the pdbs are `/_/...`, not this machine's folders). Don't add `DebugType` or `DebugSymbols` to the project files.

```bash
dotnet build -c Release                                              # AmcacheParser.slnx
dotnet test -c Release                                               # CI; CorpusTests is [Explicit]
dotnet test -c Release --filter "FullyQualifiedName~CorpusTests"     # ../Corpus/AmcacheSQL and ../Corpus/Registry (or AMCACHE_CORPUS)
dotnet pack -c Release                                               # only the library
```

## Tests

- `DatabaseTests`: databases made in the test with the real Windows 11 26H2 schemas (`TestData.Schemas`): mapping, DriverTimeStamp, shortcuts, bad dates and values, other tables and Metadata, duplicate rows, an open WAL database (read, and the evidence unchanged), a file that isn't a database, random damage.
- `HiveTests`: the DFIR Artifact Museum's Windows 10 (dirty, with logs) and Server 2012 R2 hives (`TestFiles/DFIRArtifactMuseum`, gzipped, MIT; see the README there).
- `CliTests`: AmcacheParser.dll as a process: CSV names and headers, databases (folder or a .db), a hive with databases, exit codes.
- `CorpusTests`: the private 26H2 sample (`Corpus/AmcacheSQL`) and every Amcache hive in `Corpus/Registry`. Never copy those files into the repo.

## Versioning and release

- The library's version comes from the git tag through MinVer (bare tags like `2026.10.0`); there is no `<Version>` in its csproj.
- AmcacheParser has `<Version>` in its csproj, bumped by hand. It is not packed; the user builds, signs and releases it.
- To release the library: push to `master`, then `git tag <ver> && git push origin <ver> && gh release create <ver> --verify-tag --generate-notes`.
- `.github/workflows/publish.yml` is identical to GuidMapping's: pack, validate and test on PRs and pushes to `master`; on a published GitHub Release, push to NuGet.org with trusted publishing (`NuGet/login` with the `NUGET_USER` repo variable, `EricRZimmerman`).
- Dependabot (`.github/dependabot.yml`) opens monthly PRs for GitHub Actions and NuGet updates.
- Release builds are staged, signed and zipped by `Build-ZimmermanTools.ps1` in the get-zimmermantools repository (cloned next to this one). Its program table says what goes in this tool's download zip (the files taken from the build, folders copied from the repositories, files kept from the current release zip); a new output file, a content folder or a change in how the tool is built (single file, another framework) needs that entry updated.
- AmcacheParser is released like the other command line tools (Eric, 2026-10-20: single files are for the GUIs only; the command line tools stay cross platform and never extract anything on the user's machine): `AmcacheParser.exe`, `AmcacheParser.dll` (Costura: the managed dependencies inside it) and `AmcacheParser.runtimeconfig.json` from `dotnet build`, run with the exe or `dotnet AmcacheParser.dll` on any OS. The native SQLite library (SQLitePCLRaw's e_sqlite3, which Costura does not embed) ships as files next to it, one per OS with its own name: `e_sqlite3.dll` (win-x64), `libe_sqlite3.so` (linux-x64), `libe_sqlite3.dylib` (osx-arm64), copied by Build-ZimmermanTools from the build's `runtimes/<rid>/native` (`Natives` in its table). There is no deps.json in the release, so .NET finds them in the app folder (its first probe). The zip has no folder, so they land in Get-ZimmermanTools' shared `net` folder next to WxTCmd's identical copies: keep Microsoft.Data.Sqlite (SQLitePCLRaw) at the same version as WxTCmd's (Build-ZimmermanTools fails on differing files at the same path). Never set `IncludeNativeLibrariesForSelfExtract` or publish as a single file. LINUX.md says what Linux/macOS users need.
