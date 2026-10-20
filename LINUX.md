# Running AmcacheParser on Linux and macOS

The download (`net/AmcacheParser.zip`) runs on Windows, Linux and macOS with .NET 10: `AmcacheParser.dll` is the program (its managed libraries are inside it), and the native SQLite library is a file next to it, one per system:

| File | For |
|------|-----|
| `e_sqlite3.dll` | Windows x64 |
| `libe_sqlite3.so` | Linux x64 (glibc) |
| `libe_sqlite3.dylib` | macOS on Apple silicon (arm64) |

.NET loads the one for the system it runs on from the program's folder. Nothing is extracted or written anywhere else when it runs. WxTCmd's download has the same three files (the same SQLitePCLRaw version, byte for byte), so both can be unzipped into one folder, as Get-ZimmermanTools does.

## Using the download

1. Install the .NET 10 runtime ([Install .NET on Linux](https://learn.microsoft.com/dotnet/core/install/linux), [on macOS](https://learn.microsoft.com/dotnet/core/install/macos)). `dotnet --list-runtimes` should show `Microsoft.NETCore.App 10.x`.
2. Unzip `AmcacheParser.zip` and run it with `dotnet`:

   ```
   dotnet AmcacheParser.dll -f /path/to/Amcache.hve --csv /path/to/output
   ```

`-f` takes the hive, the folder it is in (with the `Inventory*.db` databases of Windows 11 26H1 and later) or one of the databases, as on Windows. `AmcacheParser.exe` is the Windows launcher; elsewhere use `dotnet AmcacheParser.dll`.

## Other systems (Linux arm64, Alpine/musl, Intel Macs)

The download has no SQLite library for them. Either:

- copy the library for your system next to `AmcacheParser.dll`, from the `SQLitePCLRaw.lib.e_sqlite3` 2.1.13 NuGet package (a zip: `runtimes/<rid>/native/libe_sqlite3.so` or `.dylib`, such as `linux-arm64`, `linux-musl-x64`, `osx-x64`), replacing the one there of the same name; or
- build it: install the [.NET 10 SDK](https://learn.microsoft.com/dotnet/core/install/linux), then, from the folder with `AmcacheParser.slnx`:

  ```
  dotnet publish AmcacheParser/AmcacheParser.csproj -c Release -r linux-arm64 -o publish
  ./publish/AmcacheParser -f /path/to/Amcache.hve --csv /path/to/output
  ```

  (`-r osx-x64`, `-r linux-musl-x64` and so on for other systems).

## Problems

- `Couldn't find a valid ICU package installed on the system`: .NET needs ICU (`libicu`), which a minimal system (a container, a fresh WSL) may not have. Install it with the package manager, or set `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1`.
- `The type initializer for 'Microsoft.Data.Sqlite.SqliteConnection' threw an exception` (with `e_sqlite3` in the details): the SQLite library for this system is not next to `AmcacheParser.dll` (see above).
- macOS may refuse a library from a downloaded zip (quarantine): `xattr -d com.apple.quarantine libe_sqlite3.dylib`.

Tested on Ubuntu (WSL, x64) with .NET 10: the CSVs match the Windows build's. macOS has not been tested.
