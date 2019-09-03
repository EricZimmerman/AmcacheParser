using System;
using System.Collections.Generic;
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
            RegistryHive reg;

            var dirname = Path.GetDirectoryName(file);
            var hiveBase = Path.GetFileName(file);

            List<RawCopy.RawCopyReturn> rawFiles = null;

            try
            {
                try
                {
                     reg = new RegistryHive(file)
                    {
                        RecoverDeleted = true
                    };
                }
                catch (IOException)
                {
                    //file is in use

                    if (RawCopy.Helper.IsAdministrator() == false)
                    {
                        throw new UnauthorizedAccessException("Administrator privileges not found!");
                    }

                    var files = new List<string>();
                    files.Add(file);

                    var logFiles = Directory.GetFiles(dirname, $"{hiveBase}.LOG?");

                    foreach (var logFile in logFiles)
                    {
                        files.Add(logFile);
                    }

                    rawFiles = RawCopy.Helper.GetFiles(files);

                    var b = new byte[rawFiles.First().FileStream.Length];

                    rawFiles.First().FileStream.Read(b, 0, (int) rawFiles.First().FileStream.Length);

                    reg = new RegistryHive(b,rawFiles.First().InputFilename);
                }

                LogManager.DisableLogging();

                if (reg.Header.PrimarySequenceNumber != reg.Header.SecondarySequenceNumber)
                {
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
                        if (rawFiles != null)
                        {
                            var lt = new List<TransactionLogFileInfo>();
                            foreach (var rawCopyReturn in rawFiles.Skip(1).ToList())
                            {
                                var b = new byte[rawCopyReturn.FileStream.Length];

                                rawCopyReturn.FileStream.Read(b, 0, (int) rawFiles.First().FileStream.Length);

                                var tt = new TransactionLogFileInfo(rawCopyReturn.InputFilename,b);
                                lt.Add(tt);
                            }

                            reg.ProcessTransactionLogs(lt,true);
                        }
                        else
                        {
                            reg.ProcessTransactionLogs(logFiles.ToList(),true);    
                        }
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