using System;
using System.IO;
using System.Linq;
using NLog;
using Registry;

namespace Amcache
{
    public static class Helper
    {
        public static bool IsNewFormat(string file)
        {
            
            var reg = new RegistryHive(file)
            {
                RecoverDeleted = false
            };
            LogManager.DisableLogging();

            if (reg.Header.PrimarySequenceNumber != reg.Header.SecondarySequenceNumber)
            {
                var logFiles = Directory.GetFiles(Path.GetDirectoryName(file), "*.LOG*");

                if (logFiles.Length == 0)
                {
                    LogManager.EnableLogging();
                    var log = LogManager.GetCurrentClassLogger();

                    log.Warn("Registry hive is dirty and no transaction logs were found in the same directory! Aborting!!");
                    throw new Exception("Sequence numbers do not match and transaction logs were not found in the same directory as the hive. Aborting");
                }

                reg.ProcessTransactionLogs(logFiles.ToList(),true);
            }

       
            reg.ParseHive();


            var fileKey = reg.GetKey(@"Root\InventoryApplication");

          LogManager.EnableLogging();

            return fileKey != null;
        }
    }
}