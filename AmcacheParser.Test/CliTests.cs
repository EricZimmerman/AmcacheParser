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
        //the user's temp folder: an empty one, which must stay empty (the copies go to --temp or Temp next to AmcacheParser)
        Directory.CreateDirectory(SystemTemp);
        foreach (var name in new[] { "TEMP", "TMP", "TMPDIR" })
        {
            start.Environment[name] = SystemTemp;
        }

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
        Directory.GetFileSystemEntries(SystemTemp).Should().BeEmpty("nothing goes to the user's temp folder");
        if (Directory.Exists(TempNextToProgram))
        {
            Directory.GetDirectories(TempNextToProgram, "AmcacheParser-*").Should().BeEmpty("the copies are deleted");
        }
        return (process.ExitCode, stdout.Result + stderr.Result);
    }

    private string Out => Path.Combine(Dir, "out");

    private string SystemTemp => Path.Combine(Dir, "systemp");

    private static string TempNextToProgram => Path.Combine(TestContext.CurrentContext.TestDirectory, "Temp");

    private string ProgramsFolder()
    {
        Database("InventoryApplicationFile", new[] { FileRow("a.exe|1", @"c:\a.exe") }, "Programs");
        return Path.Combine(Dir, "Programs");
    }

    [Test]
    public void CopiesGoToTempNextToTheProgram()
    {
        var existed = Directory.Exists(TempNextToProgram);

        var (code, output) = Run("-f", ProgramsFolder(), "--csv", Out);

        code.Should().Be(0, output);
        //the copies' folders are deleted, and Temp too when this run made it and it is empty
        if (Directory.Exists(TempNextToProgram))
        {
            existed.Should().BeTrue("Temp is removed when the run created it and it is empty");
            Directory.GetDirectories(TempNextToProgram, "AmcacheParser-*").Should().BeEmpty();
        }
    }

    [Test]
    public void CopiesGoToTheTempGiven()
    {
        var programs = ProgramsFolder();
        var temp = Directory.CreateDirectory(Path.Combine(Dir, "my temp")).FullName;

        var (code, output) = Run("-f", programs, "--csv", Out, "--temp", temp);

        code.Should().Be(0, output);
        Directory.Exists(temp).Should().BeTrue("a folder that was there is kept");
        Directory.GetFileSystemEntries(temp).Should().BeEmpty("the copies are deleted when done");

        //one that wasn't there is created, and removed again
        var newTemp = Path.Combine(Dir, "new temp");
        Run("-f", programs, "--csv", Out, "--temp", newTemp).ExitCode.Should().Be(0);
        Directory.Exists(newTemp).Should().BeFalse();
    }

    [Test]
    public void TempThatCantBeWrittenStops()
    {
        var programs = ProgramsFolder();
        //a folder under a file can't be created
        var file = Path.Combine(Dir, "a file");
        File.WriteAllText(file, "x");

        var (code, output) = Run("-f", programs, "--csv", Out, "--temp", Path.Combine(file, "temp"));

        code.Should().Be(1, output);
        output.Should().Contain("Give one with --temp");
        Directory.Exists(Out).Should().BeFalse("nothing was processed");

        //a hive alone needs no copies, so no temp folder
        Run("-f", MuseumHive("Win10/APTSimulatorVM"), "--csv", Out, "--temp", Path.Combine(file, "temp")).ExitCode.Should().Be(0);
    }

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
        //1 is for the tool failing; damaged files are listed, but exit with 0
        Run("-f", Directory.CreateDirectory(Path.Combine(Dir, "empty")).FullName, "--csv", Out).ExitCode.Should().Be(0, "no hive and no databases: nothing to do");
        var notADatabase = Directory.CreateDirectory(Path.Combine(Dir, "notadb")).FullName;
        File.WriteAllText(Path.Combine(notADatabase, "InventoryApplication.db"), "not a database at all, but long enough to look like one");
        var (code, output) = Run("-f", notADatabase, "--csv", Out);
        code.Should().Be(0, "a damaged database is not the tool's fault");
        output.Should().Contain("InventoryApplication.db could not be read");

        var random = Path.Combine(Dir, "random.hve");
        File.WriteAllBytes(random, Enumerable.Range(0, 8192).Select(t => (byte)(t * 7919 % 251)).ToArray());
        Run("-f", random, "--csv", Out).ExitCode.Should().Be(0, "a damaged hive is not the tool's fault");
    }

    [Test]
    public void OutputThatCantBeWrittenIsAnError()
    {
        //--csv is a file, so the folder can't be created
        var file = Path.Combine(Dir, "a file");
        File.WriteAllText(file, "x");

        var (code, output) = Run("-f", MuseumHive("Win10/APTSimulatorVM"), "--csv", file);
        code.Should().Be(1, output);

        (code, output) = Run("-f", ProgramsFolder(), "--csv", file);
        code.Should().Be(1, output);
    }

    [Test]
    public void DatabaseThatCantBeOpenedIsAnError()
    {
        if (OperatingSystem.IsWindows() == false)
        {
            Assert.Ignore("Locking a file for everyone else is Windows only");
        }

        var programs = ProgramsFolder();
        using (new FileStream(Path.Combine(programs, "InventoryApplicationFile.db"), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var (code, output) = Run("-f", programs, "--csv", Out);
            code.Should().Be(1, output);
            output.Should().Contain("InventoryApplicationFile.db could not be read");
        }
    }
}
