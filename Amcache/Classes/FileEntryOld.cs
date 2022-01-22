using System;
using System.IO;

namespace Amcache.Classes;

public class FileEntryOld
{
    public FileEntryOld(string productName, string programID, string sha1, string fullPath, DateTimeOffset? lmStore,
        string volumeID, DateTimeOffset volumeLastWrite, string fileID, DateTimeOffset lastWrite, int isLocal,
        string compName, int? langId,
        string fileVerString, string peHash, string fileVerNum, string fileDesc, long binProdVer, ulong binFileVer,
        int linVer, int binType, string switchback, int? fileSize, DateTimeOffset? linkDate, int? imgSize,
        DateTimeOffset? lm, DateTimeOffset? created, uint? pecheck, int gProgramID, string keyName)
    {
        PEHeaderChecksum = pecheck;
        LastModified = lm;
        Created = created;
        SizeOfImage = imgSize;
        LinkDate = linkDate;
        SwitchBackContext = switchback;
        FileSize = fileSize;
        FileDescription = fileDesc;
        ProductName = productName;
        ProgramID = programID;

        SHA1 = string.Empty;
        if (sha1.Length > 4)
        {
            SHA1 = sha1.Substring(4).ToLowerInvariant();
        }

        FullPath = fullPath;

        FileExtension = Path.GetExtension(fullPath);

        LastModifiedStore = lmStore;
        FileID = fileID;
        FileIDLastWriteTimestamp = lastWrite;
        VolumeID = volumeID;
        VolumeIDLastWriteTimestamp = volumeLastWrite;
        BinProductVersion = binProdVer;
        BinFileVersion = binFileVer;
        LinkerVersion = linVer;
        BinaryType = binType;
        IsLocal = isLocal;
        GuessProgramID = gProgramID;
        CompanyName = compName;
        LanguageID = langId;
        FileVersionString = fileVerString;
        FileVersionNumber = fileVerNum;
        PEHeaderHash = peHash;

        var tempKey = keyName.PadLeft(8, '0');

        var seq1 = tempKey.Substring(0, 4);
        var seq2 = tempKey.Substring(2, 2);
        var seq = seq1.TrimEnd('0');

        if (seq.Length == 0)
        {
            seq = "0";
        }


        MFTSequenceNumber = Convert.ToInt32(seq, 16);
        var ent = tempKey.Substring(4);
        MFTEntryNumber = Convert.ToInt32(ent, 16);
    }

    public int MFTEntryNumber { get; }
    public int MFTSequenceNumber { get; }
    public string ProductName { get; }
    public string CompanyName { get; }
    public string FileVersionString { get; }
    public string FileVersionNumber { get; }
    public string FileDescription { get; }
    public string FullPath { get; }
    public string FileExtension { get; }
    public string PEHeaderHash { get; }
    public string ProgramID { get; }
    public string SHA1 { get; }
    public string FileID { get; }
    public string VolumeID { get; }
    public string SwitchBackContext { get; }
    public string ProgramName { get; set; }
    public long BinProductVersion { get; }
    public ulong BinFileVersion { get; }
    public int LinkerVersion { get; }
    public int BinaryType { get; }
    public int IsLocal { get; }
    public int GuessProgramID { get; }
    public int? LanguageID { get; }
    public int? FileSize { get; }
    public int? SizeOfImage { get; }
    public uint? PEHeaderChecksum { get; }
    public DateTimeOffset VolumeIDLastWriteTimestamp { get; }
    public DateTimeOffset FileIDLastWriteTimestamp { get; }
    public DateTimeOffset? LinkDate { get; }
    public DateTimeOffset? LastModified { get; }
    public DateTimeOffset? LastModifiedStore { get; }
    public DateTimeOffset? Created { get; }

    public static string Reverse(string s)
    {
        var charArray = s.ToCharArray();
        Array.Reverse(charArray);
        return new string(charArray);
    }
}