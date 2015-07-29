using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Amcache.Classes
{
    public class FileEntry
    {
        public string ProductName { get; }
        public string FullPath { get; }
        public string ProgramID { get; }
        public string SHA1 { get; }
        public string FileID { get; }
        public string VolumeID { get; }

        public DateTimeOffset VolumeLastWriteTimestamp { get; }
        public DateTimeOffset LastWriteTimestamp { get; }


        public DateTimeOffset LastModified2 { get; }

        public FileEntry(string productName, string programID, string sha1, string fullPath, DateTimeOffset lastMod2, string volumeID, DateTimeOffset volumeLastWrite, string fileID, DateTimeOffset lastWrite)
        {
            ProductName = productName;
            ProgramID = programID;
            SHA1 = sha1.Substring(4);
            FullPath = fullPath;
            LastModified2 = lastMod2;
            FileID = fileID;
            LastWriteTimestamp = lastWrite;
            VolumeID = volumeID;
            VolumeLastWriteTimestamp = volumeLastWrite;
        }
    }
}
