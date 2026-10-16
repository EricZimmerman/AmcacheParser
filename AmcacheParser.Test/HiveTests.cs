using System;
using System.Linq;
using Amcache;
using FluentAssertions;
using NUnit.Framework;

namespace AmcacheParser.Test;

/// <summary>The DFIR Artifact Museum's hives, through the library</summary>
[TestFixture]
public class HiveTests : TestData
{
    [Test]
    public void NewFormatWithLogs()
    {
        var hive = MuseumHive("Win10/APTSimulatorVM");

        Helper.IsNewFormat(hive, false).Should().BeTrue();
        var am = new AmcacheNew(hive, true, false);

        am.ProgramsEntries.Should().HaveCount(90);
        am.TotalFileEntries.Should().Be(124);
        am.UnassociatedFileEntries.Should().HaveCount(20);
        am.ProgramsEntries.Sum(t => t.FileEntries.Count).Should().Be(104);
        am.DeviceContainers.Should().HaveCount(12);
        am.DevicePnps.Should().HaveCount(189);
        am.HasOldFormatEntries.Should().BeFalse();
        am.OtherTables.Should().BeEmpty();

        //added by the logs
        var file = am.UnassociatedFileEntries.Single(t => t.FullPath.EndsWith("onedrivestandaloneupdater.exe"));
        file.SHA1.Should().Be("57b7fc9bf59cf09a9c19ad0ce0a159746554d682");
        file.Size.Should().Be(3060072);
        file.KeyName.Should().Be("onedrivestandalo|70daea7f83b4ffcb");
        file.FileKeyLastWriteTimestamp.Should().Be(new DateTimeOffset(2022, 3, 10, 19, 46, 36, TimeSpan.Zero).AddTicks(2565599));

        var program = am.ProgramsEntries.Single(t => t.ProgramId == "0000041ce3ebb98d8d6fe2ef3dfc806b03f300000904");
        program.Name.Should().Be("Microsoft.WindowsCamera");
        program.KeyName.Should().Be(program.ProgramId);
    }

    [Test]
    public void DirtyWithoutLogs()
    {
        var hive = MuseumHive("Win10/APTSimulatorVM", false);

        FluentActions.Invoking(() => new AmcacheNew(hive, true, false)).Should().Throw<Exception>().WithMessage("Sequence numbers do not match*");
        new AmcacheNew(hive, true, true).ProgramsEntries.Should().NotBeEmpty();
    }

    [Test]
    public void OldFormat()
    {
        var hive = MuseumHive("Win2012R2/StolenSzechuan");

        Helper.IsNewFormat(hive, false).Should().BeFalse();
        var am = new AmcacheOld(hive, true, false);

        am.TotalFileEntries.Should().Be(136);
        am.ProgramsEntries.Should().HaveCount(3);
        am.UnassociatedFileEntries.Should().HaveCount(2);
        am.UnassociatedFileEntries.Should().Contain(t => t.FullPath == @"C:\Program Files\Common Files\VMware\Drivers\vss\comreg.exe");
    }

    [Test]
    public void LogFiles()
    {
        var hive = MuseumHive("Win10/APTSimulatorVM");
        var dir = System.IO.Path.GetDirectoryName(hive)!;
        System.IO.File.Move(System.IO.Path.Combine(dir, "Amcache.hve.LOG2"), System.IO.Path.Combine(dir, "amcache.hve.log2"));
        System.IO.File.WriteAllBytes(System.IO.Path.Combine(dir, "Amcache.hve.LOG"), Array.Empty<byte>());
        System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "Amcache.hve.LOG3"), "x");

        //any case; .LOG too, but not empty ones
        Helper.LogFiles(dir, "Amcache.hve").Select(System.IO.Path.GetFileName).Should().Equal("Amcache.hve.LOG1", "amcache.hve.log2");
    }
}
