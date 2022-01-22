using System;

namespace Amcache.Classes;

public class DeviceContainer
{
    public DeviceContainer(string keyName, DateTimeOffset keyLastWrite, string categories, string discoveryMethod,
        string friendlyName, string icon, bool isActive, bool isConnected, bool isMachineContainer,
        bool isNetworked, bool isPaired, string manufacturer, string modelId, string modelName, string modelNumber,
        string primaryCategory, string state)
    {
        KeyName = keyName;
        KeyLastWriteTimestamp = keyLastWrite;
        Categories = categories;
        DiscoveryMethod = discoveryMethod;
        FriendlyName = friendlyName;
        Icon = icon;
        IsActive = isActive;
        IsConnected = isConnected;
        IsMachineContainer = isMachineContainer;
        IsNetworked = isNetworked;
        IsPaired = isPaired;
        Manufacturer = manufacturer;
        ModelId = modelId;
        ModelNumber = modelNumber;
        ModelName = modelName;
        PrimaryCategory = primaryCategory;
        State = state;
    }

    public string KeyName { get; }
    public DateTimeOffset KeyLastWriteTimestamp { get; }

    public string Categories { get; }
    public string DiscoveryMethod { get; }
    public string FriendlyName { get; }
    public string Icon { get; }
    public bool IsActive { get; }
    public bool IsConnected { get; }
    public bool IsMachineContainer { get; }
    public bool IsNetworked { get; }
    public bool IsPaired { get; }
    public string Manufacturer { get; }
    public string ModelId { get; }
    public string ModelName { get; }
    public string ModelNumber { get; }
    public string PrimaryCategory { get; }
    public string State { get; }
}