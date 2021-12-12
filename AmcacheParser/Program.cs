using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Help;
using System.CommandLine.Invocation;
using System.CommandLine.Parsing;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading.Tasks;
using Amcache;
using Amcache.Classes;
using CsvHelper;
using CsvHelper.TypeConversion;
using Exceptionless;
using NLog;
using NLog.Config;
using NLog.Targets;

namespace AmcacheParser
{
    using Amcache.Converters;

    internal class Program
    {
        private static readonly string _preciseTimeFormat = "yyyy-MM-dd HH:mm:ss.fffffff";

        public static string header = $"AmcacheParser version {Assembly.GetExecutingAssembly().GetName().Version}" +
                                      "\r\n\r\nAuthor: Eric Zimmerman (saericzimmerman@gmail.com)" +
                                      "\r\nhttps://github.com/EricZimmerman/AmcacheParser";
        
        public static string  footer = @"Examples: AmcacheParser.exe -f ""C:\Temp\amcache\AmcacheWin10.hve"" --csv C:\temp" +
                                       "\r\n\t " +
                                       @"   AmcacheParser.exe -f ""C:\Temp\amcache\AmcacheWin10.hve"" -i on --csv C:\temp --csvf foo.csv" + "\r\n\t " +
                                       @"   AmcacheParser.exe -f ""C:\Temp\amcache\AmcacheWin10.hve"" -w ""c:\temp\whitelist.txt"" --csv C:\temp" +
                                       "\r\n\t" +
                                       "\r\n\t" +
                                       "    Short options (single letter) are prefixed with a single dash. Long commands are prefixed with two dashes";
        
        private static Logger _logger;
        private static Stopwatch _sw;

        private static string[] _args;

        private static async Task Main(string[] args)
        {
            _args = args;
            ExceptionlessClient.Default.Startup("prIG996gFK1y6DaZEoXh3InSg8LwrHcQV4Dze2r8");
            SetupNLog();

            _logger = LogManager.GetCurrentClassLogger();

            var fOption = new Option<string>(
                "-f",

                description: "Amcache.hve file to parse");
            fOption.IsRequired = true;

            var csvOption = new Option<string>(
                "--csv",
                "Directory to save CSV formatted results to. Be sure to include the full path in double quotes");
            csvOption.IsRequired = true;
            
            var rootCommand = new RootCommand
        {
            fOption,
            new Option<bool>(
                "-i",
                getDefaultValue:()=>false,
                description: "Include file entries for Programs entries"),
            
            new Option<string>(
                "-w",
                
                "Path to file containing SHA-1 hashes to *exclude* from the results. Blacklisting overrides whitelisting\r\n"),
            
            new Option<string>(
                "-b",
                
                "Path to file containing SHA-1 hashes to *include* from the results. Blacklisting overrides whitelisting"),
                
            csvOption,
                
            new Option<string>(
                "--csvf",
                "File name to save CSV formatted results to. When present, overrides default name\r\n"),
                
            new Option<string>(
                "--dt",
                getDefaultValue:()=>"yyyy-MM-dd HH:mm:ss",
                "The custom date/time format to use when displaying time stamps. See https://goo.gl/CNVq0k for options"),
                
            new Option<bool>(
                "--mp",
                getDefaultValue:()=>false,
                "Display higher precision for time stamps"),
            
            new Option<bool>(
                "--nl",
                getDefaultValue:()=>false,
                "When true, ignore transaction log files for dirty hives. Default is FALSE\r\n"),
            
            new Option<bool>(
                "--debug",
                getDefaultValue:()=>false,
                "Show debug information during processing"),
            
            new Option<bool>(
                "--trace",
                getDefaultValue:()=>false,
                "Show trace information during processing"),
                
        };
            
            rootCommand.Description = header + "\r\n\r\n" +footer;

            rootCommand.Handler = System.CommandLine.NamingConventionBinder.CommandHandler.Create<string,bool,string,string,string, string,string,bool,bool,bool,bool>(DoWork);
            
            await rootCommand.InvokeAsync(args);
           
        }

