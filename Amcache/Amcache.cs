using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Amcache.Classes;
using NLog;
using Registry;

namespace Amcache
{
    public class Amcache
    {
        private static Logger _logger;
        public List<FileEntry> UnassociatedFileEntries { get; }
        public List<ProgramsEntry> ProgramsEntries { get; }

        private const int ProductName = 0;
        private const int CompanyName = 0x1;
        private const int FileVersionNumber = 0x2;
        private const int LanguageCode = 0x3;
        private const int SwitchBackContext = 0x4;
        private const int FileVersionString = 0x5;
        private const int FileSize = 0x6;
        private const int PEHeaderSize = 0x7;
        private const int PEHeaderHash = 0x8;
        private const int PEHeaderChecksum = 0x9;
        private const int Unknown1 = 0xa;
        private const int Unknown2 = 0xb;
        private const int FileDescription = 0xc;
        private const int Unknown3 = 0xd;
        private const int CompileTime = 0xf;
        private const int Unknown4 = 0x10;
        private const int LastModified = 0x11;
        private const int Created = 0x12;
        private const int FullPath = 0x15;
        private const int Unknown5 = 0x16;
        private const int LastModified2 = 0x17;
        private const int ProgramID = 0x100;
        private const int SHA1 = 0x101;

        public Amcache(string hive)
        {
            _logger = LogManager.GetCurrentClassLogger();

            var counter = 0;

        try
        {
            var reg = new RegistryHive(hive);
            reg.RecoverDeleted = true;
            reg.ParseHive();

            var fileKey = reg.GetKey(@"Root\File");
            var programsKey = reg.GetKey(@"Root\Programs");

            ProgramsEntries = new List<ProgramsEntry>();

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
                            Dword5 = Int32.Parse(value.ValueData);
                            break;
                        case "6":
                            InstallSource6 = value.ValueData;
                            break;
                        case "7":
                            UninstallKey7 = value.ValueData;
                            break;
                        case "a":
                            EpochA = DateTimeOffset.FromUnixTimeSeconds(Int64.Parse(value.ValueData));

                            break;
                        case "b":
                            EpochB =
                           DateTimeOffset.FromUnixTimeSeconds(Int64.Parse(value.ValueData));
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
                            Dword13 = Int32.Parse(value.ValueData);
                            break;
                        case "14":
                            Dword13 = Int32.Parse(value.ValueData);
                            break;
                        case "15":
                            Dword13 = Int32.Parse(value.ValueData);
                            break;
                        case "16":
                            UnknownBytes = value.ValueDataRaw;
                            break;
                        case "17":
                            Qword17 = Int64.Parse(value.ValueData);
                            break;
                        case "18":
                            Dword18 = Int32.Parse(value.ValueData);
                            break;
                        case "Files":
                            RawFiles = value.ValueData;
                            break;
                        default:
                            _logger.Warn($"Unknown value name in Program: {value.ValueName}");
                            break;
                    }
                }

                var pe = new ProgramsEntry(ProgramName0, ProgramVersion1, VenderName2, LocaleID3, InstallSource6, UninstallKey7, Guid10, Guid12, UninstallGuid11, Dword5, Dword13, Dword14, Dword15, UnknownBytes, Qword17, Dword18, EpochA, EpochB, PathListd, Guidf, RawFiles, registryKey.KeyName, registryKey.LastWriteTime.Value);

                ProgramsEntries.Add(pe);

            }

            //Dump stuff from Programs to a file for testing, etc
            //ProgramsEntries.ForEach(f=>File.AppendAllText(@"C:\temp\programs.txt", $"{f.ProgramName_0} EpochA: {f.InstallDateEpoch_a} EpochB: {f.InstallDateEpoch_a}\r\n"));


            //For each Programs entry, add the related Files entries from Files\Volume subkey

            UnassociatedFileEntries = new List<FileEntry>();

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
                    DateTimeOffset lm2 = new DateTimeOffset(1970, 1, 1, 0, 0, 0, TimeSpan.FromHours(0));

                    var hasLinkedProgram = false;

                    //these are the files executed from the volume
                    foreach (var keyValue in subKey.Values)
                    {
                        var keyVal = Int32.Parse(keyValue.ValueName, NumberStyles.HexNumber);

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
                                lm2 = DateTimeOffset.FromFileTime(Int64.Parse(keyValue.ValueData));
                                break;
                            case ProgramID:
                                progID = keyValue.ValueData;

                                var program = ProgramsEntries.SingleOrDefault(t => t.ProgramID == progID);
                                if (program != null)
                                {
                                    hasLinkedProgram = true;
                                }

                                break;
                            case SHA1:
                                sha = keyValue.ValueData;
                                break;
                            default:
                                _logger.Warn($"Unknown value name: 0x{keyVal:X}");
                                break;
                        }
                    }

                    var fe = new FileEntry(prodName, progID, sha, fullPath, lm2, registryKey.KeyName, registryKey.LastWriteTime.Value, subKey.KeyName, subKey.LastWriteTime.Value);

                    if (hasLinkedProgram)
                    {
                        var program = ProgramsEntries.SingleOrDefault(t => t.ProgramID == fe.ProgramID);
                        program.FileEntries.Add(fe);
                    }
                    else
                    {
                        UnassociatedFileEntries.Add(fe);
                    }



                }
            }



        }
        catch (Exception ex)
        {
            _logger.Error($"There was an error: {ex.Message}");
        }
    }
    }
}
