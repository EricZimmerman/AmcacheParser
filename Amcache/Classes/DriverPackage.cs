using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using System.Text;
using System.Threading.Tasks;

namespace Amcache.Classes
{
public    class DriverPackage
    {
        public DriverPackage(string keyname, DateTimeOffset keyLastWriteTimestamp,string clss,string classGuid, DateTimeOffset? date,string directory,bool driverInBox,string hwids,string inf,string provider,string submissionId,string sysfile,string version)
        {
            Keyname = keyname;
            KeyLastWriteTimestamp = keyLastWriteTimestamp;
            Class = clss;
            ClassGuid = classGuid;
            Date = date;
            Directory = directory;
            DriverInBox = driverInBox;
            Hwids = hwids;
            Inf = inf;
            Provider = provider;
            SubmissionId = submissionId;
            SYSFILE = sysfile;
            Version = version;
        }
        public string Keyname { get; }
        public DateTimeOffset KeyLastWriteTimestamp { get; }
        public string Class { get; }
        public string ClassGuid { get; }
        public DateTimeOffset? Date { get; }
        public string Directory { get; }
        public bool DriverInBox { get; }
        public string Hwids { get; }
        public string Inf { get; }
        public string Provider { get; }
        public string SubmissionId { get; }
        public string SYSFILE { get; }
        public string Version { get; }

    }
}
