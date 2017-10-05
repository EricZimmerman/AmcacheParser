using System;
using System.Collections.Generic;

namespace Amcache.Classes
{
    public class ProgramsEntryNew
    {
        public ProgramsEntryNew(string bundleManifestPath, bool hiddenArp, bool inboxModernApp,
            DateTimeOffset? installDate, int language, string manifestPath, string msiPackageCode,
            string msiProductCode, string name, string osVersionAtInstallTime, string packageFullName, string programId,
            string programInstanceId, string publisher, string registryKeyPath, string rootDirPath, string source,
            string storeAppType, string type, string uninstallString, string version, DateTimeOffset lastwrite)
        {
            FileEntries = new List<FileEntryNew>();


            BundleManifestPath = bundleManifestPath;
            HiddenArp = hiddenArp;
            InboxModernApp = inboxModernApp;
            InstallDate = installDate;
            Language = language;
            ManifestPath = manifestPath;
            MsiPackageCode = msiPackageCode;
            MsiProductCode = msiProductCode;
            OSVersionAtInstallTime = osVersionAtInstallTime;
            PackageFullName = packageFullName;
            ProgramId = programId;
            ProgramInstanceId = programInstanceId;
            Publisher = publisher;
            Name = name;
            UninstallString = uninstallString;
            RegistryKeyPath = registryKeyPath;
            RootDirPath = rootDirPath;
            Source = source;
            StoreAppType = storeAppType;
            Type = type;
            Version = version;
            KeyLastWriteTimestamp = lastwrite;
        }

        public string BundleManifestPath { get; }
        public bool HiddenArp { get; }
        public bool InboxModernApp { get; }
        public DateTimeOffset? InstallDate { get; }
        public int Language { get; }
        public string ManifestPath { get; }
        public string MsiPackageCode { get; }
        public string MsiProductCode { get; }
        public string Name { get; }
        public string OSVersionAtInstallTime { get; }
        public string PackageFullName { get; }
        public string ProgramId { get; }
        public string ProgramInstanceId { get; }
        public string Publisher { get; }
        public string RegistryKeyPath { get; }
        public string RootDirPath { get; }
        public string Source { get; }
        public string StoreAppType { get; }
        public string Type { get; }
        public string UninstallString { get; }
        public string Version { get; }

        public DateTimeOffset KeyLastWriteTimestamp { get; }
        public List<FileEntryNew> FileEntries { get; }
    }
}