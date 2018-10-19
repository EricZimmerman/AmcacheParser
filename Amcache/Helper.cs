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
        public static bool IsNewFormat(string file, bool noLog)
        {
            RegistryKey fileKey = null;
            try
            {
                var reg = new RegistryHive(file)
                {
                    RecoverDeleted = true
                };
                LogManager.DisableLogging();

                if (reg.Header.PrimarySequenceNumber != reg.Header.SecondarySequenceNumber)
                {
                    var hiveBase = Path.GetFileName(file);
                
                    var dirname = Path.GetDirectoryName(file);

                    if (string.IsNullOrEmpty(dirname))
                    {
                        dirname = ".";
                    }

                    var logFiles = Directory.GetFiles(dirname, $"{hiveBase}.LOG?");

                    if (logFiles.Length == 0)
                    {
                        var log = LogManager.GetCurrentClassLogger();

                        if (noLog == false)
                        {
                            log.Warn("Registry hive is dirty and no transaction logs were found in the same directory! LOGs should have same base name as the hive. Aborting!!");
                            throw new Exception("Sequence numbers do not match and transaction logs were not found in the same directory as the hive. Aborting");
                        }

                        log.Warn("Registry hive is dirty and no transaction logs were found in the same directory. Data may be missing! Continuing anyways...");

                    }
                    else
                    {
                        reg.ProcessTransactionLogs(logFiles.ToList(),true);
                    }
                }


                reg.ParseHive();

                fileKey = reg.GetKey(@"Root\InventoryApplicationFile");

                LogManager.EnableLogging();
            }
            catch (Exception )
            {
                LogManager.EnableLogging();
            }

            return fileKey != null;
        }
    }
}