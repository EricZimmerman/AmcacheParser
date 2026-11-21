using System;
using System.Globalization;
using Amcache.Classes;
using CsvHelper;

namespace AmcacheParser;

// The CSV columns of the library's classes. Their properties are get-only (set by their constructors), which CsvHelper
// 34's generated record writers don't write, and times use the --dt format given at run time, so each class has a
// hand-written record writer. The columns, their order and their text are what CsvHelper 33 wrote (2026.10.1): keep
// them, and add new columns at the end.

/// <summary>A record writer whose columns are a list of names, each with the code that writes its field</summary>
internal abstract class ColumnRecordWriter<T> : CsvRecordWriter<T>
{
    private readonly (string Name, Action<CsvWriter, T> Write)[] _columns;

    protected ColumnRecordWriter((string Name, Action<CsvWriter, T> Write)[] columns)
    {
        _columns = columns;
    }

    public override int FieldCount => _columns.Length;

    public override void WriteHeader(CsvWriter writer)
    {
        foreach (var column in _columns)
        {
            WriteColumnName(writer, column.Name);
        }
    }

    public override void Write(CsvWriter writer, T record)
    {
        foreach (var column in _columns)
        {
            column.Write(writer, record);
        }
    }

    /// <summary>A text column</summary>
    protected static (string Name, Action<CsvWriter, T> Write) Text(string name, Func<T, string> value) =>
        (name, (writer, record) => writer.WriteField(value(record)));

    /// <summary>A True/False column</summary>
    protected static (string Name, Action<CsvWriter, T> Write) Flag(string name, Func<T, bool> value) =>
        (name, (writer, record) => writer.WriteField(value(record)));

    /// <summary>A number in the invariant culture</summary>
    protected static (string Name, Action<CsvWriter, T> Write) Number<TValue>(string name, Func<T, TValue> value) where TValue : struct, IFormattable =>
        (name, (writer, record) => writer.WriteField(value(record).ToString(null, CultureInfo.InvariantCulture)));

    /// <summary>A number in the invariant culture, empty for null</summary>
    protected static (string Name, Action<CsvWriter, T> Write) Number<TValue>(string name, Func<T, TValue?> value) where TValue : struct, IFormattable =>
        (name, (writer, record) => writer.WriteField(value(record)?.ToString(null, CultureInfo.InvariantCulture)));

    /// <summary>A time in the --dt format (in the current culture, as before), empty for null</summary>
    protected static (string Name, Action<CsvWriter, T> Write) Time(string name, string dateFormat, Func<T, DateTimeOffset?> value) =>
        (name, (writer, record) => writer.WriteField(value(record)?.ToString(dateFormat)));
}

/// <summary>Root\File entries (old format). The unassociated and the associated CSVs have their columns in different orders</summary>
internal sealed class FileEntryOldWriter : ColumnRecordWriter<FileEntryOld>
{
    public FileEntryOldWriter(string dt, bool unassociated) : base(Columns(dt, unassociated))
    {
    }