        private static void DoWork(string f,bool i,string w,string b, string csv,string csvf,string dt,bool mp,bool nl,bool debug,bool trace)
        {
            if (!File.Exists(f))
            {
                _logger.Warn($"'{f}' not found. Exiting");
                return;
            }

            _logger.Info(header);
            _logger.Info("");
            _logger.Info($"Command line: {string.Join(" ", _args)}");
            _logger.Info("");

            if (mp)
            {
                dt = _preciseTimeFormat;
            }

            if (IsAdministrator() == false)
            {
                _logger.Fatal("Warning: Administrator privileges not found!\r\n");
            }

            if (debug)
            {
                LogManager.Configuration.LoggingRules.First().EnableLoggingForLevel(LogLevel.Debug);
            }

            if (trace)
            {
                LogManager.Configuration.LoggingRules.First().EnableLoggingForLevel(LogLevel.Trace);
            }

            LogManager.ReconfigExistingLoggers();

            _sw = new Stopwatch();
            _sw.Start();

            try
            {
                if (Helper.IsNewFormat(f, nl))
                {
                    _logger.Debug($"Processing new format hive");

                    var amNew = new AmcacheNew(f,
                        true, nl);

                    if (amNew.ProgramsEntries.Count == 0 && amNew.UnassociatedFileEntries.Count == 0)
                    {
                        _logger.Warn("Hive did not contain program entries nor file entries.");
                    }

                    _sw.Stop();

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
                            _logger.Warn($"'{b}' does not exist");
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
                            _logger.Warn($"'{w}' does not exist");
                        }
                    }

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
                            _logger.Error(
                                $"There was an error creating directory '{csv}'. Error: {ex.Message} Exiting");
                            return;
                        }
                    }

                    foreach (var pe in amNew.ProgramsEntries)
                    {
                        var cleanList22 =
                            pe.FileEntries.Where(t => whitelistHashes1.Contains(t.SHA1) == useBlacklist2).ToList();
                        totalProgramFileEntries2 += cleanList22.Count;
                    }

                    var ts1 = DateTime.Now.ToString("yyyyMMddHHmmss");
                    var hiveName1 = Path.GetFileNameWithoutExtension(f);

                    var outbase1 = $"{ts1}_{hiveName1}_UnassociatedFileEntries.csv";

                    if (string.IsNullOrEmpty(csvf) == false)
                    {
                        outbase1 =
                            $"{Path.GetFileNameWithoutExtension(csvf)}_UnassociatedFileEntries{Path.GetExtension(csvf)}";
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
                        

                        foo.Map(m => m.Language).Index(18);
                        foo.Map(m => m.Publisher).Ignore();

                        csvWriter.Context.RegisterClassMap(foo);
                        
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
                                $"{Path.GetFileNameWithoutExtension(csvf)}_ProgramEntries{Path.GetExtension(csvf)}";
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

                            csvWriter.Context.RegisterClassMap(foo);
                            
                            csvWriter.WriteHeader<ProgramsEntryNew>();
                            csvWriter.NextRecord();
                            csvWriter.WriteRecords(amNew.ProgramsEntries);
                        }

                        outbase1 = $"{ts1}_{hiveName1}_AssociatedFileEntries.csv";

                        if (string.IsNullOrEmpty(csvf) == false)
                        {
                            outbase1 =
                                $"{Path.GetFileNameWithoutExtension(csvf)}_AssociatedFileEntries{Path.GetExtension(csvf)}";
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

                            foo.Map(m => m.Language).Index(18);
                            foo.Map(m => m.Publisher).Ignore();

                            csvWriter.Context.RegisterClassMap(foo);
                            
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
                            $"{Path.GetFileNameWithoutExtension(csvf)}_ShortCuts{Path.GetExtension(csvf)}";
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
                        
                        csvWriter.WriteHeader<Shortcut>();
                        csvWriter.NextRecord();
                        sw.AutoFlush = true;

                        foreach (var sc in amNew.ShortCuts)
                        {
                            csvWriter.WriteRecord(sc);
                            csvWriter.NextRecord();
                        }
                    }

