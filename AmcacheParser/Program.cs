using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Help;
using System.CommandLine.Invocation;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading.Tasks;
using Amcache;
using Amcache.Classes;
using CsvHelper;
using CsvHelper.Configuration;
using CsvHelper.TypeConversion;
using Exceptionless;
using Serilog;
using Serilog.Core;
using Serilog.Events;


namespace AmcacheParser;

using Amcache.Converters;

internal class Program
{
    private static readonly string _preciseTimeFormat = "yyyy-MM-dd HH:mm:ss.fffffff";

    private static readonly string Header = $"AmcacheParser version {Assembly.GetExecutingAssembly().GetName().Version.ToString(3)}" +
                                            "\r\n\r\nAuthor: Eric Zimmerman (saericzimmerman@gmail.com)" +
                                            "\r\nhttps://github.com/EricZimmerman/AmcacheParser";

    private static readonly string  Footer = @"Examples: AmcacheParser.exe -f ""C:\Temp\amcache\AmcacheWin10.hve"" --csv C:\temp" +
                                             "\r\n\t " +
                                             @"   AmcacheParser.exe -f ""C:\Temp\amcache\AmcacheWin10.hve"" -i --csv C:\temp --csvf foo.csv" + "\r\n\t " +
                                             @"   AmcacheParser.exe -f ""C:\Temp\amcache\AmcacheWin10.hve"" -w ""c:\temp\whitelist.txt"" --csv C:\temp" + "\r\n\t " +
                                             @"   AmcacheParser.exe -f ""C:\Temp\Programs"" -i --csv C:\temp    (Amcache.hve and the Inventory*.db databases)" +
                                             "\r\n\t" +
                                             "\r\n\t" +
                                             "    Short options (single letter) are prefixed with a single dash. Long commands are prefixed with two dashes";
        
    private static RootCommand _rootCommand;
    
    private static Stopwatch _sw;

    private static string _activeDateTimeFormat;
    private static async Task<int> Main(string[] args)
    {
        ExceptionlessClient.Default.Startup("prIG996gFK1y6DaZEoXh3InSg8LwrHcQV4Dze2r8");

        var fOpt = new Option<string>("-f")
        {
            Description = "Amcache.hve to parse (Windows 7 to 11), the folder it's in (C:\\Windows\\AppCompat\\Programs), or one of the Inventory*.db SQLite databases there (Windows 11 26H1 and later). The databases next to a hive are read too"
        };
        
        var csvOpt = new Option<string>(
            "--csv")
        {
            Description = "Directory to save CSV formatted results to. Be sure to include the full path in double quotes"
        
        };
        
        var csvfOpt = new Option<string>(
            "--csvf")
        {
            Description   = "File name to save CSV formatted results to. When present, overrides default name\r\n"
        };
        
        var iOpt = new Option<bool>(
            "-i")
        {
            Description = "Include file entries for Programs entries",
            DefaultValueFactory = _ =>  false
        
        };
        
          
        var wOpt = new Option<string>(
            "-w")
        {
            Description = "Path to file containing SHA-1 hashes to *exclude* from the results. Blacklisting overrides whitelisting"
        
        };
        
        var bOpt = new Option<string>(
            "-b")
        {
            Description = "Path to file containing SHA-1 hashes to *include* from the results. Blacklisting overrides whitelisting"
        };
        
        var dtOpt = new Option<string>(
            "--dt"){
            Description =        "The custom date/time format to use when displaying time stamps. See https://goo.gl/CNVq0k for options",
            DefaultValueFactory = _ => "yyyy-MM-dd HH:mm:ss"
        };
        
        var mpOpt = new Option<bool>("--mp")
        {
            Description = "When true, display higher precision for timestamps",
            DefaultValueFactory = _ => false
        };
        
        var nlOpt = new Option<bool>("--nl")
        {
            Description = "When true, ignore transaction log files for dirty hives. Default is FALSE",
            DefaultValueFactory = _ => false
        };

        var debugOpt = new Option<bool>("--debug")
        {
            Description = "Show debug information during processing",
            DefaultValueFactory = _ => false
        };
        var traceOpt = new Option<bool>("--trace")
        {
            Description = "Show trace information during processing",
            DefaultValueFactory = _ => false
        };
        
        _rootCommand = new RootCommand
        {
            fOpt,
            iOpt,
            wOpt,
            bOpt,
            csvOpt,
            csvfOpt,
            dtOpt,
            mpOpt,
            nlOpt,
            debugOpt,
            traceOpt
        };
            
        _rootCommand.Description = Header + "\r\n\r\n" +Footer;

        _rootCommand.SetAction(result => DoWork(result.GetValue(fOpt), result.GetValue(iOpt), result.GetValue(wOpt),
            result.GetValue(bOpt), result.GetValue(csvOpt), result.GetValue(csvfOpt), result.GetValue(dtOpt),
            result.GetValue(mpOpt),result.GetValue(nlOpt),result.GetValue(debugOpt),result.GetValue(traceOpt)));
            

        var exitCode = await _rootCommand.Parse(args).InvokeAsync();

        Log.CloseAndFlush();

        return exitCode;
    }
    
    class DateTimeOffsetFormatter : IFormatProvider, ICustomFormatter
    {
        private readonly IFormatProvider _innerFormatProvider;

        public DateTimeOffsetFormatter(IFormatProvider innerFormatProvider)
        {
            _innerFormatProvider = innerFormatProvider;
        }

        public object GetFormat(Type formatType)
        {
            return formatType == typeof(ICustomFormatter) ? this : _innerFormatProvider.GetFormat(formatType);
        }

        public string Format(string format, object arg, IFormatProvider formatProvider)
        {
            if (arg is DateTimeOffset dateTimeOffset)
            {
                return dateTimeOffset.ToString(_activeDateTimeFormat);
            }

            if (arg is IFormattable formattable)
            {
                return formattable.ToString(format, _innerFormatProvider);
            }

            return arg.ToString();
        }
    }