    private static (string, Action<CsvWriter, FileEntryOld>)[] Columns(string dt, bool unassociated)
    {
        var programName = Text("ProgramName", r => r.ProgramName);
        var programId = Text("ProgramID", r => r.ProgramID);
        var productName = Text("ProductName", r => r.ProductName);
        var volumeId = Text("VolumeID", r => r.VolumeID);
        var companyName = Text("CompanyName", r => r.CompanyName);
        var volumeIdTime = Time("VolumeIDLastWriteTimestamp", dt, r => r.VolumeIDLastWriteTimestamp);
        var fileId = Text("FileID", r => r.FileID);
        var fileIdTime = Time("FileIDLastWriteTimestamp", dt, r => r.FileIDLastWriteTimestamp);
        var sha1 = Text("SHA1", r => r.SHA1);
        var fullPath = Text("FullPath", r => r.FullPath);
        var fileExtension = Text("FileExtension", r => r.FileExtension);
        var mftEntry = Number("MFTEntryNumber", r => r.MFTEntryNumber);
        var mftSequence = Number("MFTSequenceNumber", r => r.MFTSequenceNumber);
        var fileSize = Number("FileSize", r => r.FileSize);
        var fileVersionString = Text("FileVersionString", r => r.FileVersionString);
        var fileVersionNumber = Text("FileVersionNumber", r => r.FileVersionNumber);
        var fileDescription = Text("FileDescription", r => r.FileDescription);
        var switchBackContext = Text("SwitchBackContext", r => r.SwitchBackContext);
        var sizeOfImage = Number("SizeOfImage", r => r.SizeOfImage);
        var peHeaderHash = Text("PEHeaderHash", r => r.PEHeaderHash);
        var peHeaderChecksum = Number("PEHeaderChecksum", r => r.PEHeaderChecksum);
        var binProductVersion = Number("BinProductVersion", r => r.BinProductVersion);
        var binFileVersion = Number("BinFileVersion", r => r.BinFileVersion);
        var linkerVersion = Number("LinkerVersion", r => r.LinkerVersion);
        var binaryType = Number("BinaryType", r => r.BinaryType);
        var isLocal = Number("IsLocal", r => r.IsLocal);
        var guessProgramId = Number("GuessProgramID", r => r.GuessProgramID);
        var created = Time("Created", dt, r => r.Created);
        var lastModified = Time("LastModified", dt, r => r.LastModified);
        var lastModifiedStore = Time("LastModifiedStore", dt, r => r.LastModifiedStore);
        var linkDate = Time("LinkDate", dt, r => r.LinkDate);
        var languageId = Number("LanguageID", r => r.LanguageID);

        if (unassociated)
        {
            return new[]
            {
                programName, programId, volumeId, volumeIdTime, fileId, fileIdTime, sha1, fullPath, fileExtension, mftEntry, mftSequence, fileSize,
                fileVersionString, fileVersionNumber, fileDescription, sizeOfImage, peHeaderHash, peHeaderChecksum, binProductVersion, binFileVersion,
                linkerVersion, binaryType, isLocal, guessProgramId, created, lastModified, lastModifiedStore, linkDate, languageId, productName,
                companyName, switchBackContext
            };
        }

        //ProductName, CompanyName and SwitchBackContext were placed automatically here
        return new[]
        {
            programName, programId, productName, volumeId, companyName, volumeIdTime, fileId, fileIdTime, sha1, fullPath, fileExtension, mftEntry,
            mftSequence, fileSize, fileVersionString, fileVersionNumber, fileDescription, switchBackContext, sizeOfImage, peHeaderHash,
            peHeaderChecksum, binProductVersion, binFileVersion, linkerVersion, binaryType, isLocal, guessProgramId, created, lastModified,
            lastModifiedStore, linkDate, languageId
        };
    }
}

/// <summary>Root\Programs entries (old format)</summary>
internal sealed class ProgramsEntryOldWriter : ColumnRecordWriter<ProgramsEntryOld>
{
    public ProgramsEntryOldWriter(string dt) : base(new (string, Action<CsvWriter, ProgramsEntryOld>)[]
    {
            Text("ProgramID", r => r.ProgramID),
            Time("LastWriteTimestamp", dt, r => r.LastWriteTimestamp),
            Text("ProgramName_0", r => r.ProgramName_0),
            Text("ProgramVersion_1", r => r.ProgramVersion_1),
            Text("VendorName_2", r => r.VendorName_2),
            Time("InstallDateEpoch_a", dt, r => r.InstallDateEpoch_a),
            Text("UnknownGuid_10", r => r.UnknownGuid_10),
            Time("InstallDateEpoch_b", dt, r => r.InstallDateEpoch_b),
            Text("LanguageCode_3", r => r.LanguageCode_3),
            Text("UnknownGuid_12", r => r.UnknownGuid_12),
            Text("InstallSource_6", r => r.InstallSource_6),
            Text("UninstallGuid_11", r => r.UninstallGuid_11),
            Text("UninstallRegistryKey_7", r => r.UninstallRegistryKey_7),
            Number("UnknownDword_5", r => r.UnknownDword_5),
            Text("PathsList_d", r => r.PathsList_d),
            Number("UnknownDword_13", r => r.UnknownDword_13),
            Number("UnknownDword_14", r => r.UnknownDword_14),
            Number("UnknownDword_15", r => r.UnknownDword_15),
            //0x and the bytes in hex, as CsvHelper 33's byte array converter wrote them
            Text("UnknownBytes_16", r => r.UnknownBytes_16 == null ? null : $"0x{BitConverter.ToString(r.UnknownBytes_16).Replace("-", string.Empty)}"),
            Number("UnknownQWord_17", r => r.UnknownQWord_17),
            Number("UnknownDword_18", r => r.UnknownDword_18),
            Text("UninstallGuid_f", r => r.UninstallGuid_f)
    })
    {
    }
}

