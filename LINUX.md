# Running AmcacheParser on Linux and macOS

The download (`AmcacheParser.exe`) is for Windows only: it is one file with the Windows SQLite library inside, so it can't be run with `dotnet` on another system. AmcacheParser itself runs on Linux and macOS: Microsoft.Data.Sqlite brings the native SQLite library for each platform, so it only needs building there.

Install the [.NET 10 SDK](https://learn.microsoft.com/dotnet/core/install/linux), then, from the folder with `AmcacheParser.slnx`:

```
dotnet publish AmcacheParser/AmcacheParser.csproj -c Release -r linux-x64 -p:PublishSingleFile=true -o publish
./publish/AmcacheParser -f /path/to/Amcache.hve --csv /path/to/output
```

`-f` takes the hive, the folder it is in (with the `Inventory*.db` databases of Windows 11 26H1 and later) or one of the databases, as on Windows.

Use `-r linux-arm64`, `-r osx-arm64` and so on for other platforms.

.NET needs ICU (`libicu`) on Linux; a minimal system (a container, a fresh WSL) may not have it: install it with the package manager, or set `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1`.

If you'd use a Linux or macOS download, say so in an issue.
