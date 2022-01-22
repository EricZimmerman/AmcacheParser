using System;
using System.Collections.Generic;

namespace Amcache.Classes;

public class ProgramsEntryOld
{
    public ProgramsEntryOld(string programName, string programVer, string vendorName, string langCode,
        string installSource, string uninstallRegKey, string guid10, string guid12, string uninstallGuid11,
        int unknownD5, int unknownD13, int unknownD14, int unknownD15, byte[] unknown16,
        long unknownQ17, int unknownD18, DateTimeOffset? installA, DateTimeOffset? installB, string pathList,
        string uninstallGuidf, string rawFilesList, string programID, DateTimeOffset lastwrite)
    {
        FilesLinks = new List<FilesProgramEntry>();
        FileEntries = new List<FileEntryOld>();

        ProgramName_0 = programName;
        ProgramVersion_1 = programVer;
        VendorName_2 = vendorName;
        LanguageCode_3 = langCode;
        InstallSource_6 = installSource;
        UninstallRegistryKey_7 = uninstallRegKey;
        UnknownGuid_10 = guid10;
        UnknownGuid_12 = guid12;
        UninstallGuid_11 = uninstallGuid11;
        UnknownDword_5 = unknownD5;
        UnknownDword_13 = unknownD13;
        UnknownDword_14 = unknownD14;
        UnknownDword_15 = unknownD15;
        UnknownBytes_16 = unknown16;
        UnknownQWord_17 = unknownQ17;
        UnknownDword_18 = unknownD18;
        InstallDateEpoch_a = installA;
        InstallDateEpoch_b = installB;
        PathsList_d = pathList;
        UninstallGuid_f = uninstallGuidf;
        ProgramID = programID;
        LastWriteTimestamp = lastwrite;

        var chunks = rawFilesList.Split(' ');
        foreach (var chunk in chunks)
        {
            if (chunk.Trim().Length == 0)
            {
                break;
            }

            var segs = chunk.Split('@');

            FilesLinks.Add(new FilesProgramEntry(segs[0], segs[1]));
        }
    }

    public string ProgramName_0 { get; }
    public string ProgramVersion_1 { get; }
    public string VendorName_2 { get; }
    public string LanguageCode_3 { get; }
    public string InstallSource_6 { get; }
    public string UninstallRegistryKey_7 { get; }
    public string UnknownGuid_10 { get; }
    public string UnknownGuid_12 { get; }
    public string UninstallGuid_11 { get; }
    public int UnknownDword_5 { get; }
    public int UnknownDword_13 { get; }
    public int UnknownDword_14 { get; }
    public int UnknownDword_15 { get; }
    public byte[] UnknownBytes_16 { get; }
    public long UnknownQWord_17 { get; }
    public int UnknownDword_18 { get; }
    public DateTimeOffset? InstallDateEpoch_a { get; }
    public DateTimeOffset? InstallDateEpoch_b { get; }
    public string PathsList_d { get; }
    public string UninstallGuid_f { get; }
    public List<FilesProgramEntry> FilesLinks { get; }
    public string ProgramID { get; }
    public DateTimeOffset LastWriteTimestamp { get; }
    public List<FileEntryOld> FileEntries { get; }
}

public class FilesProgramEntry
{
    public FilesProgramEntry(string sourceGuid, string fileEntry)
    {
        SourceGuid = sourceGuid;
        FileEntry = fileEntry;
    }

    public string SourceGuid { get; }
    public string FileEntry { get; }
}