using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Amcache.Classes;
using NLog;
using RawCopy;
using Registry;

namespace Amcache
{
    public class AmcacheNew
    {
        private static Logger _logger;

        public AmcacheNew(string hive, bool recoverDeleted, bool noLogs)
        {
            _logger = LogManager.GetCurrentClassLogger();

            RegistryHive reg;

            var dirname = Path.GetDirectoryName(hive);
            var hiveBase = Path.GetFileName(hive);

            List<RawCopyReturn> rawFiles = null;

            try
            {
                reg = new RegistryHive(hive)
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

                _logger.Warn($"'{hive}' is in use. Rerouting...\r\n");

                var files = new List<string>();
                files.Add(hive);

                var logFiles = Directory.GetFiles(dirname, $"{hiveBase}.LOG?");

                foreach (var logFile in logFiles)
                {
                    files.Add(logFile);
                }

                rawFiles = RawCopy.Helper.GetFiles(files);

                var b = new byte[rawFiles.First().FileStream.Length];

                rawFiles.First().FileStream.Read(b, 0, (int) rawFiles.First().FileStream.Length);

                reg = new RegistryHive(b, rawFiles.First().InputFilename);
            }

            if (reg.Header.PrimarySequenceNumber != reg.Header.SecondarySequenceNumber)
            {
                if (string.IsNullOrEmpty(dirname))
                {
                    dirname = ".";
                }

                var logFiles = Directory.GetFiles(dirname, $"{hiveBase}.LOG?");
                var log = LogManager.GetCurrentClassLogger();

                if (logFiles.Length == 0)
                {
                    if (noLogs == false)
                    {
                        log.Warn(
                            "Registry hive is dirty and no transaction logs were found in the same directory! LOGs should have same base name as the hive. Aborting!!");
                        throw new Exception(
                            "Sequence numbers do not match and transaction logs were not found in the same directory as the hive. Aborting");
                    }

                    log.Warn(
                        "Registry hive is dirty and no transaction logs were found in the same directory. Data may be missing! Continuing anyways...");
                }
                else
                {
                    if (noLogs == false)
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

                            reg.ProcessTransactionLogs(lt, true);
                        }
                        else
                        {
                            reg.ProcessTransactionLogs(logFiles.ToList(), true);
                        }
                    }
                    else
                    {
                        log.Warn(
                            "Registry hive is dirty and transaction logs were found in the same directory, but --nl was provided. Data may be missing! Continuing anyways...");
                    }
                }
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

            _logger.Debug("Getting Programs data");


            if (programsKey != null)
            {
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
                    var installDateArpLastModified = string.Empty;
                    DateTimeOffset? installDateMsi = null;
                    var installDateFromLinkFile = string.Empty;
                    var manufacturer = string.Empty;
                    var driverVerVersion = string.Empty;


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
                                        var d = new DateTimeOffset(
                                            DateTime.Parse(registryKeyValue.ValueData, DateTimeFormatInfo.InvariantInfo)
                                                .Ticks,
                                            TimeSpan.Zero);
                                        installDate = d;
                                    }

                                    break;
                                case "Language":
                                    if (registryKeyValue.ValueData.Length == 0)
                                    {
                                        language = 0;
                                    }
                                    else
                                    {
                                        language = int.Parse(registryKeyValue.ValueData);
                                    }

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
                                case "InstallDateArpLastModified":
                                    if (registryKeyValue.ValueData.Length > 0)
                                    {
                                        installDateArpLastModified = registryKeyValue.ValueData;
                                    }

                                    break;
                                case "InstallDateMsi":
                                    if (registryKeyValue.ValueData.Length > 0)
                                    {
                                        // _logger.Warn($"registryKeyValue.ValueData for InstallDate as InvariantCulture: {registryKeyValue.ValueData.ToString(CultureInfo.InvariantCulture)}");
                                        var d = new DateTimeOffset(
                                            DateTime.Parse(registryKeyValue.ValueData, DateTimeFormatInfo.InvariantInfo)
                                                .Ticks,
                                            TimeSpan.Zero);
                                        installDateMsi = d;
                                    }

                                    break;
                                case "InstallDateFromLinkFile":
                                    if (registryKeyValue.ValueData.Length > 0)
                                    {
                                        installDateFromLinkFile = registryKeyValue.ValueData;
                                    }

                                    break;
                                case "DriverVerVersion":
                                case "BusReportedDescription":
                                case "HWID":
                                case "COMPID":
                                case "STACKID":
                                case "UpperClassFilters":
                                case "UpperFilters":
                                case "LowerFilters":
                                case "BinFileVersion":
                                case "(default)":


                                    break;

                                case "Manufacturer":
                                    if (registryKeyValue.ValueData.Length > 0)
                                    {
                                        manufacturer = registryKeyValue.ValueData;
                                    }

