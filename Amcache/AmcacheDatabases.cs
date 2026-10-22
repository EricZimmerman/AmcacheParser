using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Microsoft.Data.Sqlite;
using Serilog;

namespace Amcache;

public partial class AmcacheNew
{
    /// <summary>The databases read into the classes, with the hive's logic; every other one goes to OtherTables</summary>
    private static readonly string[] TypedDatabases =
    {
        "InventoryApplication", "InventoryApplicationFile", "InventoryApplicationShortcut", "InventoryDeviceContainer",
        "InventoryDevicePnp", "InventoryDriverBinary", "InventoryDriverPackage"
    };

    /// <summary>Whether a folder has the Inventory*.db databases that replace Amcache.hve's Root keys (Windows 11 26H1 on)</summary>
    public static bool HasDatabases(string directory) =>
        Directory.Exists(directory) && Directory.EnumerateFiles(directory, "Inventory*.db").Any();

    /// <summary>
    ///     Reads the Amcache databases in a folder (C:\Windows\AppCompat\Programs, next to Amcache.hve). Each database is
    ///     copied (with its -wal and -journal) to a new folder in the system's temporary folder and only the copy is
    ///     opened, so nothing is written next to the evidence. A row is read as the hive key of the same name (ItemKey)
    ///     with its columns as values, and LastModified as the key's last write time
    /// </summary>
    public static AmcacheNew FromDatabases(string directory) => FromDatabases(directory, null);

