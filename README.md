# AmcacheParser

Parses Amcache data: Amcache.hve (Windows 7 to 11) and the `Inventory*.db` SQLite databases that replace its
`Root\Inventory*` keys from Windows 11 26H1 on. Unlike other Amcache parsers it does not dump everything: it links File
entries to the Program entries they came from, so what didn't come from an installed program stands out, and writes
programs, files, shortcuts, drivers and devices to CSV. SHA-1 lists can exclude known files or include only the ones
looked for.

| | |
|---|---|
| **Version** | 2026.10.0 |
| **Download** | [.NET 10](https://download.ericzimmermanstools.com/net/AmcacheParser.zip) |
| **All tools** | [ericzimmerman.github.io](https://ericzimmerman.github.io) or [Get-ZimmermanTools](https://download.ericzimmermanstools.com/Get-ZimmermanTools.zip), which downloads them all and keeps them current |
| **Changes** | [ChangeLog.txt](https://download.ericzimmermanstools.com/ChangeLog.txt) |
| **Issues** | [EricZimmerman/AmcacheParser](https://github.com/EricZimmerman/AmcacheParser/issues) or [EricZimmerman/Issues](https://github.com/EricZimmerman/Issues/issues) |

## Requirements

- **.NET 10**: the [.NET 10 runtime](https://dotnet.microsoft.com/download/dotnet/10.0). Run `AmcacheParser.exe` on
  Windows, or `dotnet AmcacheParser.dll` on any system with the runtime. The native SQLite library it needs ships next to
  it for Windows x64 (`e_sqlite3.dll`), Linux x64 (`libe_sqlite3.so`) and macOS on Apple silicon (`libe_sqlite3.dylib`);
  see [LINUX.md](LINUX.md) for Linux, macOS and other systems.

There is no .NET Framework build.

## Command line

```text
Usage:
  AmcacheParser [options]

Options:
  -f <f>          Amcache.hve to parse (Windows 7 to 11), the folder it's in (C:\Windows\AppCompat\Programs), or one of the Inventory*.db SQLite databases there (Windows 11 26H1 and later). The databases next to a hive are read too
  -i              Include file entries for Programs entries
  -w <w>          Path to file containing SHA-1 hashes to *exclude* from the results. Blacklisting overrides whitelisting
  -b <b>          Path to file containing SHA-1 hashes to *include* from the results. Blacklisting overrides whitelisting
  --csv <csv>     Directory to save CSV formatted results to. Be sure to include the full path in double quotes
  --csvf <csvf>   File name to save CSV formatted results to. When present, overrides default name
  --dt <dt>       The custom date/time format to use when displaying time stamps. See https://goo.gl/CNVq0k for options [default: yyyy-MM-dd HH:mm:ss]
  --mp            When true, display higher precision for timestamps
  --nl            When true, ignore transaction log files for dirty hives. Default is FALSE
  --temp <temp>   Folder for the temporary copies of the SQLite databases, which are opened instead of the evidence and deleted when done. Default: a Temp folder next to AmcacheParser. A folder that isn't there is created, and removed again when done. If that folder can't be written to, AmcacheParser stops: give a writable folder with --temp
  --debug         Show debug information during processing
  --trace         Show trace information during processing
  -?, -h, --help  Show help and usage information
  --version       Show version information
```

Short options (a single letter) start with one dash, long options with two.

## Examples

```text
AmcacheParser.exe -f "C:\Temp\amcache\AmcacheWin10.hve" --csv C:\temp
AmcacheParser.exe -f "C:\Temp\amcache\AmcacheWin10.hve" -i --csv C:\temp --csvf foo.csv
AmcacheParser.exe -f "C:\Temp\amcache\AmcacheWin10.hve" -w "c:\temp\whitelist.txt" --csv C:\temp
AmcacheParser.exe -f "C:\Temp\Programs" -i --csv C:\temp    (Amcache.hve and the Inventory*.db databases)
```

## Output

There are three kinds of Amcache data, and AmcacheParser reads each it finds (the console says which):

- **Old format** (Windows 7 and 8): Program entries are under `Root\Programs` and File entries under `Root\File` in
  Amcache.hve.
- **New format** (Windows 10 and 11): `Root\InventoryApplication`, `Root\InventoryApplicationFile` and the other
  `Root\Inventory*` keys in Amcache.hve. A hive upgraded from Windows 8 can have old format entries too; they are written
  to the `_OldFormat` CSVs.
- **SQLite databases** (Windows 11 26H1 and later): `Inventory*.db` next to Amcache.hve (`C:\Windows\AppCompat\Programs`)
  hold what the `Root\Inventory*` keys did. A row is read like the key of the same name (`ItemKey`), its columns like the
  key's values, and `LastModified` like the key's last write time, so the same CSVs come out (`_Databases` in their
  names), with every column the databases have. Every other database (InventoryInstaller, InventoryMiscellaneous, Mare,
  ...) gets a CSV with its columns as they are, and the `Metadata` tables of all of them one more.

The CSVs are named `<timestamp>_<hive name>_<kind>.csv`: `UnassociatedFileEntries` always, `ProgramEntries` and
`AssociatedFileEntries` with `-i`, and for the new format and the databases also `ShortCuts`, `DriverBinaries`,
`DeviceContainers`, `DriverPackages` and `DevicePnps`. Times are UTC in the `--dt` format. A date that doesn't parse (an
invalid program or driver date) is left empty instead of losing the entry.

The databases are copied (with any `-wal` and `-journal`) to a temporary folder and only the copies are opened, so nothing
is written next to the evidence. That folder is `--temp`, or by default a `Temp` folder next to AmcacheParser (never the
user's `%TEMP%`); each copy is deleted when it has been read. If the folder can't be written to (AmcacheParser in a
read-only place), AmcacheParser stops and asks for `--temp`. Damaged databases are reported, and the newest copy of a row
is used.

## How programs and files are matched

AmcacheParser gathers information about all the Program entries, then looks at all the File entries. In each File entry
is a pointer to a Program ID (value 100). If this Program ID exists in Program entries, the File entry is associated with
the Program entry. At the end of this process you are left with things that didn't come from some kind of installed
application.

- Using the minimum options, AmcacheParser will only export unassociated File entries.
- With `-i`, AmcacheParser also exports a Programs entry list and its associated File entry list.
- With whitelisting (`-w`) and blacklisting (`-b`) you can further reduce the output by excluding certain File entries,
  or including only File entries whose SHA-1 is found in a blacklist file.

AmcacheParser strips the extra 0s from the front of the SHA-1 value, so you can use the SHA-1s as shown in the output
files for generating whitelists or blacklists.

## Exit codes

| Code | Meaning |
|---|---|
| `0` | Success. Damaged hives and databases, entries that could not be parsed, and no Amcache data are reported but are not a failure |
| `1` | The tool failed: invalid arguments, a missing `-f` path, a hive or database it can't access or read, a dirty hive without its transaction logs (unless `--nl`), or an export or `--temp` folder that can't be written |

## What's new in 2026.10

- Hives load about 2x faster with less memory (Registry 2026.10.0); transaction logs are replayed as Windows does, and a
  hive in the test set that would not open is read.
- The Windows 11 26H1/26H2 `Inventory*.db` databases are read: programs, files, shortcuts, devices and drivers with the
  same logic and CSVs as the hive, and every other database and their Metadata tables as CSVs of their own.
- `-f` can be Amcache.hve, the folder it's in, or one of the databases. Databases are read from temporary copies in a
  `Temp` folder next to AmcacheParser (or `--temp`), so nothing is written next to the evidence.
- Old format entries in hives upgraded from Windows 8 are written to `_OldFormat` CSVs (they were left out; Issues #203).
- Programs and drivers with a date that is not valid are kept with that date empty (they were left out with an error).
- New columns at the end of the new format CSVs, such as `Publisher` and `Sha256` for files and `UserSid` and
  `MsiInstallDate` for programs.
- .NET 10 only, with the native SQLite library next to it for Windows x64, Linux x64 and macOS on Apple silicon.
- Exit code 1 when the tool fails (it was always 0). Error reports include source file and line numbers.

The full list is in the [ChangeLog](https://download.ericzimmermanstools.com/ChangeLog.txt).

## Building

```text
dotnet build AmcacheParser.slnx -c Release
```

Builds `net10.0`, the `Amcache` library (`netstandard2.0`, [NuGet](https://www.nuget.org/packages/Amcache)) and the tests.
Hives are opened with the [Registry](https://github.com/EricZimmerman/Registry) library
([NuGet](https://www.nuget.org/packages/Registry)).

## Further reading

- [AmcacheParser: Reducing the noise, finding the signal](https://binaryforay.blogspot.com/2015/07/amcacheparser-reducing-noise-finding.html)
- [(Am)cache still rules everything around me (part 2 of 1)](https://binaryforay.blogspot.com/2017/10/amcache-still-rules-everything-around.html)
- [Updates to the left of me, updates to the right of me, version 1 releases are here (for the most part)](https://binaryforay.blogspot.com/2018/03/updates-to-left-of-me-updates-to-right.html)
- [Everything gets an update, Sept 2018 edition](https://binaryforay.blogspot.com/2018/09/everything-gets-update-sept-2018-edition.html)
- [Registry Explorer and RECmd 1.2.0.0 released!](https://binaryforay.blogspot.com/2019/01/registry-explorer-and-recmd-1200.html)
- [Locked file support added to AmcacheParser, AppCompatCacheParser, MFTECmd, ShellBags Explorer (and SBECmd), and Registry Explorer (and RECmd)](https://binaryforay.blogspot.com/2019/01/locked-file-support-added-to.html)

## Special thanks

Open source development funding and support provided by the [SANS Institute](https://www.sans.org/) and
[SANS DFIR](https://www.sans.org/digital-forensics-incident-response/).

## License

[MIT](LICENSE.md), Copyright (c) Eric R. Zimmerman 2027.
