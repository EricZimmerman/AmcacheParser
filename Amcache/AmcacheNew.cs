using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Amcache.Classes;
using RawCopy;
using Registry;
using Registry.Abstractions;
using Serilog;

namespace Amcache;

/// <summary>
///     The Windows 10 and later format: the Root\Inventory* keys of Amcache.hve, or (from Windows 11 26H1/26H2 on) the
///     Inventory*.db SQLite databases next to it (see <see cref="FromDatabases" />). Both are read with the same logic:
///     a database column is the registry value of the same name, and a row (by ItemKey) is a key
/// </summary>
public partial class AmcacheNew
{
    private AmcacheNew()
    {
        UnassociatedFileEntries = new List<FileEntryNew>();
        ProgramsEntries = new List<ProgramsEntryNew>();
        DeviceContainers = new List<DeviceContainer>();
        DevicePnps = new List<DevicePnp>();
        DriveBinaries = new List<DriverBinary>();
        DriverPackages = new List<DriverPackage>();
        ShortCuts = new List<Shortcut>();
        OtherTables = new List<DatabaseTable>();
    }

    public AmcacheNew(string hive, bool recoverDeleted, bool noLogs) : this()
    {
        RegistryHive reg;

        var dirname = Path.GetDirectoryName(hive);
        var hiveBase = Path.GetFileName(hive);

        List<RawCopyReturn> rawFiles = null;

        try
        {
            reg = new RegistryHive(hive)
            {
                RecoverDeleted = recoverDeleted
            };
        }
        catch (IOException)
        {
            //file is in use

            if (RawCopy.Helper.IsAdministrator() == false)
            {
                throw new UnauthorizedAccessException("Administrator privileges not found!");
            }

            Log.Warning("'{Hive}' is in use. Rerouting...",hive);
            Console.WriteLine();

            var files = new List<string>();
            files.Add(hive);

            var logFiles = Helper.LogFiles(dirname, hiveBase);

            foreach (var logFile in logFiles)
            {
                files.Add(logFile);
            }

            rawFiles = RawCopy.Helper.GetRawFiles(files);

            var b = new byte[rawFiles.First().FileStream.Length];

            rawFiles.First().FileStream.Read(b, 0, (int) rawFiles.First().FileStream.Length);

            reg = new RegistryHive(b, rawFiles.First().InputFilename)
            {
                RecoverDeleted = recoverDeleted
            };
        }

        //dirty: the sequence numbers differ, or the header's checksum is wrong and there are logs to take it from
        if (reg.Header.PrimarySequenceNumber != reg.Header.SecondarySequenceNumber ||
            reg.Header.ValidateCheckSum() == false && Helper.LogFiles(dirname, hiveBase).Count > 0)
        {
            if (string.IsNullOrEmpty(dirname))
            {
                dirname = ".";
            }

            var logFiles = Helper.LogFiles(dirname, hiveBase);

            if (logFiles.Count == 0)
            {
                if (noLogs == false)
                {
                    Log.Warning(
                        "Registry hive is dirty and no transaction logs were found in the same directory! LOGs should have same base name as the hive. Aborting!!");
                    throw new Exception(
                        "Sequence numbers do not match and transaction logs were not found in the same directory as the hive. Aborting");
                }

                Log.Warning(
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

                            rawCopyReturn.FileStream.Read(b, 0, (int) rawCopyReturn.FileStream.Length);

                            var tt = new TransactionLogFileInfo(rawCopyReturn.InputFilename,b);
                            lt.Add(tt);
                        }

                        reg.ProcessTransactionLogs(lt, true);
                    }
                    else
                    {
                        reg.ProcessTransactionLogs(logFiles, true);
                    }
                }
                else
                {
                    Log.Warning(
                        "Registry hive is dirty and transaction logs were found in the same directory, but --nl was provided. Data may be missing! Continuing anyways...");
                }
            }
        }


        reg.ParseHive();

        //hives upgraded from Windows 8 can have the old format's keys as well (AmcacheOld reads them)
        HasOldFormatEntries = reg.GetKey(@"Root\File")?.SubKeys.Count > 0 || reg.GetKey(@"Root\Programs")?.SubKeys.Count > 0;

        var reporter = new HiveReporter();

        Log.Debug("Getting Programs data");

        var programsKey = reg.GetKey(@"Root\InventoryApplication");
        if (programsKey != null)
        {
            AddPrograms(programsKey.SubKeys.Select(FromKey), reporter);
        }
        else
        {
            Log.Warning("Hive does not contain a {Key} key","Root\\InventoryApplication");
        }

        Log.Debug("Getting Files data");

        var fileKey = reg.GetKey(@"Root\InventoryApplicationFile");
        if (fileKey != null)
        {
            AddFiles(fileKey.SubKeys.Select(FromKey), reporter);
        }
        else
        {
            Log.Warning("Hive does not contain a {Key} key","Root\\InventoryApplicationFile");
        }

        Log.Debug("Getting Shortcut data");

        var shortCutkey = reg.GetKey(@"Root\InventoryApplicationShortcut");
        if (shortCutkey != null)
        {
            AddShortcuts(shortCutkey.SubKeys.Select(FromKey), false);
        }

        Log.Debug("Getting InventoryDeviceContainer data");

        var deviceKey = reg.GetKey(@"Root\InventoryDeviceContainer");
        if (deviceKey != null)
        {
            AddDeviceContainers(deviceKey.SubKeys.Select(FromKey), reporter);
        }

        Log.Debug("Getting InventoryDevicePnp data");

        var pnpKey = reg.GetKey(@"Root\InventoryDevicePnp");
        if (pnpKey != null)
        {
            AddDevicePnps(pnpKey.SubKeys.Select(FromKey), reporter);
        }

        Log.Debug("Getting InventoryDriverBinary data");

        var binaryKey = reg.GetKey(@"Root\InventoryDriverBinary");
        if (binaryKey != null)
        {
            AddDriverBinaries(binaryKey.SubKeys.Select(FromKey), reporter);
        }

        Log.Debug("Getting InventoryDriverPackage data");

        var packageKey = reg.GetKey(@"Root\InventoryDriverPackage");
        if (packageKey != null)
        {
            AddDriverPackages(packageKey.SubKeys.Select(FromKey), reporter);
        }
    }

    public List<FileEntryNew> UnassociatedFileEntries { get; }
    public List<ProgramsEntryNew> ProgramsEntries { get; }
    public List<DeviceContainer> DeviceContainers { get; }
    public List<DevicePnp> DevicePnps { get; }
    public List<DriverBinary> DriveBinaries { get; }
    public List<DriverPackage> DriverPackages { get; }

    public List<Shortcut> ShortCuts { get; }

    public int TotalFileEntries { get; private set; }

    /// <summary>The hive also has Root\File or Root\Programs entries (the old format; see AmcacheOld)</summary>
    public bool HasOldFormatEntries { get; private set; }

    /// <summary>Dates that could not be read (two dates in one value, or none at all) and were left empty</summary>
    public int InvalidDates { get; private set; }

    /// <summary>
    ///     Databases only: the tables (and the Metadata tables) that have no class here, as they are. Empty for a hive
    /// </summary>
    public List<DatabaseTable> OtherTables { get; }

    private static InventoryItem FromKey(RegistryKey key) =>
        new(key.KeyName, key.KeyPath, key.LastWriteTime ?? DateTimeOffset.MinValue, key.NkRecord.IsFree,
            key.Values.Select(t => new InventoryValue(t.ValueName, t.ValueData, t.VkRecord.IsFree)).ToList());

    private void AddPrograms(IEnumerable<InventoryItem> items, IReporter reporter)
    {
        foreach (var registryKey in items)
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
            var userSid = string.Empty;
            DateTimeOffset? msiInstallDate = null;

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
                            installDate = ParseDate(registryKey, registryKeyValue);
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
                            installDateMsi = ParseDate(registryKey, registryKeyValue);
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
                        case "SentDetailedInv":
                        case "ProductName":
                        case "Provider":
                        case "Inf":
                        case "(default)":
                            reporter.NotTracked("InventoryApplication", registryKey, registryKeyValue);
                            break;
                        case "MsiInstallDate":
                            msiInstallDate = ParseDate(registryKey, registryKeyValue);
                            break;
                        case "UserSid":
                            userSid = registryKeyValue.ValueData;
                            break;

                        case "Manufacturer":
                            if (registryKeyValue.ValueData.Length > 0)
                            {
                                manufacturer = registryKeyValue.ValueData;
                            }

                            break;

                        default:
                            reporter.Unknown("InventoryApplication", registryKey, registryKeyValue, false);
                            break;
                    }
                }

                var pe = new ProgramsEntryNew(bundleManifestPath, hiddenArp, inboxModernApp, installDate,
                    language,
                    manifestPath, msiPackageCode, msiProductCode, name, osVersionAtInstallTime, packageFullName,
                    programId, programInstanceId, publisher, registryKeyPath, rootDirPath, source, storeAppType,
                    type, uninstallString, version, registryKey.LastWriteTime, installDateArpLastModified,
                    installDateMsi, installDateFromLinkFile, manufacturer)
                {
                    KeyName = registryKey.KeyName,
                    UserSid = userSid,
                    MsiInstallDate = msiInstallDate
                };

                ProgramsEntries.Add(pe);
            }
            catch (Exception ex)
            {
                reporter.Error("ProgramsEntry", registryKey, ex);
            }
        }
    }

    private void AddFiles(IEnumerable<InventoryItem> items, IReporter reporter)
    {
        //ProgramId to program, for the files of each program (the first program with an id, as before)
        var programs = new Dictionary<string, ProgramsEntryNew>();
        foreach (var program in ProgramsEntries)
        {
            if (program.ProgramId != null && programs.ContainsKey(program.ProgramId) == false)
            {
                programs.Add(program.ProgramId, program);
            }
        }

        foreach (var subKey in items)
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
            var originalFileName = string.Empty;
            var sha256 = string.Empty;
            var appxPackageFullName = string.Empty;
            var appxPackageRelativeId = string.Empty;
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
                                try
                                {
                                    var d = new DateTimeOffset(
                                        DateTime.Parse(subKeyValue.ValueData, DateTimeFormatInfo.InvariantInfo)
                                            .Ticks,
                                        TimeSpan.Zero);
                                    linkDate = d;
                                }
                                catch (Exception)
                                {
                                    Log.Debug("LinkDate appears to not be a valid timestamp! Setting to 0. Data found: {L}",subKeyValue.ValueData);
                                    linkDate = DateTimeOffset.MinValue;
                                }
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

                            if (programs.ContainsKey(programId))
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
                            catch (Exception)
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
                        case "LowerFilters":
                        case "ProblemCode":
                        case "LocationPaths":
                        case "Provider":
                        case "Class":
                            reporter.NotTracked("FileEntry", subKey, subKeyValue);
                            break;
                        case "OriginalFileName":
                            originalFileName = subKeyValue.ValueData;
                            break;
                        case "AppxPackageFullName":
                            appxPackageFullName = subKeyValue.ValueData;
                            break;
                        case "AppxPackageRelativeId":
                            appxPackageRelativeId = subKeyValue.ValueData;
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
                        //the databases have it (a column of its own); FileId is still 0000 + SHA-1
                        case "Sha256":
                            sha256 = subKeyValue.ValueData;
                            break;
                        default:
                            reporter.Unknown("FileEntry", subKey, subKeyValue, subKeyValue.IsDeleted);
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                reporter.Error("FileEntry", subKey, ex);
            }

            TotalFileEntries += 1;

            var fe = new FileEntryNew(binaryType, binFileVersion, productVersion, fileId, isOsComponent,
                isPeFile,
                language, linkDate, longPathHash, lowerCaseLongPath, name, productName, productVersion,
                programId,
                publisher, size, version, subKey.LastWriteTime, binProductVersion, usn, description,originalFileName)
            {
                Sha256 = sha256,
                KeyName = subKey.KeyName,
                AppxPackageFullName = appxPackageFullName,
                AppxPackageRelativeId = appxPackageRelativeId
            };

            if (hasLinkedProgram && programs.TryGetValue(fe.ProgramId, out var program))
            {
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

    /// <param name="byName">
    ///     true for databases: the path is the ShortcutPath column. A hive key has it as its only value (the first is
    ///     used)
    /// </param>
    private void AddShortcuts(IEnumerable<InventoryItem> items, bool byName)
    {
        foreach (var shortCutkeySubKey in items)
        {
            string Get(string name) => shortCutkeySubKey.Values.FirstOrDefault(t => t.ValueName == name).ValueData ?? string.Empty;

            var lnkName = "";
            if (byName)
            {
                lnkName = Get("ShortcutPath");
            }
            else if (shortCutkeySubKey.Values.Count > 0)
            {
                lnkName = shortCutkeySubKey.Values.First().ValueData;
            }

            ShortCuts.Add(new Shortcut(shortCutkeySubKey.KeyName, lnkName,
                shortCutkeySubKey.LastWriteTime)
            {
                ShortcutTargetPath = Get("ShortcutTargetPath"),
                ShortcutAumid = Get("ShortcutAumid"),
                ShortcutProgramId = Get("ShortcutProgramId")
            });
        }
    }

    private void AddDeviceContainers(IEnumerable<InventoryItem> items, IReporter reporter)
    {
        foreach (var deviceSubKey in items)
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
                            reporter.NotTracked("DeviceContainer", deviceSubKey, keyValue);
                            break;

                        default:
                            reporter.Unknown("DeviceContainer", deviceSubKey, keyValue, false);
                            break;
                    }
                }

                var dc = new DeviceContainer(deviceSubKey.KeyName, deviceSubKey.LastWriteTime, categories,
                    discoveryMethod, friendlyName, icon, isActive, isConnected, isMachineContainer, isNetworked,
                    isPaired, manufacturer, modelId, modelName, modelNumber, primaryCategory, state);

                DeviceContainers.Add(dc);
            }
            catch (Exception ex)
            {
                reporter.Error("DeviceContainer", deviceSubKey, ex);
            }
        }
    }

    private void AddDevicePnps(IEnumerable<InventoryItem> items, IReporter reporter)
    {
        foreach (var pnpsKey in items)
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
            var extra = new Dictionary<string, string>();

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
                        //kept as they are, like DriverVerDate
                        case "InstallDate":
                        case "FirstInstallDate":
                        case "ExtendedInfs":
                        case "UpperClassFilters":
                        case "LowerClassFilters":
                        case "UpperFilters":
                        case "LowerFilters":
                        case "DeviceInterfaceClasses":
                        case "LocationPaths":
                            extra[keyValue.ValueName] = keyValue.ValueData;
                            break;
                        case "(default)":
                        case "DeviceExtDriversFlightIds":
                        case "DeviceDriverFlightId":
                            reporter.NotTracked("DevicePnp", pnpsKey, keyValue);
                            break;
                        default:
                            reporter.Unknown("DevicePnp", pnpsKey, keyValue, false);
                            break;
                    }
                }

                var dp = new DevicePnp(pnpsKey.KeyName, pnpsKey.LastWriteTime, busReportedDescription,
                    Class, classGuid, compid, containerId, description, deviceState, driverId, driverName,
                    driverPackageStrongName, driverVerDate, driverVerVersion, enumerator, hwid, inf,
                    installState, manufacturer, matchingId, model, parentId, problemCode, provider, service,
                    stackid)
                {
                    InstallDate = Extra(extra, "InstallDate"),
                    FirstInstallDate = Extra(extra, "FirstInstallDate"),
                    ExtendedInfs = Extra(extra, "ExtendedInfs"),
                    UpperClassFilters = Extra(extra, "UpperClassFilters"),
                    LowerClassFilters = Extra(extra, "LowerClassFilters"),
                    UpperFilters = Extra(extra, "UpperFilters"),
                    LowerFilters = Extra(extra, "LowerFilters"),
                    DeviceInterfaceClasses = Extra(extra, "DeviceInterfaceClasses"),
                    LocationPaths = Extra(extra, "LocationPaths")
                };

                DevicePnps.Add(dp);
            }
            catch (Exception ex)
            {
                reporter.Error("DevicePnp", pnpsKey, ex);
            }
        }
    }

    private void AddDriverBinaries(IEnumerable<InventoryItem> items, IReporter reporter)
    {
        foreach (var binKey in items)
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
                            driverId = keyValue.ValueData.Substring(4);
                            break;
                        case "DriverInBox":
                            driverInBox = keyValue.ValueData == "1";
                            break;
                        case "DriverIsKernelMode":
                            driverIsKernelMode = keyValue.ValueData == "1";
                            break;
                        case "DriverLastWriteTime":
                            driverLastWriteTime = ParseDate(binKey, keyValue);
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
                            reporter.NotTracked("DriverBinary", binKey, keyValue);
                            break;
                        default:
                            reporter.Unknown("DriverBinary", binKey, keyValue, false);
                            break;
                    }
                }

                var db = new DriverBinary(binKey.KeyName, binKey.LastWriteTime, driverCheckSum,
                    driverCompany, driverId, driverInBox, driverIsKernelMode, driverLastWriteTime, driverName,
                    driverPackageStrongName, driverSigned, driverTimeStamp, driverType, driverVersion,
                    imageSize, inf, product, productVersion, service, wdfVersion);

                DriveBinaries.Add(db);
            }
            catch (Exception ex)
            {
                reporter.Error("DriverBinary", binKey, ex);
            }
        }
    }

    private void AddDriverPackages(IEnumerable<InventoryItem> items, IReporter reporter)
    {
        foreach (var packKey in items)
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
            var extra = new Dictionary<string, string>();

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
                            Date = ParseDate(packKey, keyValue);
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

                        case "DriverPackageStrongName":
                        case "FlightIds":
                        case "RecoveryIds":
                        case "IsActive":
                            extra[keyValue.ValueName] = keyValue.ValueData;
                            break;
                        case "LowerFilters":
                        case "Language":
                            reporter.NotTracked("DriverPackage", packKey, keyValue);
                            break;
                        default:
                            reporter.Unknown("DriverPackage", packKey, keyValue, false);
                            break;
                    }
                }

                var dp = new DriverPackage(packKey.KeyName, packKey.LastWriteTime, Class, ClassGuid,
                    Date, Directory, DriverInBox, Hwids, Inf, Provider, SubmissionId, SYSFILE, Version)
                {
                    DriverPackageStrongName = Extra(extra, "DriverPackageStrongName"),
                    FlightIds = Extra(extra, "FlightIds"),
                    RecoveryIds = Extra(extra, "RecoveryIds"),
                    IsActive = Extra(extra, "IsActive")
                };

                DriverPackages.Add(dp);
            }
            catch (Exception ex)
            {
                reporter.Error("DriverPackage", packKey, ex);
            }
        }
    }

    /// <summary>
    ///     A date as Windows writes it (MM/dd/yyyy HH:mm:ss, yyyy-M-d), as UTC. Programs can write anything (Discord:
    ///     53/16/2024), and such a date is left empty instead of losing the entry, which happened before
    /// </summary>
    private DateTimeOffset? ParseDate(InventoryItem item, InventoryValue value)
    {
        if (value.ValueData.Length == 0)
        {
            return null;
        }

        if (DateTime.TryParse(value.ValueData, DateTimeFormatInfo.InvariantInfo, DateTimeStyles.None, out var date))
        {
            return new DateTimeOffset(date.Ticks, TimeSpan.Zero);
        }

        Log.Debug("{ValueName} at {KeyPath} is not a valid date: {Data}", value.ValueName, item.KeyPath, value.ValueData);
        InvalidDates += 1;
        return null;
    }

    private static string Extra(Dictionary<string, string> values, string name) =>
        values.TryGetValue(name, out var value) ? value : string.Empty;

    /// <summary>A hive key or a database row</summary>
    internal sealed class InventoryItem
    {
        public InventoryItem(string keyName, string keyPath, DateTimeOffset lastWriteTime, bool isDeleted, List<InventoryValue> values)
        {
            KeyName = keyName;
            KeyPath = keyPath;
            LastWriteTime = lastWriteTime;
            IsDeleted = isDeleted;
            Values = values;
        }

        public string KeyName { get; }
        public string KeyPath { get; }
        public DateTimeOffset LastWriteTime { get; }
        public bool IsDeleted { get; }
        public List<InventoryValue> Values { get; }
    }

    /// <summary>A hive value or a database column, with its data as the registry has it (numbers in decimal)</summary>
    internal readonly struct InventoryValue
    {
        public InventoryValue(string valueName, string valueData, bool isDeleted)
        {
            ValueName = valueName;
            ValueData = valueData ?? string.Empty;
            IsDeleted = isDeleted;
        }

        public string ValueName { get; }
        public string ValueData { get; }
        public bool IsDeleted { get; }
    }

    internal interface IReporter
    {
        void NotTracked(string category, InventoryItem item, InventoryValue value);
        void Unknown(string category, InventoryItem item, InventoryValue value, bool onlyWhenNotDeleted);
        void Error(string category, InventoryItem item, Exception ex);
    }

    /// <summary>As the hive was always reported: per key</summary>
    private sealed class HiveReporter : IReporter
    {
        public void NotTracked(string category, InventoryItem item, InventoryValue value)
        {
            if (category == "InventoryApplication")
            {
                Log.Debug("Non-tracked data when processing InventoryApplication at path {KeyPath}: {ValueName}==>{ValueData}",item.KeyPath,value.ValueName,value.ValueData);
                return;
            }

            Log.Debug("Non-tracked data when processing {Category} at path {KeyPath}: {ValueName}==>{ValueData}",category,item.KeyPath,value.ValueName,value.ValueData);
        }

        public void Unknown(string category, InventoryItem item, InventoryValue value, bool onlyWhenNotDeleted)
        {
            if (onlyWhenNotDeleted && value.IsDeleted)
            {
                return;
            }

            if (category == "InventoryApplication")
            {
                Log.Warning("Unknown value name in InventoryApplication at path {KeyPath}: {ValueName}",item.KeyPath,value.ValueName);
                return;
            }

            Log.Warning("Unknown value name when processing {Category} at path {KeyPath}: {ValueName}",category,item.KeyPath,value.ValueName);
        }

        public void Error(string category, InventoryItem item, Exception ex)
        {
            if (item.IsDeleted)
            {
                return;
            }

            Log.Error(ex,"Error parsing {Category} at {KeyPath}. Error: {Message}",category,item.KeyPath,ex.Message);
            Log.Error("Please send the following text to {Email}","saericzimmerman@gmail.com");
            Log.Error("Key data: {Values}",string.Join(", ",item.Values.Select(t => $"{t.ValueName}={t.ValueData}")));
        }
    }
}
