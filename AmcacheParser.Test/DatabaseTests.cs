using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Amcache;
using FluentAssertions;
using NUnit.Framework;

namespace AmcacheParser.Test;

/// <summary>The Windows 11 26H1/26H2 databases, read with the hive's logic</summary>
[TestFixture]
public class DatabaseTests : TestData
{
    private string Folder => Path.Combine(Dir, "hive");

    [Test]
    public void FilesAndPrograms()
    {
        Database("InventoryApplication", new[] { ProgramRow("0000aaaa", "App") });
        Database("InventoryApplicationFile", new[]
        {
            FileRow("app.exe|1111", @"c:\program files\app\app.exe", "0000aaaa"),
            FileRow("other.exe|2222", @"c:\temp\other.exe", "0000ffff", FileTime + 10_000_000)
        });

        AmcacheNew.HasDatabases(Folder).Should().BeTrue();
        var am = AmcacheNew.FromDatabases(Folder);

        var program = am.ProgramsEntries.Single();
        program.Name.Should().Be("App");
        program.KeyName.Should().Be("0000aaaa");
        program.HiddenArp.Should().BeTrue();
        program.Language.Should().Be(1033);
        program.UserSid.Should().Be("S-1-5-21-1-2-3-1001");
        program.InstallDate.Should().Be(new DateTimeOffset(2025, 5, 19, 0, 0, 0, TimeSpan.Zero));
        program.MsiInstallDate.Should().Be(new DateTimeOffset(2025, 5, 19, 0, 0, 0, TimeSpan.Zero));
        //LastModified is the key's last write time
        program.KeyLastWriteTimestamp.Should().Be(new DateTimeOffset(2026, 7, 30, 0, 27, 37, TimeSpan.Zero).AddTicks(6863666));

        var file = program.FileEntries.Single();
        file.ApplicationName.Should().Be("App");
        file.FullPath.Should().Be(@"c:\program files\app\app.exe");
        file.KeyName.Should().Be("app.exe|1111");
        file.SHA1.Should().Be("ab".PadRight(40, 'c'));
        file.Size.Should().Be(194768);
        file.Usn.Should().Be(209033863936);
        file.IsPeFile.Should().BeTrue();
        file.LinkDate.Should().Be(new DateTimeOffset(2026, 1, 20, 4, 9, 36, TimeSpan.Zero));
        file.Publisher.Should().Be("pub");

        var unassociated = am.UnassociatedFileEntries.Single();
        unassociated.ApplicationName.Should().Be("Unassociated");
        unassociated.FileKeyLastWriteTimestamp.Should().Be(program.KeyLastWriteTimestamp.AddSeconds(1));
        am.TotalFileEntries.Should().Be(2);
    }

    [Test]
    public void DriverTimeStampIsUnsignedAsInTheHive()
    {
        Database("InventoryDriverBinary", new[]
        {
            new Dictionary<string, object>
            {
                ["ItemKey"] = "c:/windows/system32/drivers/1394ohci.sys", ["DriverName"] = "1394ohci.sys", ["DriverCheckSum"] = 325325L,
                ["DriverTimeStamp"] = -1167992687L, ["DriverType"] = 8650778L, ["DriverInBox"] = "1", ["DriverSigned"] = "1",
                ["DriverIsKernelMode"] = "1", ["ImageSize"] = 294912L, ["DriverId"] = "0000c7dea815", ["DriverLastWriteTime"] = "10/17/2025 14:59:50",
                ["LastModified"] = FileTime
            }
        });

        var driver = AmcacheNew.FromDatabases(Folder).DriveBinaries.Single();

        //a DWORD in the hive: 3,126,974,609 seconds (a reproducible build value, not a time)
        driver.DriverTimeStamp.Should().Be(DateTimeOffset.FromUnixTimeSeconds(3126974609));
        driver.DriverId.Should().Be("c7dea815");
        driver.DriverType.Should().Be("8650778");
        driver.DriverInBox.Should().BeTrue();
        driver.DriverLastWriteTime.Should().Be(new DateTimeOffset(2025, 10, 17, 14, 59, 50, TimeSpan.Zero));
    }

    [Test]
    public void ShortcutsHaveTheNewColumns()
    {
        Database("InventoryApplicationShortcut", new[]
        {
            new Dictionary<string, object>
            {
                ["ItemKey"] = "icue.lnk|9232d941e34224b4", ["ShortcutPath"] = @"C:\ProgramData\Microsoft\Windows\Start Menu\iCUE.lnk",
                ["ShortcutTargetPath"] = @"C:\Program Files\Corsair\iCUE.exe", ["ShortcutAumid"] = "{6D809377}\\iCUE.exe", ["ShortcutProgramId"] = "000011d2",
                ["LastModified"] = FileTime
            }
        });

        var shortcut = AmcacheNew.FromDatabases(Folder).ShortCuts.Single();

        shortcut.KeyName.Should().Be("icue.lnk|9232d941e34224b4");
        shortcut.LnkName.Should().Be(@"C:\ProgramData\Microsoft\Windows\Start Menu\iCUE.lnk");
        shortcut.ShortcutTargetPath.Should().Be(@"C:\Program Files\Corsair\iCUE.exe");
        shortcut.ShortcutAumid.Should().Be("{6D809377}\\iCUE.exe");
        shortcut.ShortcutProgramId.Should().Be("000011d2");
    }