    /// <summary>
    ///     As <see cref="FromDatabases(string)" />, with the copies of the databases made in new folders in
    ///     <paramref name="tempDirectory" /> (which must exist; null: the system's temporary folder), each deleted when
    ///     the database has been read
    /// </summary>
    public static AmcacheNew FromDatabases(string directory, string tempDirectory)
    {
        var am = new AmcacheNew();
        var reporter = new DatabaseReporter();
        var notRead = 0;

        List<InventoryItem> Items(string name)
        {
            var path = Path.Combine(directory, name + ".db");
            if (File.Exists(path) == false)
            {
                Log.Debug("{Path} not found", path);
                return new List<InventoryItem>();
            }

            var table = ReadDatabase(path, am.OtherTables, true, tempDirectory, ref notRead);
            return table == null ? new List<InventoryItem>() : ToItems(table, name);
        }

        //programs first, so files can be associated with them
        am.AddPrograms(Items("InventoryApplication"), reporter);
        am.AddFiles(Items("InventoryApplicationFile"), reporter);
        am.AddShortcuts(Items("InventoryApplicationShortcut"), true);
        am.AddDeviceContainers(Items("InventoryDeviceContainer"), reporter);
        am.AddDevicePnps(Items("InventoryDevicePnp"), reporter);
        am.AddDriverBinaries(Items("InventoryDriverBinary"), reporter);
        am.AddDriverPackages(Items("InventoryDriverPackage"), reporter);

        foreach (var path in Directory.EnumerateFiles(directory, "*.db").OrderBy(t => t, StringComparer.OrdinalIgnoreCase))
        {
            var name = Path.GetFileNameWithoutExtension(path);
            if (TypedDatabases.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            var table = ReadDatabase(path, am.OtherTables, false, tempDirectory, ref notRead);
            if (table != null)
            {
                am.OtherTables.Add(table);
            }
        }

        reporter.Flush();

        am.EntriesNotParsed = reporter.Errors;
        am.DatabasesNotRead = notRead;

        return am;
    }

    /// <summary>
    ///     A database's table (the one that isn't Metadata_N), newest row per ItemKey. Its Metadata_N rows go to the
    ///     shared Metadata table in <paramref name="otherTables" />
    /// </summary>
    private static DatabaseTable ReadDatabase(string path, List<DatabaseTable> otherTables, bool typed, string tempDirectory, ref int notRead)
    {
        var fileName = Path.GetFileName(path);

        if (new FileInfo(path).Length == 0)
        {
            Log.Debug("{Database} is empty", fileName);
            return null;
        }

        try
        {
            using var db = SqliteCopy.Open(path, tempDirectory);

            var check = db.QuickCheck();
            if (check != "ok")
            {
                Log.Warning("{Database} is damaged ({Problem}). A row can appear more than once, from older copies of it; the newest is used",
                    fileName, check.Split('\n')[0]);
            }

            DatabaseTable table = null;

            foreach (var name in db.Tables())
            {
                var read = db.ReadTable(fileName, name);

                if (name.StartsWith("Metadata_", StringComparison.OrdinalIgnoreCase))
                {
                    AddMetadata(otherTables, read);
                    continue;
                }

                if (table != null)
                {
                    //not seen so far
                    Log.Warning("{Database} has more than one table; {Table} is listed with the other tables", fileName, name);
                    otherTables.Add(Newest(read));
                    continue;
                }

                table = Newest(read);
            }

            if (table == null)
            {
                Log.Warning("{Database} has no data table", fileName);
            }

            return table;
        }
        catch (Exception ex)
        {
            Log.Error("{Database} could not be read: {Message}", fileName, ex.Message);
            notRead += 1;
            return null;
        }
    }

    private static void AddMetadata(List<DatabaseTable> otherTables, DatabaseTable read)
    {
        var metadata = otherTables.FirstOrDefault(t => t.Name == "Metadata");
        if (metadata == null)
        {
            metadata = new DatabaseTable(string.Empty, "Metadata", new List<string> { "Database", "Name", "Value" });
            otherTables.Add(metadata);
        }

        var nameIndex = read.Columns.IndexOf("Name");
        var valueIndex = read.Columns.IndexOf("Value");
        foreach (var row in read.Rows)
        {
            metadata.Rows.Add(new[] { read.Database, nameIndex < 0 ? null : row[nameIndex], valueIndex < 0 ? null : row[valueIndex] });
        }
    }

    /// <summary>One row per ItemKey: the one with the latest LastModified (a damaged database can have older copies)</summary>
    private static DatabaseTable Newest(DatabaseTable table)
    {
        var keyIndex = table.Columns.IndexOf("ItemKey");
        var timeIndex = table.Columns.IndexOf("LastModified");
        if (keyIndex < 0 || timeIndex < 0)
        {
            return table;
        }

        var newest = new Dictionary<string, object[]>(StringComparer.Ordinal);
        var order = new List<string>();
        var older = 0;

        foreach (var row in table.Rows)
        {
            var key = row[keyIndex] as string ?? string.Empty;
            if (newest.TryGetValue(key, out var seen) == false)
            {
                newest.Add(key, row);
                order.Add(key);
                continue;
            }

            older += 1;
            if (AsLong(row[timeIndex]) > AsLong(seen[timeIndex]))
            {
                newest[key] = row;
            }
        }

        if (older == 0)
        {
            return table;
        }

        Log.Warning("{Database}: {Older:N0} older copies of rows were left out ({Rows:N0} rows, {Keys:N0} ItemKeys)", table.Database, older,
            table.Rows.Count, order.Count);

        var result = new DatabaseTable(table.Database, table.Name, table.Columns) { OlderCopies = older };
        result.Rows.AddRange(order.Select(t => newest[t]));
        return result;
    }

    private static long AsLong(object value) => value is long l ? l : 0;

    /// <summary>The rows as hive keys: ItemKey is the key name, LastModified the last write time, the other columns its values</summary>
    private static List<InventoryItem> ToItems(DatabaseTable table, string databaseName)
    {
        var keyIndex = table.Columns.IndexOf("ItemKey");
        var timeIndex = table.Columns.IndexOf("LastModified");

        var items = new List<InventoryItem>(table.Rows.Count);

        foreach (var row in table.Rows)
        {
            var keyName = keyIndex < 0 ? string.Empty : row[keyIndex]?.ToString() ?? string.Empty;
            var lastWrite = timeIndex < 0 ? DateTimeOffset.MinValue : FromFileTime(row[timeIndex]);

            var values = new List<InventoryValue>(row.Length);
            for (var i = 0; i < row.Length; i++)
            {
                if (i == keyIndex || i == timeIndex)
                {
                    continue;
                }

                var name = table.Columns[i];
                values.Add(new InventoryValue(name, AsRegistryData(databaseName, name, row[i]), false));
            }

            items.Add(new InventoryItem(keyName, $"{table.Database}: {keyName}", lastWrite, false, values));
        }

        return items;
    }

    /// <summary>A column's value as the registry value's data was: text as is, numbers in decimal</summary>
    internal static string AsRegistryData(string databaseName, string column, object value)
    {
        switch (value)
        {
            case null:
                return string.Empty;
            case string s:
                return s;
            case long l:
                //a DWORD in the hive (unsigned: Microsoft's reproducible build values are past 2038), signed here
                if (l < 0 && l >= int.MinValue && column == "DriverTimeStamp" &&
                    databaseName.Equals("InventoryDriverBinary", StringComparison.OrdinalIgnoreCase))
                {
                    return (l + 0x1_0000_0000L).ToString(CultureInfo.InvariantCulture);
                }

                return l.ToString(CultureInfo.InvariantCulture);
            case double d:
                return d.ToString("R", CultureInfo.InvariantCulture);
            case byte[] b:
                return BitConverter.ToString(b).Replace("-", string.Empty);
            default:
                return Convert.ToString(value, CultureInfo.InvariantCulture);
        }
    }

    /// <summary>LastModified (a FILETIME, UTC); 0 or out of range is DateTimeOffset.MinValue</summary>
    public static DateTimeOffset FromFileTime(object value)
    {
        if (value is long l && l > 0)
        {
            try
            {
                return DateTimeOffset.FromFileTime(l).ToUniversalTime();
            }
            catch (ArgumentOutOfRangeException)
            {
            }
        }

        return DateTimeOffset.MinValue;
    }

    /// <summary>Per database table instead of per row: a column the hive logic doesn't use is the same in every row</summary>
    private sealed class DatabaseReporter : IReporter
    {
        private const int MaxErrors = 10;

        private readonly Dictionary<string, SortedSet<string>> _notTracked = new();
        private readonly Dictionary<string, SortedSet<string>> _unknown = new();
        private readonly Dictionary<string, int> _errors = new();

        public int Errors { get; private set; }

        public void NotTracked(string category, InventoryItem item, InventoryValue value) =>
            Add(_notTracked, category, value.ValueName);

        public void Unknown(string category, InventoryItem item, InventoryValue value, bool onlyWhenNotDeleted) =>
            Add(_unknown, category, value.ValueName);

        public void Error(string category, InventoryItem item, Exception ex)
        {
            _errors.TryGetValue(category, out var count);
            _errors[category] = count + 1;
            Errors += 1;

            if (count < MaxErrors)
            {
                Log.Error("Error parsing {Category} at {KeyPath}. Error: {Message}", category, item.KeyPath, ex.Message);
                Log.Error("Row data: {Values}", string.Join(", ", item.Values.Select(t => $"{t.ValueName}={t.ValueData}")));
            }
        }

        public void Flush()
        {
            foreach (var pair in _notTracked)
            {
                Log.Debug("Columns not used for {Category}: {Columns}", pair.Key, string.Join(", ", pair.Value));
            }

            foreach (var pair in _unknown)
            {
                Log.Warning("Unknown columns for {Category}: {Columns}", pair.Key, string.Join(", ", pair.Value));
            }

            foreach (var pair in _errors.Where(t => t.Value > MaxErrors))
            {
                Log.Error("{Count:N0} {Category} rows could not be parsed ({Max} shown)", pair.Value, pair.Key, MaxErrors);
            }
        }

        private static void Add(Dictionary<string, SortedSet<string>> sets, string category, string name)
        {
            if (sets.TryGetValue(category, out var set) == false)
            {
                set = new SortedSet<string>(StringComparer.Ordinal);
                sets.Add(category, set);
            }

            set.Add(name);
        }
    }

    /// <summary>A database copied (with its -wal and -journal) to a new folder in a temporary folder, deleted when disposed</summary>
    private sealed class SqliteCopy : IDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly string _folder;

        private SqliteCopy(string folder, SqliteConnection connection)
        {
            _folder = folder;
            _connection = connection;
        }

        public static SqliteCopy Open(string path, string tempDirectory)
        {
            var folder = Path.Combine(tempDirectory ?? Path.GetTempPath(), "AmcacheParser-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);

            try
            {
                var copy = Path.Combine(folder, Path.GetFileName(path));
                foreach (var suffix in new[] { "", "-wal", "-journal" })
                {
                    if (File.Exists(path + suffix))
                    {
                        CopyShared(path + suffix, copy + suffix);
                    }
                }

                var connection = new SqliteConnection(new SqliteConnectionStringBuilder
                {
                    DataSource = copy,
                    Mode = SqliteOpenMode.ReadWrite,
                    Pooling = false
                }.ToString());
                connection.Open();

                return new SqliteCopy(folder, connection);
            }
            catch
            {
                TryDelete(folder);
                throw;
            }
        }

        public void Dispose()
        {
            _connection.Dispose();
            TryDelete(_folder);
        }

        /// <summary>Databases are in use on a live system</summary>
        private static void CopyShared(string source, string target)
        {
            using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write);
            input.CopyTo(output);
        }

        private static void TryDelete(string folder)
        {
            try
            {
                Directory.Delete(folder, true);
            }
            catch (Exception ex)
            {
                Log.Debug("Could not delete {Folder}: {Message}", folder, ex.Message);
            }
        }

        public string QuickCheck()
        {
            try
            {
                using var command = _connection.CreateCommand();
                command.CommandText = "PRAGMA quick_check";
                using var reader = command.ExecuteReader();
                var problems = new List<string>();
                while (reader.Read())
                {
                    problems.Add(reader.GetString(0));
                }

                return string.Join("\n", problems);
            }
            catch (SqliteException ex)
            {
                return ex.Message;
            }
        }

        public List<string> Tables()
        {
            using var command = _connection.CreateCommand();
            command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%' ORDER BY rowid";
            using var reader = command.ExecuteReader();
            var tables = new List<string>();
            while (reader.Read())
            {
                tables.Add(reader.GetString(0));
            }

            return tables;
        }

        /// <summary>Every row; a damaged database can fail part way, and the rows read until then are kept</summary>
        public DatabaseTable ReadTable(string database, string name)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = $"SELECT * FROM \"{name.Replace("\"", "\"\"")}\"";
            using var reader = command.ExecuteReader();

            var columns = Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToList();
            var table = new DatabaseTable(database, name, columns);

            try
            {
                while (reader.Read())
                {
                    var row = new object[reader.FieldCount];
                    for (var i = 0; i < row.Length; i++)
                    {
                        row[i] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                    }

                    table.Rows.Add(row);
                }
            }
            catch (SqliteException ex)
            {
                Log.Error("{Database}: reading {Table} stopped after {Count:N0} rows: {Message}", database, name, table.Rows.Count, ex.Message);
            }

            return table;
        }
    }
}

/// <summary>A database table as it is: its columns and rows (null, long, double, string or byte[])</summary>
public class DatabaseTable
{
    public DatabaseTable(string database, string name, List<string> columns)
    {
        Database = database;
        Name = name;
        Columns = columns;
        Rows = new List<object[]>();
    }

    /// <summary>The database's file name (empty for the Metadata table, which has a Database column)</summary>
    public string Database { get; }

    public string Name { get; }
    public List<string> Columns { get; }
    public List<object[]> Rows { get; }

    /// <summary>Rows left out because a newer copy of the same ItemKey was there (damaged databases)</summary>
    public int OlderCopies { get; set; }
}