                                    break;

                                default:
                                    _logger.Warn(
                                        $"Unknown value name in InventoryApplication at path {registryKey.KeyPath}: {registryKeyValue.ValueName}");
                                    break;
                            }
                        }

                        var pe = new ProgramsEntryNew(bundleManifestPath, hiddenArp, inboxModernApp, installDate,
                            language,
                            manifestPath, msiPackageCode, msiProductCode, name, osVersionAtInstallTime, packageFullName,
                            programId, programInstanceId, publisher, registryKeyPath, rootDirPath, source, storeAppType,
                            type, uninstallString, version, registryKey.LastWriteTime.Value, installDateArpLastModified,
                            installDateMsi, installDateFromLinkFile, manufacturer);

                        ProgramsEntries.Add(pe);
                    }
                    catch (Exception ex)
                    {
                        if (registryKey.NkRecord.IsFree == false)
                        {
                            _logger.Error($"Error parsing ProgramsEntry at {registryKey.KeyPath}. Error: {ex.Message}");
                            _logger.Error(
                                $"Please send the following text to saericzimmerman@gmail.com. \r\n\r\nKey data: {registryKey}");
                        }
                    }
                }
            }
            else
            {
                _logger.Warn("Hive does not contain a Root\\InventoryApplication key.");
            }

            _logger.Debug("Getting Files data");


            if (fileKey != null)
            {
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
                    long size = 0;
                    ulong usn = 0;
                    var version = string.Empty;
                    var description = string.Empty;

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
                                        var d = new DateTimeOffset(
                                            DateTime.Parse(subKeyValue.ValueData, DateTimeFormatInfo.InvariantInfo)
                                                .Ticks,
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
                                    try
                                    {
                                        if (subKeyValue.ValueData.StartsWith("0x"))
                                        {
                                            size = long.Parse(subKeyValue.ValueData.Replace("0x", ""),
                                                NumberStyles.HexNumber);
                                        }
                                        else
                                        {
                                            size = long.Parse(subKeyValue.ValueData);
                                        }
                                    }
                                    catch (Exception e)
                                    {
                                    }

                                    break;

                                case "BusReportedDescription":
                                case "FileSize":
                                case "Model":
                                case "Manufacturer":
                                case "ParentId":
                                case "MatchingID":
                                case "ClassGuid":
                                case "DriverName":
                                case "Enumerator":
                                case "Service":
                                case "DeviceState":
                                case "InstallState":
                                case "DriverVerVersion":
                                case "DriverPackageStrongName":
                                case "DriverVerDate":
                                case "ContainerId":
                                case "HiddenArp":
                                case "Inf":
                                case "ProblemCode":

                                case "Provider":
                                case "Class":
                                    break;
                                case "Description":
                                    description = subKeyValue.ValueData;
                                    break;
                                case "Version":
                                    version = subKeyValue.ValueData;
                                    break;
                                case "Usn":
                                    usn = ulong.Parse(subKeyValue.ValueData);
                                    break;
                                default:
                                    if (subKeyValue.VkRecord.IsFree == false)
                                    {
                                        _logger.Warn(
                                            $"Unknown value name when processing FileEntry at path '{subKey.KeyPath}': {subKeyValue.ValueName}");
                                    }

                                    break;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        if (subKey.NkRecord.IsFree == false)
                        {
                            _logger.Error($"Error parsing FileEntry at {subKey.KeyPath}. Error: {ex.Message}");
                            _logger.Error(
                                $"Please send the following text to saericzimmerman@gmail.com. \r\n\r\nKey data: {subKey}");
                        }
                    }

                    TotalFileEntries += 1;


                    var fe = new FileEntryNew(binaryType, binFileVersion, productVersion, fileId, isOsComponent,
                        isPeFile,
                        language, linkDate, longPathHash, lowerCaseLongPath, name, productName, productVersion,
                        programId,
                        publisher, size, version, subKey.LastWriteTime.Value, binProductVersion, usn, description);

                    if (hasLinkedProgram)
                    {
                        var program = ProgramsEntries.SingleOrDefault(t => t.ProgramId == fe.ProgramId);
                        if (program != null)
                        {
                            fe.ApplicationName = program.Name;
                            program.FileEntries.Add(fe);
                        }
                    }
                    else
                    {
                        fe.ApplicationName = "Unassociated";
                        UnassociatedFileEntries.Add(fe);
                    }
                }
            }
            else
            {
                _logger.Warn("Hive does not contain a Root\\InventoryApplicationFile key.");
            }

            _logger.Debug("Getting Shortcut data");

            var shortCutkey = reg.GetKey(@"Root\InventoryApplicationShortcut");

            if (shortCutkey != null)
            {
                foreach (var shortCutkeySubKey in shortCutkey.SubKeys)
                {
                    var lnkName = "";
                    if (shortCutkeySubKey.Values.Count > 0)
                    {
                        lnkName = shortCutkeySubKey.Values.First().ValueData;
                    }
                    ShortCuts.Add(new Shortcut(shortCutkeySubKey.KeyName, lnkName,
                        shortCutkeySubKey.LastWriteTime.Value));
                }
            }

            _logger.Debug("Getting InventoryDeviceContainer data");


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
                                case "(default)":
                                case "Model":
                                case "BusReportedDescription":
                                case "Version":
                                case "LowerClassFilters":
                                case "ManifestPath":
                                case "UpperClassFilters":
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
                        if (deviceSubKey.NkRecord.IsFree == false)
                        {
                            _logger.Error(
                                $"Error parsing DeviceContainer at {deviceSubKey.KeyPath}. Error: {ex.Message}");
                            _logger.Error(
                                $"Please send the following text to saericzimmerman@gmail.com. \r\n\r\nKey data: {deviceSubKey}");
                        }
                    }
                }
            }

            _logger.Debug("Getting InventoryDevicePnp data");

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
                                case "ExtendedInfs":
                                case "DeviceInterfaceClasses":
                                case "(default)":
                                    break;
                                default:
                                    _logger.Warn(
                                        $"Unknown value name when processing DevicePnp at path '{pnpsKey.KeyPath}': {keyValue.ValueName}");
                                    break;
                            }
                        }

                        var dp = new DevicePnp(pnpsKey.KeyName, pnpsKey.LastWriteTime.Value, busReportedDescription,
                            Class, classGuid, compid, containerId, description, deviceState, driverId, driverName,
                            driverPackageStrongName, driverVerDate, driverVerVersion, enumerator, hwid, inf,
                            installState, manufacturer, matchingId, model, parentId, problemCode, provider, service,
                            stackid);

                        DevicePnps.Add(dp);
                    }
                    catch (Exception ex)
                    {
                        if (pnpKey.NkRecord.IsFree == false)
                        {
                            _logger.Error($"Error parsing DevicePnp at {pnpKey.KeyPath}. Error: {ex.Message}");
                            _logger.Error(
                                $"Please send the following text to saericzimmerman@gmail.com. \r\n\r\nKey data: {pnpKey}");
                        }
                    }
                }
            }

            _logger.Debug("Getting InventoryDriverBinary data");

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
                                        var d = new DateTimeOffset(
                                            DateTime.Parse(keyValue.ValueData, DateTimeFormatInfo.InvariantInfo).Ticks,
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
                                case "(default)":
                                case "COMPID":
                                case "HWID":
                                    break;
                                default:
                                    _logger.Warn(
                                        $"Unknown value name when processing DriverBinary at path '{binKey.KeyPath}': {keyValue.ValueName}");
                                    break;
                            }
                        }

                        var db = new DriverBinary(binKey.KeyName, binKey.LastWriteTime.Value, driverCheckSum,
                            driverCompany, driverId, driverInBox, driverIsKernelMode, driverLastWriteTime, driverName,
                            driverPackageStrongName, driverSigned, driverTimeStamp, driverType, driverVersion,
                            imageSize, inf, product, productVersion, service, wdfVersion);

                        DriveBinaries.Add(db);
                    }
                    catch (Exception ex)
                    {
                        if (binaryKey.NkRecord.IsFree == false)
                        {
                            _logger.Error($"Error parsing DriverBinary at {binaryKey.KeyPath}. Error: {ex.Message}");
                            _logger.Error(
                                $"Please send the following text to saericzimmerman@gmail.com. \r\n\r\nKey data: {binaryKey}");
                        }
                    }
                }
            }

            _logger.Debug("Getting InventoryDriverPackage data");

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
                                        var d = new DateTimeOffset(
                                            DateTime.Parse(keyValue.ValueData, DateTimeFormatInfo.InvariantInfo).Ticks,
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
                                    case "IsActive":

                                        break;
                                default:
                                    _logger.Warn(
                                        $"Unknown value name when processing DriverPackage at path '{packKey.KeyPath}': {keyValue.ValueName}");
                                    break;
                            }
                        }

                        var dp = new DriverPackage(packKey.KeyName, packKey.LastWriteTime.Value, Class, ClassGuid,
                            Date, Directory, DriverInBox, Hwids, Inf, Provider, SubmissionId, SYSFILE, Version);

                        DriverPackages.Add(dp);
                    }
                    catch (Exception ex)
                    {
                        if (packaheKey.NkRecord.IsFree == false)
                        {
                            _logger.Error($"Error parsing DriverPackage at {packaheKey.KeyPath}. Error: {ex.Message}");
                            _logger.Error(
                                $"Please send the following text to saericzimmerman@gmail.com. \r\n\r\nKey data: {packaheKey}");
                        }
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