    /// <returns>0, or 1 for bad arguments, a hive or database that could not be read, or no data</returns>
    private static int DoWork(string f,bool i,string w,string b, string csv,string csvf,string dt,bool mp,bool nl,bool debug,bool trace)
    {
        var levelSwitch = new LoggingLevelSwitch();

        _activeDateTimeFormat = dt;
        
        if (mp)
        {
            _activeDateTimeFormat = _preciseTimeFormat;
        }
        
        var formatter  =
            new DateTimeOffsetFormatter(CultureInfo.CurrentCulture);
        

        var template = "{Message:lj}{NewLine}{Exception}";

        if (debug)
        {
            levelSwitch.MinimumLevel = LogEventLevel.Debug;
            template = "[{Timestamp:HH:mm:ss.fff} {Level:u3}] {Message:lj}{NewLine}{Exception}";
        }

        if (trace)
        {
            levelSwitch.MinimumLevel = LogEventLevel.Verbose;
            template = "[{Timestamp:HH:mm:ss.fff} {Level:u3}] {Message:lj}{NewLine}{Exception}";
        }
        
        var conf = new LoggerConfiguration()
            .WriteTo.Console(outputTemplate: template,formatProvider: formatter)
            .MinimumLevel.ControlledBy(levelSwitch);
      
        Log.Logger = conf.CreateLogger();
        
        if (string.IsNullOrEmpty(f))
        {
            var aaa = new CustomHelpAction(new HelpAction());
            aaa.Invoke(_rootCommand.Parse("-f is required. Exiting"));

            return 1;
        }
        
        if (string.IsNullOrEmpty(csv))
        {
            var aaa = new CustomHelpAction(new HelpAction());
            aaa.Invoke(_rootCommand.Parse("--csv is required. Exiting"));

            return 1;
        }
        
        if (!File.Exists(f) && !Directory.Exists(f))
        {
            Log.Error("{F} not found. Exiting",f);
            Console.WriteLine();
            return 1;
        }

        Log.Information("{Header}",Header);
        Console.WriteLine();
        Log.Information("Command line: {Args}",string.Join(" ",Environment.GetCommandLineArgs().Skip(1)));
        Console.WriteLine();

        if (mp)
        {
            dt = _preciseTimeFormat;
        }

        if (IsAdministrator() == false)
        {
            Log.Warning("Warning: Administrator privileges not found!");
            Console.WriteLine();
        }

        _sw = new Stopwatch();
        _sw.Start();
        
        var ts1 = DateTime.Now.ToString("yyyyMMddHHmmss");

        f = Path.GetFullPath(f);

        //from Windows 11 26H1 on, Inventory*.db databases next to Amcache.hve have what was in its Root keys. -f can
        //also be that folder, or one of the databases
        var databaseDirectory = Directory.Exists(f) ? f : Path.GetDirectoryName(f);
        var hasDatabases = AmcacheNew.HasDatabases(databaseDirectory);
        var hiveGiven = File.Exists(f) && f.EndsWith(".db", StringComparison.OrdinalIgnoreCase) == false;
        var failed = false;

        //which kinds of data there are, before the details of each
        var found = new List<string>();
        if (hiveGiven)
        {
            found.Add($"registry hive ({Path.GetFileName(f)})");
        }

        if (hasDatabases)
        {
            found.Add($"SQLite databases ({Directory.GetFiles(databaseDirectory, "*.db").Length:N0} .db files)");
        }

        Log.Information("Amcache data found: {Found}",found.Count == 0 ? "none" : string.Join(" and ", found));
        Console.WriteLine();

        try
        {
            if (hiveGiven == false || hasDatabases || Helper.IsNewFormat(f, nl))
            {
                var whitelistHashes1 = new HashSet<string>();

                var useBlacklist2 = false;

                if (b?.Length > 0)
                {
                    if (File.Exists(b))
                    {
                        foreach (var readLine in File.ReadLines(b))
                        {
                            whitelistHashes1.Add(readLine.ToLowerInvariant());
                        }

                        useBlacklist2 = true;
                    }
                    else
                    {
                        Log.Warning("{B} does not exist",b);
                    }
                }
                else if (w?.Length > 0)
                {
                    if (File.Exists(w))
                    {
                        foreach (var readLine in File.ReadLines(w))
                        {
                            whitelistHashes1.Add(readLine.ToLowerInvariant());
                        }
                    }
                    else
                    {
                        Log.Warning("{W} does not exist",w);
                    }
                }

                var hiveName1 = hiveGiven ? Path.GetFileNameWithoutExtension(f) : "Amcache";

                if (hiveGiven)
                {
                    Log.Debug($"Processing new format hive");

                    AmcacheNew amNew = null;
                    try
                    {
                        amNew = new AmcacheNew(f,
                            true, nl);
                    }
                    catch (Exception ex) when (hasDatabases)
                    {
                        //the databases can still be read
                        Log.Error("{F} could not be read: {Message}",f,ex.Message);
                        failed = true;
                    }

                    if (amNew != null)
                    {
                        if (amNew.ProgramsEntries.Count == 0 && amNew.UnassociatedFileEntries.Count == 0)
                        {
                            Log.Warning("Hive did not contain program entries nor file entries");
                        }

                        if (WriteNewFormat(amNew, f, hiveName1, string.Empty, ts1, csv, csvf, dt, i, whitelistHashes1, useBlacklist2, b) == false)
                        {
                            return 1;
                        }

                        //issue 203 (Issues repo): hives upgraded from Windows 8 can have the old keys too
                        if (amNew.HasOldFormatEntries)
                        {
                            var amOld = new AmcacheOld(f, true, nl);
                            if (WriteOldFormat(amOld, f, hiveName1 + "_OldFormat", "_OldFormat", ts1, csv, csvf, dt, i, whitelistHashes1, useBlacklist2, b) == false)
                            {
                                return 1;
                            }
                        }
                    }
                }

                if (hasDatabases)
                {
                    Console.WriteLine();
                    Log.Information("Processing the Amcache databases in {Directory}",databaseDirectory);

                    var amDb = AmcacheNew.FromDatabases(databaseDirectory);

                    if (WriteNewFormat(amDb, databaseDirectory, hiveName1 + "_Databases", "_Databases", ts1, csv, csvf, dt, i, whitelistHashes1, useBlacklist2, b) == false)
                    {
                        return 1;
                    }

                    WriteDatabaseTables(amDb.OtherTables, hiveName1 + "_Databases", "_Databases", ts1, csv, csvf, dt);
                }
                else if (hiveGiven == false)
                {
                    Log.Warning("No Amcache databases (Inventory*.db) found in {Directory}",databaseDirectory);
                    failed = true;
                }

                _sw.Stop();

                Console.WriteLine();

                Log.Information("Results saved to: {Csv}",csv);


                Console.WriteLine();
                Log.Information(
                    "Total parsing time: {TotalSeconds:N3} seconds",_sw.Elapsed.TotalSeconds);
                Console.WriteLine();


                return failed ? 1 : 0;
            }

            Log.Debug($"Processing old format hive");

            var am = new AmcacheOld(f,
                true, nl);


            if (am.ProgramsEntries.Count == 0 && am.UnassociatedFileEntries.Count == 0)
            {
                Log.Warning("Hive did not contain program entries nor file entries. Exiting");
                Console.WriteLine();
                return 1;
            }

            _sw.Stop();

            var whitelistHashes = new HashSet<string>();

            var useBlacklist = false;

            if (b?.Length > 0)
            {
                if (File.Exists(b))
                {
                    foreach (var readLine in File.ReadLines(b))
                    {
                        whitelistHashes.Add(readLine.ToLowerInvariant());
                    }

                    useBlacklist = true;
                }
                else
                {
                    Log.Warning("{B} does not exist",b);
                }
            }
            else if (w?.Length > 0)
            {
                if (File.Exists(w))
                {
                    foreach (var readLine in File.ReadLines(w))
                    {
                        whitelistHashes.Add(readLine.ToLowerInvariant());
                    }
                }
                else
                {
                    Log.Warning("{W} does not exist",w);
                }
            }

            if (WriteOldFormat(am, f, Path.GetFileNameWithoutExtension(f), string.Empty, ts1, csv, csvf, dt, i, whitelistHashes, useBlacklist, b) == false)
            {
                return 1;
            }

            Console.WriteLine();

            Log.Information("Results saved to: {Csv}",csv);


            Console.WriteLine();
            Log.Information("Total parsing time: {TotalSeconds:N3} seconds",_sw.Elapsed.TotalSeconds);
            Console.WriteLine();
        }
        catch (Exception ex)
        {
            if (!ex.Message.Contains(
                    "Sequence numbers do not match and transaction logs were not found in the same directory as the hive. Abort")
               )
            {
                if (ex.Message.Contains("Administrator privileges not found"))
                {
                    Log.Fatal("Could not access {F} because it is in use",f);
                    Console.WriteLine();
                    Log.Fatal("Rerun the program with Administrator privileges to try again");
                    Console.WriteLine();
                }
                else
                {
                    Log.Error(ex,"There was an error: {Message}",ex.Message);
                    Console.WriteLine();
                    Log.Error("Please send {F} to {Email} in order to fix the issue",f,"saericzimmerman@gmail.com");                        
                }

            }

            return 1;
        }

        return 0;
    }
    
