using System;
using System.IO;
using System.Linq;
using NLog;
using Registry;
using Registry.Abstractions;

namespace Amcache
{
    public static class Helper
    {
        public static bool IsNewFormat(string file)
        {
            RegistryKey fileKey = null;
            try
            {
                var reg = new RegistryHive(file)
                {
                    RecoverDeleted = false
                };
                LogManager.DisableLogging();

                if (reg.Header.PrimarySequenceNumber != reg.Header.SecondarySequenceNumber)
                {
                    var hiveBase = Path.GetFileName(file);
                    
                        var dirname = Path.GetDirectoryName(file);

                if (dirname == "")
                {
                    dirname = ".";
                }

                    var logFiles = Directory.GetFiles(dirname, $"{hiveBase}.LOG?");

                    if (logFiles.Length == 0)
                    {
                        LogManager.EnableLogging();
                        var log = LogManager.GetCurrentClassLogger();

                        log.Warn("Registry hive is dirty and no transaction logs were found in the same directory! LOGs should have same base name as the hive. Aborting!!");
                        throw new Exception("Sequence numbers do not match and transaction logs were not found in the same directory as the hive. Aborting");
                    }

                    reg.ProcessTransactionLogs(logFiles.ToList(),true);
                }

       
                reg.ParseHive();

                 fileKey = reg.GetKey(@"Root\InventoryApplicationFile");

                LogManager.EnableLogging();
            }
            catch (Exception e)
            {
                LogManager.EnableLogging();
            }

            return fileKey != null;
        }
    }
}