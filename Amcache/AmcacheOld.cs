using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Amcache.Classes;
using NLog;
using Registry;

namespace Amcache
{
    public class AmcacheOld
    {
        private const int ProductName = 0;
        private const int CompanyName = 0x1;
        private const int FileVersionNumber = 0x2;
        private const int LanguageCode = 0x3;
        private const int SwitchBackContext = 0x4;
        private const int FileVersionString = 0x5;
        private const int FileSize = 0x6;
        private const int SizeOfImage = 0x7;
        private const int PEHeaderHash = 0x8;
        private const int PEHeaderChecksum = 0x9;
        private const int BinProductVersion = 0xa;
        private const int BinFileVersion = 0xb;
        private const int FileDescription = 0xc;
        private const int LinkerVersion = 0xd;
        private const int LinkDate = 0xf;
        private const int BinaryType = 0x10;
        private const int LastModified = 0x11;
        private const int Created = 0x12;
        private const int FullPath = 0x15;
        private const int IsLocal = 0x16;
        private const int LastModifiedStore = 0x17;
        private const int ProgramID = 0x100;
        private const int GuessProgramID = 0x106;
        private const int SHA1 = 0x101;
        private static Logger _logger;

        public AmcacheOld(string hive, bool recoverDeleted)
        {
            _logger = LogManager.GetCurrentClassLogger();

            var reg = new RegistryHive(hive)
            {
                RecoverDeleted = recoverDeleted
            };
            reg.ParseHive();

            var fileKey = reg.GetKey(@"Root\File");
            var programsKey = reg.GetKey(@"Root\Programs");


            UnassociatedFileEntries = new List<FileEntryOld>();
            ProgramsEntries = new List<ProgramsEntryOld>();

            if (fileKey == null || programsKey == null)
            {
                _logger.Error($"Hive does not contain a File and/or Programs key. Processing cannot continue");
                return;
            }

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
                DateTimeOffset? EpochA = null;
                DateTimeOffset? EpochB = null;
                var PathListd = "";
                var Guidf = "";
                var RawFiles = "";

                try
                {
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
                                try
                                {
                                    var seca = long.Parse(value.ValueData);
                                    if (seca > 0)
                                    {
                                        EpochA = DateTimeOffset.FromUnixTimeSeconds(seca).ToUniversalTime();
                                    }
                                }
                                catch (Exception )
                                {
                                    //sometimes the number is way too big
                                }

                                break;
                            case "b":
                                var seconds = long.Parse(value.ValueData);
                                if (seconds > 0)
                                {
                                    EpochB =
                                        DateTimeOffset.FromUnixTimeSeconds(seconds).ToUniversalTime();
                                }

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
                                Dword13 = int.Parse(value.ValueData);
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
                                _logger.Warn(
                                    $"Unknown value name in Program at path {registryKey.KeyPath}: {value.ValueName}");
                                break;
                        }
                    }

                    var pe = new ProgramsEntryOld(ProgramName0, ProgramVersion1, VenderName2, LocaleID3, InstallSource6,
                        UninstallKey7, Guid10, Guid12, UninstallGuid11, Dword5, Dword13, Dword14, Dword15, UnknownBytes,
                        Qword17, Dword18, EpochA, EpochB, PathListd, Guidf, RawFiles, registryKey.KeyName,
                        registryKey.LastWriteTime.Value);

