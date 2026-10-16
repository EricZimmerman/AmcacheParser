using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;

namespace AmcacheParser.Test;

/// <summary>The built AmcacheParser.dll, run as a separate process</summary>
[TestFixture]
public class CliTests : TestData
{
    private (int ExitCode, string Output) Run(params string[] args)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = Dir
        };
        start.ArgumentList.Add(Path.Combine(TestContext.CurrentContext.TestDirectory, "AmcacheParser.dll"));
        foreach (var arg in args)
        {
            start.ArgumentList.Add(arg);
        }

        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        process.StandardInput.Close();

        process.WaitForExit(300_000).Should().BeTrue("AmcacheParser should finish");
        return (process.ExitCode, stdout.Result + stderr.Result);
    }

    private string Out => Path.Combine(Dir, "out");

    private string[] Header(string file) => File.ReadLines(Path.Combine(Out, file)).First().Split(',');

    [Test]
    public void NewFormatHive()
    {
        var (code, output) = Run("-f", MuseumHive("Win10/APTSimulatorVM"), "--csv", Out, "--csvf", "a.csv", "-i");

        code.Should().Be(0, output);
        output.Should().Contain("Amcache data found: registry hive (Amcache.hve)").And.Contain("is in new format (registry hive");
        Directory.GetFiles(Out).Select(Path.GetFileName).Should().BeEquivalentTo("a_UnassociatedFileEntries.csv", "a_AssociatedFileEntries.csv",
            "a_ProgramEntries.csv", "a_ShortCuts.csv", "a_DriverBinaries.csv", "a_DeviceContainers.csv", "a_DriverPackages.csv", "a_DevicePnps.csv");

        //2026.5's columns in their order, then the new ones
        Header("a_UnassociatedFileEntries.csv").Should().Equal("ApplicationName", "ProgramId", "FileKeyLastWriteTimestamp", "SHA1", "IsOsComponent",
            "FullPath", "Name", "FileExtension", "LinkDate", "ProductName", "Size", "Version", "ProductVersion", "LongPathHash", "BinaryType", "IsPeFile",
            "BinFileVersion", "BinProductVersion", "Usn", "Language", "Description", "OriginalFileName", "Publisher", "Sha256", "KeyName",
            "AppxPackageFullName", "AppxPackageRelativeId");
        Header("a_ShortCuts.csv").Should().Equal("KeyName", "LnkName", "KeyLastWriteTimestamp", "ShortcutTargetPath", "ShortcutAumid", "ShortcutProgramId");
        Header("a_ProgramEntries.csv").Take(26).Last().Should().Be("Manufacturer");
        Header("a_ProgramEntries.csv").Skip(26).Should().Equal("KeyName", "UserSid", "MsiInstallDate");
        File.ReadLines(Path.Combine(Out, "a_UnassociatedFileEntries.csv")).Should().HaveCount(21);
    }

    [Test]
    public void OldFormatHive()
    {
        var (code, output) = Run("-f", MuseumHive("Win2012R2/StolenSzechuan"), "--csv", Out, "--csvf", "a.csv");

        code.Should().Be(0, output);
        output.Should().Contain("is in old format");
        File.ReadLines(Path.Combine(Out, "a_UnassociatedFileEntries.csv")).Should().HaveCount(3);
    }

    [Test]
    public void Databases()
    {
        Database("InventoryApplication", new[] { ProgramRow("0000aaaa", "App") }, "Programs");
        Database("InventoryApplicationFile", new[] { FileRow("a.exe|1", @"c:\a.exe", "0000aaaa"), FileRow("b.exe|2", @"c:\b.exe") }, "Programs");
        Database("InventoryInstaller", new[]
        {
            new Dictionary<string, object> { ["ItemKey"] = "1", ["InstallerName"] = "setup.exe", ["LastModified"] = FileTime }
        }, "Programs");

        //the folder, or one of the databases
        foreach (var f in new[] { Path.Combine(Dir, "Programs"), Path.Combine(Dir, "Programs", "InventoryInstaller.db") })
        {
            var (code, output) = Run("-f", f, "--csv", Out, "--csvf", "a.csv", "-i");

            code.Should().Be(0, output);
            output.Should().Contain("Amcache data found: SQLite databases (3 .db files)").And.Contain("has the new format SQLite databases");
            Directory.GetFiles(Out).Select(Path.GetFileName).Should().Contain(new[]
            {
                "a_Databases_UnassociatedFileEntries.csv", "a_Databases_AssociatedFileEntries.csv", "a_Databases_ProgramEntries.csv",
                "a_Databases_InventoryInstaller.csv", "a_Databases_Metadata.csv"
            });

            File.ReadAllLines(Path.Combine(Out, "a_Databases_InventoryInstaller.csv")).Should().Equal(
                "ItemKey,InstallerId,InstallerName,InstallerVersion,InstallerPublisher,LastModified", "1,,setup.exe,,,2026-07-30 00:27:37");
            //FILETIMEs in the metadata are times
            File.ReadAllLines(Path.Combine(Out, "a_Databases_Metadata.csv")).Should().Contain("InventoryApplication.db,lastFullSync,2026-07-30 00:27:37")
                .And.Contain("InventoryApplication.db,OsVersion,10.0.26100");
            File.ReadLines(Path.Combine(Out, "a_Databases_UnassociatedFileEntries.csv")).Should().HaveCount(2);

            Directory.Delete(Out, true);
        }
    }

    [Test]
    public void HiveAndDatabases()
    {
        var hive = MuseumHive("Win10/APTSimulatorVM", false, "Programs");
        Database("InventoryApplicationFile", new[] { FileRow("a.exe|1", @"c:\a.exe") }, "Programs");

        //the hive is dirty and its logs aren't there: an error, but the databases are still read
        var (code, output) = Run("-f", hive, "--csv", Out, "--csvf", "a.csv");
        code.Should().Be(1, output);
        output.Should().Contain("Amcache data found: registry hive (Amcache.hve) and SQLite databases (1 .db files)");
        File.Exists(Path.Combine(Out, "a_Databases_UnassociatedFileEntries.csv")).Should().BeTrue();

        Directory.Delete(Out, true);
        (code, output) = Run("-f", hive, "--csv", Out, "--csvf", "a.csv", "--nl");
        code.Should().Be(0, output);
        File.Exists(Path.Combine(Out, "a_UnassociatedFileEntries.csv")).Should().BeTrue();
        File.Exists(Path.Combine(Out, "a_Databases_UnassociatedFileEntries.csv")).Should().BeTrue();
    }

    [Test]
    public void ExitCodes()
    {
        Run("-f", Path.Combine(Dir, "missing"), "--csv", Out).ExitCode.Should().Be(1);
        Run("-f", MuseumHive("Win10/APTSimulatorVM")).ExitCode.Should().Be(1, "--csv is required");
        Run("--csv", Out).ExitCode.Should().Be(1, "-f is required");
        Run("-f", MuseumHive("Win10/APTSimulatorVM", false, "dirty"), "--csv", Out).ExitCode.Should().Be(1, "the hive is dirty and has no logs");
        Run("-f", Directory.CreateDirectory(Path.Combine(Dir, "empty")).FullName, "--csv", Out).ExitCode.Should().Be(1, "no hive and no databases");
    }
}
