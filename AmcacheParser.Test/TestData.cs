using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Microsoft.Data.Sqlite;
using NUnit.Framework;

namespace AmcacheParser.Test;

/// <summary>A folder of its own per test, with Museum hives and databases made from the Windows 11 26H2 schemas</summary>
public abstract class TestData
{
    protected string Dir;

    [SetUp]
    public void SetUp()
    {
        Dir = Path.Combine(Path.GetTempPath(), "amcache-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Dir);
    }

    [TearDown]
    public void TearDown()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(Dir, true);
    }

    /// <summary>A DFIR Artifact Museum hive (Win10/APTSimulatorVM, Win2012R2/StolenSzechuan) and, with logs, its logs</summary>
    protected string MuseumHive(string name, bool logs = true, string folder = "hive")
    {
        var source = Path.Combine(TestContext.CurrentContext.TestDirectory, "TestFiles", "DFIRArtifactMuseum", name);
        var target = Path.Combine(Dir, folder);
        Directory.CreateDirectory(target);

        foreach (var file in logs ? new[] { "Amcache.hve", "Amcache.hve.LOG1", "Amcache.hve.LOG2" } : new[] { "Amcache.hve" })
        {
            using var gz = new GZipStream(File.OpenRead(Path.Combine(source, file + ".gz")), CompressionMode.Decompress);
            using var output = File.Create(Path.Combine(target, file));
            gz.CopyTo(output);
        }

        return Path.Combine(target, "Amcache.hve");
    }

    /// <summary>The tables as Windows 11 26H2 creates them (from a real system), with a Metadata table each</summary>
    public static readonly Dictionary<string, string> Schemas = new()
    {
        ["InventoryApplication"] = "CREATE TABLE InventoryApplication_0 (ItemKey TEXT PRIMARY KEY, \"ProgramId\" TEXT UNIQUE, \"ProgramInstanceId\" TEXT, \"Name\" TEXT, \"Version\" TEXT, \"Publisher\" TEXT, \"Language\" INTEGER, \"InstallDate\" TEXT, \"UserSid\" TEXT, \"Source\" TEXT, \"StoreAppType\" TEXT, \"PackageFullName\" TEXT, \"InboxModernApp\" INTEGER, \"ManifestPath\" TEXT, \"BundleManifestPath\" TEXT, \"HiddenArp\" INTEGER, \"RootDirPath\" TEXT, \"UninstallString\" TEXT, \"RegistryKeyPath\" TEXT, \"MsiPackageCode\" TEXT, \"MsiProductCode\" TEXT, \"MsiInstallDate\" TEXT, \"Type\" TEXT, \"LastModified\" INTEGER DEFAULT 0)",
        ["InventoryApplicationFile"] = "CREATE TABLE InventoryApplicationFile_0 (ItemKey TEXT PRIMARY KEY, \"ProgramId\" TEXT, \"FileId\" TEXT, \"LowerCaseLongPath\" TEXT UNIQUE, \"Name\" TEXT, \"OriginalFileName\" TEXT, \"Publisher\" TEXT, \"Version\" TEXT, \"BinFileVersion\" TEXT, \"BinaryType\" TEXT, \"ProductName\" TEXT, \"ProductVersion\" TEXT, \"LinkDate\" TEXT, \"BinProductVersion\" TEXT, \"AppxPackageFullName\" TEXT, \"AppxPackageRelativeId\" TEXT, \"Size\" INTEGER, \"Language\" INTEGER, \"IsPeFile\" INTEGER, \"IsOsComponent\" INTEGER, \"Sha256\" TEXT, \"Usn\" INTEGER, \"LastModified\" INTEGER DEFAULT 0)",
        ["InventoryApplicationShortcut"] = "CREATE TABLE InventoryApplicationShortcut_0 (ItemKey TEXT PRIMARY KEY, \"ShortcutPath\" TEXT, \"ShortcutTargetPath\" TEXT, \"ShortcutAumid\" TEXT, \"ShortcutProgramId\" TEXT, \"LastModified\" INTEGER DEFAULT 0)",
        ["InventoryDriverBinary"] = "CREATE TABLE InventoryDriverBinary_2 (ItemKey TEXT PRIMARY KEY, \"DriverName\" TEXT, \"Inf\" TEXT, \"DriverPackageStrongName\" TEXT, \"DriverCompany\" TEXT, \"DriverCheckSum\" INTEGER, \"DriverTimeStamp\" INTEGER, \"DriverType\" INTEGER, \"DriverInBox\" TEXT, \"DriverSigned\" TEXT, \"DriverIsKernelMode\" TEXT, \"DriverVersion\" TEXT, \"ImageSize\" INTEGER, \"Product\" TEXT, \"ProductVersion\" TEXT, \"WdfVersion\" TEXT, \"Service\" TEXT, \"DriverId\" TEXT, \"DriverLastWriteTime\" TEXT, \"LastModified\" INTEGER DEFAULT 0)",
        ["InventoryInstaller"] = "CREATE TABLE InventoryInstaller_0 (ItemKey TEXT PRIMARY KEY, \"InstallerId\" TEXT, \"InstallerName\" TEXT, \"InstallerVersion\" TEXT, \"InstallerPublisher\" TEXT, \"LastModified\" INTEGER DEFAULT 0)"
    };

