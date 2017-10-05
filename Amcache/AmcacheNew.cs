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
        public List<DeviceContainer> DeviceContainers { get; }

        public Dictionary<string,string> ShortCuts { get; }

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
            DeviceContainers = new List<DeviceContainer>();
            ShortCuts = new Dictionary<string, string>();

            if (fileKey == null || programsKey == null)
            {
                _logger.Error("Hive does not contain a InventoryApplicationFile and/or InventoryApplication key. Processing cannot continue");
                return;
            }


            foreach (var registryKey in programsKey.SubKeys)
            {
                var bundleManifestPath = string.Empty;
                var hiddenArp = false;
                var inboxModernApp = false;
                DateTimeOffset? installDate = null;
                var language = 0;
                var manifestPath = string.Empty;
                var msiPackageCode = string.Empty;
                var msiProductCode = string.Empty;
                var name = string.Empty;
                var osVersionAtInstallTime = string.Empty;
                var packageFullName = string.Empty;
                var programId = string.Empty;
                var programInstanceId = string.Empty;
                var publisher = string.Empty;
                var registryKeyPath = string.Empty;
                var rootDirPath = string.Empty;
                var source = string.Empty;
                var storeAppType = string.Empty;
                var type = string.Empty;
                var uninstallString = string.Empty;
                var version = string.Empty;


                try
                {
                    foreach (var registryKeyValue in registryKey.Values)
                    {
                        switch (registryKeyValue.ValueName)
                        {
                            case "BundleManifestPath":
                                bundleManifestPath = registryKeyValue.ValueData;
                                break;
                            case "HiddenArp":
                                hiddenArp = registryKeyValue.ValueData == "1";
                                break;
                            case "InboxModernApp":
                                inboxModernApp = registryKeyValue.ValueData == "1";
                                break;
                            case "InstallDate":
                                if (registryKeyValue.ValueData.Length > 0)
                                {
                                    var d = new DateTimeOffset(DateTime.Parse(registryKeyValue.ValueData).Ticks,TimeSpan.Zero);
                                    installDate = new DateTimeOffset?(d);
                                }
                                break;
                            case "Language":
                                language = int.Parse(registryKeyValue.ValueData);
                                break;
                            case "ManifestPath":
                                manifestPath = registryKeyValue.ValueData;
                                break;
                            case "MsiPackageCode":
                                msiPackageCode = registryKeyValue.ValueData;
                                break;
                            case "MsiProductCode":
                                msiProductCode = registryKeyValue.ValueData;
                                break;
                            case "Name":
                                name = registryKeyValue.ValueData;
                                break;
                            case "OSVersionAtInstallTime":
                                osVersionAtInstallTime = registryKeyValue.ValueData;
                                break;
                            case "PackageFullName":
                                packageFullName = registryKeyValue.ValueData;
                                break;
                            case "ProgramId":
                                programId = registryKeyValue.ValueData;
                                break;
                            case "ProgramInstanceId":
                                programInstanceId = registryKeyValue.ValueData;
                                break;
                            case "Publisher":
                                publisher = registryKeyValue.ValueData;
                                break;
                            case "RegistryKeyPath":
                                registryKeyPath = registryKeyValue.ValueData;
                                break;
                            case "RootDirPath":
                                rootDirPath = registryKeyValue.ValueData;
                                break;
                            case "Source":
                                source = registryKeyValue.ValueData;
                                break;
                            case "StoreAppType":
                                storeAppType = registryKeyValue.ValueData;
                                break;
                            case "Type":
                                type = registryKeyValue.ValueData;
                                break;
                            case "UninstallString":
                                uninstallString = registryKeyValue.ValueData;
                                break;
                            case "Version":
                                version = registryKeyValue.ValueData;
                                break;
                            default:
                                _logger.Warn(
                                    $"Unknown value name in InventoryApplication at path {registryKey.KeyPath}: {registryKeyValue.ValueName}");
                                break;
                        }
                        }

                    var pe = new ProgramsEntryNew(bundleManifestPath,hiddenArp,inboxModernApp,installDate,language,manifestPath,msiPackageCode,msiProductCode,name,osVersionAtInstallTime,packageFullName,programId,programInstanceId,publisher,registryKeyPath,rootDirPath,source,storeAppType,type,uninstallString,version, registryKey.LastWriteTime.Value);

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
                var binaryType = string.Empty;
                var binFileVersion = string.Empty;
                var binProductVersion = string.Empty;
                var fileId = string.Empty;
                var isOsComponent =false;
                var isPeFile = false;
                int language = 0;
                DateTimeOffset? linkDate = null;
                var longPathHash = string.Empty;
                var lowerCaseLongPath = string.Empty;
                var name = string.Empty;
                var productName = string.Empty;
                var productVersion = string.Empty;
                var programId = string.Empty;
                var publisher = string.Empty;
                var size = 0;
                var version = string.Empty;

                var hasLinkedProgram = false;

                try
                {
                    foreach (var subKeyValue in subKey.Values)
                    {
                        switch (subKeyValue.ValueName)
                        {
                            case "BinaryType":
                                binaryType = subKeyValue.ValueData;
                                break;
                            case "BinFileVersion":
                                binFileVersion = subKeyValue.ValueData;
                                break;
                            case "BinProductVersion":
                                binProductVersion = subKeyValue.ValueData;
                                break;
                            case "FileId":
                                fileId = subKeyValue.ValueData;
                                break;
                            case "IsOsComponent":
                                isOsComponent = subKeyValue.ValueData == "1";
                                break;
                            case "IsPeFile":
                                isPeFile = subKeyValue.ValueData == "1";
                                break;
                            case "Language":
                                language = int.Parse(subKeyValue.ValueData);
                                break;
                            case "LinkDate":
                                if (subKeyValue.ValueData.Length > 0)
                                {
                                    var d = new DateTimeOffset(DateTime.Parse(subKeyValue.ValueData).Ticks, TimeSpan.Zero);
                                    linkDate = d;
                                }

                           
                                break;
                            case "LongPathHash":
                                longPathHash = subKeyValue.ValueData;
                                break;
                            case "LowerCaseLongPath":
                                lowerCaseLongPath = subKeyValue.ValueData;
                                break;
                            case "Name":
                                name = subKeyValue.ValueData;
                                break;
                            case "ProductName":
                                productName = subKeyValue.ValueData;
                                break;
                            case "ProductVersion":
                                productVersion = subKeyValue.ValueData;
                                break;
                            case "ProgramId":
                                programId = subKeyValue.ValueData;

                                var program = ProgramsEntries.SingleOrDefault(t => t.ProgramId == programId);
                                if (program != null)
                                {
                                    hasLinkedProgram = true;
                                }
                                break;
                            case "Publisher":
                                publisher = subKeyValue.ValueData;
                                break;
                            case "Size":
                                size = int.Parse(subKeyValue.ValueData);
                                break;
                            case "Version":
                                version = subKeyValue.ValueData;
                                break;
                            default:
                                _logger.Warn(
                                    $"Unknown value name when processing FileEntry at path '{subKey.KeyPath}': {subKeyValue.ValueName}");
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

                Debug.WriteLine(name);
                var fe = new FileEntryNew(binaryType,binFileVersion,productVersion, fileId,isOsComponent,isPeFile,language,linkDate,longPathHash,lowerCaseLongPath,name,productName,productVersion,programId,publisher,size,version,subKey.LastWriteTime.Value,binProductVersion);

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


            var shortCutkey = reg.GetKey(@"Root\InventoryApplicationShortcut");

            if (shortCutkey != null)
            {
                foreach (var shortCutkeySubKey in shortCutkey.SubKeys)
                {
                    ShortCuts.Add(shortCutkeySubKey.KeyName,shortCutkeySubKey.Values.First().ValueData);
                }
            }


            var deviceKey = reg.GetKey(@"Root\InventoryDeviceContainer");

            if (deviceKey != null)
            {
                foreach (var deviceSubKey in deviceKey.SubKeys)
                {
                    var categories = string.Empty;
                    var discoveryMethod = string.Empty;
                    var friendlyName = string.Empty;
                    var icon = string.Empty;
                    var isActive = false;
                    var isConnected = false;
                    var isMachineContainer = false;
                    var isNetworked = false;
                    var isPaired = false;
                    var manufacturer = string.Empty;
                    var modelId = string.Empty;
                    var modelName = string.Empty;
                    var modelNumber = string.Empty;
                    var primaryCategory = string.Empty;
                    var state = string.Empty;

                    try
                    {
                        foreach (var keyValue in deviceSubKey.Values)
                        {
                            switch (keyValue.ValueName)
                            {
                                case "Categories":
                                    categories = keyValue.ValueData;
                                    break;
                                case "DiscoveryMethod":
                                    discoveryMethod = keyValue.ValueData;
                                    break;
                                case "FriendlyName":
                                    friendlyName = keyValue.ValueData;
                                    break;
                                case "Icon":
                                    icon = keyValue.ValueData;
                                    break;
                                case "IsActive":
                                    isActive = keyValue.ValueData == "1";
                                    break;
                                case "IsConnected":
                                    isConnected = keyValue.ValueData == "1";
                                    break;
                                case "IsMachineContainer":
                                    isMachineContainer = keyValue.ValueData == "1";
                                    break;
                                case "IsNetworked":
                                    isNetworked = keyValue.ValueData == "1";
                                    break;
                                case "IsPaired":
                                    isPaired = keyValue.ValueData == "1";
                                    break;
                                case "Manufacturer":
                                    manufacturer = keyValue.ValueData;
                                    break;
                                case "ModelId":
                                    modelId = keyValue.ValueData;
                                    break;
                                case "ModelName":
                                    modelName = keyValue.ValueData;
                                    break;
                                case "ModelNumber":
                                    modelNumber = keyValue.ValueData;
                                    break;
                                case "PrimaryCategory":
                                    primaryCategory = keyValue.ValueData;
                                    break;
                                case "State":
                                    state = keyValue.ValueData;
                                    break;
                                default:
                                _logger.Warn(
                                    $"Unknown value name when processing DeviceContainer at path '{deviceSubKey.KeyPath}': {keyValue.ValueName}");
                                break;

                            }
                        }

                        var dc = new DeviceContainer(deviceSubKey.KeyName,deviceSubKey.LastWriteTime.Value,categories,discoveryMethod,friendlyName,icon,isActive,isConnected,isMachineContainer,isNetworked,isPaired,manufacturer,modelId,modelName,modelNumber,primaryCategory,state);

                        DeviceContainers.Add(dc);
                    }
                    catch (Exception ex)
                    {
                        _logger.Error($"Error parsing DeviceContainer at {deviceSubKey.KeyPath}. Error: {ex.Message}");
                        _logger.Error(
                            $"Please send the following text to saericzimmerman@gmail.com. \r\n\r\nKey data: {deviceSubKey}");
                    }

                }
            }





        }
    }
}