    /// <summary>
    ///     One CSV per database table that has no class (and one for the Metadata tables of all of them), with the
    ///     table's columns as they are. LastModified (a FILETIME) is written as a time, BLOBs as hex
    /// </summary>
    private static void WriteDatabaseTables(List<DatabaseTable> tables, string hiveName1, string csvfSuffix, string ts1, string csv, string csvf, string dt)
    {
        foreach (var table in tables)
        {
            //InventoryMiscellaneousUser_0 is InventoryMiscellaneousUser
            var name = Regex.Replace(table.Name, @"_\d+$", string.Empty);

            var outbase = $"{ts1}_{hiveName1}_{name}.csv";
            if (string.IsNullOrEmpty(csvf) == false)
            {
                outbase = $"{Path.GetFileNameWithoutExtension(csvf)}{csvfSuffix}_{name}{Path.GetExtension(csvf)}";
            }

            var timeIndex = table.Columns.IndexOf("LastModified");
            var isMetadata = table.Name == "Metadata";

            using var sw = new StreamWriter(Path.Combine(csv, outbase));
            using var csvWriter = new CsvWriter(sw, CultureInfo.InvariantCulture);

            foreach (var column in table.Columns)
            {
                csvWriter.WriteField(column);
            }

            csvWriter.NextRecord();

            foreach (var row in table.Rows)
            {
                for (var n = 0; n < row.Length; n++)
                {
                    //Metadata: lastFullSync, LastScanTime, ProgramIdListEventSentTime are FILETIMEs (as text)
                    if (isMetadata && n == 2 && row[1] is string metadataName && IsFileTimeName(metadataName) &&
                        long.TryParse(row[2] as string, NumberStyles.None, CultureInfo.InvariantCulture, out var fileTime) && IsPlausibleFileTime(fileTime))
                    {
                        csvWriter.WriteField(AmcacheNew.FromFileTime(fileTime).ToString(dt));
                        continue;
                    }

                    if (n == timeIndex && row[n] is long)
                    {
                        var time = AmcacheNew.FromFileTime(row[n]);
                        csvWriter.WriteField(time == DateTimeOffset.MinValue ? string.Empty : time.ToString(dt));
                        continue;
                    }

                    csvWriter.WriteField(row[n] switch
                    {
                        null => string.Empty,
                        byte[] bytes => BitConverter.ToString(bytes).Replace("-", string.Empty),
                        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
                        _ => row[n].ToString()
                    });
                }

                csvWriter.NextRecord();
            }

            Log.Information("{Name}: {Count:N0} rows",name,table.Rows.Count);
        }
    }

    /// <summary>The columns in this order (2026.5's first, new ones at the end); members not named are left out</summary>
    private static void Order<T>(ClassMap<T> map, params string[] columns)
    {
        foreach (var memberMap in map.MemberMaps)
        {
            var index = Array.IndexOf(columns, memberMap.Data.Member.Name);
            if (index < 0)
            {
                memberMap.Ignore();
            }
            else
            {
                //Publisher, DeviceState and ClassGuid were left out before
                memberMap.Data.Ignore = false;
                memberMap.Index(index);
            }
        }
    }

