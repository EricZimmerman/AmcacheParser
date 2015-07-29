using System;

namespace Amcache.Classes
{
    public class FileEntry
    {
        public FileEntry(string productName, string programID, string sha1, string fullPath, DateTimeOffset lastMod2,
            string volumeID, DateTimeOffset volumeLastWrite, string fileID, DateTimeOffset lastWrite, int unknown5,
            string compName, int langId,
            string fileVerString, string peHash, string fileVerNum, string fileDesc, long unknown1, long unknown2,
            int unknown3, int unknown4, string switchback, int fileSize, DateTimeOffset compTime, int peHeaderSize,
            DateTimeOffset lm, DateTimeOffset created, int pecheck)
        {
            PEHeaderChecksum = pecheck;
            LastModified = lm;
            Created = created;
            PEHeaderSize = peHeaderSize;
            CompileTime = compTime;
            SwitchBackContext = switchback;
            FileSize = fileSize;
            FileDescription = fileDesc;
            ProductName = productName;
            ProgramID = programID;
            SHA1 = sha1.Substring(4);
            FullPath = fullPath;
            LastModified2 = lastMod2;
            FileID = fileID;
            LastWriteTimestamp = lastWrite;
            VolumeID = volumeID;
            VolumeLastWriteTimestamp = volumeLastWrite;
            Unknown1 = unknown1;
            Unknown2 = unknown2;
            Unknown3 = unknown3;
            Unknown4 = unknown4;
            Unknown5 = unknown5;
            CompanyName = compName;
            LanguageID = langId;
            FileVersionString = fileVerString;
            FileVersionNumber = fileVerNum;
            PEHeaderHash = peHash;
        }

        public string ProductName { get; }
        public string CompanyName { get; }
        public string FileVersionString { get; }
        public string FileVersionNumber { get; }
        public string FileDescription { get; }
        public string FullPath { get; }
        public string PEHeaderHash { get; }
        public string ProgramID { get; }
        public string SHA1 { get; }
        public string FileID { get; }
        public string VolumeID { get; }
        public string SwitchBackContext { get; }
        public long Unknown1 { get; }
        public long Unknown2 { get; }
        public int Unknown3 { get; }
        public int Unknown4 { get; }
        public int Unknown5 { get; }
        public int LanguageID { get; }
        public int FileSize { get; }
        public int PEHeaderSize { get; }
        public int PEHeaderChecksum { get; }
        public DateTimeOffset VolumeLastWriteTimestamp { get; }
        public DateTimeOffset LastWriteTimestamp { get; }
        public DateTimeOffset CompileTime { get; }
        public DateTimeOffset LastModified { get; }
        public DateTimeOffset LastModified2 { get; }
        public DateTimeOffset Created { get; }
    }
}