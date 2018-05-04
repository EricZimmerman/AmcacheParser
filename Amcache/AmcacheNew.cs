using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using Amcache.Classes;
using NLog;
using Registry;

namespace Amcache
{
    public class AmcacheNew
    {
        private static Logger _logger;

        public AmcacheNew(string hive, bool recoverDeleted)
        {
            _logger = LogManager.GetCurrentClassLogger();

            var reg = new RegistryHive(hive)
            {
                RecoverDeleted = recoverDeleted
            };


            if (reg.Header.PrimarySequenceNumber != reg.Header.SecondarySequenceNumber)
            {
                var hiveBase = Path.GetFileName(hive);

                var logFiles = Directory.GetFiles(Path.GetDirectoryName(hive), $"{hiveBase}.LOG*");

                if (logFiles.Length == 0)
                {
                    var log = LogManager.GetCurrentClassLogger();

                    log.Warn("Registry hive is dirty and no transaction logs were found in the same directory! LOGs should have same base name as the hive. Aborting!!");
                    throw new Exception("Sequence numbers do not match and transaction logs were not found in the same directory as the hive. Aborting");
                }

                reg.ProcessTransactionLogs(logFiles.ToList(),true);
            }


            reg.ParseHive();

            var fileKey = reg.GetKey(@"Root\InventoryApplicationFile");
            var programsKey = reg.GetKey(@"Root\InventoryApplication");


            UnassociatedFileEntries = new List<FileEntryNew>();
            ProgramsEntries = new List<ProgramsEntryNew>();
            DeviceContainers = new List<DeviceContainer>();
            DevicePnps = new List<DevicePnp>();
            DriveBinaries = new List<DriverBinary>();
            DriverPackages = new List<DriverPackage>();
            ShortCuts = new List<Shortcut>();

            if (fileKey == null || programsKey == null)
            {
                _logger.Error(
                    "Hive does not contain a InventoryApplicationFile and/or InventoryApplication key. Processing cannot continue");
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
                                   // _logger.Warn($"registryKeyValue.ValueData for InstallDate as InvariantCulture: {registryKeyValue.ValueData.ToString(CultureInfo.InvariantCulture)}");
                                    var d = new DateTimeOffset(DateTime.Parse(registryKeyValue.ValueData,DateTimeFormatInfo.InvariantInfo).Ticks,
                                        TimeSpan.Zero);
                                    installDate = d;
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

                    var pe = new ProgramsEntryNew(bundleManifestPath, hiddenArp, inboxModernApp, installDate, language,
                        manifestPath, msiPackageCode, msiProductCode, name, osVersionAtInstallTime, packageFullName,
                        programId, programInstanceId, publisher, registryKeyPath, rootDirPath, source, storeAppType,
                        type, uninstallString, version, registryKey.LastWriteTime.Value);

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
                var isOsComponent = false;
                var isPeFile = false;
                var language = 0;
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
                                    var d = new DateTimeOffset(DateTime.Parse(subKeyValue.ValueData,DateTimeFormatInfo.InvariantInfo).Ticks,
                                        TimeSpan.Zero);
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

           
                var fe = new FileEntryNew(binaryType, binFileVersion, productVersion, fileId, isOsComponent, isPeFile,
                    language, linkDate, longPathHash, lowerCaseLongPath, name, productName, productVersion, programId,
                    publisher, size, version, subKey.LastWriteTime.Value, binProductVersion);

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
                    ShortCuts.Add(new Shortcut(shortCutkeySubKey.KeyName, shortCutkeySubKey.Values.First().ValueData,
                        shortCutkeySubKey.LastWriteTime.Value));
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

                        var dc = new DeviceContainer(deviceSubKey.KeyName, deviceSubKey.LastWriteTime.Value, categories,
                            discoveryMethod, friendlyName, icon, isActive, isConnected, isMachineContainer, isNetworked,
                            isPaired, manufacturer, modelId, modelName, modelNumber, primaryCategory, state);

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


            var pnpKey = reg.GetKey(@"Root\InventoryDevicePnp");

            if (pnpKey != null)
            {
                foreach (var pnpsKey in pnpKey.SubKeys)
                {
                    var busReportedDescription = string.Empty;
                    var Class = string.Empty;
                    var classGuid = string.Empty;
                    var compid = string.Empty;
                    var containerId = string.Empty;
                    var description = string.Empty;
                    var deviceState = string.Empty;
                    var driverId = string.Empty;
                    var driverName = string.Empty;
                    var driverPackageStrongName = string.Empty;
                    var driverVerDate = string.Empty;
                    var driverVerVersion = string.Empty;
                    var enumerator = string.Empty;
                    var hwid = string.Empty;
                    var inf = string.Empty;
                    var installState = string.Empty;
                    var manufacturer = string.Empty;
                    var matchingId = string.Empty;
                    var model = string.Empty;
                    var parentId = string.Empty;
                    var problemCode = string.Empty;
                    var provider = string.Empty;
                    var service = string.Empty;
                    var stackid = string.Empty;

                    try
                    {
                        foreach (var keyValue in pnpsKey.Values)
                        {
                            switch (keyValue.ValueName)
                            {
                                case "BusReportedDescription":
                                    busReportedDescription = keyValue.ValueData;
                                    break;
                                case "Class":
                                    Class = keyValue.ValueData;
                                    break;
                                case "ClassGuid":
                                    classGuid = keyValue.ValueData;
                                    break;
                                case "COMPID":
                                    compid = keyValue.ValueData;
                                    break;
                                case "ContainerId":
                                    containerId = keyValue.ValueData;
                                    break;
                                case "Description":
                                    description = keyValue.ValueData;
                                    break;
                                case "DeviceState":
                                    deviceState = keyValue.ValueData;
                                    break;
                                case "DriverId":
                                    driverId = keyValue.ValueData;
                                    break;
                                case "DriverName":
                                    driverName = keyValue.ValueData;
                                    break;
                                case "DriverPackageStrongName":
                                    driverPackageStrongName = keyValue.ValueData;
                                    break;
                                case "DriverVerDate":
                                    driverVerDate = keyValue.ValueData;
                                    break;
                                case "DriverVerVersion":
                                    driverVerVersion = keyValue.ValueData;
                                    break;
                                case "Enumerator":
                                    enumerator = keyValue.ValueData;
                                    break;
                                case "HWID":
                                    hwid = keyValue.ValueData;
                                    break;
                                case "Inf":
                                    inf = keyValue.ValueData;
                                    break;
                                case "InstallState":
                                    installState = keyValue.ValueData;
                                    break;
                                case "LowerClassFilters":
                                case "LowerFilters":
                                    break;
                                case "Manufacturer":
                                    manufacturer = keyValue.ValueData;
                                    break;
                                case "MatchingID":
                                    matchingId = keyValue.ValueData;
                                    break;
                                case "Model":
                                    model = keyValue.ValueData;
                                    break;
                                case "ParentId":
                                    parentId = keyValue.ValueData;
                                    break;
                                case "ProblemCode":
                                    problemCode = keyValue.ValueData;
                                    break;
                                case "Provider":
                                    provider = keyValue.ValueData;
                                    break;
                                case "Service":
                                    service = keyValue.ValueData;
                                    break;
                                case "STACKID":
                                    stackid = keyValue.ValueData;
                                    break;
                                case "UpperClassFilters":
                                case "UpperFilters":
                                    break;
                                default:
                                    _logger.Warn(
                                        $"Unknown value name when processing DevicePnp at path '{pnpsKey.KeyPath}': {keyValue.ValueName}");
                                    break;
                            }
                        }

                        var dp = new DevicePnp(pnpsKey.KeyName, pnpKey.LastWriteTime.Value, busReportedDescription,
                            Class, classGuid, compid, containerId, description, deviceState, driverId, driverName,
                            driverPackageStrongName, driverVerDate, driverVerVersion, enumerator, hwid, inf,
                            installState, manufacturer, matchingId, model, parentId, problemCode, provider, service,
                            stackid);

                        DevicePnps.Add(dp);
                    }
                    catch (Exception ex)
                    {
                        _logger.Error($"Error parsing DevicePnp at {pnpKey.KeyPath}. Error: {ex.Message}");
                        _logger.Error(
                            $"Please send the following text to saericzimmerman@gmail.com. \r\n\r\nKey data: {pnpKey}");
                    }
                }
            }


            var binaryKey = reg.GetKey(@"Root\InventoryDriverBinary");

            if (binaryKey != null)
            {
                foreach (var binKey in binaryKey.SubKeys)
                {
                    var driverCheckSum = 0;
                    var driverCompany = string.Empty;
                    var driverId = string.Empty;
                    var driverInBox = false;
                    var driverIsKernelMode = false;
                    DateTimeOffset? driverLastWriteTime = null;
                    var driverName = string.Empty;
                    var driverPackageStrongName = string.Empty;
                    var driverSigned = false;
                    DateTimeOffset? driverTimeStamp = null;
                    var driverType = string.Empty;
                    var driverVersion = string.Empty;
                    var imageSize = 0;
                    var inf = string.Empty;
                    var product = string.Empty;
                    var productVersion = string.Empty;
                    var service = string.Empty;
                    var wdfVersion = string.Empty;


                    try
                    {
                        foreach (var keyValue in binKey.Values)
                        {
                            switch (keyValue.ValueName)
                            {
                                case "DriverCheckSum":
                                    driverCheckSum = int.Parse(keyValue.ValueData);
                                    break;
                                case "DriverCompany":
                                    driverCompany = keyValue.ValueData;
                                    break;
                                case "DriverId":
                                    driverId = keyValue.ValueData;
                                    break;
                                case "DriverInBox":
                                    driverInBox = keyValue.ValueData == "1";
                                    break;
                                case "DriverIsKernelMode":
                                    driverIsKernelMode = keyValue.ValueData == "1";
                                    break;
                                case "DriverLastWriteTime":
                                    if (keyValue.ValueData.Length > 0)
                                    {
                                        var d = new DateTimeOffset(DateTime.Parse(keyValue.ValueData,DateTimeFormatInfo.InvariantInfo).Ticks,
                                            TimeSpan.Zero);
                                        driverLastWriteTime = d;
                                    }

                                    break;
                                case "DriverName":
                                    driverName = keyValue.ValueData;
                                    break;
                                case "DriverPackageStrongName":
                                    driverPackageStrongName = keyValue.ValueData;
                                    break;
                                case "DriverSigned":
                                    driverSigned = keyValue.ValueData == "1";
                                    break;
                                case "DriverTimeStamp":
                                    //DateTimeOffset.FromUnixTimeSeconds(seca).ToUniversalTime();
                                    var seca = long.Parse(keyValue.ValueData);
                                    if (seca > 0)
                                    {
                                        driverTimeStamp = DateTimeOffset.FromUnixTimeSeconds(seca).ToUniversalTime();
                                    }
                                    break;
                                case "DriverType":
                                    driverType = keyValue.ValueData;
                                    break;
                                case "DriverVersion":
                                    driverVersion = keyValue.ValueData;
                                    break;
                                case "ImageSize":
                                    imageSize = int.Parse(keyValue.ValueData);
                                    break;
                                case "Inf":
                                    inf = keyValue.ValueData;
                                    break;
                                case "Product":
                                    product = keyValue.ValueData;
                                    break;
                                case "ProductVersion":
                                    productVersion = keyValue.ValueData;
                                    break;
                                case "Service":
                                    service = keyValue.ValueData;
                                    break;
                                case "WdfVersion":
                                    wdfVersion = keyValue.ValueData;
                                    break;
                                default:
                                    _logger.Warn(
                                        $"Unknown value name when processing DriverBinary at path '{binKey.KeyPath}': {keyValue.ValueName}");
                                    break;
                            }
                        }

                        var db = new DriverBinary(binKey.KeyName, binaryKey.LastWriteTime.Value, driverCheckSum,
                            driverCompany, driverId, driverInBox, driverIsKernelMode, driverLastWriteTime, driverName,
                            driverPackageStrongName, driverSigned, driverTimeStamp, driverType, driverVersion,
                            imageSize, inf, product, productVersion, service, wdfVersion);

                        DriveBinaries.Add(db);
                    }
                    catch (Exception ex)
                    {
                        _logger.Error($"Error parsing DriverBinary at {binaryKey.KeyPath}. Error: {ex.Message}");
                        _logger.Error(
                            $"Please send the following text to saericzimmerman@gmail.com. \r\n\r\nKey data: {binaryKey}");
                    }
                }
            }


            var packaheKey = reg.GetKey(@"Root\InventoryDriverPackage");

            if (packaheKey != null)
            {
                foreach (var packKey in packaheKey.SubKeys)
                {
                    var Class = string.Empty;
                    var ClassGuid = string.Empty;
                    DateTimeOffset? Date = null;
                    var Directory = string.Empty;
                    var DriverInBox = false;
                    var Hwids = string.Empty;
                    var Inf = string.Empty;
                    var Provider = string.Empty;
                    var SubmissionId = string.Empty;
                    var SYSFILE = string.Empty;
                    var Version = string.Empty;


                    try
                    {
                        foreach (var keyValue in packKey.Values)
                        {
                            switch (keyValue.ValueName)
                            {
                                case "Class":
                                    Class = keyValue.ValueData;
                                    break;
                                case "ClassGuid":
                                    ClassGuid = keyValue.ValueData;
                                    break;
                                case "Date":
                                    if (keyValue.ValueData.Length > 0)
                                    {
                                        var d = new DateTimeOffset(DateTime.Parse(keyValue.ValueData,DateTimeFormatInfo.InvariantInfo).Ticks,
                                            TimeSpan.Zero);
                                        Date = d;
                                    }

                                    break;
                                case "Directory":
                                    Directory = keyValue.ValueData;
                                    break;
                                case "DriverInBox":
                                    DriverInBox = keyValue.ValueData == "1";
                                    break;
                                case "Hwids":
                                    Hwids = keyValue.ValueData;
                                    break;
                                case "Inf":
                                    Inf = keyValue.ValueData;
                                    break;
                                case "Provider":
                                    Provider = keyValue.ValueData;
                                    break;
                                case "SubmissionId":
                                    SubmissionId = keyValue.ValueData;
                                    break;
                                case "SYSFILE":
                                    SYSFILE = keyValue.ValueData;
                                    break;
                                case "Version":
                                    Version = keyValue.ValueData;
                                    break;

                                default:
                                    _logger.Warn(
                                        $"Unknown value name when processing DriverPackage at path '{packKey.KeyPath}': {keyValue.ValueName}");
                                    break;
                            }
                        }

                        var dp = new DriverPackage(packKey.KeyName, packaheKey.LastWriteTime.Value, Class, ClassGuid,
                            Date, Directory, DriverInBox, Hwids, Inf, Provider, SubmissionId, SYSFILE, Version);

                        DriverPackages.Add(dp);
                    }
                    catch (Exception ex)
                    {
                        _logger.Error($"Error parsing DriverPackage at {packaheKey.KeyPath}. Error: {ex.Message}");
                        _logger.Error(
                            $"Please send the following text to saericzimmerman@gmail.com. \r\n\r\nKey data: {packaheKey}");
                    }
                }
            }
        }

        public List<FileEntryNew> UnassociatedFileEntries { get; }
        public List<ProgramsEntryNew> ProgramsEntries { get; }
        public List<DeviceContainer> DeviceContainers { get; }
        public List<DevicePnp> DevicePnps { get; }
        public List<DriverBinary> DriveBinaries { get; }
        public List<DriverPackage> DriverPackages { get; }

        public List<Shortcut> ShortCuts { get; }

        public int TotalFileEntries { get; }
    }
}