    private static bool IsFileTimeName(string name) =>
        name.IndexOf("time", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("sync", StringComparison.OrdinalIgnoreCase) >= 0;

    /// <summary>1990 to 2100 as a FILETIME</summary>
    private static bool IsPlausibleFileTime(long value) => value >= 0x01B41E2A18D64000 && value <= 0x022F716377640000;

    /// <summary>The CSVs for the old format, from an old hive or (with <paramref name="csvfSuffix" /> _OldFormat) the old keys of a new one</summary>
    private static bool WriteOldFormat(AmcacheOld am, string source, string hiveName, string csvfSuffix, string ts1, string csv, string csvf, string dt, bool i, HashSet<string> whitelistHashes, bool useBlacklist, string b)
    {
        var cleanList =
            am.UnassociatedFileEntries.Where(t => whitelistHashes.Contains(t.SHA1) == useBlacklist).ToList();
        var totalProgramFileEntries = 0;

        if (Directory.Exists(csv) == false)
        {
            try
            {
                Directory.CreateDirectory(csv);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "There was an error creating directory {Csv}. Error: {Message} Exiting",csv,ex.Message);
                Console.WriteLine();
                return false;
            }
        }


        foreach (var pe in am.ProgramsEntries)
        {
            var cleanList2 =
                pe.FileEntries.Where(t => whitelistHashes.Contains(t.SHA1) == useBlacklist).ToList();
            totalProgramFileEntries += cleanList2.Count;
        }

        //var ts = DateTime.Now.ToString("yyyyMMddHHmmss");

        var outbase = $"{ts1}_{hiveName}_UnassociatedFileEntries.csv";

        if (string.IsNullOrEmpty(csvf) == false)
        {
            outbase =
                $"{Path.GetFileNameWithoutExtension(csvf)}{csvfSuffix}_UnassociatedFileEntries{Path.GetExtension(csvf)}";
        }

        var outFile = Path.Combine(csv, outbase);

        using (var sw = new StreamWriter(outFile))
        {
            sw.AutoFlush = true;

            var csvWriter = new CsvWriter(sw,CultureInfo.InvariantCulture);

            var o = new TypeConverterOptions
            {
                DateTimeStyle = DateTimeStyles.AssumeUniversal & DateTimeStyles.AdjustToUniversal
            };
            csvWriter.Context.TypeConverterOptionsCache.AddOptions<FileEntryOld>(o);

            var foo = csvWriter.Context.AutoMap<FileEntryOld>();

            foo.Map(m => m.ProgramName).Index(0);
            foo.Map(m => m.ProgramID).Index(1);
            foo.Map(m => m.VolumeID).Index(2);
            foo.Map(t => t.VolumeIDLastWriteTimestamp).Convert(t =>
                t.Value.VolumeIDLastWriteTimestamp.ToString(dt)).Index(3);
            foo.Map(m => m.FileID).Index(4);
            foo.Map(t => t.FileIDLastWriteTimestamp).Convert(t =>
                t.Value.FileIDLastWriteTimestamp.ToString(dt)).Index(5);
            foo.Map(m => m.SHA1).Index(6);
            foo.Map(m => m.FullPath).Index(7);
            foo.Map(m => m.FileExtension).Index(8);
            foo.Map(m => m.MFTEntryNumber).Index(9);
            foo.Map(m => m.MFTSequenceNumber).Index(10);
            foo.Map(m => m.FileSize).TypeConverter<CustomNullTypeConverter<string>>().Index(11);
            foo.Map(m => m.FileVersionString).Index(12);
            foo.Map(m => m.FileVersionNumber).Index(13);
            foo.Map(m => m.FileDescription).Index(14);

            foo.Map(m => m.SizeOfImage).TypeConverter<CustomNullTypeConverter<string>>().Index(15);
            foo.Map(m => m.PEHeaderHash).Index(16);
            foo.Map(m => m.PEHeaderChecksum).TypeConverter<CustomNullTypeConverter<string>>().Index(17);

            foo.Map(m => m.BinProductVersion).Index(18);
            foo.Map(m => m.BinFileVersion).Index(19);
            foo.Map(m => m.LinkerVersion).Index(20);
            foo.Map(m => m.BinaryType).Index(21);
            foo.Map(m => m.IsLocal).Index(22);
            foo.Map(m => m.GuessProgramID).Index(23);

            foo.Map(t => t.Created)
                .Convert(t => t.Value.Created == null ? string.Empty : t.Value.Created?.ToString(dt))
                .Index(24);
            foo.Map(t => t.LastModified).Convert(t =>
                t.Value.LastModified == null ? string.Empty : t.Value.LastModified?.ToString(dt)).Index(25);
            foo.Map(t => t.LastModifiedStore).Convert(t =>
                t.Value.LastModifiedStore == null ? string.Empty : t.Value.LastModifiedStore?.ToString(dt)).Index(26);
            foo.Map(t => t.LinkDate)
                .Convert(t => t.Value.LinkDate == null ? string.Empty : t.Value.LinkDate?.ToString(dt))
                .Index(27);
            foo.Map(m => m.LanguageID).TypeConverter<CustomNullTypeConverter<string>>().Index(28);

            foo.Map(m => m.ProductName).TypeConverter<CustomNullTypeConverter<string>>().Index(29);
            foo.Map(m => m.CompanyName).TypeConverter<CustomNullTypeConverter<string>>().Index(30);
            foo.Map(m => m.SwitchBackContext).TypeConverter<CustomNullTypeConverter<string>>().Index(31);

            csvWriter.Context.RegisterClassMap(foo);

           

            csvWriter.WriteHeader<FileEntryOld>();
            csvWriter.NextRecord();
            csvWriter.WriteRecords(cleanList);
        }

        if (i)
        {
            outbase = $"{ts1}_{hiveName}_ProgramEntries.csv";

            if (string.IsNullOrEmpty(csvf) == false)
            {
                outbase =
                    $"{Path.GetFileNameWithoutExtension(csvf)}{csvfSuffix}_ProgramEntries{Path.GetExtension(csvf)}";
            }

            outFile = Path.Combine(csv, outbase);

            using (var sw = new StreamWriter(outFile))
            {
                sw.AutoFlush = true;

                var csvWriter = new CsvWriter(sw,CultureInfo.InvariantCulture);

                var o = new TypeConverterOptions
                {
                    DateTimeStyle = DateTimeStyles.AssumeUniversal & DateTimeStyles.AdjustToUniversal
                };
                csvWriter.Context.TypeConverterOptionsCache.AddOptions<ProgramsEntryOld>(o);

                var foo = csvWriter.Context.AutoMap<ProgramsEntryOld>();

                foo.Map(m => m.ProgramID).Index(0);
                foo.Map(t => t.LastWriteTimestamp).Convert(t =>
                    t.Value.LastWriteTimestamp.ToString(dt)).Index(1);
                foo.Map(m => m.ProgramName_0).Index(2);
                foo.Map(m => m.ProgramVersion_1).Index(3);
                foo.Map(m => m.VendorName_2).Index(4);

                foo.Map(t => t.InstallDateEpoch_a).Convert(t =>
                    t.Value.InstallDateEpoch_a?.ToString(dt)).Index(5);
                foo.Map(t => t.InstallDateEpoch_b).Convert(t =>
                    t.Value.InstallDateEpoch_b?.ToString(dt)).Index(6);

                foo.Map(m => m.LanguageCode_3).Index(7);
                foo.Map(m => m.InstallSource_6).Index(8);
                foo.Map(m => m.UninstallRegistryKey_7).Index(9);
                foo.Map(m => m.PathsList_d).Index(10);

                csvWriter.Context.RegisterClassMap(foo);

      

                csvWriter.WriteHeader<ProgramsEntryOld>();
                csvWriter.NextRecord();
                csvWriter.WriteRecords(am.ProgramsEntries);
            }

            outbase = $"{ts1}_{hiveName}_AssociatedFileEntries.csv";

            if (string.IsNullOrEmpty(csvf) == false)
            {
                outbase =
                    $"{Path.GetFileNameWithoutExtension(csvf)}{csvfSuffix}_AssociatedFileEntries{Path.GetExtension(csvf)}";
            }

            outFile = Path.Combine(csv, outbase);

            using (var sw = new StreamWriter(outFile))
            {
                var csvWriter = new CsvWriter(sw,CultureInfo.InvariantCulture);

                var o = new TypeConverterOptions
                {
                    DateTimeStyle = DateTimeStyles.AssumeUniversal & DateTimeStyles.AdjustToUniversal
                };
                csvWriter.Context.TypeConverterOptionsCache.AddOptions<FileEntryOld>(o);

                var foo = csvWriter.Context.AutoMap<FileEntryOld>();

                foo.Map(m => m.ProgramName).Index(0);
                foo.Map(m => m.ProgramID).Index(1);
                foo.Map(m => m.VolumeID).Index(2);
                foo.Map(t => t.VolumeIDLastWriteTimestamp).Convert(t =>
                        t.Value.VolumeIDLastWriteTimestamp.ToString(dt))
                    .Index(3);
                foo.Map(m => m.FileID).Index(4);
                foo.Map(t => t.FileIDLastWriteTimestamp).Convert(t =>
                        t.Value.FileIDLastWriteTimestamp.ToString(dt))
                    .Index(5);
                foo.Map(m => m.SHA1).Index(6);
                foo.Map(m => m.FullPath).Index(7);
                foo.Map(m => m.FileExtension).Index(8);
                foo.Map(m => m.MFTEntryNumber).Index(9);
                foo.Map(m => m.MFTSequenceNumber).Index(10);
                foo.Map(m => m.FileSize).Index(11);
                foo.Map(m => m.FileVersionString).Index(12);
                foo.Map(m => m.FileVersionNumber).Index(13);
                foo.Map(m => m.FileDescription).Index(14);

                foo.Map(m => m.SizeOfImage).Index(15);
                foo.Map(m => m.PEHeaderHash).Index(16);
                foo.Map(m => m.PEHeaderChecksum).Index(17);

                foo.Map(m => m.BinProductVersion).Index(18);
                foo.Map(m => m.BinFileVersion).Index(19);
                foo.Map(m => m.LinkerVersion).Index(20);
                foo.Map(m => m.BinaryType).Index(21);
                foo.Map(m => m.IsLocal).Index(22);
                foo.Map(m => m.GuessProgramID).Index(23);

                foo.Map(t => t.Created)
                    .Convert(t => t.Value.Created?.ToString(dt))
                    .Index(24);
                foo.Map(t => t.LastModified).Convert(t =>
                    t.Value.LastModified?.ToString(dt)).Index(25);
                foo.Map(t => t.LastModifiedStore).Convert(t =>
                    t.Value.LastModifiedStore?.ToString(dt)).Index(26);
                foo.Map(t => t.LinkDate)
                    .Convert(t => t.Value.LinkDate?.ToString(dt))
                    .Index(27);
                foo.Map(m => m.LanguageID).Index(28);


                csvWriter.Context.RegisterClassMap(foo);


                csvWriter.WriteHeader<FileEntryOld>();
                csvWriter.NextRecord();

                sw.AutoFlush = true;

                foreach (var pe in am.ProgramsEntries)
                {
                    var cleanList2 =
                        pe.FileEntries.Where(t => whitelistHashes.Contains(t.SHA1) == useBlacklist).ToList();

                    csvWriter.WriteRecords(cleanList2);
                }
            }
        }

        Console.WriteLine();
        if (csvfSuffix.Length == 0)
        {
            Log.Information("{F} is in old format (registry hive, Root\\File and Root\\Programs keys)",source);
        }
        else
        {
            Log.Information("{F} also has old format entries (Root\\File and Root\\Programs keys), as hives upgraded from Windows 8 can",source);
        }

        Console.WriteLine();

        Log.Information("Total file entries found: {TotalFileEntries:N0}",am.TotalFileEntries);

        if (am.UnassociatedFileEntries.Count == 1)
        {
            if (i)
            {
                Log.Information("Found {Count:N0} unassociated file entries and {TotalProgramFileEntries:N0} program file entries (across {ProgramsEntriesCount:N0} program entries)",cleanList.Count,totalProgramFileEntries,am.ProgramsEntries.Count);
            }
            else
            {
                Log.Information("Found {Count:N0} unassociated file entry",cleanList.Count);    
            }
            
        }
        else
        {
            if (i)
            {
                Log.Information("Found {Count:N0} unassociated file entries and {TotalProgramFileEntries:N0} program file entries (across {ProgramsEntriesCount:N0} program entries)",cleanList.Count,totalProgramFileEntries,am.ProgramsEntries.Count);
            }
            else
            {
                Log.Information("Found {Count:N0} unassociated file entries",cleanList.Count);
            }
                
        }

        if (whitelistHashes.Count > 0)
        {
            var per = (double) (totalProgramFileEntries + cleanList.Count) / am.TotalFileEntries;

            Console.WriteLine();

            var list = "whitelist";
            if (b?.Length > 0)
            {
                list = "blacklist";
            }

            Log.Information("{List} hash count: {Count:N0}",UppercaseFirst(list),whitelistHashes.Count);

            Console.WriteLine();

            Log.Information("Percentage of total shown based on {List}: {Per:P3} ({Per2:P3} savings)",list,per,1-per);
        }

        return true;
    }

