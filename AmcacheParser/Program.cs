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
using Exceptionless;
using Serilog;
using Serilog.Core;
using Serilog.Events;


namespace AmcacheParser;

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

        var tempOpt = new Option<string>("--temp")
        {
            Description = TempFolder.Help
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
        var coOpt = new Option<bool>("--co")
        {
            Description = "When true, save all console messages to <timestamp>_AmcacheParser_<version>_ConsoleLog.txt in the --csv directory. Use --co false to turn it off",
            DefaultValueFactory = _ => true
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
            tempOpt,
            debugOpt,
            traceOpt,
            coOpt
        };
            
        _rootCommand.Description = Header + "\r\n\r\n" +Footer;

        _rootCommand.SetAction(result => DoWork(result.GetValue(fOpt), result.GetValue(iOpt), result.GetValue(wOpt),
            result.GetValue(bOpt), result.GetValue(csvOpt), result.GetValue(csvfOpt), result.GetValue(dtOpt),
            result.GetValue(mpOpt),result.GetValue(nlOpt),result.GetValue(tempOpt),result.GetValue(debugOpt),result.GetValue(traceOpt),result.GetValue(coOpt)));
            

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
                //a format in the template ({Timestamp:HH:mm:ss.fff}) is kept: it was shown in --dt's format
                return string.IsNullOrEmpty(format) ? dateTimeOffset.ToString(_activeDateTimeFormat) : dateTimeOffset.ToString(format, _innerFormatProvider);
            }

            if (arg is IFormattable formattable)
            {
                return formattable.ToString(format, _innerFormatProvider);
            }

            return arg.ToString();
        }
    }

    /// <returns>0, or 1 for bad arguments, a hive or database that could not be read, or no data</returns>
    private static int DoWork(string f,bool i,string w,string b, string csv,string csvf,string dt,bool mp,bool nl,string temp,bool debug,bool trace,bool co)
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

        //one UTC time per run, for the CSV names and the console log
        var runTime = DateTimeOffset.UtcNow;

        var consoleLog = co ? ConsoleLog.GetPath(runTime, csv) : null;
        if (consoleLog != null)
        {
            conf.WriteTo.File(consoleLog, outputTemplate: ConsoleLog.Template, formatProvider: formatter);
        }

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

        if (consoleLog != null)
        {
            Log.Information("Console log: {Path}",consoleLog);
            Console.WriteLine();
        }

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
        
        var ts1 = runTime.ToString("yyyyMMddHHmmss");

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

        //the databases are read from copies, made in --temp or Temp next to AmcacheParser (never the user's %TEMP%)
        string tempFolder = null;
        var tempCreated = false;
        if (hasDatabases)
        {
            tempFolder = TempFolder.Prepare(temp, out tempCreated, out var tempError);
            if (tempFolder == null)
            {
                Log.Error("The folder for the temporary copies of the databases can't be written to ({Error}). Give one with --temp. Exiting", tempError);
                Console.WriteLine();
                return 1;
            }
        }

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
                        //a damaged hive is not the tool's fault (0); one it could not access is (1)
                        failed |= CouldNotProcess(ex);
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

                        //entries that could not be parsed were left out and listed (a damaged hive: exit code 0)
                    }
                }

                if (hasDatabases)
                {
                    Console.WriteLine();
                    Log.Information("Processing the Amcache databases in {Directory}",databaseDirectory);

                    AmcacheNew amDb;
                    try
                    {
                        amDb = AmcacheNew.FromDatabases(databaseDirectory, tempFolder);
                    }
                    finally
                    {
                        TempFolder.RemoveIfEmpty(tempFolder, tempCreated);
                    }

                    if (WriteNewFormat(amDb, databaseDirectory, hiveName1 + "_Databases", "_Databases", ts1, csv, csvf, dt, i, whitelistHashes1, useBlacklist2, b) == false)
                    {
                        return 1;
                    }

                    WriteDatabaseTables(amDb.OtherTables, hiveName1 + "_Databases", "_Databases", ts1, csv, csvf, dt);

                    //a database that could not be opened or copied (access, IO); damaged ones and rows that could not be
                    //parsed are listed, but are not the tool's fault (exit code 0)
                    failed |= amDb.DatabasesNotAccessed > 0;
                }
                else if (hiveGiven == false)
                {
                    //nothing to do (exit code 0)
                    Log.Warning("No Amcache databases (Inventory*.db) found in {Directory}",databaseDirectory);
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
                return 0;
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

            //keys that could not be parsed were left out and listed (a damaged hive: exit code 0)
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

            //a damaged hive is not the tool's fault (0); access, IO, output or a dirty hive without its logs is (1)
            return CouldNotProcess(ex) ? 1 : 0;
        }

        return 0;
    }

    /// <summary>
    ///     Exit code 1: the file could not be accessed or read (in use, access denied, IO errors, also when writing the
    ///     output), or the hive is dirty and its logs are not there. Anything else is a damaged file (exit code 0)
    /// </summary>
    private static bool CouldNotProcess(Exception ex)
    {
        for (var e = ex; e != null; e = e.InnerException)
        {
            //EndOfStreamException is an IOException, but means a truncated file
            if (e is IOException and not EndOfStreamException or UnauthorizedAccessException || e.Message.Contains("Administrator privileges not found") ||
                e.Message.Contains("transaction logs were not found"))
            {
                return true;
            }
        }

        return false;
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

            using var csvWriter = CsvWriter.ToFile(Path.Combine(csv, outbase), CsvOptions);

            foreach (var column in table.Columns)
            {
                csvWriter.WriteField(column);
            }

            csvWriter.EndRecord();

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

                csvWriter.EndRecord();
            }

            Log.Information("{Name}: {Count:N0} rows",name,table.Rows.Count);
        }
    }

    /// <summary>
    ///     CsvHelper 34 writing what CsvHelper 33 wrote: True/False, CRLF, no BOM, and fields that start or end with a
    ///     space quoted. Not CsvQuoting.WhenNeededOrWhitespace: that also quotes a leading or trailing tab, which 33 did
    ///     not (a value in the newformat corpus hive ends with a tab)
    /// </summary>
    private static readonly CsvWriterOptions CsvOptions = new()
    {
        ShouldQuote = (field, _, _, _) => field.Length > 0 && (field[0] == ' ' || field[field.Length - 1] == ' '),
        TrueValue = "True",
        FalseValue = "False"
    };

    /// <summary>The header and <paramref name="records" />, with the columns of <paramref name="recordWriter" /></summary>
    private static void WriteCsv<T>(string outFile, IEnumerable<T> records, CsvRecordWriter<T> recordWriter)
    {
        using var csvWriter = CsvWriter.ToFile(outFile, CsvOptions);
        csvWriter.WriteRecords(records, recordWriter);
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

        WriteCsv(outFile, cleanList, new FileEntryOldWriter(dt, true));

        if (i)
        {
            outbase = $"{ts1}_{hiveName}_ProgramEntries.csv";

            if (string.IsNullOrEmpty(csvf) == false)
            {
                outbase =
                    $"{Path.GetFileNameWithoutExtension(csvf)}{csvfSuffix}_ProgramEntries{Path.GetExtension(csvf)}";
            }

            outFile = Path.Combine(csv, outbase);

            WriteCsv(outFile, am.ProgramsEntries, new ProgramsEntryOldWriter(dt));

            outbase = $"{ts1}_{hiveName}_AssociatedFileEntries.csv";

            if (string.IsNullOrEmpty(csvf) == false)
            {
                outbase =
                    $"{Path.GetFileNameWithoutExtension(csvf)}{csvfSuffix}_AssociatedFileEntries{Path.GetExtension(csvf)}";
            }

            outFile = Path.Combine(csv, outbase);

            WriteCsv(outFile, am.ProgramsEntries.SelectMany(pe => pe.FileEntries.Where(t => whitelistHashes.Contains(t.SHA1) == useBlacklist)), new FileEntryOldWriter(dt, false));
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

        WriteCsv(outFile1, cleanList2, new FileEntryNewWriter(dt));

        if (i)
        {
            outbase1 = $"{ts1}_{hiveName1}_ProgramEntries.csv";

            if (string.IsNullOrEmpty(csvf) == false)
            {
                outbase1 =
                    $"{Path.GetFileNameWithoutExtension(csvf)}{csvfSuffix}_ProgramEntries{Path.GetExtension(csvf)}";
            }

            outFile1 = Path.Combine(csv, outbase1);

            WriteCsv(outFile1, amNew.ProgramsEntries, new ProgramsEntryNewWriter(dt));

            outbase1 = $"{ts1}_{hiveName1}_AssociatedFileEntries.csv";

            if (string.IsNullOrEmpty(csvf) == false)
            {
                outbase1 =
                    $"{Path.GetFileNameWithoutExtension(csvf)}{csvfSuffix}_AssociatedFileEntries{Path.GetExtension(csvf)}";
            }

            outFile1 = Path.Combine(csv, outbase1);

            WriteCsv(outFile1, amNew.ProgramsEntries.SelectMany(pe => pe.FileEntries.Where(t => whitelistHashes1.Contains(t.SHA1) == useBlacklist2)), new FileEntryNewWriter(dt));
        }


        outbase1 = $"{ts1}_{hiveName1}_ShortCuts.csv";

        if (string.IsNullOrEmpty(csvf) == false)
        {
            outbase1 =
                $"{Path.GetFileNameWithoutExtension(csvf)}{csvfSuffix}_ShortCuts{Path.GetExtension(csvf)}";
        }

        outFile1 = Path.Combine(csv, outbase1);

        WriteCsv(outFile1, amNew.ShortCuts, new ShortcutWriter(dt));

        outbase1 = $"{ts1}_{hiveName1}_DriverBinaries.csv";

        if (string.IsNullOrEmpty(csvf) == false)
        {
            outbase1 =
                $"{Path.GetFileNameWithoutExtension(csvf)}{csvfSuffix}_DriverBinaries{Path.GetExtension(csvf)}";
        }

        outFile1 = Path.Combine(csv, outbase1);

        WriteCsv(outFile1, amNew.DriveBinaries, new DriverBinaryWriter(dt));


        outbase1 = $"{ts1}_{hiveName1}_DeviceContainers.csv";

        if (string.IsNullOrEmpty(csvf) == false)
        {
            outbase1 =
                $"{Path.GetFileNameWithoutExtension(csvf)}{csvfSuffix}_DeviceContainers{Path.GetExtension(csvf)}";
        }

        outFile1 = Path.Combine(csv, outbase1);

        WriteCsv(outFile1, amNew.DeviceContainers, new DeviceContainerWriter(dt));

        outbase1 = $"{ts1}_{hiveName1}_DriverPackages.csv";

        if (string.IsNullOrEmpty(csvf) == false)
        {
            outbase1 =
                $"{Path.GetFileNameWithoutExtension(csvf)}{csvfSuffix}_DriverPackages{Path.GetExtension(csvf)}";
        }

        outFile1 = Path.Combine(csv, outbase1);

        WriteCsv(outFile1, amNew.DriverPackages, new DriverPackageWriter(dt));

        outbase1 = $"{ts1}_{hiveName1}_DevicePnps.csv";

        if (string.IsNullOrEmpty(csvf) == false)
        {
            outbase1 =
                $"{Path.GetFileNameWithoutExtension(csvf)}{csvfSuffix}_DevicePnps{Path.GetExtension(csvf)}";
        }


        outFile1 = Path.Combine(csv, outbase1);

        WriteCsv(outFile1, amNew.DevicePnps, new DevicePnpWriter(dt));

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