    /// <summary>A database in <paramref name="folder" /> with the schema of <paramref name="name" /> and these rows</summary>
    protected string Database(string name, IEnumerable<Dictionary<string, object>> rows, string folder = "hive", string journalMode = null,
        bool keepOpen = false, string schema = null)
    {
        var target = Path.Combine(Dir, folder);
        Directory.CreateDirectory(target);
        var path = Path.Combine(target, name + ".db");

        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        connection.Open();

        void Execute(string sql, Dictionary<string, object> parameters = null)
        {
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            foreach (var pair in parameters ?? new Dictionary<string, object>())
            {
                command.Parameters.AddWithValue("$" + pair.Key, pair.Value ?? DBNull.Value);
            }

            command.ExecuteNonQuery();
        }

        if (journalMode != null)
        {
            Execute($"PRAGMA journal_mode={journalMode}");
        }

        schema ??= Schemas[name];
        var table = schema.Split(' ')[2];
        Execute(schema);
        Execute($"CREATE TABLE Metadata_{table.Split('_').Last()} (Name TEXT PRIMARY KEY, Value TEXT)");
        Execute($"INSERT INTO Metadata_{table.Split('_').Last()} VALUES ('lastFullSync', '134298448576863666'), ('OsVersion', '10.0.26100')");

        foreach (var row in rows)
        {
            Execute($"INSERT INTO {table} ({string.Join(", ", row.Keys.Select(t => $"\"{t}\""))}) VALUES ({string.Join(", ", row.Keys.Select(t => "$" + t))})", row);
        }

        if (keepOpen)
        {
            Kept.Add(connection);
        }
        else
        {
            connection.Dispose();
        }

        return path;
    }

    /// <summary>Connections left open (so a -wal stays), closed at the end of the test</summary>
    protected readonly List<SqliteConnection> Kept = new();

    [TearDown]
    public void CloseKept()
    {
        foreach (var connection in Kept)
        {
            connection.Dispose();
        }

        Kept.Clear();
    }

    /// <summary>2026-07-30 00:27:37.6863666 UTC</summary>
    public const long FileTime = 134298448576863666;

    public static Dictionary<string, object> FileRow(string key, string path, string programId = "", long lastModified = FileTime) => new()
    {
        ["ItemKey"] = key, ["ProgramId"] = programId, ["FileId"] = "0000" + "AB".PadRight(40, 'C'), ["LowerCaseLongPath"] = path,
        ["Name"] = Path.GetFileName(path), ["OriginalFileName"] = "orig.exe", ["Publisher"] = "pub", ["Version"] = "1.0", ["BinFileVersion"] = "1.0.0.0",
        ["BinaryType"] = "pe64_amd64", ["ProductName"] = "prod", ["ProductVersion"] = "1.0", ["LinkDate"] = "01/20/2026 04:09:36",
        ["BinProductVersion"] = "1.0.0.0", ["AppxPackageFullName"] = "", ["AppxPackageRelativeId"] = "", ["Size"] = 194768L, ["Language"] = 1033L,
        ["IsPeFile"] = 1L, ["IsOsComponent"] = 0L, ["Sha256"] = "", ["Usn"] = 209033863936L, ["LastModified"] = lastModified
    };

    public static Dictionary<string, object> ProgramRow(string programId, string name, string installDate = "05/19/2025 00:00:00") => new()
    {
        ["ItemKey"] = programId, ["ProgramId"] = programId, ["ProgramInstanceId"] = "0000da39", ["Name"] = name, ["Version"] = "2.0",
        ["Publisher"] = "pub", ["Language"] = 1033L, ["InstallDate"] = installDate, ["UserSid"] = "S-1-5-21-1-2-3-1001", ["Source"] = "Msi",
        ["StoreAppType"] = "", ["PackageFullName"] = "", ["InboxModernApp"] = 0L, ["ManifestPath"] = "", ["BundleManifestPath"] = "",
        ["HiddenArp"] = 1L, ["RootDirPath"] = @"C:\Program Files\App", ["UninstallString"] = "uninstall.exe", ["RegistryKeyPath"] = "",
        ["MsiPackageCode"] = "", ["MsiProductCode"] = "", ["MsiInstallDate"] = "05/19/2025 00:00:00", ["Type"] = "Application", ["LastModified"] = FileTime
    };
}