/// <summary>InventoryApplicationFile entries (new format), unassociated or associated</summary>
internal sealed class FileEntryNewWriter : ColumnRecordWriter<FileEntryNew>
{
    public FileEntryNewWriter(string dt) : base(new (string, Action<CsvWriter, FileEntryNew>)[]
    {
            Text("ApplicationName", r => r.ApplicationName),
            Text("ProgramId", r => r.ProgramId),
            Time("FileKeyLastWriteTimestamp", dt, r => r.FileKeyLastWriteTimestamp),
            Text("SHA1", r => r.SHA1),
            Flag("IsOsComponent", r => r.IsOsComponent),
            Text("FullPath", r => r.FullPath),
            Text("Name", r => r.Name),
            Text("FileExtension", r => r.FileExtension),
            Time("LinkDate", dt, r => r.LinkDate),
            Text("ProductName", r => r.ProductName),
            Number("Size", r => r.Size),
            Text("Version", r => r.Version),
            Text("ProductVersion", r => r.ProductVersion),
            Text("LongPathHash", r => r.LongPathHash),
            Text("BinaryType", r => r.BinaryType),
            Flag("IsPeFile", r => r.IsPeFile),
            Text("BinFileVersion", r => r.BinFileVersion),
            Text("BinProductVersion", r => r.BinProductVersion),
            Number("Usn", r => r.Usn),
            Number("Language", r => r.Language),
            Text("Description", r => r.Description),
            Text("OriginalFileName", r => r.OriginalFileName),
            Text("Publisher", r => r.Publisher),
            Text("Sha256", r => r.Sha256),
            Text("KeyName", r => r.KeyName),
            Text("AppxPackageFullName", r => r.AppxPackageFullName),
            Text("AppxPackageRelativeId", r => r.AppxPackageRelativeId)
    })
    {
    }
}

/// <summary>InventoryApplication entries (new format)</summary>
internal sealed class ProgramsEntryNewWriter : ColumnRecordWriter<ProgramsEntryNew>
{
    public ProgramsEntryNewWriter(string dt) : base(new (string, Action<CsvWriter, ProgramsEntryNew>)[]
    {
            Text("ProgramId", r => r.ProgramId),
            Time("KeyLastWriteTimestamp", dt, r => r.KeyLastWriteTimestamp),
            Text("Name", r => r.Name),
            Text("Version", r => r.Version),
            Text("Publisher", r => r.Publisher),
            Text("InstallDateArpLastModified", r => r.InstallDateArpLastModified),
            Time("InstallDate", dt, r => r.InstallDate),
            //never had the --dt format: CsvHelper 33's default for a DateTimeOffset (MM/dd/yyyy HH:mm:ss zzz)
            Text("InstallDateMsi", r => r.InstallDateMsi?.ToString(null, CultureInfo.InvariantCulture)),
            Text("OSVersionAtInstallTime", r => r.OSVersionAtInstallTime),
            Text("InstallDateFromLinkFile", r => r.InstallDateFromLinkFile),
            Text("BundleManifestPath", r => r.BundleManifestPath),
            Flag("HiddenArp", r => r.HiddenArp),
            Flag("InboxModernApp", r => r.InboxModernApp),
            Number("Language", r => r.Language),
            Text("ManifestPath", r => r.ManifestPath),
            Text("MsiPackageCode", r => r.MsiPackageCode),
            Text("MsiProductCode", r => r.MsiProductCode),
            Text("PackageFullName", r => r.PackageFullName),
            Text("ProgramInstanceId", r => r.ProgramInstanceId),
            Text("RegistryKeyPath", r => r.RegistryKeyPath),
            Text("RootDirPath", r => r.RootDirPath),
            Text("Type", r => r.Type),
            Text("Source", r => r.Source),
            Text("StoreAppType", r => r.StoreAppType),
            Text("UninstallString", r => r.UninstallString),
            Text("Manufacturer", r => r.Manufacturer),
            Text("KeyName", r => r.KeyName),
            Text("UserSid", r => r.UserSid),
            Time("MsiInstallDate", dt, r => r.MsiInstallDate)
    })
    {
    }
}