    /// <summary>The CSVs for the new format, from a hive or (with <paramref name="csvfSuffix" /> _Databases) from the databases</summary>
    private static bool WriteNewFormat(AmcacheNew amNew, string source, string hiveName1, string csvfSuffix, string ts1, string csv, string csvf, string dt, bool i, HashSet<string> whitelistHashes1, bool useBlacklist2, string b)
    {
        var cleanList2 =
            amNew.UnassociatedFileEntries.Where(t => whitelistHashes1.Contains(t.SHA1) == useBlacklist2)
                .ToList();
        var totalProgramFileEntries2 = 0;

        if (Directory.Exists(csv) == false)
        {
            try
            {
                Directory.CreateDirectory(csv);
            }
            catch (Exception ex)
            {
                Log.Error(ex,"There was an error creating directory {Csv}. Error: {Message} Exiting",csv,ex.Message);
                Console.WriteLine();
                return false;
            }
        }

        foreach (var pe in amNew.ProgramsEntries)
        {
            var cleanList22 =
                pe.FileEntries.Where(t => whitelistHashes1.Contains(t.SHA1) == useBlacklist2).ToList();
            totalProgramFileEntries2 += cleanList22.Count;
        }

        
        var outbase1 = $"{ts1}_{hiveName1}_UnassociatedFileEntries.csv";

        if (string.IsNullOrEmpty(csvf) == false)
        {
            outbase1 =
                $"{Path.GetFileNameWithoutExtension(csvf)}{csvfSuffix}_UnassociatedFileEntries{Path.GetExtension(csvf)}";
        }

        var outFile1 = Path.Combine(csv, outbase1);

        using (var sw = new StreamWriter(outFile1))
        {
            sw.AutoFlush = true;

            var csvWriter = new CsvWriter(sw,CultureInfo.InvariantCulture);

            var o = new TypeConverterOptions
            {
                DateTimeStyle = DateTimeStyles.AssumeUniversal & DateTimeStyles.AdjustToUniversal
            };
            csvWriter.Context.TypeConverterOptionsCache.AddOptions<FileEntryNew>(o);

            var foo = csvWriter.Context.AutoMap<FileEntryNew>();

            foo.Map(m => m.ApplicationName).Index(0);

            foo.Map(m => m.ProgramId).Index(1);
            foo.Map(t => t.FileKeyLastWriteTimestamp).Convert(t =>
                    t.Value.FileKeyLastWriteTimestamp.ToString(dt))
                .Index(2);
            foo.Map(m => m.SHA1).Index(3);
            foo.Map(m => m.IsOsComponent).Index(4);
            foo.Map(m => m.FullPath).Index(5);
            foo.Map(m => m.Name).Index(6);
            foo.Map(m => m.FileExtension).Index(7);

            foo.Map(t => t.LinkDate).Convert(
                t => t.Value.LinkDate == null ? string.Empty : t.Value.LinkDate?.ToString(dt)).Index(8);
            foo.Map(m => m.ProductName).TypeConverter<CustomNullTypeConverter<string>>().Index(9);

            foo.Map(m => m.Size).Index(10);

            foo.Map(m => m.Version).Index(11);
            foo.Map(m => m.ProductVersion).Index(12);

            foo.Map(m => m.LongPathHash).Index(13);

            foo.Map(m => m.BinaryType).Index(14);
            foo.Map(m => m.IsPeFile).Index(15);

            foo.Map(m => m.BinFileVersion).Index(16);
            foo.Map(m => m.BinProductVersion).Index(17);
                

            foo.Map(m => m.Usn).Index(18);
            foo.Map(m => m.Language).Index(19);
            foo.Map(m => m.Description).Index(20);
            foo.Map(m => m.OriginalFileName).Index(21);
            //new columns go at the end
            foo.Map(m => m.Sha256).Index(22);
            foo.Map(m => m.Publisher).Ignore();

            csvWriter.Context.RegisterClassMap(foo);
                
            //the columns of 2026.5 in their order, new ones at the end
            Order(foo, "ApplicationName", "ProgramId", "FileKeyLastWriteTimestamp", "SHA1", "IsOsComponent", "FullPath", "Name", "FileExtension", "LinkDate", "ProductName", "Size", "Version", "ProductVersion", "LongPathHash", "BinaryType", "IsPeFile", "BinFileVersion", "BinProductVersion", "Usn", "Language", "Description", "OriginalFileName", "Publisher", "Sha256", "KeyName", "AppxPackageFullName", "AppxPackageRelativeId");
            csvWriter.WriteHeader<FileEntryNew>();
            csvWriter.NextRecord();
            csvWriter.WriteRecords(cleanList2);
        }

        if (i)
        {
            outbase1 = $"{ts1}_{hiveName1}_ProgramEntries.csv";

            if (string.IsNullOrEmpty(csvf) == false)
            {
                outbase1 =
                    $"{Path.GetFileNameWithoutExtension(csvf)}{csvfSuffix}_ProgramEntries{Path.GetExtension(csvf)}";
            }

            outFile1 = Path.Combine(csv, outbase1);

            using (var sw = new StreamWriter(outFile1))
            {
                sw.AutoFlush = true;

                var csvWriter = new CsvWriter(sw,CultureInfo.InvariantCulture);

                var o = new TypeConverterOptions
                {
                    DateTimeStyle = DateTimeStyles.AssumeUniversal & DateTimeStyles.AdjustToUniversal
                };
                csvWriter.Context.TypeConverterOptionsCache.AddOptions<ProgramsEntryNew>(o);

                var foo = csvWriter.Context.AutoMap<ProgramsEntryNew>();

                foo.Map(m => m.ProgramId).Index(0);
                foo.Map(t => t.KeyLastWriteTimestamp).Convert(t =>
                        t.Value.KeyLastWriteTimestamp.ToString(dt))
                    .Index(1);
                foo.Map(m => m.Name).Index(2);
                foo.Map(m => m.Version).Index(3);
                foo.Map(m => m.Publisher).Index(4);

                foo.Map(t => t.InstallDate).Convert(row =>
                {
                    if (row.Value.InstallDate.HasValue)
                    {
                        return row.Value.InstallDate.Value.ToString(
                            dt);
                    }

                    return "";
                }).Index(5);

                foo.Map(m => m.OSVersionAtInstallTime).Index(6);

                foo.Map(m => m.BundleManifestPath).Index(7);
                foo.Map(m => m.HiddenArp).Index(8);
                foo.Map(m => m.InboxModernApp).Index(9);
                foo.Map(m => m.Language).Index(10);
                foo.Map(m => m.ManifestPath).Index(11);
                foo.Map(m => m.MsiPackageCode).Index(12);
                foo.Map(m => m.MsiProductCode).Index(13);

                foo.Map(m => m.PackageFullName).Index(14);
                foo.Map(m => m.ProgramInstanceId).Index(15);
                foo.Map(m => m.RegistryKeyPath).Index(16);
                foo.Map(m => m.RootDirPath).Index(17);

                foo.Map(m => m.Type).Index(18);
                foo.Map(m => m.Source).Index(19);
                foo.Map(m => m.StoreAppType).Index(20);

                foo.Map(m => m.UninstallString).Index(21);
                foo.Map(t => t.MsiInstallDate).Convert(row => row.Value.MsiInstallDate?.ToString(dt) ?? string.Empty);

                csvWriter.Context.RegisterClassMap(foo);
                    
                //the columns of 2026.5 in their order, new ones at the end
                Order(foo, "ProgramId", "KeyLastWriteTimestamp", "Name", "Version", "Publisher", "InstallDateArpLastModified", "InstallDate", "InstallDateMsi", "OSVersionAtInstallTime", "InstallDateFromLinkFile", "BundleManifestPath", "HiddenArp", "InboxModernApp", "Language", "ManifestPath", "MsiPackageCode", "MsiProductCode", "PackageFullName", "ProgramInstanceId", "RegistryKeyPath", "RootDirPath", "Type", "Source", "StoreAppType", "UninstallString", "Manufacturer", "KeyName", "UserSid", "MsiInstallDate");
                csvWriter.WriteHeader<ProgramsEntryNew>();
                csvWriter.NextRecord();
                csvWriter.WriteRecords(amNew.ProgramsEntries);
            }

            outbase1 = $"{ts1}_{hiveName1}_AssociatedFileEntries.csv";

            if (string.IsNullOrEmpty(csvf) == false)
            {
                outbase1 =
                    $"{Path.GetFileNameWithoutExtension(csvf)}{csvfSuffix}_AssociatedFileEntries{Path.GetExtension(csvf)}";
            }

            outFile1 = Path.Combine(csv, outbase1);

            using (var sw = new StreamWriter(outFile1))
            {
                var csvWriter = new CsvWriter(sw,CultureInfo.InvariantCulture);

                var o = new TypeConverterOptions
                {
                    DateTimeStyle = DateTimeStyles.AssumeUniversal & DateTimeStyles.AdjustToUniversal
                };
                csvWriter.Context.TypeConverterOptionsCache.AddOptions<FileEntryNew>(o);

                var foo = csvWriter.Context.AutoMap<FileEntryNew>();

                foo.Map(m => m.ApplicationName).Index(0);

                foo.Map(m => m.ProgramId).Index(1);
                foo.Map(t => t.FileKeyLastWriteTimestamp).Convert(t =>
                        t.Value.FileKeyLastWriteTimestamp.ToString(dt))
                    .Index(2);
                foo.Map(m => m.SHA1).Index(3);
                foo.Map(m => m.IsOsComponent).Index(4);
                foo.Map(m => m.FullPath).Index(5);
                foo.Map(m => m.Name).Index(6);
                foo.Map(m => m.FileExtension).Index(7);

                foo.Map(t => t.LinkDate).Convert(t =>
                    t.Value.LinkDate?.ToString(dt)).Index(8);
                foo.Map(m => m.ProductName).Index(9);

                foo.Map(m => m.Size).Index(10);

                foo.Map(m => m.Version).Index(11);
                foo.Map(m => m.ProductVersion).Index(12);

                foo.Map(m => m.LongPathHash).Index(13);

                foo.Map(m => m.BinaryType).Index(14);
                foo.Map(m => m.IsPeFile).Index(15);

                foo.Map(m => m.BinFileVersion).Index(16);
                foo.Map(m => m.BinProductVersion).Index(17);

                foo.Map(m => m.Usn).Index(18);
                foo.Map(m => m.Language).Index(19);
                foo.Map(m => m.Description).Index(20);
                foo.Map(m => m.OriginalFileName).Index(21);
                //new columns go at the end
                foo.Map(m => m.Sha256).Index(22);
                foo.Map(m => m.Publisher).Ignore();

                csvWriter.Context.RegisterClassMap(foo);
                    
                //the columns of 2026.5 in their order, new ones at the end
                Order(foo, "ApplicationName", "ProgramId", "FileKeyLastWriteTimestamp", "SHA1", "IsOsComponent", "FullPath", "Name", "FileExtension", "LinkDate", "ProductName", "Size", "Version", "ProductVersion", "LongPathHash", "BinaryType", "IsPeFile", "BinFileVersion", "BinProductVersion", "Usn", "Language", "Description", "OriginalFileName", "Publisher", "Sha256", "KeyName", "AppxPackageFullName", "AppxPackageRelativeId");
                csvWriter.WriteHeader<FileEntryNew>();
                csvWriter.NextRecord();

                sw.AutoFlush = true;

                foreach (var pe in amNew.ProgramsEntries)
                {
                    var cleanList22 =
                        pe.FileEntries.Where(t => whitelistHashes1.Contains(t.SHA1) == useBlacklist2)
                            .ToList();

                    csvWriter.WriteRecords(cleanList22);
                }
            }
        }


        outbase1 = $"{ts1}_{hiveName1}_ShortCuts.csv";

        if (string.IsNullOrEmpty(csvf) == false)
        {
            outbase1 =
                $"{Path.GetFileNameWithoutExtension(csvf)}{csvfSuffix}_ShortCuts{Path.GetExtension(csvf)}";
        }

        outFile1 = Path.Combine(csv, outbase1);

        using (var sw = new StreamWriter(outFile1))
        {
            var csvWriter = new CsvWriter(sw,CultureInfo.InvariantCulture);

            var o = new TypeConverterOptions
            {
                DateTimeStyle = DateTimeStyles.AssumeUniversal & DateTimeStyles.AdjustToUniversal
            };
            csvWriter.Context.TypeConverterOptionsCache.AddOptions<Shortcut>(o);

            var foo = csvWriter.Context.AutoMap<Shortcut>();

            foo.Map(m => m.KeyName).Index(0);
            foo.Map(m => m.LnkName).Index(1);

            foo.Map(t => t.KeyLastWriteTimestamp).Convert(t =>
                t.Value.KeyLastWriteTimestamp.ToString(dt)).Index(2);

            csvWriter.Context.RegisterClassMap(foo);
                
            //the columns of 2026.5 in their order, new ones at the end
            Order(foo, "KeyName", "LnkName", "KeyLastWriteTimestamp", "ShortcutTargetPath", "ShortcutAumid", "ShortcutProgramId");
            csvWriter.WriteHeader<Shortcut>();
            csvWriter.NextRecord();
            sw.AutoFlush = true;

            foreach (var sc in amNew.ShortCuts)
            {
                csvWriter.WriteRecord(sc);
                csvWriter.NextRecord();
            }
        }

        outbase1 = $"{ts1}_{hiveName1}_DriverBinaries.csv";

        if (string.IsNullOrEmpty(csvf) == false)
        {
            outbase1 =
                $"{Path.GetFileNameWithoutExtension(csvf)}{csvfSuffix}_DriverBinaries{Path.GetExtension(csvf)}";
        }

        outFile1 = Path.Combine(csv, outbase1);

        using (var sw = new StreamWriter(outFile1))
        {
            var csvWriter = new CsvWriter(sw,CultureInfo.InvariantCulture);


            var o = new TypeConverterOptions
            {
                DateTimeStyle = DateTimeStyles.AssumeUniversal & DateTimeStyles.AdjustToUniversal
            };
            csvWriter.Context.TypeConverterOptionsCache.AddOptions<DriverBinary>(o);

            var foo = csvWriter.Context.AutoMap<DriverBinary>();

            foo.Map(m => m.KeyName).Index(0);
            foo.Map(t => t.KeyLastWriteTimestamp).Convert(t =>
                t.Value.KeyLastWriteTimestamp.ToString(dt)).Index(1);
            foo.Map(t => t.DriverTimeStamp).Convert(t =>
                t.Value.DriverTimeStamp?.ToString(dt)).Index(2);
            foo.Map(t => t.DriverLastWriteTime).Convert(t =>
                t.Value.DriverLastWriteTime?.ToString(dt)).Index(3);

            foo.Map(m => m.DriverName).Index(4);

            foo.Map(m => m.DriverInBox).Index(5);
            foo.Map(m => m.DriverIsKernelMode).Index(6);
            foo.Map(m => m.DriverSigned).Index(7);
            foo.Map(m => m.DriverCheckSum).Index(8);
            foo.Map(m => m.DriverCompany).Index(9);
            foo.Map(m => m.DriverId).Index(10);
            foo.Map(m => m.DriverPackageStrongName).Index(11);
            foo.Map(m => m.DriverType).Index(12);
            foo.Map(m => m.DriverVersion).Index(13);
            foo.Map(m => m.ImageSize).Index(14);
            foo.Map(m => m.Inf).Index(15);
            foo.Map(m => m.Product).Index(16);
            foo.Map(m => m.ProductVersion).Index(17);
            foo.Map(m => m.Service).Index(18);
            foo.Map(m => m.WdfVersion).Index(19);

            csvWriter.Context.RegisterClassMap(foo);
                
            //the columns of 2026.5 in their order, new ones at the end
            Order(foo, "KeyName", "KeyLastWriteTimestamp", "DriverTimeStamp", "DriverLastWriteTime", "DriverName", "DriverInBox", "DriverIsKernelMode", "DriverSigned", "DriverCheckSum", "DriverCompany", "DriverId", "DriverPackageStrongName", "DriverType", "DriverVersion", "ImageSize", "Inf", "Product", "ProductVersion", "Service", "WdfVersion");
            csvWriter.WriteHeader<DriverBinary>();
            csvWriter.NextRecord();
            sw.AutoFlush = true;

            foreach (var sc in amNew.DriveBinaries)
            {
                csvWriter.WriteRecord(sc);
                csvWriter.NextRecord();
            }
        }


        outbase1 = $"{ts1}_{hiveName1}_DeviceContainers.csv";

        if (string.IsNullOrEmpty(csvf) == false)
        {
            outbase1 =
                $"{Path.GetFileNameWithoutExtension(csvf)}{csvfSuffix}_DeviceContainers{Path.GetExtension(csvf)}";
        }

        outFile1 = Path.Combine(csv, outbase1);

        using (var sw = new StreamWriter(outFile1))
        {
            var csvWriter = new CsvWriter(sw,CultureInfo.InvariantCulture);


            var o = new TypeConverterOptions
            {
                DateTimeStyle = DateTimeStyles.AssumeUniversal & DateTimeStyles.AdjustToUniversal
            };
            csvWriter.Context.TypeConverterOptionsCache.AddOptions<DeviceContainer>(o);

            var foo = csvWriter.Context.AutoMap<DeviceContainer>();

            foo.Map(m => m.KeyName).Index(0);
            foo.Map(t => t.KeyLastWriteTimestamp).Convert(t =>
                t.Value.KeyLastWriteTimestamp.ToString(dt)).Index(1);

            foo.Map(m => m.Categories).Index(2);

            foo.Map(m => m.DiscoveryMethod).Index(3);
            foo.Map(m => m.FriendlyName).Index(4);
            foo.Map(m => m.Icon).Index(5);
            foo.Map(m => m.IsActive).Index(6);
            foo.Map(m => m.IsConnected).Index(7);
            foo.Map(m => m.IsMachineContainer).Index(8);
            foo.Map(m => m.IsNetworked).Index(9);
            foo.Map(m => m.IsPaired).Index(10);
            foo.Map(m => m.Manufacturer).Index(11);
            foo.Map(m => m.ModelId).Index(12);
            foo.Map(m => m.ModelName).Index(13);
            foo.Map(m => m.ModelNumber).Index(14);
            foo.Map(m => m.PrimaryCategory).Index(15);
            foo.Map(m => m.State).Index(16);

            csvWriter.Context.RegisterClassMap(foo);
                
            //the columns of 2026.5 in their order, new ones at the end
            Order(foo, "KeyName", "KeyLastWriteTimestamp", "Categories", "DiscoveryMethod", "FriendlyName", "Icon", "IsActive", "IsConnected", "IsMachineContainer", "IsNetworked", "IsPaired", "Manufacturer", "ModelId", "ModelName", "ModelNumber", "PrimaryCategory", "State");
            csvWriter.WriteHeader<DeviceContainer>();
            csvWriter.NextRecord();
            sw.AutoFlush = true;

            foreach (var sc in amNew.DeviceContainers)
            {
                csvWriter.WriteRecord(sc);
                csvWriter.NextRecord();
            }
        }

        outbase1 = $"{ts1}_{hiveName1}_DriverPackages.csv";

        if (string.IsNullOrEmpty(csvf) == false)
        {
            outbase1 =
                $"{Path.GetFileNameWithoutExtension(csvf)}{csvfSuffix}_DriverPackages{Path.GetExtension(csvf)}";
        }

        outFile1 = Path.Combine(csv, outbase1);

        using (var sw = new StreamWriter(outFile1))
        {
            var csvWriter = new CsvWriter(sw,CultureInfo.InvariantCulture);

            var o = new TypeConverterOptions
            {
                DateTimeStyle = DateTimeStyles.AssumeUniversal & DateTimeStyles.AdjustToUniversal
            };
            csvWriter.Context.TypeConverterOptionsCache.AddOptions<DriverPackage>(o);

            var foo = csvWriter.Context.AutoMap<DriverPackage>();

            csvWriter.Context.RegisterClassMap(foo);

            foo.Map(m => m.KeyName).Index(0);
            foo.Map(t => t.KeyLastWriteTimestamp).Convert(t =>
                t.Value.KeyLastWriteTimestamp.ToString(dt)).Index(1);
            foo.Map(t => t.Date)
                .Convert(t => t.Value.Date?.ToString(dt))
                .Index(2);

            foo.Map(m => m.Class).Index(3);

            foo.Map(m => m.Directory).Index(4);
            foo.Map(m => m.DriverInBox).Index(5);
            foo.Map(m => m.Hwids).Index(6);
            foo.Map(m => m.Inf).Index(7);
            foo.Map(m => m.Provider).Index(8);
            foo.Map(m => m.SubmissionId).Index(9);
            foo.Map(m => m.SYSFILE).Index(10);
            foo.Map(m => m.Version).Index(11);
            foo.Map(m => m.ClassGuid).Ignore();

            //the columns of 2026.5 in their order, new ones at the end
            Order(foo, "KeyName", "KeyLastWriteTimestamp", "Date", "Class", "Directory", "DriverInBox", "Hwids", "Inf", "Provider", "SubmissionId", "SYSFILE", "Version", "ClassGuid", "DriverPackageStrongName", "FlightIds", "RecoveryIds", "IsActive");
            csvWriter.WriteHeader<DriverPackage>();
            csvWriter.NextRecord();
            sw.AutoFlush = true;

            foreach (var sc in amNew.DriverPackages)
            {
                csvWriter.WriteRecord(sc);
                csvWriter.NextRecord();
            }
        }

        outbase1 = $"{ts1}_{hiveName1}_DevicePnps.csv";

        if (string.IsNullOrEmpty(csvf) == false)
        {
            outbase1 =
                $"{Path.GetFileNameWithoutExtension(csvf)}{csvfSuffix}_DevicePnps{Path.GetExtension(csvf)}";
        }


        outFile1 = Path.Combine(csv, outbase1);

        using (var sw = new StreamWriter(outFile1))
        {
            var csvWriter = new CsvWriter(sw,CultureInfo.InvariantCulture);

            var o = new TypeConverterOptions
            {
                DateTimeStyle = DateTimeStyles.AssumeUniversal & DateTimeStyles.AdjustToUniversal
            };
            csvWriter.Context.TypeConverterOptionsCache.AddOptions<DevicePnp>(o);

            var foo = csvWriter.Context.AutoMap<DevicePnp>();

            foo.Map(m => m.KeyName).Index(0);
            foo.Map(t => t.KeyLastWriteTimestamp).Convert(t =>
                t.Value.KeyLastWriteTimestamp.ToString(dt)).Index(1);

            foo.Map(m => m.BusReportedDescription).Index(2);

            foo.Map(m => m.Class).Index(3);
            foo.Map(m => m.ClassGuid).Index(4);
            foo.Map(m => m.Compid).Index(5);
            foo.Map(m => m.ContainerId).Index(6);
            foo.Map(m => m.Description).Index(7);
            foo.Map(m => m.DriverId).Index(8);
            foo.Map(m => m.DriverPackageStrongName).Index(9);
            foo.Map(m => m.DriverName).Index(10);
            foo.Map(m => m.DriverVerDate).Index(11);
            foo.Map(m => m.DriverVerVersion).Index(12);
            foo.Map(m => m.Enumerator).Index(13);
            foo.Map(m => m.HWID).Index(14);
            foo.Map(m => m.Inf).Index(15);
            foo.Map(m => m.InstallState).Index(16);
            foo.Map(m => m.Manufacturer).Index(17);
            foo.Map(m => m.MatchingId).Index(18);
            foo.Map(m => m.Model).Index(19);
            foo.Map(m => m.ParentId).Index(20);
            foo.Map(m => m.ProblemCode).Index(21);
            foo.Map(m => m.Provider).Index(22);
            foo.Map(m => m.Service).Index(23);
            foo.Map(m => m.Stackid).Index(24);
            foo.Map(m => m.DeviceState).Ignore();

            csvWriter.Context.RegisterClassMap(foo);
                
            //the columns of 2026.5 in their order, new ones at the end
            Order(foo, "KeyName", "KeyLastWriteTimestamp", "BusReportedDescription", "Class", "ClassGuid", "Compid", "ContainerId", "Description", "DriverId", "DriverPackageStrongName", "DriverName", "DriverVerDate", "DriverVerVersion", "Enumerator", "HWID", "Inf", "InstallState", "Manufacturer", "MatchingId", "Model", "ParentId", "ProblemCode", "Provider", "Service", "Stackid", "DeviceState", "InstallDate", "FirstInstallDate", "ExtendedInfs", "UpperClassFilters", "LowerClassFilters", "UpperFilters", "LowerFilters", "DeviceInterfaceClasses", "LocationPaths");
            csvWriter.WriteHeader<DevicePnp>();
            csvWriter.NextRecord();
            sw.AutoFlush = true;

            foreach (var sc in amNew.DevicePnps)
            {
                csvWriter.WriteRecord(sc);
                csvWriter.NextRecord();
            }
        }

        Console.WriteLine();
        if (csvfSuffix.Length == 0)
        {
            Log.Information("{F} is in new format (registry hive, Root\\Inventory* keys)",source);
        }
        else
        {
            Log.Information("{F} has the new format SQLite databases (Inventory*.db)",source);
        }

        Console.WriteLine();

        Log.Information("Total file entries found: {TotalFileEntries:N0}",amNew.TotalFileEntries);
        if (amNew.InvalidDates > 0)
        {
            Log.Warning("Dates that are not valid were left empty: {Count:N0} (use --debug to see them)",amNew.InvalidDates);
        }
        if (amNew.ShortCuts.Count > 0)
        {
            Log.Information("Total shortcuts found: {Count:N0}",amNew.ShortCuts.Count);
        }

        if (amNew.DeviceContainers.Count > 0)
        {
            Log.Information("Total device containers found: {Count:N0}",amNew.DeviceContainers.Count);
        }

        if (amNew.DevicePnps.Count > 0)
        {
            Log.Information("Total device PnPs found: {Count:N0}",amNew.DevicePnps.Count);
        }

        if (amNew.DriveBinaries.Count > 0)
        {
            Log.Information("Total drive binaries found: {Count:N0}",amNew.DriveBinaries.Count);
        }

        if (amNew.DriverPackages.Count > 0)
        {
            Log.Information("Total driver packages found: {Count:N0}",amNew.DriverPackages.Count);
        }

        if (amNew.UnassociatedFileEntries.Count == 1)
        {
            Console.WriteLine();
            if (i)
            {
                Log.Information("Found {Count:N0} unassociated file entry and {TotalProgramFileEntries2:N0} program file entries (across {ProgramsEntries.Count:N0} program entries)",cleanList2.Count,totalProgramFileEntries2,amNew.ProgramsEntries.Count);
            }
            else
            {
                Log.Information("Found {Count:N0} unassociated file entry",cleanList2.Count);
            }
        }
        else
        {
            Console.WriteLine();
            if (i)
            {
                Log.Information("Found {Count:N0} unassociated file entry and {TotalProgramFileEntries2:N0} program file entries (across {ProgramsEntriesCount:N0} program entries)",cleanList2.Count,totalProgramFileEntries2,amNew.ProgramsEntries.Count);
            }
            else
            {
                Log.Information("Found {Count:N0} unassociated file entry",cleanList2.Count);    
            }
        }

        if (whitelistHashes1.Count > 0)
        {
            var per = (double) (totalProgramFileEntries2 + cleanList2.Count) / amNew.TotalFileEntries;

            Console.WriteLine();

            var list = "whitelist";
            if (b?.Length > 0)
            {
                list = "blacklist";
            }

            Log.Information("{List} hash count: {Count:N0}",UppercaseFirst(list),whitelistHashes1.Count);

            Console.WriteLine();

            Log.Information("Percentage of total shown based on {List}: {Per:P3} ({Per2:P3} savings)",list,per,1-per);
        }

        return true;
    }

