using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using AmcacheParser.Classes;
using Fclp;
using Microsoft.Win32;
using erzReg = Registry;
using NLog;
using NLog.Config;
using NLog.Fluent;
using NLog.Targets;

namespace AmcacheParser
{
    class Program
    {
        private static Logger _logger;
        private static Stopwatch _sw;

        private static bool CheckForDotnet46()
        {
            using (
                var ndpKey =
                    RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32)
                        .OpenSubKey("SOFTWARE\\Microsoft\\NET Framework Setup\\NDP\\v4\\Full\\"))
            {
                var releaseKey = Convert.ToInt32(ndpKey.GetValue("Release"));

                return (releaseKey >= 393295);
            }
        }


        const int  ProductName = 0;
        const int CompanyName = 0x1;
        const int FileVersionNumber = 0x2;
        const int LanguageCode = 0x3;
        const int SwitchBackContext = 0x4;
        const int FileVersionString = 0x5;
        const int FileSize = 0x6;
        const int PEHeaderSize = 0x7;
        const int PEHeaderHash = 0x8;
        const int PEHeaderChecksum = 0x9;
        const int Unknown1 = 0xa;
        const int Unknown2 = 0xb;
        const int FileDescription = 0xc;
        const int Unknown3 = 0xd;
        const int CompileTime = 0xf;
        const int Unknown4 = 0x10;
        const int LastModified = 0x11;
        const int Created = 0x12;
        const int FullPath = 0x15;
        const int Unknown5 = 0x16;
        const int LastModified2 = 0x17;
        const int ProgramID = 0x100;
        const int SHA1 = 0x101;

       