                    outbase1 = $"{ts1}_{hiveName1}_DriveBinaries.csv";

                    if (string.IsNullOrEmpty(csvf) == false)
                    {
                        outbase1 =
                            $"{Path.GetFileNameWithoutExtension(csvf)}_DriveBinaries{Path.GetExtension(csvf)}";
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
                            $"{Path.GetFileNameWithoutExtension(csvf)}_DeviceContainers{Path.GetExtension(csvf)}";
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
                            $"{Path.GetFileNameWithoutExtension(csvf)}_DriverPackages{Path.GetExtension(csvf)}";
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
                            $"{Path.GetFileNameWithoutExtension(csvf)}_DevicePnps{Path.GetExtension(csvf)}";
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
                        
                        csvWriter.WriteHeader<DevicePnp>();
                        csvWriter.NextRecord();
                        sw.AutoFlush = true;

                        foreach (var sc in amNew.DevicePnps)
                        {
                            csvWriter.WriteRecord(sc);
                            csvWriter.NextRecord();
                        }
                    }


                    _logger.Error($"\r\n'{f}' is in new format!");

                    var suffix1 = amNew.UnassociatedFileEntries.Count == 1 ? "y" : "ies";


                    var linked1 = "";
                    if (i)
                    {
                        linked1 =
                            $"and {totalProgramFileEntries2:N0} program file entries (across {amNew.ProgramsEntries.Count:N0} program entries) ";
                    }

                    _logger.Info("");

                    _logger.Info($"Total file entries found: {amNew.TotalFileEntries:N0}");
                    if (amNew.ShortCuts.Count > 0)
                    {
                        _logger.Info($"Total shortcuts found: {amNew.ShortCuts.Count:N0}");
                    }

                    if (amNew.DeviceContainers.Count > 0)
                    {
                        _logger.Info($"Total device containers found: {amNew.DeviceContainers.Count:N0}");
                    }

                    if (amNew.DevicePnps.Count > 0)
                    {
                        _logger.Info($"Total device PnPs found: {amNew.DevicePnps.Count:N0}");
                    }

                    if (amNew.DriveBinaries.Count > 0)
                    {
                        _logger.Info($"Total drive binaries found: {amNew.DriveBinaries.Count:N0}");
                    }

                    if (amNew.DriverPackages.Count > 0)
                    {
                        _logger.Info($"Total driver packages found: {amNew.DriverPackages.Count:N0}");
                    }

                    _logger.Info(
                        $"\r\nFound {cleanList2.Count:N0} unassociated file entr{suffix1} {linked1}");

                    if (whitelistHashes1.Count > 0)
                    {
                        var per = (double) (totalProgramFileEntries2 + cleanList2.Count) / amNew.TotalFileEntries;

                        _logger.Info("");

                        var list = "whitelist";
                        if (b?.Length > 0)
                        {
                            list = "blacklist";
                        }

                        _logger.Info($"{UppercaseFirst(list)} hash count: {whitelistHashes1.Count:N0}");

                        _logger.Info("");

                        _logger.Info($"Percentage of total shown based on {list}: {per:P3} ({1 - per:P3} savings)");
                    }

                    _logger.Info("");

                    _logger.Info($"Results saved to: {csv}");


                    _logger.Info("");
                    _logger.Info(
                        $"Total parsing time: {_sw.Elapsed.TotalSeconds:N3} seconds.\r\n");