/// <summary>InventoryApplicationShortcut entries</summary>
internal sealed class ShortcutWriter : ColumnRecordWriter<Shortcut>
{
    public ShortcutWriter(string dt) : base(new (string, Action<CsvWriter, Shortcut>)[]
    {
            Text("KeyName", r => r.KeyName),
            Text("LnkName", r => r.LnkName),
            Time("KeyLastWriteTimestamp", dt, r => r.KeyLastWriteTimestamp),
            Text("ShortcutTargetPath", r => r.ShortcutTargetPath),
            Text("ShortcutAumid", r => r.ShortcutAumid),
            Text("ShortcutProgramId", r => r.ShortcutProgramId)
    })
    {
    }
}

/// <summary>InventoryDriverBinary entries</summary>
internal sealed class DriverBinaryWriter : ColumnRecordWriter<DriverBinary>
{
    public DriverBinaryWriter(string dt) : base(new (string, Action<CsvWriter, DriverBinary>)[]
    {
            Text("KeyName", r => r.KeyName),
            Time("KeyLastWriteTimestamp", dt, r => r.KeyLastWriteTimestamp),
            Time("DriverTimeStamp", dt, r => r.DriverTimeStamp),
            Time("DriverLastWriteTime", dt, r => r.DriverLastWriteTime),
            Text("DriverName", r => r.DriverName),
            Flag("DriverInBox", r => r.DriverInBox),
            Flag("DriverIsKernelMode", r => r.DriverIsKernelMode),
            Flag("DriverSigned", r => r.DriverSigned),
            Number("DriverCheckSum", r => r.DriverCheckSum),
            Text("DriverCompany", r => r.DriverCompany),
            Text("DriverId", r => r.DriverId),
            Text("DriverPackageStrongName", r => r.DriverPackageStrongName),
            Text("DriverType", r => r.DriverType),
            Text("DriverVersion", r => r.DriverVersion),
            Number("ImageSize", r => r.ImageSize),
            Text("Inf", r => r.Inf),
            Text("Product", r => r.Product),
            Text("ProductVersion", r => r.ProductVersion),
            Text("Service", r => r.Service),
            Text("WdfVersion", r => r.WdfVersion)
    })
    {
    }
}

/// <summary>InventoryDeviceContainer entries</summary>
internal sealed class DeviceContainerWriter : ColumnRecordWriter<DeviceContainer>
{
    public DeviceContainerWriter(string dt) : base(new (string, Action<CsvWriter, DeviceContainer>)[]
    {
            Text("KeyName", r => r.KeyName),
            Time("KeyLastWriteTimestamp", dt, r => r.KeyLastWriteTimestamp),
            Text("Categories", r => r.Categories),
            Text("DiscoveryMethod", r => r.DiscoveryMethod),
            Text("FriendlyName", r => r.FriendlyName),
            Text("Icon", r => r.Icon),
            Flag("IsActive", r => r.IsActive),
            Flag("IsConnected", r => r.IsConnected),
            Flag("IsMachineContainer", r => r.IsMachineContainer),
            Flag("IsNetworked", r => r.IsNetworked),
            Flag("IsPaired", r => r.IsPaired),
            Text("Manufacturer", r => r.Manufacturer),
            Text("ModelId", r => r.ModelId),
            Text("ModelName", r => r.ModelName),
            Text("ModelNumber", r => r.ModelNumber),
            Text("PrimaryCategory", r => r.PrimaryCategory),
            Text("State", r => r.State)
    })
    {
    }
}

