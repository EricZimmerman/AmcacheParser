using System;

namespace Amcache.Classes;

public class DriverBinary
{
    public DriverBinary(string keyName, DateTimeOffset keyLastWriteTimestamp, int driverChecksum,
        string driverCompany, string driverId, bool driverInBox, bool driverIsKernelMode,
        DateTimeOffset? driverLastWriteTime, string driverName, string driverPackageStrongName,
        bool driverSigned, DateTimeOffset? driverTimeStamp, string driverType, string driverVer, int imageSize,
        string inf, string product, string productVersion, string service, string wdfVersion)
    {
        KeyName = keyName;
        KeyLastWriteTimestamp = keyLastWriteTimestamp;
        DriverCheckSum = driverChecksum;
        DriverCompany = driverCompany;
        DriverId = driverId;
        DriverInBox = driverInBox;
        DriverIsKernelMode = driverIsKernelMode;
        DriverLastWriteTime = driverLastWriteTime;
        DriverName = driverName;
        DriverTimeStamp = driverTimeStamp;
        DriverPackageStrongName = driverPackageStrongName;
        DriverSigned = driverSigned;
        DriverType = driverType;
        DriverVersion = driverVer;
        ImageSize = imageSize;
        Inf = inf;
        Product = product;
        ProductVersion = productVersion;
        Service = service;
        WdfVersion = wdfVersion;
    }

    public string KeyName { get; }
    public DateTimeOffset KeyLastWriteTimestamp { get; }
    public int DriverCheckSum { get; }
    public string DriverCompany { get; }
    public string DriverId { get; }
    public bool DriverInBox { get; }
    public bool DriverIsKernelMode { get; }
    public DateTimeOffset? DriverLastWriteTime { get; }
    public string DriverName { get; }
    public string DriverPackageStrongName { get; }
    public bool DriverSigned { get; }
    public DateTimeOffset? DriverTimeStamp { get; }
    public string DriverType { get; }
    public string DriverVersion { get; }
    public int ImageSize { get; }
    public string Inf { get; }
    public string Product { get; }
    public string ProductVersion { get; }
    public string Service { get; }
    public string WdfVersion { get; }
}