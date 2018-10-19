using System;
using System.IO;

namespace Amcache.Classes
{
    public class FileEntryNew
    {
        public FileEntryNew(string binaryType, string fileVer, string prodVer, string sha1, bool isOsComp, bool isPe,
            int lang, DateTimeOffset? linkDate, string pathHash, string longPath, string name, string productName,
            string prodVersion, string programId, string publisher, int size, string version, DateTimeOffset lastwrite,
            string binProductVersion)
        {
            BinaryType = binaryType;
            BinFileVersion = fileVer;
            BinProductVersion = prodVer;

            SHA1 = string.Empty;
            if (sha1.Length > 4)
            {
                SHA1 = sha1.Substring(4).ToLowerInvariant();
            }

            IsOsComponent = isOsComp;
            IsPeFile = isPe;
            Language = lang;
            LinkDate = linkDate;
            LongPathHash = pathHash;
            FullPath = longPath;
            Name = name;
            ProductName = productName;

            if (ProductName == null)
            {
                ProductName = " ";
            }

        

            ProductVersion = prodVersion;
            BinProductVersion = binProductVersion;
            ProgramId = programId;
            Publisher = publisher;
            Size = size;
            Version = version;

            FileKeyLastWriteTimestamp = lastwrite;

            ApplicationName = string.Empty;

            FileExtension = Path.GetExtension(longPath);
        }

        public string BinaryType { get; }
        public string BinFileVersion { get; }
        public string BinProductVersion { get; }
        public string SHA1 { get; }
        public bool IsOsComponent { get; }
        public bool IsPeFile { get; }
        public int Language { get; }
        public DateTimeOffset? LinkDate { get; }
        public string LongPathHash { get; }
        public string FullPath { get; }
        public string Name { get; }
        public string ApplicationName { get; set; }
        public string ProductName { get; }
        public string ProductVersion { get; }
        public string ProgramId { get; }
        public string Publisher { get; }
        public int Size { get; }
        public string Version { get; }

        public string FileExtension { get; }

        public DateTimeOffset FileKeyLastWriteTimestamp { get; }
    }
}