/// <summary>InventoryDriverPackage entries</summary>
internal sealed class DriverPackageWriter : ColumnRecordWriter<DriverPackage>
{
    public DriverPackageWriter(string dt) : base(new (string, Action<CsvWriter, DriverPackage>)[]
    {
            Text("KeyName", r => r.KeyName),
            Time("KeyLastWriteTimestamp", dt, r => r.KeyLastWriteTimestamp),
            Time("Date", dt, r => r.Date),
            Text("Class", r => r.Class),
            Text("Directory", r => r.Directory),
            Flag("DriverInBox", r => r.DriverInBox),
            Text("Hwids", r => r.Hwids),
            Text("Inf", r => r.Inf),
            Text("Provider", r => r.Provider),
            Text("SubmissionId", r => r.SubmissionId),
            Text("SYSFILE", r => r.SYSFILE),
            Text("Version", r => r.Version),
            Text("ClassGuid", r => r.ClassGuid),
            Text("DriverPackageStrongName", r => r.DriverPackageStrongName),
            Text("FlightIds", r => r.FlightIds),
            Text("RecoveryIds", r => r.RecoveryIds),
            Text("IsActive", r => r.IsActive)
    })
    {
    }
}

/// <summary>InventoryDevicePnp entries</summary>
internal sealed class DevicePnpWriter : ColumnRecordWriter<DevicePnp>
{
    public DevicePnpWriter(string dt) : base(new (string, Action<CsvWriter, DevicePnp>)[]
    {
            Text("KeyName", r => r.KeyName),
            Time("KeyLastWriteTimestamp", dt, r => r.KeyLastWriteTimestamp),
            Text("BusReportedDescription", r => r.BusReportedDescription),
            Text("Class", r => r.Class),
            Text("ClassGuid", r => r.ClassGuid),
            Text("Compid", r => r.Compid),
            Text("ContainerId", r => r.ContainerId),
            Text("Description", r => r.Description),
            Text("DriverId", r => r.DriverId),
            Text("DriverPackageStrongName", r => r.DriverPackageStrongName),
            Text("DriverName", r => r.DriverName),
            Text("DriverVerDate", r => r.DriverVerDate),
            Text("DriverVerVersion", r => r.DriverVerVersion),
            Text("Enumerator", r => r.Enumerator),
            Text("HWID", r => r.HWID),
            Text("Inf", r => r.Inf),
            Text("InstallState", r => r.InstallState),
            Text("Manufacturer", r => r.Manufacturer),
            Text("MatchingId", r => r.MatchingId),
            Text("Model", r => r.Model),
            Text("ParentId", r => r.ParentId),
            Text("ProblemCode", r => r.ProblemCode),
            Text("Provider", r => r.Provider),
            Text("Service", r => r.Service),
            Text("Stackid", r => r.Stackid),
            Text("DeviceState", r => r.DeviceState),
            Text("InstallDate", r => r.InstallDate),
            Text("FirstInstallDate", r => r.FirstInstallDate),
            Text("ExtendedInfs", r => r.ExtendedInfs),
            Text("UpperClassFilters", r => r.UpperClassFilters),
            Text("LowerClassFilters", r => r.LowerClassFilters),
            Text("UpperFilters", r => r.UpperFilters),
            Text("LowerFilters", r => r.LowerFilters),
            Text("DeviceInterfaceClasses", r => r.DeviceInterfaceClasses),
            Text("LocationPaths", r => r.LocationPaths)
    })
    {
    }
}
