using System;
using System.IO;
using System.Linq;
using Amcache;
using FluentAssertions;
using NUnit.Framework;

namespace AmcacheParser.Test;

/// <summary>
///     The local corpus (never copied into the repo): Corpus/AmcacheSQL and Corpus/Registry next to the repository, or
///     AMCACHE_CORPUS (the Corpus folder). Ignored when it isn't there
/// </summary>
[TestFixture]
[Explicit("needs the local corpus")]
public class CorpusTests
{
    private static string CorpusRoot()
    {
        var fromEnvironment = Environment.GetEnvironmentVariable("AMCACHE_CORPUS");
        if (string.IsNullOrEmpty(fromEnvironment) == false)
        {
            return fromEnvironment;
        }

        for (var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory); dir != null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "AmcacheParser.slnx")))
            {
                var corpus = Path.Combine(dir.Parent?.FullName ?? dir.FullName, "Corpus");
                return Directory.Exists(corpus) ? corpus : null;
            }
        }

        return null;
    }

    private static string Corpus(string name)
    {
        var path = CorpusRoot() == null ? null : Path.Combine(CorpusRoot(), name);
        if (path == null || (File.Exists(path) == false && Directory.Exists(path) == false))
        {
            Assert.Ignore($"{name} is not in the local corpus");
        }

        return path;
    }

    /// <summary>Windows 11 26H2 (Andrew Rathbun's desktop): its InventoryApplicationFile.db is a damaged copy</summary>
    [Test]
    public void Windows11Databases()
    {
        var am = AmcacheNew.FromDatabases(Corpus(Path.Combine("AmcacheSQL", "26H2Amcache_RathbunPersonalDesktop")));

        am.TotalFileEntries.Should().Be(16102);
        am.ProgramsEntries.Should().HaveCount(1408);
        am.ShortCuts.Should().HaveCount(777);
        am.DeviceContainers.Should().HaveCount(52);
        am.DevicePnps.Should().HaveCount(340);
        am.DriveBinaries.Should().HaveCount(516);
        am.DriverPackages.Should().HaveCount(67);
        am.OtherTables.Select(t => t.Name).Should().Contain(new[] { "Metadata", "InventoryInstaller_0", "InventoryInstallerApplicationFile_0", "Mare_0" });
    }

    /// <summary>Every Amcache hive (not the logs) under Registry</summary>
    [Test]
    public void Hives()
    {
        var root = Corpus("Registry");
        var hives = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(t => t.IndexOf("amcache", StringComparison.OrdinalIgnoreCase) >= 0 && IsHive(t)).ToList();
        hives.Should().NotBeEmpty();

        foreach (var hive in hives)
        {
            FluentActions.Invoking(() =>
            {
                if (Helper.IsNewFormat(hive, true))
                {
                    _ = new AmcacheNew(hive, true, true);
                }
                else
                {
                    _ = new AmcacheOld(hive, true, true);
                }
            }).Should().NotThrow(hive);
        }
    }

    private static bool IsHive(string file)
    {
        var header = new byte[32];
        using var fs = File.OpenRead(file);
        return fs.Read(header, 0, 32) == 32 && BitConverter.ToUInt32(header, 0) == 0x66676572 && BitConverter.ToUInt32(header, 28) == 0;
    }
}
