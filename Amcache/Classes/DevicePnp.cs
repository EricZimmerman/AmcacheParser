using System;

namespace Amcache.Classes;

public class DevicePnp
{
    public DevicePnp(string keyName, DateTimeOffset keyLastWriteTimestamp, string busReportedDesc, string clss,
        string classGuid, string compId, string containerId, string desc, string deviceState, string driverId,
        string driverName, string driverPackageStrongName, string driverVerDate, string driverVerVersion,
        string enumerator, string hwid, string inf,
        string installState, string manufacturer, string matchingId, string model, string parentId,
        string problemCode, string provider, string service, string stackId)
    {
        BusReportedDescription = busReportedDesc;
        Class = clss;
        ClassGuid = classGuid;
        Compid = compId;
        ContainerId = containerId;
        Description = desc;
        DeviceState = deviceState;
        DriverId = driverId;
        DriverName = driverName;
        DriverPackageStrongName = driverPackageStrongName;
        DriverVerDate = driverVerDate;
        DriverVerVersion = driverVerVersion;
        Enumerator = enumerator;
        HWID = hwid;
        Inf = inf;
        InstallState = installState;
        Manufacturer = manufacturer;
        MatchingId = matchingId;
        Model = model;
        ParentId = parentId;
        ProblemCode = problemCode;
        Provider = provider;
        Service = service;
        Stackid = stackId;

        KeyName = keyName;
        KeyLastWriteTimestamp = keyLastWriteTimestamp;
    }


    public string KeyName { get; }
    public DateTimeOffset KeyLastWriteTimestamp { get; }
    public string BusReportedDescription { get; }
    public string Class { get; }
    public string ClassGuid { get; }
    public string Compid { get; }
    public string ContainerId { get; }
    public string Description { get; }
    public string DeviceState { get; }
    public string DriverId { get; }
    public string DriverName { get; }
    public string DriverPackageStrongName { get; }
    public string DriverVerDate { get; }
    public string DriverVerVersion { get; }
    public string Enumerator { get; }
    public string HWID { get; }
    public string Inf { get; }
    public string InstallState { get; }
    public string Manufacturer { get; }
    public string MatchingId { get; }
    public string Model { get; }
    public string ParentId { get; }
    public string ProblemCode { get; }
    public string Provider { get; }
    public string Service { get; }
    public string Stackid { get; }
}