                    return;
                }

                _logger.Debug($"Processing old format hive");

                var am = new AmcacheOld(f,
                    true, nl);


                if (am.ProgramsEntries.Count == 0 && am.UnassociatedFileEntries.Count == 0)
                {
                    _logger.Warn("Hive did not contain program entries nor file entries. Exiting");
                    return;
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
                        _logger.Warn($"'{b}' does not exist");
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
                        _logger.Warn($"'{w}' does not exist");
                    }
                }

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
                        _logger.Error(
                            $"There was an error creating directory '{csv}'. Error: {ex.Message} Exiting");
                        return;
                    }
                }


                foreach (var pe in am.ProgramsEntries)
                {
                    var cleanList2 =
                        pe.FileEntries.Where(t => whitelistHashes.Contains(t.SHA1) == useBlacklist).ToList();
                    totalProgramFileEntries += cleanList2.Count;
                }

                var ts = DateTime.Now.ToString("yyyyMMddHHmmss");
                var hiveName = Path.GetFileNameWithoutExtension(f);

                var outbase = $"{ts}_{hiveName}_UnassociatedFileEntries.csv";

                if (string.IsNullOrEmpty(csvf) == false)
                {
                    outbase =
                        $"{Path.GetFileNameWithoutExtension(csvf)}_UnassociatedFileEntries{Path.GetExtension(csvf)}";
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
                    outbase = $"{ts}_{hiveName}_ProgramEntries.csv";

                    if (string.IsNullOrEmpty(csvf) == false)
                    {
                        outbase =
                            $"{Path.GetFileNameWithoutExtension(csvf)}_ProgramEntries{Path.GetExtension(csvf)}";
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

                    outbase = $"{ts}_{hiveName}_AssociatedFileEntries.csv";

                    if (string.IsNullOrEmpty(csvf) == false)
                    {
                        outbase =
                            $"{Path.GetFileNameWithoutExtension(csvf)}_AssociatedFileEntries{Path.GetExtension(csvf)}";
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

                _logger.Error($"\r\n'{f}' is in old format!");

                var suffix = am.UnassociatedFileEntries.Count == 1 ? "y" : "ies";


                var linked = "";
                if (i)
                {
                    linked =
                        $"and {totalProgramFileEntries:N0} program file entries (across {am.ProgramsEntries.Count:N0} program entries) ";
                }


                _logger.Info("");

                _logger.Info($"Total file entries found: {am.TotalFileEntries:N0}");

                _logger.Info(
                    $"Found {cleanList.Count:N0} unassociated file entr{suffix} {linked}");

                if (whitelistHashes.Count > 0)
                {
                    var per = (double) (totalProgramFileEntries + cleanList.Count) / am.TotalFileEntries;

                    _logger.Info("");

                    var list = "whitelist";
                    if (b?.Length > 0)
                    {
                        list = "blacklist";
                    }

                    _logger.Info($"{UppercaseFirst(list)} hash count: {whitelistHashes.Count:N0}");

                    _logger.Info("");

                    _logger.Info($"Percentage of total shown based on {list}: {per:P3} ({1 - per:P3} savings)");
                }

                _logger.Info("");

                _logger.Info($"Results saved to: {csv}");


                _logger.Info("");
                _logger.Info(
                    $"Total parsing time: {_sw.Elapsed.TotalSeconds:N3} seconds.\r\n");
            }
            catch (Exception ex)
            {
                if (!ex.Message.Contains(
                    "Sequence numbers do not match and transaction logs were not found in the same directory as the hive. Abort")
                )
                {
                    if (ex.Message.Contains("Administrator privileges not found"))
                    {
                        _logger.Fatal($"Could not access '{f}' because it is in use");
                        _logger.Error("");
                        _logger.Fatal("Rerun the program with Administrator privileges to try again\r\n");
                    }
                    else
                    {
                        _logger.Error($"There was an error: {ex.Message}");
                        _logger.Error($"Stacktrace: {ex.StackTrace}");
                        _logger.Info("");
                        _logger.Error(
                            $"Please send '{f}' to saericzimmerman@gmail.com in order to fix the issue");                        
                    }

                }
            }
        }

        private static readonly string BaseDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

        private static void SetupNLog()
        {
            if (File.Exists( Path.Combine(BaseDirectory,"Nlog.config")))
            {
                return;
            }

            var config = new LoggingConfiguration();
            var loglevel = LogLevel.Info;

            var layout = @"${message}";

            var consoleTarget = new ColoredConsoleTarget();

            config.AddTarget("console", consoleTarget);

            consoleTarget.Layout = layout;

            var rule1 = new LoggingRule("*", loglevel, consoleTarget);
            config.LoggingRules.Add(rule1);

            LogManager.Configuration = config;
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
            
            _logger.Debug("Checking for admin rights");
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
}