                    ProgramsEntries.Add(pe);
                }
                catch (Exception ex)
                {
                    _logger.Error($"Error parsing ProgramsEntry at {registryKey.KeyPath}. Error: {ex.Message}");
                    _logger.Error(
                        $"Please send the following text to saericzimmerman@gmail.com. \r\n\r\nKey data: {registryKey}");
                }
            }

            //For each Programs entry, add the related Files entries from Files\Volume subkey, put the rest in unassociated


            foreach (var registryKey in fileKey.SubKeys)
            {
                //These are the guids for volumes
                foreach (var subKey in registryKey.SubKeys)
                {
                    var prodName = "";
                    int? langId = null;
                    var fileVerString = "";
                    var fileVerNum = "";
                    var fileDesc = "";
                    var compName = "";
                    var fullPath = "";
                    var switchBack = "";
                    var peHash = "";
                    var progID = "";
                    var sha = "";

                    long binProdVersion = 0;
                    ulong binFileVersion = 0;
                    var linkerVersion = 0;
                    var binType = 0;
                    var isLocal = 0;
                    var gProgramID = 0;
                    int? fileSize = null;
                    int? sizeOfImage = null;
                    int? peHeaderChecksum = null;

                    DateTimeOffset? created = null;
                    DateTimeOffset? lm = null;
                    DateTimeOffset? lmStore = null;
                    DateTimeOffset? linkDate = null;

                    var hasLinkedProgram = false;

                    try
                    {
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
                                    compName = keyValue.ValueData;
                                    break;
                                case FileVersionNumber:
                                    fileVerNum = keyValue.ValueData;
                                    break;
                                case LanguageCode:
                                    langId = int.Parse(keyValue.ValueData);
                                    break;
                                case SwitchBackContext:
                                    switchBack = keyValue.ValueData;
                                    break;
                                case FileVersionString:
                                    fileVerString = keyValue.ValueData;
                                    break;
                                case FileSize:
                                    fileSize = int.Parse(keyValue.ValueData);
                                    break;
                                case SizeOfImage:
                                    sizeOfImage = int.Parse(keyValue.ValueData);
                                    break;
                                case PEHeaderHash:
                                    peHash = keyValue.ValueData;
                                    break;
                                case PEHeaderChecksum:
                                    peHeaderChecksum = int.Parse(keyValue.ValueData);
                                    break;
                                case BinProductVersion:
                                    binProdVersion = long.Parse(keyValue.ValueData);
                                    break;
                                case BinFileVersion:
                                    binFileVersion = ulong.Parse(keyValue.ValueData);
                                    break;
                                case FileDescription:
                                    fileDesc = keyValue.ValueData;
                                    break;
                                case LinkerVersion:
                                    linkerVersion = int.Parse(keyValue.ValueData);
                                    break;
                                case LinkDate:
                                    linkDate =
                                        DateTimeOffset.FromUnixTimeSeconds(long.Parse(keyValue.ValueData))
                                            .ToUniversalTime();
                                    break;
                                case BinaryType:
                                    binType = int.Parse(keyValue.ValueData);
                                    break;
                                case LastModified:
                                    lm = DateTimeOffset.FromFileTime(long.Parse(keyValue.ValueData)).ToUniversalTime();
                                    break;
                                case Created:
                                    created =
                                        DateTimeOffset.FromFileTime(long.Parse(keyValue.ValueData)).ToUniversalTime();
                                    break;
                                case FullPath:
                                    fullPath = keyValue.ValueData;
                                    break;
                                case IsLocal:
                                    isLocal = int.Parse(keyValue.ValueData);
                                    break;
                                case GuessProgramID:
                                    gProgramID = int.Parse(keyValue.ValueData);
                                    break;
                                case LastModifiedStore:
                                    lmStore = DateTimeOffset.FromFileTime(long.Parse(keyValue.ValueData)).ToUniversalTime();
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
                                    _logger.Warn(
                                        $"Unknown value name when processing FileEntry at path '{subKey.KeyPath}': 0x{keyVal:X}");
                                    break;
                            }
                        }

                        if (fullPath.Length == 0)
                        {
                            continue;
                        }

                        TotalFileEntries += 1;

                        var fe = new FileEntryOld(prodName, progID, sha, fullPath, lmStore, registryKey.KeyName,
                            registryKey.LastWriteTime.Value, subKey.KeyName, subKey.LastWriteTime.Value,
                            isLocal, compName, langId, fileVerString, peHash, fileVerNum, fileDesc, binProdVersion, binFileVersion,
                            linkerVersion, binType, switchBack, fileSize, linkDate, sizeOfImage,
                            lm, created, peHeaderChecksum, gProgramID, subKey.KeyName);

                        if (hasLinkedProgram)
                        {
                            var program = ProgramsEntries.SingleOrDefault(t => t.ProgramID == fe.ProgramID);
                            fe.ProgramName = program.ProgramName_0;
                            program.FileEntries.Add(fe);
                        }
                        else
                        {
                            fe.ProgramName = "Unassociated";
                            UnassociatedFileEntries.Add(fe);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Error($"Error parsing FileEntry at {subKey.KeyPath}. Error: {ex.Message}");
                        _logger.Error(
                            $"Please send the following text to saericzimmerman@gmail.com. \r\n\r\nKey data: {subKey}");
                    }
                }
            }
        }

        public List<FileEntryOld> UnassociatedFileEntries { get; }
        public List<ProgramsEntryOld> ProgramsEntries { get; }

        public int TotalFileEntries { get; }
    }
}