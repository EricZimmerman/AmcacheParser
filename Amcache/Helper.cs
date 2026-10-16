using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Registry;
using Registry.Abstractions;
using Serilog;


namespace Amcache;

public static class Helper
{
    /// <summary>
    ///     The hive's transaction logs next to it that aren't empty: .LOG (Windows 8 and earlier), .LOG1 and .LOG2, in
    ///     any case (the file system may be case sensitive)
    /// </summary>
    public static List<string> LogFiles(string directory, string hiveName)
    {
        var logs = new List<string>();
        if (string.IsNullOrEmpty(directory))
        {
            directory = ".";
        }

        foreach (var file in Directory.GetFiles(directory))
        {
            var name = Path.GetFileName(file);
            if (name.Length > hiveName.Length + 5 || name.StartsWith(hiveName + ".LOG", StringComparison.OrdinalIgnoreCase) == false)
            {
                continue;
            }

            var suffix = name.Substring(hiveName.Length + 4);
            if ((suffix == "" || suffix == "1" || suffix == "2") && new FileInfo(file).Length > 0)
            {
                logs.Add(file);
            }
        }

        return logs.OrderBy(t => t, StringComparer.OrdinalIgnoreCase).ToList();
    }

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

                var logFiles = Helper.LogFiles(dirname, hiveBase);

                foreach (var logFile in logFiles)
                {
                    files.Add(logFile);
                }

                rawFiles = RawCopy.Helper.GetRawFiles(files);

                var b = new byte[rawFiles.First().FileStream.Length];

                rawFiles.First().FileStream.Read(b, 0, (int) rawFiles.First().FileStream.Length);

                reg = new RegistryHive(b,rawFiles.First().InputFilename);
            }

            
            //LogManager.DisableLogging();

            //dirty: the sequence numbers differ, or the header's checksum is wrong and there are logs to take it from
        if (reg.Header.PrimarySequenceNumber != reg.Header.SecondarySequenceNumber ||
            reg.Header.ValidateCheckSum() == false && Helper.LogFiles(dirname, hiveBase).Count > 0)
            {
                if (string.IsNullOrEmpty(dirname))
                {
                    dirname = ".";
                }

                var logFiles = Helper.LogFiles(dirname, hiveBase);

                if (logFiles.Count == 0 || noLog)
                {
                    //var log = LogManager.GetCurrentClassLogger();

                    if (noLog == false)
                    {
                        Log.Warning("Registry hive is dirty and no transaction logs were found in the same directory! LOGs should have same base name as the hive. Aborting!!");
                        throw new Exception("Sequence numbers do not match and transaction logs were not found in the same directory as the hive. Aborting");
                    }

                    Log.Warning("Registry hive is dirty and no transaction logs were found in the same directory. Data may be missing! Continuing anyways...");
                }
                else
                {
                    if (rawFiles != null)
                    {
                        var lt = new List<TransactionLogFileInfo>();
                        foreach (var rawCopyReturn in rawFiles.Skip(1).ToList())
                        {
                            var b = new byte[rawCopyReturn.FileStream.Length];

                            rawCopyReturn.FileStream.Read(b, 0, (int) rawCopyReturn.FileStream.Length);

                            var tt = new TransactionLogFileInfo(rawCopyReturn.InputFilename,b);
                            lt.Add(tt);
                        }

                        reg.ProcessTransactionLogs(lt,true);
                    }
                    else
                    {
                        reg.ProcessTransactionLogs(logFiles,true);    
                    }
                }
            }

           // LogManager.EnableLogging();


            reg.ParseHive();

            fileKey = reg.GetKey(@"Root\InventoryApplicationFile");

                
        }
        catch (Exception )
        {
           // LogManager.EnableLogging();
        }

        return fileKey != null;
    }
}