    [Test]
    public void InvalidDateKeepsTheEntry()
    {
        //Discord writes this
        Database("InventoryApplication", new[] { ProgramRow("0000aaaa", "Discord", "53/16/2024 00:00:00") });

        var am = AmcacheNew.FromDatabases(Folder);

        am.ProgramsEntries.Single().InstallDate.Should().BeNull();
        am.InvalidDates.Should().Be(1);
    }

    [Test]
    public void OtherTablesAndMetadata()
    {
        Database("InventoryInstaller", new[]
        {
            new Dictionary<string, object>
            {
                ["ItemKey"] = "0000b302", ["InstallerId"] = "0000b302", ["InstallerName"] = "Plexamp-4.50.4-x86_64-setup.exe", ["InstallerVersion"] = "4.50.4",
                ["InstallerPublisher"] = "", ["LastModified"] = FileTime
            }
        });
        Database("InventoryApplication", new[] { ProgramRow("0000aaaa", "App") });

        var am = AmcacheNew.FromDatabases(Folder);

        var installer = am.OtherTables.Single(t => t.Name == "InventoryInstaller_0");
        installer.Database.Should().Be("InventoryInstaller.db");
        installer.Rows.Single()[installer.Columns.IndexOf("InstallerName")].Should().Be("Plexamp-4.50.4-x86_64-setup.exe");

        //every database's metadata in one table
        var metadata = am.OtherTables.Single(t => t.Name == "Metadata");
        metadata.Columns.Should().Equal("Database", "Name", "Value");
        metadata.Rows.Select(t => (string)t[0]).Distinct().Should().BeEquivalentTo("InventoryApplication.db", "InventoryInstaller.db");
    }

    [Test]
    public void NewestCopyOfARowIsUsed()
    {
        //a damaged copy of a database can have a row more than once (older copies of it); without the key this one can
        var schema = Schemas["InventoryApplicationFile"].Replace("ItemKey TEXT PRIMARY KEY", "ItemKey TEXT").Replace("TEXT UNIQUE", "TEXT");
        Database("InventoryApplicationFile", new[]
        {
            FileRow("a.exe|1", @"c:\old.exe", lastModified: FileTime),
            FileRow("a.exe|1", @"c:\newest.exe", lastModified: FileTime + 20),
            FileRow("a.exe|1", @"c:\middle.exe", lastModified: FileTime + 10)
        }, schema: schema);

        var am = AmcacheNew.FromDatabases(Folder);

        am.UnassociatedFileEntries.Select(t => t.FullPath).Should().Equal(@"c:\newest.exe");
    }

    [Test]
    public void WalIsReadAndTheEvidenceIsNotChanged()
    {
        //an open WAL database: the rows are in the -wal file
        Database("InventoryApplicationFile", new[] { FileRow("a.exe|1", @"c:\a.exe") }, journalMode: "WAL", keepOpen: true);
        File.Exists(Path.Combine(Folder, "InventoryApplicationFile.db-wal")).Should().BeTrue();

        var before = Hashes();

        var am = AmcacheNew.FromDatabases(Folder);

        am.UnassociatedFileEntries.Single().FullPath.Should().Be(@"c:\a.exe");
        Hashes().Should().BeEquivalentTo(before, "nothing next to the evidence may change or be added");
    }

    [Test]
    public void NotADatabase()
    {
        File.WriteAllText(Path.Combine(Directory.CreateDirectory(Folder).FullName, "InventoryApplication.db"), "not a database at all, but long enough to look like one");
        Database("InventoryApplicationFile", new[] { FileRow("a.exe|1", @"c:\a.exe") });

        var am = AmcacheNew.FromDatabases(Folder);

        am.ProgramsEntries.Should().BeEmpty();
        am.UnassociatedFileEntries.Should().ContainSingle();
        am.DatabasesNotRead.Should().Be(1, "the exit code is 1 then");
    }

    [Test]
    public void BadValuesDoNotStopTheRest()
    {
        var bad = FileRow("bad.exe|1", @"c:\bad.exe");
        bad["Language"] = "not a number";
        bad["Usn"] = -5L;
        Database("InventoryApplicationFile", new[] { bad, FileRow("good.exe|2", @"c:\good.exe") });

        var am = AmcacheNew.FromDatabases(Folder);

        //as with a hive key, the entry is kept with what could be read
        am.UnassociatedFileEntries.Select(t => t.FullPath).Should().BeEquivalentTo(@"c:\bad.exe", @"c:\good.exe");
    }

    [Test]
    public void RandomDamage()
    {
        var random = new Random(26);
        var path = Database("InventoryApplicationFile", Enumerable.Range(0, 200).Select(t => FileRow($"f{t}.exe|{t}", $@"c:\f{t}.exe")));
        var original = File.ReadAllBytes(path);

        for (var round = 0; round < 100; round++)
        {
            var damaged = (byte[])original.Clone();
            for (var n = random.Next(1, 40); n > 0; n--)
            {
                damaged[random.Next(damaged.Length)] = (byte)random.Next(256);
            }

            File.WriteAllBytes(path, round % 10 == 0 ? damaged.Take(random.Next(damaged.Length)).ToArray() : damaged);

            FluentActions.Invoking(() => AmcacheNew.FromDatabases(Folder)).Should().NotThrow();
        }
    }

    private Dictionary<string, string> Hashes() =>
        Directory.GetFiles(Folder).ToDictionary(Path.GetFileName, t =>
        {
            using var stream = new FileStream(t, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return Convert.ToHexString(SHA256.HashData(stream));
        });
}