    private class CustomHelpAction : SynchronousCommandLineAction
    {
        private readonly HelpAction _defaultHelp;

        public CustomHelpAction(HelpAction action)
        {
            _defaultHelp = action;
        }

        public override int Invoke(ParseResult parseResult)
        {
            var result = _defaultHelp.Invoke(parseResult);

            Log.Warning("{Msg}", string.Join(" ",parseResult.Tokens));

            return result;
        }
    }

    private static string UppercaseFirst(string s)
    {
        if (string.IsNullOrEmpty(s))
        {
            return string.Empty;
        }

        var a = s.ToCharArray();
        a[0] = char.ToUpper(a[0]);
        return new string(a);
    }

    private static bool IsAdministrator()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return true;
        }
            
        Log.Debug("Checking for admin rights");
        var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }
}



internal class ApplicationArguments
{
    public string File { get; set; }

    //       public string Extension { get; set; } = string.Empty;
    public string Whitelist { get; set; } = string.Empty;

    public string Blacklist { get; set; } = string.Empty;
    public string CsvDirectory { get; set; } = string.Empty;
    public string CsvName { get; set; } = string.Empty;
    public bool IncludeLinked { get; set; } = false;
    public bool RecoverDeleted { get; set; } = false;
    public bool NoTransLogs { get; set; } = false;
    public string DateTimeFormat { get; set; }
    public bool PreciseTimestamps { get; set; }
    public bool Debug { get; set; }
    public bool Trace { get; set; }
}