using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using Amcache;
using Amcache.Classes;
using CsvHelper;
using CsvHelper.TypeConversion;
using Exceptionless;
using Fclp;
using Microsoft.Win32;
using NLog;
using NLog.Config;
using NLog.Targets;

namespace AmcacheParser
{
    internal class Program
    {
        private static readonly string _preciseTimeFormat = "yyyy-MM-dd HH:mm:ss.fffffff";


        private static Logger _logger;
        private static Stopwatch _sw;
        private static FluentCommandLineParser<ApplicationArguments> _fluentCommandLineParser;

        private static string exportExt = "tsv";

        private static bool CheckForDotnet46()
        {
            using (
                var ndpKey =
                    RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32)
                        .OpenSubKey("SOFTWARE\\Microsoft\\NET Framework Setup\\NDP\\v4\\Full\\"))
            {
                if (ndpKey == null)
                {
                    return false;
                }

                var releaseKey = Convert.ToInt32(ndpKey.GetValue("Release"));

                return releaseKey >= 393295;
            }
        }

        private static void Main(string[] args)
        {
            ExceptionlessClient.Default.Startup("prIG996gFK1y6DaZEoXh3InSg8LwrHcQV4Dze2r8");
            SetupNLog();

            _logger = LogManager.GetCurrentClassLogger();

            if (!CheckForDotnet46())
            {
                _logger.Warn(".net 4.6 not detected. Please install .net 4.6 and try again.");
                return;
            }


            _fluentCommandLineParser = new FluentCommandLineParser<ApplicationArguments>
            {
                IsCaseSensitive = false
            };

            _fluentCommandLineParser.Setup(arg => arg.File)
                .As('f')
                .WithDescription("Amcache.hve file to parse. Required").Required();

            _fluentCommandLineParser.Setup(arg => arg.IncludeLinked)
                .As('i').SetDefault(false)
                .WithDescription("Include file entries for Programs entries");

            _fluentCommandLineParser.Setup(arg => arg.Whitelist)
                .As('w')
                .WithDescription(
                    "Path to file containing SHA-1 hashes to *exclude* from the results. Blacklisting overrides whitelisting\r\n");

            _fluentCommandLineParser.Setup(arg => arg.Blacklist)
                .As('b')
                .WithDescription(
                    "Path to file containing SHA-1 hashes to *include* from the results. Blacklisting overrides whitelisting");

            _fluentCommandLineParser.Setup(arg => arg.SaveTo)
                .As("csv").Required()
                .WithDescription("Directory where CSV results will be saved to. Required\r\n");


            _fluentCommandLineParser.Setup(arg => arg.DateTimeFormat)
                .As("dt")
                .WithDescription(
                    "The custom date/time format to use when displaying timestamps. See https://goo.gl/CNVq0k for options. Default is: yyyy-MM-dd HH:mm:ss")
                .SetDefault("yyyy-MM-dd HH:mm:ss");

            _fluentCommandLineParser.Setup(arg => arg.PreciseTimestamps)
                .As("mp")
                .WithDescription(
                    "When true, display higher precision for timestamps. Default is FALSE").SetDefault(false);

            _fluentCommandLineParser.Setup(arg => arg.CsvSeparator)
                .As("cs")
                .WithDescription(
                    "When true, use comma instead of tab for field separator. Default is TRUE").SetDefault(true);

            _fluentCommandLineParser.Setup(arg => arg.NoTransLogs)
                .As("nl")
                .WithDescription(
                    "When true, ignore transaction log files for dirty hives. Default is FALSE").SetDefault(false);

            var header =
                $"AmcacheParser version {Assembly.GetExecutingAssembly().GetName().Version}" +
                "\r\n\r\nAuthor: Eric Zimmerman (saericzimmerman@gmail.com)" +
                "\r\nhttps://github.com/EricZimmerman/AmcacheParser";

            var footer = @"Examples: AmcacheParser.exe -f ""C:\Temp\amcache\AmcacheWin10.hve"" --csv C:\temp" +
                         "\r\n\t " +
                         @" AmcacheParser.exe -f ""C:\Temp\amcache\AmcacheWin10.hve"" -i on --csv C:\temp" + "\r\n\t " +
                         @" AmcacheParser.exe -f ""C:\Temp\amcache\AmcacheWin10.hve"" -w ""c:\temp\whitelist.txt"" --csv C:\temp" +
                         "\r\n\t" +
                         "\r\n\t" +
                         "  Short options (single letter) are prefixed with a single dash. Long commands are prefixed with two dashes\r\n";

            _fluentCommandLineParser.SetupHelp("?", "help")
                .WithHeader(header)
                .Callback(text => _logger.Info(text + "\r\n" + footer));

            var result = _fluentCommandLineParser.Parse(args);

            if (result.HelpCalled)
            {
                return;
            }

            if (result.HasErrors)
            {
                _fluentCommandLineParser.HelpOption.ShowHelp(_fluentCommandLineParser.Options);

                _logger.Warn("Both -f and --csv are required. Exiting");

                return;
            }

            if (!File.Exists(_fluentCommandLineParser.Object.File))
            {
                _logger.Warn($"'{_fluentCommandLineParser.Object.File}' not found. Exiting");
                return;
            }

            _logger.Info(header);
            _logger.Info("");
            _logger.Info($"Command line: {string.Join(" ", args)}");
            _logger.Info("");

            if (_fluentCommandLineParser.Object.PreciseTimestamps)
            {
                _fluentCommandLineParser.Object.DateTimeFormat = _preciseTimeFormat;
            }

            if (_fluentCommandLineParser.Object.CsvSeparator)
            {
                exportExt = "csv";
            }

            _sw = new Stopwatch();
            _sw.Start();

            try
            {
                if (Helper.IsNewFormat(_fluentCommandLineParser.Object.File,
                    _fluentCommandLineParser.Object.NoTransLogs))
                {
                    var amNew = new AmcacheNew(_fluentCommandLineParser.Object.File,
                        _fluentCommandLineParser.Object.RecoverDeleted, _fluentCommandLineParser.Object.NoTransLogs);

                    if (amNew.ProgramsEntries.Count == 0 && amNew.UnassociatedFileEntries.Count == 0)
                    {
                        _logger.Warn("Hive did not contain program entries nor file entries.");
                    }

                    _sw.Stop();

                    var whitelistHashes1 = new HashSet<string>();

                    var useBlacklist2 = false;

                    if (_fluentCommandLineParser.Object.Blacklist.Length > 0)
                    {
                        if (File.Exists(_fluentCommandLineParser.Object.Blacklist))
                        {
                            foreach (var readLine in File.ReadLines(_fluentCommandLineParser.Object.Blacklist))
                            {
                                whitelistHashes1.Add(readLine.ToLowerInvariant());
                            }

                            useBlacklist2 = true;
                        }
                        else
                        {
                            _logger.Warn($"'{_fluentCommandLineParser.Object.Blacklist}' does not exist");
                        }
                    }
                    else if (_fluentCommandLineParser.Object.Whitelist.Length > 0)
                    {
                        if (File.Exists(_fluentCommandLineParser.Object.Whitelist))
                        {
                            foreach (var readLine in File.ReadLines(_fluentCommandLineParser.Object.Whitelist))
                            {
                                whitelistHashes1.Add(readLine.ToLowerInvariant());
                            }
                        }
                        else
                        {
                            _logger.Warn($"'{_fluentCommandLineParser.Object.Whitelist}' does not exist");
                        }
                    }

                    var cleanList2 =
                        amNew.UnassociatedFileEntries.Where(t => whitelistHashes1.Contains(t.SHA1) == useBlacklist2)
                            .ToList();
                    var totalProgramFileEntries2 = 0;

                    if (Directory.Exists(_fluentCommandLineParser.Object.SaveTo) == false)
                    {
                        try
                        {
                            Directory.CreateDirectory(_fluentCommandLineParser.Object.SaveTo);
                        }
                        catch (Exception ex)
                        {
                            _logger.Error(
                                $"There was an error creating directory '{_fluentCommandLineParser.Object.SaveTo}'. Error: {ex.Message} Exiting");
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
                    var hiveName1 = Path.GetFileNameWithoutExtension(_fluentCommandLineParser.Object.File);

                    var outbase1 = $"{ts1}_{hiveName1}_Unassociated file entries.{exportExt}";

                    var outFile1 = Path.Combine(_fluentCommandLineParser.Object.SaveTo, outbase1);

                    using (var sw = new StreamWriter(outFile1))
                    {
                        sw.AutoFlush = true;

                        var csv = new CsvWriter(sw);

                        var o = new TypeConverterOptions
                        {
                            DateTimeStyle = DateTimeStyles.AssumeUniversal & DateTimeStyles.AdjustToUniversal
                        };
                        csv.Configuration.TypeConverterOptionsCache.AddOptions<FileEntryNew>(o);

                        var foo = csv.Configuration.AutoMap<FileEntryNew>();

                        foo.Map(m => m.ApplicationName).Index(0);

                        foo.Map(m => m.ProgramId).Index(1);
                        foo.Map(t => t.FileKeyLastWriteTimestamp).ConvertUsing(t =>
                                t.FileKeyLastWriteTimestamp.ToString(_fluentCommandLineParser.Object.DateTimeFormat))
                            .Index(2);
                        foo.Map(m => m.SHA1).Index(3);
                        foo.Map(m => m.IsOsComponent).Index(4);
                        foo.Map(m => m.FullPath).Index(5);
                        foo.Map(m => m.Name).Index(6);
                        foo.Map(m => m.FileExtension).Index(7);

                        foo.Map(t => t.LinkDate).ConvertUsing(t =>
                            t.LinkDate?.ToString(_fluentCommandLineParser.Object.DateTimeFormat)).Index(8);
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


                        csv.Configuration.RegisterClassMap(foo);

                        if (_fluentCommandLineParser.Object.CsvSeparator == false)
                        {
                            csv.Configuration.Delimiter = "\t";
                        }

                        csv.WriteHeader<FileEntryNew>();
                        csv.NextRecord();
                        csv.WriteRecords(cleanList2);
                    }


                    if (_fluentCommandLineParser.Object.IncludeLinked)
                    {
                        outbase1 = $"{ts1}_{hiveName1}_Program entries.{exportExt}";
                        outFile1 = Path.Combine(_fluentCommandLineParser.Object.SaveTo, outbase1);

                        using (var sw = new StreamWriter(outFile1))
                        {
                            sw.AutoFlush = true;

                            var csv = new CsvWriter(sw);

                            var o = new TypeConverterOptions
                            {
                                DateTimeStyle = DateTimeStyles.AssumeUniversal & DateTimeStyles.AdjustToUniversal
                            };
                            csv.Configuration.TypeConverterOptionsCache.AddOptions<ProgramsEntryNew>(o);

                            var foo = csv.Configuration.AutoMap<ProgramsEntryNew>();

                            foo.Map(m => m.ProgramId).Index(0);
                            foo.Map(t => t.KeyLastWriteTimestamp).ConvertUsing(t =>
                                    t.KeyLastWriteTimestamp.ToString(_fluentCommandLineParser.Object.DateTimeFormat))
                                .Index(1);
                            foo.Map(m => m.Name).Index(2);
                            foo.Map(m => m.Version).Index(3);
                            foo.Map(m => m.Publisher).Index(4);

                            foo.Map(t => t.InstallDate).ConvertUsing(row =>
                            {
                                if (row.InstallDate.HasValue)
                                {
                                    return row.InstallDate.Value.ToString(
                                        _fluentCommandLineParser.Object.DateTimeFormat);
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

                            csv.Configuration.RegisterClassMap(foo);

                            if (_fluentCommandLineParser.Object.CsvSeparator == false)
                            {
                                csv.Configuration.Delimiter = "\t";
                            }

                            csv.WriteHeader<ProgramsEntryNew>();
                            csv.NextRecord();
                            csv.WriteRecords(amNew.ProgramsEntries);
                        }

                        outbase1 = $"{ts1}_{hiveName1}_Associated file entries.{exportExt}";
                        outFile1 = Path.Combine(_fluentCommandLineParser.Object.SaveTo, outbase1);

                        using (var sw = new StreamWriter(outFile1))
                        {
                            var csv = new CsvWriter(sw);

                            var o = new TypeConverterOptions
                            {
                                DateTimeStyle = DateTimeStyles.AssumeUniversal & DateTimeStyles.AdjustToUniversal
                            };
                            csv.Configuration.TypeConverterOptionsCache.AddOptions<FileEntryNew>(o);

                            var foo = csv.Configuration.AutoMap<FileEntryNew>();

                            foo.Map(m => m.ApplicationName).Index(0);

                            foo.Map(m => m.ProgramId).Index(1);
                            foo.Map(t => t.FileKeyLastWriteTimestamp).ConvertUsing(t =>
                                    t.FileKeyLastWriteTimestamp.ToString(_fluentCommandLineParser.Object
                                        .DateTimeFormat))
                                .Index(2);
                            foo.Map(m => m.SHA1).Index(3);
                            foo.Map(m => m.IsOsComponent).Index(4);
                            foo.Map(m => m.FullPath).Index(5);
                            foo.Map(m => m.Name).Index(6);
                            foo.Map(m => m.FileExtension).Index(7);

                            foo.Map(t => t.LinkDate).ConvertUsing(t =>
                                t.LinkDate?.ToString(_fluentCommandLineParser.Object.DateTimeFormat)).Index(8);
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

                            csv.Configuration.RegisterClassMap(foo);

                            if (_fluentCommandLineParser.Object.CsvSeparator == false)
                            {
                                csv.Configuration.Delimiter = "\t";
                            }

                            csv.WriteHeader<FileEntryNew>();
                            csv.NextRecord();

                            sw.AutoFlush = true;

                            foreach (var pe in amNew.ProgramsEntries)
                            {
                                var cleanList22 =
                                    pe.FileEntries.Where(t => whitelistHashes1.Contains(t.SHA1) == useBlacklist2)
                                        .ToList();

                                csv.WriteRecords(cleanList22);
                            }
                        }
                    }


                    outbase1 = $"{ts1}_{hiveName1}_ShortCuts.{exportExt}";
                    outFile1 = Path.Combine(_fluentCommandLineParser.Object.SaveTo, outbase1);

                    using (var sw = new StreamWriter(outFile1))
                    {
                        var csv = new CsvWriter(sw);

                        var o = new TypeConverterOptions
                        {
                            DateTimeStyle = DateTimeStyles.AssumeUniversal & DateTimeStyles.AdjustToUniversal
                        };
                        csv.Configuration.TypeConverterOptionsCache.AddOptions<Shortcut>(o);

                        var foo = csv.Configuration.AutoMap<Shortcut>();

                        foo.Map(m => m.KeyName).Index(0);
                        foo.Map(m => m.LnkName).Index(1);

                        foo.Map(t => t.KeyLastWriteTimestamp).ConvertUsing(t =>
                            t.KeyLastWriteTimestamp.ToString(_fluentCommandLineParser.Object.DateTimeFormat)).Index(2);

                        csv.Configuration.RegisterClassMap(foo);

                        if (_fluentCommandLineParser.Object.CsvSeparator == false)
                        {
                            csv.Configuration.Delimiter = "\t";
                        }

                        csv.WriteHeader<Shortcut>();
                        csv.NextRecord();
                        sw.AutoFlush = true;

                        foreach (var sc in amNew.ShortCuts)
                        {
                            csv.WriteRecord(sc);
                            csv.NextRecord();
                        }
                    }

                    outbase1 = $"{ts1}_{hiveName1}_DriveBinaries.{exportExt}";
                    outFile1 = Path.Combine(_fluentCommandLineParser.Object.SaveTo, outbase1);

                    using (var sw = new StreamWriter(outFile1))
                    {
                        var csv = new CsvWriter(sw);


                        var o = new TypeConverterOptions
                        {
                            DateTimeStyle = DateTimeStyles.AssumeUniversal & DateTimeStyles.AdjustToUniversal
                        };
                        csv.Configuration.TypeConverterOptionsCache.AddOptions<DriverBinary>(o);

                        var foo = csv.Configuration.AutoMap<DriverBinary>();

                        foo.Map(m => m.KeyName).Index(0);
                        foo.Map(t => t.KeyLastWriteTimestamp).ConvertUsing(t =>
                            t.KeyLastWriteTimestamp.ToString(_fluentCommandLineParser.Object.DateTimeFormat)).Index(1);
                        foo.Map(t => t.DriverTimeStamp).ConvertUsing(t =>
                            t.DriverTimeStamp?.ToString(_fluentCommandLineParser.Object.DateTimeFormat)).Index(2);
                        foo.Map(t => t.DriverLastWriteTime).ConvertUsing(t =>
                            t.DriverLastWriteTime?.ToString(_fluentCommandLineParser.Object.DateTimeFormat)).Index(3);

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

                        csv.Configuration.RegisterClassMap(foo);

                        if (_fluentCommandLineParser.Object.CsvSeparator == false)
                        {
                            csv.Configuration.Delimiter = "\t";
                        }

                        csv.WriteHeader<DriverBinary>();
                        csv.NextRecord();
                        sw.AutoFlush = true;

                        foreach (var sc in amNew.DriveBinaries)
                        {
                            csv.WriteRecord(sc);
                            csv.NextRecord();
                        }
                    }


                    outbase1 = $"{ts1}_{hiveName1}_DeviceContainers.{exportExt}";
                    outFile1 = Path.Combine(_fluentCommandLineParser.Object.SaveTo, outbase1);

                    using (var sw = new StreamWriter(outFile1))
                    {
                        var csv = new CsvWriter(sw);


                        var o = new TypeConverterOptions
                        {
                            DateTimeStyle = DateTimeStyles.AssumeUniversal & DateTimeStyles.AdjustToUniversal
                        };
                        csv.Configuration.TypeConverterOptionsCache.AddOptions<DeviceContainer>(o);

                        var foo = csv.Configuration.AutoMap<DeviceContainer>();

                        foo.Map(m => m.KeyName).Index(0);
                        foo.Map(t => t.KeyLastWriteTimestamp).ConvertUsing(t =>
                            t.KeyLastWriteTimestamp.ToString(_fluentCommandLineParser.Object.DateTimeFormat)).Index(1);

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

                        csv.Configuration.RegisterClassMap(foo);

                        if (_fluentCommandLineParser.Object.CsvSeparator == false)
                        {
                            csv.Configuration.Delimiter = "\t";
                        }

                        csv.WriteHeader<DeviceContainer>();
                        csv.NextRecord();
                        sw.AutoFlush = true;

                        foreach (var sc in amNew.DeviceContainers)
                        {
                            csv.WriteRecord(sc);
                            csv.NextRecord();
                        }
                    }

                    outbase1 = $"{ts1}_{hiveName1}_DriverPackages.{exportExt}";
                    outFile1 = Path.Combine(_fluentCommandLineParser.Object.SaveTo, outbase1);

                    using (var sw = new StreamWriter(outFile1))
                    {
                        var csv = new CsvWriter(sw);

                        var o = new TypeConverterOptions
                        {
                            DateTimeStyle = DateTimeStyles.AssumeUniversal & DateTimeStyles.AdjustToUniversal
                        };
                        csv.Configuration.TypeConverterOptionsCache.AddOptions<DriverPackage>(o);

                        var foo = csv.Configuration.AutoMap<DriverPackage>();

                        csv.Configuration.RegisterClassMap(foo);

                        foo.Map(m => m.KeyName).Index(0);
                        foo.Map(t => t.KeyLastWriteTimestamp).ConvertUsing(t =>
                            t.KeyLastWriteTimestamp.ToString(_fluentCommandLineParser.Object.DateTimeFormat)).Index(1);
                        foo.Map(t => t.Date)
                            .ConvertUsing(t => t.Date?.ToString(_fluentCommandLineParser.Object.DateTimeFormat))
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

                        if (_fluentCommandLineParser.Object.CsvSeparator == false)
                        {
                            csv.Configuration.Delimiter = "\t";
                        }

                        csv.WriteHeader<DriverPackage>();
                        csv.NextRecord();
                        sw.AutoFlush = true;

                        foreach (var sc in amNew.DriverPackages)
                        {
                            csv.WriteRecord(sc);
                            csv.NextRecord();
                        }
                    }

                    outbase1 = $"{ts1}_{hiveName1}_DevicePnps.{exportExt}";
                    outFile1 = Path.Combine(_fluentCommandLineParser.Object.SaveTo, outbase1);

                    using (var sw = new StreamWriter(outFile1))
                    {
                        var csv = new CsvWriter(sw);

                        var o = new TypeConverterOptions
                        {
                            DateTimeStyle = DateTimeStyles.AssumeUniversal & DateTimeStyles.AdjustToUniversal
                        };
                        csv.Configuration.TypeConverterOptionsCache.AddOptions<DevicePnp>(o);

                        var foo = csv.Configuration.AutoMap<DevicePnp>();

                        foo.Map(m => m.KeyName).Index(0);
                        foo.Map(t => t.KeyLastWriteTimestamp).ConvertUsing(t =>
                            t.KeyLastWriteTimestamp.ToString(_fluentCommandLineParser.Object.DateTimeFormat)).Index(1);

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

                        csv.Configuration.RegisterClassMap(foo);

                        if (_fluentCommandLineParser.Object.CsvSeparator == false)
                        {
                            csv.Configuration.Delimiter = "\t";
                        }

                        csv.WriteHeader<DevicePnp>();
                        csv.NextRecord();
                        sw.AutoFlush = true;

                        foreach (var sc in amNew.DevicePnps)
                        {
                            csv.WriteRecord(sc);
                            csv.NextRecord();
                        }
                    }


                    _logger.Error($"\r\n'{_fluentCommandLineParser.Object.File}' is in new format!");

                    var suffix1 = amNew.UnassociatedFileEntries.Count == 1 ? "y" : "ies";


                    var linked1 = "";
                    if (_fluentCommandLineParser.Object.IncludeLinked)
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
                        if (_fluentCommandLineParser.Object.Blacklist.Length > 0)
                        {
                            list = "blacklist";
                        }

                        _logger.Info($"{UppercaseFirst(list)} hash count: {whitelistHashes1.Count:N0}");

                        _logger.Info("");

                        _logger.Info($"Percentage of total shown based on {list}: {per:P3} ({1 - per:P3} savings)");
                    }

                    _logger.Info("");

                    _logger.Info($"Results saved to: {_fluentCommandLineParser.Object.SaveTo}");


                    _logger.Info("");
                    _logger.Info(
                        $"Total parsing time: {_sw.Elapsed.TotalSeconds:N3} seconds.\r\n");


                    return;
                }

                var am = new AmcacheOld(_fluentCommandLineParser.Object.File,
                    _fluentCommandLineParser.Object.RecoverDeleted, _fluentCommandLineParser.Object.NoTransLogs);


                if (am.ProgramsEntries.Count == 0 && am.UnassociatedFileEntries.Count == 0)
                {
                    _logger.Warn("Hive did not contain program entries nor file entries. Exiting");
                    return;
                }

                _sw.Stop();

                var whitelistHashes = new HashSet<string>();

                var useBlacklist = false;

                if (_fluentCommandLineParser.Object.Blacklist.Length > 0)
                {
                    if (File.Exists(_fluentCommandLineParser.Object.Blacklist))
                    {
                        foreach (var readLine in File.ReadLines(_fluentCommandLineParser.Object.Blacklist))
                        {
                            whitelistHashes.Add(readLine.ToLowerInvariant());
                        }

                        useBlacklist = true;
                    }
                    else
                    {
                        _logger.Warn($"'{_fluentCommandLineParser.Object.Blacklist}' does not exist");
                    }
                }
                else if (_fluentCommandLineParser.Object.Whitelist.Length > 0)
                {
                    if (File.Exists(_fluentCommandLineParser.Object.Whitelist))
                    {
                        foreach (var readLine in File.ReadLines(_fluentCommandLineParser.Object.Whitelist))
                        {
                            whitelistHashes.Add(readLine.ToLowerInvariant());
                        }
                    }
                    else
                    {
                        _logger.Warn($"'{_fluentCommandLineParser.Object.Whitelist}' does not exist");
                    }
                }

                var cleanList =
                    am.UnassociatedFileEntries.Where(t => whitelistHashes.Contains(t.SHA1) == useBlacklist).ToList();
                var totalProgramFileEntries = 0;

                if (Directory.Exists(_fluentCommandLineParser.Object.SaveTo) == false)
                {
                    try
                    {
                        Directory.CreateDirectory(_fluentCommandLineParser.Object.SaveTo);
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(
                            $"There was an error creating directory '{_fluentCommandLineParser.Object.SaveTo}'. Error: {ex.Message} Exiting");
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
                var hiveName = Path.GetFileNameWithoutExtension(_fluentCommandLineParser.Object.File);

                var outbase = $"{ts}_{hiveName}_Unassociated file entries.{exportExt}";
                var outFile = Path.Combine(_fluentCommandLineParser.Object.SaveTo, outbase);

                using (var sw = new StreamWriter(outFile))
                {
                    sw.AutoFlush = true;

                    var csv = new CsvWriter(sw);

                    var o = new TypeConverterOptions
                    {
                        DateTimeStyle = DateTimeStyles.AssumeUniversal & DateTimeStyles.AdjustToUniversal
                    };
                    csv.Configuration.TypeConverterOptionsCache.AddOptions<FileEntryOld>(o);

                    var foo = csv.Configuration.AutoMap<FileEntryOld>();

                    foo.Map(m => m.ProgramName).Index(0);
                    foo.Map(m => m.ProgramID).Index(1);
                    foo.Map(m => m.VolumeID).Index(2);
                    foo.Map(t => t.VolumeIDLastWriteTimestamp).ConvertUsing(t =>
                        t.VolumeIDLastWriteTimestamp.ToString(_fluentCommandLineParser.Object.DateTimeFormat)).Index(3);
                    foo.Map(m => m.FileID).Index(4);
                    foo.Map(t => t.FileIDLastWriteTimestamp).ConvertUsing(t =>
                        t.FileIDLastWriteTimestamp.ToString(_fluentCommandLineParser.Object.DateTimeFormat)).Index(5);
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
                        .ConvertUsing(t => t.Created?.ToString(_fluentCommandLineParser.Object.DateTimeFormat))
                        .Index(24);
                    foo.Map(t => t.LastModified).ConvertUsing(t =>
                        t.LastModified?.ToString(_fluentCommandLineParser.Object.DateTimeFormat)).Index(25);
                    foo.Map(t => t.LastModifiedStore).ConvertUsing(t =>
                        t.LastModifiedStore?.ToString(_fluentCommandLineParser.Object.DateTimeFormat)).Index(26);
                    foo.Map(t => t.LinkDate)
                        .ConvertUsing(t => t.LinkDate?.ToString(_fluentCommandLineParser.Object.DateTimeFormat))
                        .Index(27);
                    foo.Map(m => m.LanguageID).Index(28);
                    foo.Map(m => m.CompanyName).Ignore();
                    foo.Map(m => m.SwitchBackContext).Ignore();


                    csv.Configuration.RegisterClassMap(foo);


                    if (_fluentCommandLineParser.Object.CsvSeparator == false)
                    {
                        csv.Configuration.Delimiter = "\t";
                    }

                    csv.WriteHeader<FileEntryOld>();
                    csv.NextRecord();
                    csv.WriteRecords(cleanList);
                }

                if (_fluentCommandLineParser.Object.IncludeLinked)
                {
                    outbase = $"{ts}_{hiveName}_Program entries.{exportExt}";
                    outFile = Path.Combine(_fluentCommandLineParser.Object.SaveTo, outbase);

                    using (var sw = new StreamWriter(outFile))
                    {
                        sw.AutoFlush = true;

                        var csv = new CsvWriter(sw);

                        var o = new TypeConverterOptions
                        {
                            DateTimeStyle = DateTimeStyles.AssumeUniversal & DateTimeStyles.AdjustToUniversal
                        };
                        csv.Configuration.TypeConverterOptionsCache.AddOptions<ProgramsEntryOld>(o);

                        var foo = csv.Configuration.AutoMap<ProgramsEntryOld>();

                        foo.Map(m => m.ProgramID).Index(0);
                        foo.Map(t => t.LastWriteTimestamp).ConvertUsing(t =>
                            t.LastWriteTimestamp.ToString(_fluentCommandLineParser.Object.DateTimeFormat)).Index(1);
                        foo.Map(m => m.ProgramName_0).Index(2);
                        foo.Map(m => m.ProgramVersion_1).Index(3);
                        foo.Map(m => m.VendorName_2).Index(4);

                        foo.Map(t => t.InstallDateEpoch_a).ConvertUsing(t =>
                            t.InstallDateEpoch_a?.ToString(_fluentCommandLineParser.Object.DateTimeFormat)).Index(5);
                        foo.Map(t => t.InstallDateEpoch_b).ConvertUsing(t =>
                            t.InstallDateEpoch_b?.ToString(_fluentCommandLineParser.Object.DateTimeFormat)).Index(6);

                        foo.Map(m => m.LanguageCode_3).Index(7);
                        foo.Map(m => m.InstallSource_6).Index(8);
                        foo.Map(m => m.UninstallRegistryKey_7).Index(9);
                        foo.Map(m => m.PathsList_d).Index(10);

                        csv.Configuration.RegisterClassMap(foo);

                        if (_fluentCommandLineParser.Object.CsvSeparator == false)
                        {
                            csv.Configuration.Delimiter = "\t";
                        }

                        csv.WriteHeader<ProgramsEntryOld>();
                        csv.NextRecord();
                        csv.WriteRecords(am.ProgramsEntries);
                    }

                    outbase = $"{ts}_{hiveName}_Associated file entries.{exportExt}";
                    outFile = Path.Combine(_fluentCommandLineParser.Object.SaveTo, outbase);

                    using (var sw = new StreamWriter(outFile))
                    {
                        var csv = new CsvWriter(sw);

                        var o = new TypeConverterOptions
                        {
                            DateTimeStyle = DateTimeStyles.AssumeUniversal & DateTimeStyles.AdjustToUniversal
                        };
                        csv.Configuration.TypeConverterOptionsCache.AddOptions<FileEntryOld>(o);

                        var foo = csv.Configuration.AutoMap<FileEntryOld>();

                        foo.Map(m => m.ProgramName).Index(0);
                        foo.Map(m => m.ProgramID).Index(1);
                        foo.Map(m => m.VolumeID).Index(2);
                        foo.Map(t => t.VolumeIDLastWriteTimestamp).ConvertUsing(t =>
                                t.VolumeIDLastWriteTimestamp.ToString(_fluentCommandLineParser.Object.DateTimeFormat))
                            .Index(3);
                        foo.Map(m => m.FileID).Index(4);
                        foo.Map(t => t.FileIDLastWriteTimestamp).ConvertUsing(t =>
                                t.FileIDLastWriteTimestamp.ToString(_fluentCommandLineParser.Object.DateTimeFormat))
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
                            .ConvertUsing(t => t.Created?.ToString(_fluentCommandLineParser.Object.DateTimeFormat))
                            .Index(24);
                        foo.Map(t => t.LastModified).ConvertUsing(t =>
                            t.LastModified?.ToString(_fluentCommandLineParser.Object.DateTimeFormat)).Index(25);
                        foo.Map(t => t.LastModifiedStore).ConvertUsing(t =>
                            t.LastModifiedStore?.ToString(_fluentCommandLineParser.Object.DateTimeFormat)).Index(26);
                        foo.Map(t => t.LinkDate)
                            .ConvertUsing(t => t.LinkDate?.ToString(_fluentCommandLineParser.Object.DateTimeFormat))
                            .Index(27);
                        foo.Map(m => m.LanguageID).Index(28);


                        csv.Configuration.RegisterClassMap(foo);

                        if (_fluentCommandLineParser.Object.CsvSeparator == false)
                        {
                            csv.Configuration.Delimiter = "\t";
                        }

                        csv.WriteHeader<FileEntryOld>();
                        csv.NextRecord();

                        sw.AutoFlush = true;

                        foreach (var pe in am.ProgramsEntries)
                        {
                            var cleanList2 =
                                pe.FileEntries.Where(t => whitelistHashes.Contains(t.SHA1) == useBlacklist).ToList();

                            csv.WriteRecords(cleanList2);
                        }
                    }
                }

                _logger.Error($"\r\n'{_fluentCommandLineParser.Object.File}' is in old format!");

                var suffix = am.UnassociatedFileEntries.Count == 1 ? "y" : "ies";


                var linked = "";
                if (_fluentCommandLineParser.Object.IncludeLinked)
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
                    if (_fluentCommandLineParser.Object.Blacklist.Length > 0)
                    {
                        list = "blacklist";
                    }

                    _logger.Info($"{UppercaseFirst(list)} hash count: {whitelistHashes.Count:N0}");

                    _logger.Info("");

                    _logger.Info($"Percentage of total shown based on {list}: {per:P3} ({1 - per:P3} savings)");
                }

                _logger.Info("");

                _logger.Info($"Results saved to: {_fluentCommandLineParser.Object.SaveTo}");


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
                    _logger.Error($"There was an error: {ex.Message}");
                    _logger.Error($"Stacktrace: {ex.StackTrace}");
                    _logger.Info("");
                    _logger.Error(
                        $"Please send '{_fluentCommandLineParser.Object.File}' to saericzimmerman@gmail.com in order to fix the issue");
                }
            }
        }

        private static void SetupNLog()
        {
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
    }


    internal class ApplicationArguments
    {
        public string File { get; set; }

        //       public string Extension { get; set; } = string.Empty;
        public string Whitelist { get; set; } = string.Empty;

        public string Blacklist { get; set; } = string.Empty;
        public string SaveTo { get; set; } = string.Empty;
        public bool IncludeLinked { get; set; } = false;
        public bool RecoverDeleted { get; set; } = false;
        public bool NoTransLogs { get; set; } = false;
        public string DateTimeFormat { get; set; }
        public bool PreciseTimestamps { get; set; }
        public bool CsvSeparator { get; set; }
    }
}