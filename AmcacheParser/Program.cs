using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using Amcache;
using Amcache.Classes;
using CsvHelper;
using CsvHelper.Configuration;
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
                .WithDescription("AmcacheOld.hve file to parse. Required").Required();

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
                .WithDescription("Directory where results will be saved. Required");


            _fluentCommandLineParser.Setup(arg => arg.DateTimeFormat)
                .As("dt")
                .WithDescription(
                    "The custom date/time format to use when displaying timestamps. See https://goo.gl/CNVq0k for options. Default is: yyyy-MM-dd HH:mm:ss")
                .SetDefault("yyyy-MM-dd HH:mm:ss");

            _fluentCommandLineParser.Setup(arg => arg.PreciseTimestamps)
                .As("mp")
                .WithDescription(
                    "When true, display higher precision for timestamps. Default is false").SetDefault(false);

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


            _sw = new Stopwatch();
            _sw.Start();

            try
            {
                _sw.Start();

                //determine format here
                //fork accordingly

                if (Helper.IsNewFormat(_fluentCommandLineParser.Object.File))
                {
                    var amNew = new AmcacheNew(_fluentCommandLineParser.Object.File,
                        _fluentCommandLineParser.Object.RecoverDeleted);

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

                    var outbase1 = $"{ts1}_{hiveName1}_Unassociated file entries.tsv";
                    var outFile1 = Path.Combine(_fluentCommandLineParser.Object.SaveTo, outbase1);

                    using (var sw = new StreamWriter(outFile1))
                    {
                        sw.AutoFlush = true;

                        var csv = new CsvWriter(sw);
                        csv.Configuration.RegisterClassMap(
                            new FECacheOutputMapNew(_fluentCommandLineParser.Object.DateTimeFormat));
                        csv.Configuration.Delimiter = "\t";

                        csv.WriteHeader<FileEntryNew>();
                        csv.WriteRecords(cleanList2);
                    }


                    if (_fluentCommandLineParser.Object.IncludeLinked)
                    {
                        outbase1 = $"{ts1}_{hiveName1}_Program entries.tsv";
                        outFile1 = Path.Combine(_fluentCommandLineParser.Object.SaveTo, outbase1);

                        using (var sw = new StreamWriter(outFile1))
                        {
                            sw.AutoFlush = true;

                            var csv = new CsvWriter(sw);
                            csv.Configuration.RegisterClassMap(
                                new PECacheOutputMapNew(_fluentCommandLineParser.Object.DateTimeFormat));
                            csv.Configuration.Delimiter = "\t";

                            csv.WriteHeader<ProgramsEntryNew>();
                            csv.WriteRecords(amNew.ProgramsEntries);
                        }

                        outbase1 = $"{ts1}_{hiveName1}_Associated file entries.tsv";
                        outFile1 = Path.Combine(_fluentCommandLineParser.Object.SaveTo, outbase1);

                        using (var sw = new StreamWriter(outFile1))
                        {
                            var csv = new CsvWriter(sw);
                            csv.Configuration.RegisterClassMap(
                                new FECacheOutputMapNew(_fluentCommandLineParser.Object.DateTimeFormat));
                            csv.Configuration.Delimiter = "\t";

                            csv.WriteHeader<FileEntryNew>();

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


                    //DUMP NEW STUFF HERE


                    outbase1 = $"{ts1}_{hiveName1}_ShortCuts.tsv";
                    outFile1 = Path.Combine(_fluentCommandLineParser.Object.SaveTo, outbase1);

                    using (var sw = new StreamWriter(outFile1))
                    {
                        var csv = new CsvWriter(sw);
                        csv.Configuration.RegisterClassMap(
                            new ShortCuts(_fluentCommandLineParser.Object.DateTimeFormat));
                        csv.Configuration.Delimiter = "\t";

                        csv.WriteHeader<Shortcut>();

                        sw.AutoFlush = true;

                        foreach (var sc in amNew.ShortCuts)
                        {
                            csv.WriteRecord(sc);
                        }
                    }

                    outbase1 = $"{ts1}_{hiveName1}_DriveBinaries.tsv";
                    outFile1 = Path.Combine(_fluentCommandLineParser.Object.SaveTo, outbase1);

                    using (var sw = new StreamWriter(outFile1))
                    {
                        var csv = new CsvWriter(sw);
                        csv.Configuration.RegisterClassMap(
                            new DriverBinaries(_fluentCommandLineParser.Object.DateTimeFormat));
                        csv.Configuration.Delimiter = "\t";

                        csv.WriteHeader<DriverBinary>();

                        sw.AutoFlush = true;

                        foreach (var sc in amNew.DriveBinaries)
                        {
                            csv.WriteRecord(sc);
                        }
                    }


                    outbase1 = $"{ts1}_{hiveName1}_DeviceContainers.tsv";
                    outFile1 = Path.Combine(_fluentCommandLineParser.Object.SaveTo, outbase1);

                    using (var sw = new StreamWriter(outFile1))
                    {
                        var csv = new CsvWriter(sw);
                        csv.Configuration.RegisterClassMap(
                            new DeviceContainers(_fluentCommandLineParser.Object.DateTimeFormat));
                        csv.Configuration.Delimiter = "\t";

                        csv.WriteHeader<DeviceContainer>();

                        sw.AutoFlush = true;

                        foreach (var sc in amNew.DeviceContainers)
                        {
                            csv.WriteRecord(sc);
                        }
                    }

                    outbase1 = $"{ts1}_{hiveName1}_DriverPackages.tsv";
                    outFile1 = Path.Combine(_fluentCommandLineParser.Object.SaveTo, outbase1);

                    using (var sw = new StreamWriter(outFile1))
                    {
                        var csv = new CsvWriter(sw);
                        csv.Configuration.RegisterClassMap(
                            new DriverPackages(_fluentCommandLineParser.Object.DateTimeFormat));
                        csv.Configuration.Delimiter = "\t";

                        csv.WriteHeader<DriverPackage>();

                        sw.AutoFlush = true;

                        foreach (var sc in amNew.DriverPackages)
                        {
                            csv.WriteRecord(sc);
                        }
                    }

                    outbase1 = $"{ts1}_{hiveName1}_DevicePnps.tsv";
                    outFile1 = Path.Combine(_fluentCommandLineParser.Object.SaveTo, outbase1);

                    using (var sw = new StreamWriter(outFile1))
                    {
                        var csv = new CsvWriter(sw);
                        csv.Configuration.RegisterClassMap(
                            new DevicePnps(_fluentCommandLineParser.Object.DateTimeFormat));
                        csv.Configuration.Delimiter = "\t";

                        csv.WriteHeader<DevicePnp>();

                        sw.AutoFlush = true;

                        foreach (var sc in amNew.DevicePnps)
                        {
                            csv.WriteRecord(sc);
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
                        _logger.Info($"Total short cuts found: {amNew.ShortCuts.Count:N0}");
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
                        $"Total search time: {_sw.Elapsed.TotalSeconds:N3} seconds.");


                    return;
                }

                var am = new AmcacheOld(_fluentCommandLineParser.Object.File,
                    _fluentCommandLineParser.Object.RecoverDeleted);


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

                var outbase = $"{ts}_{hiveName}_Unassociated file entries.tsv";
                var outFile = Path.Combine(_fluentCommandLineParser.Object.SaveTo, outbase);

                using (var sw = new StreamWriter(outFile))
                {
                    sw.AutoFlush = true;

                    var csv = new CsvWriter(sw);
                    csv.Configuration.RegisterClassMap(
                        new FECacheOutputMapOld(_fluentCommandLineParser.Object.DateTimeFormat));
                    csv.Configuration.Delimiter = "\t";

                    csv.WriteHeader<FileEntryOld>();
                    csv.WriteRecords(cleanList);
                }

                if (_fluentCommandLineParser.Object.IncludeLinked)
                {
                    outbase = $"{ts}_{hiveName}_Program entries.tsv";
                    outFile = Path.Combine(_fluentCommandLineParser.Object.SaveTo, outbase);

                    using (var sw = new StreamWriter(outFile))
                    {
                        sw.AutoFlush = true;

                        var csv = new CsvWriter(sw);
                        csv.Configuration.RegisterClassMap(
                            new PECacheOutputMapOld(_fluentCommandLineParser.Object.DateTimeFormat));
                        csv.Configuration.Delimiter = "\t";

                        csv.WriteHeader<ProgramsEntryOld>();
                        csv.WriteRecords(am.ProgramsEntries);
                    }

                    outbase = $"{ts}_{hiveName}_Associated file entries.tsv";
                    outFile = Path.Combine(_fluentCommandLineParser.Object.SaveTo, outbase);

                    using (var sw = new StreamWriter(outFile))
                    {
                        var csv = new CsvWriter(sw);
                        csv.Configuration.RegisterClassMap(
                            new FECacheOutputMapOld(_fluentCommandLineParser.Object.DateTimeFormat));
                        csv.Configuration.Delimiter = "\t";

                        csv.WriteHeader<FileEntryOld>();

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
                    $"Total search time: {_sw.Elapsed.TotalSeconds:N3} seconds.");
            }
            catch (Exception ex)
            {
                _logger.Error($"There was an error: {ex.Message}");
                _logger.Error($"Stacktrace: {ex.StackTrace}");
                _logger.Info("");
                _logger.Error(
                    $"Please send '{_fluentCommandLineParser.Object.File}' to saericzimmerman@gmail.com in order to fix the issue");
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
        public string DateTimeFormat { get; set; }
        public bool PreciseTimestamps { get; set; }
    }

    public sealed class FECacheOutputMapOld : CsvClassMap<FileEntryOld>
    {
        public FECacheOutputMapOld(string dateformat)
        {
            Map(m => m.ProgramName);
            Map(m => m.ProgramID);
            Map(m => m.VolumeID);
            Map(m => m.VolumeIDLastWriteTimestamp).TypeConverterOption(dateformat);
            Map(m => m.FileID);
            Map(m => m.FileIDLastWriteTimestamp).TypeConverterOption(dateformat);
            Map(m => m.SHA1);
            Map(m => m.FullPath);
            Map(m => m.FileExtension);
            Map(m => m.MFTEntryNumber);
            Map(m => m.MFTSequenceNumber);
            Map(m => m.FileSize);
            Map(m => m.FileVersionString);
            Map(m => m.FileVersionNumber);
            Map(m => m.FileDescription);

            Map(m => m.PEHeaderSize);
            Map(m => m.PEHeaderHash);
            Map(m => m.PEHeaderChecksum);

            Map(m => m.Created).TypeConverterOption(dateformat);
            Map(m => m.LastModified).TypeConverterOption(dateformat);
            Map(m => m.LastModified2).TypeConverterOption(dateformat);
            Map(m => m.CompileTime).TypeConverterOption(dateformat);
            Map(m => m.LanguageID);
        }
    }

    public sealed class PECacheOutputMapOld : CsvClassMap<ProgramsEntryOld>
    {
        public PECacheOutputMapOld(string dateformat)
        {
            Map(m => m.ProgramID);
            Map(m => m.LastWriteTimestamp).TypeConverterOption(dateformat);
            Map(m => m.ProgramName_0);
            Map(m => m.ProgramVersion_1);
            Map(m => m.VendorName_2);

            Map(m => m.InstallDateEpoch_a).TypeConverterOption(dateformat);
            Map(m => m.InstallDateEpoch_b).TypeConverterOption(dateformat);

            Map(m => m.LanguageCode_3);
            Map(m => m.InstallSource_6);
            Map(m => m.UninstallRegistryKey_7);
            Map(m => m.PathsList_d);
        }
    }


    public sealed class PECacheOutputMapNew : CsvClassMap<ProgramsEntryNew>
    {
        public PECacheOutputMapNew(string dateformat)
        {
            Map(m => m.ProgramId);
            Map(m => m.KeyLastWriteTimestamp).TypeConverterOption(dateformat);
            Map(m => m.Name);
            Map(m => m.Version);
            Map(m => m.Publisher);

            Map(m => m.InstallDate).TypeConverterOption(dateformat);
            Map(m => m.OSVersionAtInstallTime);

            Map(m => m.BundleManifestPath);
            Map(m => m.HiddenArp);
            Map(m => m.InboxModernApp);
            Map(m => m.Language);
            Map(m => m.ManifestPath);
            Map(m => m.MsiPackageCode);
            Map(m => m.MsiProductCode);

            Map(m => m.PackageFullName);
            Map(m => m.ProgramInstanceId);
            Map(m => m.RegistryKeyPath);
            Map(m => m.RootDirPath);


            Map(m => m.Type);
            Map(m => m.Source);
            Map(m => m.StoreAppType);

            Map(m => m.UninstallString);
        }
    }

    public sealed class ShortCuts : CsvClassMap<Shortcut>
    {
        public ShortCuts(string dateformat)
        {
            Map(m => m.KeyName);
            Map(m => m.LnkName);
            Map(m => m.KeyLastWriteTimestamp).TypeConverterOption(dateformat);
        }
    }

    public sealed class FECacheOutputMapNew : CsvClassMap<FileEntryNew>
    {
        public FECacheOutputMapNew(string dateformat)
        {
            Map(m => m.ApplicationName);

            Map(m => m.ProgramId);
            Map(m => m.FileKeyLastWriteTimestamp).TypeConverterOption(dateformat);
            Map(m => m.SHA1);
            Map(m => m.IsOsComponent);
            Map(m => m.FullPath);
            Map(m => m.Name);
            Map(m => m.FileExtension);

            Map(m => m.LinkDate).TypeConverterOption(dateformat);
            Map(m => m.ProductName);

            Map(m => m.Size);

            Map(m => m.Version);
            Map(m => m.ProductVersion);

            Map(m => m.LongPathHash);

            Map(m => m.BinaryType);
            Map(m => m.IsPeFile);

            Map(m => m.BinFileVersion);
            Map(m => m.BinProductVersion);

            Map(m => m.Language);
        }
    }

    public sealed class DriverBinaries : CsvClassMap<DriverBinary>
    {
        public DriverBinaries(string dateformat)
        {
            Map(m => m.KeyName);
            Map(m => m.KeyLastWriteTimestamp).TypeConverterOption(dateformat);
            Map(m => m.DriverTimeStamp).TypeConverterOption(dateformat);
            Map(m => m.DriverLastWriteTime).TypeConverterOption(dateformat);


            Map(m => m.DriverName);

            Map(m => m.DriverInBox);
            Map(m => m.DriverIsKernelMode);
            Map(m => m.DriverSigned);
            Map(m => m.DriverCheckSum);
            Map(m => m.DriverCompany);
            Map(m => m.DriverId);
            Map(m => m.DriverPackageStrongName);
            Map(m => m.DriverType);
            Map(m => m.DriverVersion);
            Map(m => m.ImageSize);
            Map(m => m.Inf);
            Map(m => m.Product);
            Map(m => m.ProductVersion);
            Map(m => m.Service);
            Map(m => m.WdfVersion);
        }
    }

    public sealed class DevicePnps : CsvClassMap<DevicePnp>
    {
        public DevicePnps(string dateformat)
        {
            Map(m => m.KeyName);
            Map(m => m.KeyLastWriteTimestamp).TypeConverterOption(dateformat);

            Map(m => m.BusReportedDescription);

            Map(m => m.Class);
            Map(m => m.ClassGuid);
            Map(m => m.Compid);
            Map(m => m.ContainerId);
            Map(m => m.Description);
            Map(m => m.DriverId);
            Map(m => m.DriverPackageStrongName);
            Map(m => m.DriverName);
            Map(m => m.DriverVerDate);
            Map(m => m.DriverVerVersion);
            Map(m => m.Enumerator);
            Map(m => m.HWID);
            Map(m => m.Inf);
            Map(m => m.InstallState);
            Map(m => m.Manufacturer);
            Map(m => m.MatchingId);
            Map(m => m.Model);
            Map(m => m.ParentId);
            Map(m => m.ProblemCode);
            Map(m => m.Provider);
            Map(m => m.Service);
            Map(m => m.Stackid);
        }
    }


    public sealed class DeviceContainers : CsvClassMap<DeviceContainer>
    {
        public DeviceContainers(string dateformat)
        {
            Map(m => m.KeyName);
            Map(m => m.KeyLastWriteTimestamp).TypeConverterOption(dateformat);

            Map(m => m.Categories);

            Map(m => m.DiscoveryMethod);
            Map(m => m.FriendlyName);
            Map(m => m.Icon);
            Map(m => m.IsActive);
            Map(m => m.IsConnected);
            Map(m => m.IsMachineContainer);
            Map(m => m.IsNetworked);
            Map(m => m.IsPaired);
            Map(m => m.Manufacturer);
            Map(m => m.ModelId);
            Map(m => m.ModelName);
            Map(m => m.ModelNumber);
            Map(m => m.PrimaryCategory);
            Map(m => m.State);
        }
    }

    public sealed class DriverPackages : CsvClassMap<DriverPackage>
    {
        public DriverPackages(string dateformat)
        {
            Map(m => m.KeyName);
            Map(m => m.KeyLastWriteTimestamp).TypeConverterOption(dateformat);
            Map(m => m.Date).TypeConverterOption(dateformat);

            Map(m => m.Class);


            Map(m => m.Directory);
            Map(m => m.DriverInBox);
            Map(m => m.Hwids);
            Map(m => m.Inf);
            Map(m => m.Provider);
            Map(m => m.SubmissionId);
            Map(m => m.SYSFILE);
            Map(m => m.Version);
        }
    }
}