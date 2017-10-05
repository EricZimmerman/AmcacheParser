using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Amcache.Classes;
using NLog;
using Registry;

namespace Amcache
{
    public class AmcacheNew
    {
        private static Logger _logger;

        public List<FileEntryNew> UnassociatedFileEntries { get; }
        public List<ProgramsEntryNew> ProgramsEntries { get; }

        public int TotalFileEntries { get; }

        public AmcacheNew(string hive, bool recoverDeleted)
        {
            _logger = LogManager.GetCurrentClassLogger();

            var reg = new RegistryHive(hive)
            {
                RecoverDeleted = recoverDeleted
            };
            reg.ParseHive();

            var fileKey = reg.GetKey(@"Root\InventoryApplicationFile");
            var programsKey = reg.GetKey(@"Root\InventoryApplication");

            UnassociatedFileEntries = new List<FileEntryNew>();
            ProgramsEntries = new List<ProgramsEntryNew>();

            if (fileKey == null || programsKey == null)
            {
                _logger.Error("Hive does not contain a File and/or Programs key. Processing cannot continue");
                return;
            }


            foreach (var registryKey in programsKey.SubKeys)
            {
                var BundleManifestPath = string.Empty;
                var HiddenArp = false;
                var InboxModernApp = false;
                DateTimeOffset? InstallDate = null;
                int Language = 0;
                var ManifestPath = string.Empty;
                var MsiPackageCode = string.Empty;
                var MsiProductCode = string.Empty;
                var Name = string.Empty;
                var OSVersionAtInstallTime = string.Empty;
                var PackageFullName = string.Empty;
                var ProgramId = string.Empty;
                var ProgramInstanceId = string.Empty;
                var Publisher = string.Empty;
                var RegistryKeyPath = string.Empty;
                var RootDirPath = string.Empty;
                var Source = string.Empty;
                var StoreAppType = string.Empty;
                var Type = string.Empty;
                var UninstallString = string.Empty;
                var Version = string.Empty;


                try
                {
                    foreach (var registryKeyValue in registryKey.Values)
                    {
                        switch (registryKeyValue.ValueName)
                        {
                            case "BundleManifestPath":
                                BundleManifestPath = registryKeyValue.ValueData;
                                break;
                            case "HiddenArp":
                                HiddenArp = registryKeyValue.ValueData == "1";
                                break;
                            case "InboxModernApp":
                                InboxModernApp = registryKeyValue.ValueData == "1";
                                break;
                            case "InstallDate":
                                if (registryKeyValue.ValueData.Length > 0)
                                {
                                    var d = new DateTimeOffset(DateTime.Parse(registryKeyValue.ValueData).Ticks,TimeSpan.Zero);
                                    InstallDate = new DateTimeOffset?(d);
                                }
                                break;
                            case "Language":
                                Language = int.Parse(registryKeyValue.ValueData);
                                break;
                            case "ManifestPath":
                                ManifestPath = registryKeyValue.ValueData;
                                break;
                            case "MsiPackageCode":
                                MsiPackageCode = registryKeyValue.ValueData;
                                break;
                            case "MsiProductCode":
                                MsiProductCode = registryKeyValue.ValueData;
                                break;
                            case "Name":
                                Name = registryKeyValue.ValueData;
                                break;
                            case "OSVersionAtInstallTime":
                                OSVersionAtInstallTime = registryKeyValue.ValueData;
                                break;
                            case "PackageFullName":
                                PackageFullName = registryKeyValue.ValueData;
                                break;
                            case "ProgramId":
                                ProgramId = registryKeyValue.ValueData;
                                break;
                            case "ProgramInstanceId":
                                ProgramInstanceId = registryKeyValue.ValueData;
                                break;
                            case "Publisher":
                                Publisher = registryKeyValue.ValueData;
                                break;
                            case "RegistryKeyPath":
                                RegistryKeyPath = registryKeyValue.ValueData;
                                break;
                            case "RootDirPath":
                                RootDirPath = registryKeyValue.ValueData;
                                break;
                            case "Source":
                                Source = registryKeyValue.ValueData;
                                break;
                            case "StoreAppType":
                                StoreAppType = registryKeyValue.ValueData;
                                break;
                            case "Type":
                                Type = registryKeyValue.ValueData;
                                break;
                            case "UninstallString":
                                UninstallString = registryKeyValue.ValueData;
                                break;
                            case "Version":
                                Version = registryKeyValue.ValueData;
                                break;
                            default:
                                _logger.Warn(
                                    $"Unknown value name in InventoryApplication at path {registryKey.KeyPath}: {registryKeyValue.ValueName}");
                                break;
                        }
                        }


                    var pe = new ProgramsEntryNew(BundleManifestPath,HiddenArp,InboxModernApp,InstallDate,Language,ManifestPath,MsiPackageCode,MsiProductCode,Name,OSVersionAtInstallTime,PackageFullName,ProgramId,ProgramInstanceId,Publisher,RegistryKeyPath,RootDirPath,Source,StoreAppType,Type,UninstallString,Version, registryKey.LastWriteTime.Value);

                    ProgramsEntries.Add(pe);


                }
                catch (Exception ex)
                {
                    _logger.Error($"Error parsing ProgramsEntry at {registryKey.KeyPath}. Error: {ex.Message}");
                    _logger.Error(
                        $"Please send the following text to saericzimmerman@gmail.com. \r\n\r\nKey data: {registryKey}");

                }
            }


            foreach (var subKey in fileKey.SubKeys)
            {
                var BinaryType = string.Empty;
                var BinFileVersion = string.Empty;
                var BinProductVersion = string.Empty;
                var FileId = string.Empty;
                var IsOsComponent =false;
                var IsPeFile = false;
                int Language = 0;
                DateTimeOffset? LinkDate = null;
                var LongPathHash = string.Empty;
                var LowerCaseLongPath = string.Empty;
                var Name = string.Empty;
                var ProductName = string.Empty;
                var ProductVersion = string.Empty;
                var ProgramId = string.Empty;
                var Publisher = string.Empty;
                var Size = 0;
                var Version = string.Empty;

                var hasLinkedProgram = false;

                try
                {
                    foreach (var subKeyValue in subKey.Values)
                    {
                        switch (subKeyValue.ValueName)
                        {
                            case "BinaryType":
                                BinaryType = subKeyValue.ValueData;
                                break;
                            case "BinFileVersion":
                                BinFileVersion = subKeyValue.ValueData;
                                break;
                            case "BinProductVersion":
                                BinProductVersion = subKeyValue.ValueData;
                                break;
                            case "FileId":
                                FileId = subKeyValue.ValueData;
                                break;
                            case "IsOsComponent":
                                IsOsComponent = subKeyValue.ValueData == "1";
                                break;
                            case "IsPeFile":
                                IsPeFile = subKeyValue.ValueData == "1";
                                break;
                            case "Language":
                                Language = int.Parse(subKeyValue.ValueData);
                                break;
                            case "LinkDate":
                                if (subKeyValue.ValueData.Length > 0)
                                {
                                    var d = new DateTimeOffset(DateTime.Parse(subKeyValue.ValueData).Ticks, TimeSpan.Zero);
                                    LinkDate = d;
                                }

                           
                                break;
                            case "LongPathHash":
                                LongPathHash = subKeyValue.ValueData;
                                break;
                            case "LowerCaseLongPath":
                                LowerCaseLongPath = subKeyValue.ValueData;
                                break;
                            case "Name":
                                Name = subKeyValue.ValueData;
                                break;
                            case "ProductName":
                                ProductName = subKeyValue.ValueData;
                                break;
                            case "ProductVersion":
                                ProductVersion = subKeyValue.ValueData;
                                break;
                            case "ProgramId":
                                ProgramId = subKeyValue.ValueData;

                                var program = ProgramsEntries.SingleOrDefault(t => t.ProgramId == ProgramId);
                                if (program != null)
                                {
                                    hasLinkedProgram = true;
                                }
                                break;
                            case "Publisher":
                                Publisher = subKeyValue.ValueData;
                                break;
                            case "Size":
                                Size = int.Parse(subKeyValue.ValueData);
                                break;
                            case "Version":
                                Version = subKeyValue.ValueData;
                                break;
                            default:
                                _logger.Warn(
                                    $"Unknown value name when processing FileEntry at path '{subKey.KeyPath}': 0x{subKeyValue:X}");
                                break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error($"Error parsing FileEntry at {subKey.KeyPath}. Error: {ex.Message}");
                    _logger.Error(
                        $"Please send the following text to saericzimmerman@gmail.com. \r\n\r\nKey data: {subKey}");
                }



                TotalFileEntries += 1;

                Debug.WriteLine(Name);
                var fe = new FileEntryNew(BinaryType,BinFileVersion,ProductVersion, FileId,IsOsComponent,IsPeFile,Language,LinkDate,LongPathHash,LowerCaseLongPath,Name,ProductName,ProductVersion,ProgramId,Publisher,Size,Version,subKey.LastWriteTime.Value);

                if (hasLinkedProgram)
                {
                    var program = ProgramsEntries.SingleOrDefault(t => t.ProgramId == fe.ProgramId);
                    fe.ApplicationName = program.Name;
                    program.FileEntries.Add(fe);
                }
                else
                {
                    fe.ApplicationName = "Unassociated";
                    UnassociatedFileEntries.Add(fe);
                }

            }


        }
    }
}