        static void Main(string[] args)
        {
            SetupNLog();

            _logger = LogManager.GetCurrentClassLogger();

            if (!CheckForDotnet46())
            {
                _logger.Warn(".net 4.6 not detected. Please install .net 4.6 and try again.");
                return;
            }

            var p = new FluentCommandLineParser<ApplicationArguments>
            {
                IsCaseSensitive = false
            };

            p.Setup(arg => arg.File)
                .As('f')
                .WithDescription("Amcache.hve file to parse. This is required").Required();

            p.Setup(arg => arg.Extension)
                .As('e')
                .WithDescription("File extension to include. Default is all extensions. dll would include only files ending in .dll, exe would include only .exe files");

            var header =
                $"AmcacheParser version {Assembly.GetExecutingAssembly().GetName().Version}" +
                "\r\n\r\nAuthor: Eric Zimmerman (saericzimmerman@gmail.com)" +
                "\r\nhttps://github.com/EricZimmerman/AmcacheParser";

            var footer = @"Examples: AmcacheParser.exe -f ""C:\Temp\UsrClass 1.dat"" --ls URL" + "\r\n\t " +
                         @" AmcacheParser.exe -f ""C:\Temp\someFile.txt"" --lr guid" + "\r\n\t " +
                         @" AmcacheParser.exe -f ""C:\Temp\someOtherFile.txt"" --lr cc -sa" + "\r\n\t " +
                         @" AmcacheParser.exe -f ""C:\Temp\someOtherFile.txt"" --lr cc -sa -m 15 -x 22" + "\r\n\t " +
                         @" AmcacheParser.exe -f ""C:\Temp\UsrClass 1.dat"" --ls mui -sl" + "\r\n\t ";

            p.SetupHelp("?", "help").WithHeader(header).Callback(text => _logger.Info(text + "\r\n" + footer));

            var result = p.Parse(args);

            if (result.HelpCalled)
            {
                return;
            }

            if (result.HasErrors)
            {
                _logger.Error("");
                _logger.Error(result.ErrorText);

                p.HelpOption.ShowHelp(p.Options);

                return;
            }

            if (!File.Exists(p.Object.File))
            {
                _logger.Warn($"'{p.Object.File}' not found. Exiting");
                return;
            }

                _logger.Info(header);
                _logger.Info("");
   

            _sw = new Stopwatch();
            _sw.Start();

            var counter = 0;

            try
            {
                var reg = new erzReg.RegistryHive(p.Object.File);
                reg.RecoverDeleted = true;
                reg.ParseHive();

                var fileKey = reg.GetKey(@"Root\File");
                var programsKey = reg.GetKey(@"Root\Programs");

                var ProgramsEntries = new List<ProgramsEntry>();

                //First, we get data for all the Program entries under Programs key

                foreach (var registryKey in programsKey.SubKeys)
                {
                    var ProgramName0 = "";
                    var ProgramVersion1 = "";
                    var Guid10 = "";
                    var UninstallGuid11 = "";
                    var Guid12 = "";
                    var Dword13 = 0;
                    var Dword14 = 0;
                    var Dword15 = 0;
                    var UnknownBytes = new byte[0];
                    long Qword17 = 0;
                    var Dword18 = 0;
                    var VenderName2 = "";
                    var LocaleID3 = "";
                    var Dword5 = 0;
                    var InstallSource6 = "";
                    var UninstallKey7 = "";
                    DateTimeOffset EpochA = DateTimeOffset.FromUnixTimeSeconds(0);
                    DateTimeOffset EpochB = DateTimeOffset.FromUnixTimeSeconds(0);
                    var PathListd = "";
                    var Guidf = "";
                    var RawFiles = "";

                    foreach (var value in registryKey.Values)
                    {
                        switch (value.ValueName)
                        {
                            case "0":
                                ProgramName0 = value.ValueData;
                                break;
                            case "1":
                                ProgramVersion1 = value.ValueData;
                                break;
                            case "2":
                                VenderName2 = value.ValueData;
                                break;
                            case "3":
                                LocaleID3 = value.ValueData;
                                break;
                            case "5":
                                Dword5 = int.Parse(value.ValueData);
                                break;
                            case "6":
                                InstallSource6 = value.ValueData;
                                break;
                            case "7":
                                UninstallKey7 = value.ValueData;
                                break;
                            case "a":
                                EpochA = DateTimeOffset.FromUnixTimeSeconds(long.Parse(value.ValueData));

                                break;
                            case "b":
                                EpochB =
                               DateTimeOffset.FromUnixTimeSeconds(long.Parse(value.ValueData));
                                break;
                            case "d":
                                PathListd = value.ValueData;
                                break;
                            case "f":
                                Guidf = value.ValueData;
                                break;
                            case "10":
                                Guid10 = value.ValueData;
                                break;
                            case "11":
                                UninstallGuid11 = value.ValueData;
                                break;
                            case "12":
                                Guid12 = value.ValueData;
                                break;
                            case "13":
                                Dword13= int.Parse(value.ValueData);
                                break;
                            case "14":
                                Dword13 = int.Parse(value.ValueData);
                                break;
                            case "15":
                                Dword13 = int.Parse(value.ValueData);
                                break;
                            case "16":
                                UnknownBytes = value.ValueDataRaw;
                                break;
                            case "17":
                                Qword17 = long.Parse(value.ValueData);
                                break;
                            case "18":
                                Dword18 = int.Parse(value.ValueData);
                                break;
                            case "Files":
                                RawFiles = value.ValueData;
                                break;
                            default:
                                _logger.Warn($"Unknown value name in Program: {value.ValueName}");
                                break;
                        }
                    }

                        var pe = new ProgramsEntry(ProgramName0, ProgramVersion1, VenderName2, LocaleID3, InstallSource6, UninstallKey7, Guid10, Guid12, UninstallGuid11, Dword5, Dword13, Dword14, Dword15, UnknownBytes, Qword17, Dword18, EpochA, EpochB, PathListd, Guidf, RawFiles,registryKey.KeyName,registryKey.LastWriteTime.Value);

                        ProgramsEntries.Add(pe);
                  
                 }

                //Dump stuff from Programs to a file for testing, etc
                //ProgramsEntries.ForEach(f=>File.AppendAllText(@"C:\temp\programs.txt", $"{f.ProgramName_0} EpochA: {f.InstallDateEpoch_a} EpochB: {f.InstallDateEpoch_a}\r\n"));


                //For each Programs entry, add the related Files entries from Files\Volume subkey

                var unassociatedFiles = new List<FileEntry>();

                foreach (var registryKey in fileKey.SubKeys)
                {
                    //These are the guids for volumes
                    foreach (var subKey in registryKey.SubKeys)
                    {
                        counter += 1;

                        var prodName = "";
                        var fullPath = "";
                        var progID = "";
                        var sha = "";
                        DateTimeOffset lm2 = new DateTimeOffset(1970,1,1,0,0,0,TimeSpan.FromHours(0));

                        var hasLinkedProgram = false;

                        //these are the files executed from the volume
                        foreach (var keyValue in subKey.Values)
                        {
                            var keyVal = int.Parse(keyValue.ValueName, NumberStyles.HexNumber);

                            switch (keyVal)
                            {
                                case ProductName:
                                    prodName = keyValue.ValueData;
                                    break;
                                case CompanyName:
                                    break;
                                case FileVersionNumber:
                                    break;
                                case LanguageCode:
                                    break;
                                case SwitchBackContext:
                                    break;
                                case FileVersionString:
                                    break;
                                case FileSize:
                                    break;
                                case PEHeaderSize:
                                    break;
                                case PEHeaderHash:
                                    break;
                                case PEHeaderChecksum:
                                    break;
                                case Unknown1:
                                    break;
                                case Unknown2:
                                    break;
                                case FileDescription:
                                    break;
                                case Unknown3:
                                    break;
                                case CompileTime:
                                    break;
                                case Unknown4:
                                    break;
                                case LastModified:
                                    break;
                                case Created:
                                    break;
                                case FullPath:
                                    fullPath = keyValue.ValueData;
                                    break;
                                case Unknown5:
                                    break;
                                case LastModified2:
                                    lm2 = DateTimeOffset.FromFileTime(long.Parse(keyValue.ValueData));
                                    break;
                                case ProgramID:
                                    progID = keyValue.ValueData;

                                    var program = ProgramsEntries.SingleOrDefault(t => t.ProgramID == progID);
                                    if (program != null)
                                    {
                                        //_logger.Info(program.ProgramVersion_1);
                                        hasLinkedProgram = true;
                                    }
//                                    else
//                                    {
//                                        _logger.Error($"{subKey.KeyName} has no linking Program value");
//                                    }
                                    
                                    break;
                                case SHA1:
                                    sha = keyValue.ValueData;
                                    break;
                                default:
                                    _logger.Warn($"Unknown value name: 0x{keyVal:X}");
                                    break;
                            }
                        }

                        var fe = new FileEntry(prodName, progID, sha, fullPath, lm2,registryKey.KeyName,registryKey.LastWriteTime.Value,subKey.KeyName,subKey.LastWriteTime.Value);

                        if (hasLinkedProgram)
                        {
                            var program = ProgramsEntries.SingleOrDefault(t => t.ProgramID == fe.ProgramID);
                            program.FileEntries.Add(fe);
                        }
                        else
                        {
                            unassociatedFiles.Add(fe);
                        }

                        

                    }
                }

                _sw.Stop();

                var suffix = counter == 1 ? "" : "s";

                _logger.Info("");
                _logger.Info(
                    $"Found {counter:N0} string{suffix} in {_sw.Elapsed.TotalSeconds:N3} seconds.");

            }
            catch (Exception ex)
            {
                _logger.Error($"There was an error: {ex.Message}");              
            }

            _logger.Warn("Press a key to exit");
            Console.ReadKey();

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
    }

    internal class ApplicationArguments
    {
        public string File { get; set; }
        public string Extension { get; set; } = string.Empty;

    }
}
