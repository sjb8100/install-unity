using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace sttz.InstallUnity
{

/// <summary>
/// Platforms supported by the cache.
/// </summary>
public enum CachePlatform
{
    None,
    macOS,
    Windows,
    Linux
}

/// <summary>
/// Information about a Unity version available to install.
/// </summary>
public struct VersionMetadata
{
    /// <summary>
    /// Unity version.
    /// </summary>
    public UnityVersion version;

    /// <summary>
    /// Base URL of where INIs are stored.
    /// </summary>
    public string baseUrl;

    /// <summary>
    /// macOS packages.
    /// </summary>
    public PackageMetadata[] macPackages;

    /// <summary>
    /// Windows packages.
    /// </summary>
    public PackageMetadata[] winPackages;

    /// <summary>
    /// Linux packages.
    /// </summary>
    public PackageMetadata[] linuxPackages;

    /// <summary>
    /// Get platform specific packages.
    /// </summary>
    /// <param name="platform">Platform to get.</param>
    public PackageMetadata[] GetPackages(CachePlatform platform)
    {
        switch (platform) {
            case CachePlatform.macOS:
                return macPackages;
            case CachePlatform.Windows:
                return winPackages;
            case CachePlatform.Linux:
                return linuxPackages;
            default:
                throw new Exception("Invalid platform name: " + platform);
        }
    }

    /// <summary>
    /// Set platform specific packages.
    /// </summary>
    /// <param name="platform">Platform to set.</param>
    public void SetPackages(CachePlatform platform, PackageMetadata[] packages)
    {
        switch (platform) {
            case CachePlatform.macOS:
                macPackages = packages;
                break;
            case CachePlatform.Windows:
                winPackages = packages;
                break;
            case CachePlatform.Linux:
                linuxPackages = packages;
                break;
            default:
                throw new Exception("Invalid platform name: " + platform);
        }
    }

    /// <summary>
    /// Find a package by name, ignoring case.
    /// </summary>
    public PackageMetadata GetPackage(CachePlatform platform, string name)
    {
        var packages = GetPackages(platform);
        foreach (var package in packages) {
            if (package.name.Equals(name, StringComparison.OrdinalIgnoreCase)) {
                return package;
            }
        }
        return default;
    }
}

/// <summary>
/// Information about an version's individual package.
/// </summary>
public struct PackageMetadata
{
    /// <summary>
    /// Name of the main editor pacakge.
    /// </summary>
    public const string EDITOR_PACKAGE_NAME = "Unity";

    /// <summary>
    /// Identifier of the package.
    /// </summary>
    public string name;

    /// <summary>
    /// Title of the package.
    /// </summary>
    public string title;

    /// <summary>
    /// Description of the package.
    /// </summary>
    public string description;

    /// <summary>
    /// Relative or absolute url to the package download.
    /// </summary>
    public string url;

    /// <summary>
    /// Wether the package is installed by default.
    /// </summary>
    public bool install;

    /// <summary>
    /// Wether the package is mandatory.
    /// </summary>
    public bool mandatory;

    /// <summary>
    /// The download size in bytes.
    /// </summary>
    public long size;

    /// <summary>
    /// The installed size in bytes.
    /// </summary>
    public long installedsize;

    /// <summary>
    /// The version of the package.
    /// </summary>
    public string version;

    /// <summary>
    /// File extension to use.
    /// </summary>
    public string extension;

    /// <summary>
    /// Wether the package is hidden.
    /// </summary>
    public bool hidden;

    /// <summary>
    /// Install this package together with another one.
    /// </summary>
    public string sync;

    /// <summary>
    /// The md5 hash of the package download.
    /// </summary>
    public string md5;

    /// <summary>
    /// Wether the package can be installed without the editor.
    /// </summary>
    public bool requires_unity;

    /// <summary>
    /// Bundle Identifier of app in package.
    /// </summary>
    public string appidentifier;

    /// <summary>
    /// Message for extra EULA terms.
    /// </summary>
    public string eulamessage;

    /// <summary>
    /// Label of first extra EULA.
    /// </summary>
    public string eulalabel1;

    /// <summary>
    /// URL of first extra EULA.
    /// </summary>
    public string eulaurl1;

    /// <summary>
    /// Label of second extra EULA.
    /// </summary>
    public string eulalabel2;

    /// <summary>
    /// URL of second extra EULA.
    /// </summary>
    public string eulaurl2;

    /// <summary>
    /// Get the file name to use for the package.
    /// </summary>
    public string GetFileName()
    {
        string fileName;

        // Try to get file name from URL
        var uri = new Uri(url, UriKind.RelativeOrAbsolute);
        if (uri.IsAbsoluteUri) {
            fileName = uri.Segments.Last();
        } else {
            fileName = Path.GetFileName(url);
        }

        // Fallback to given extension if the url doesn't match
        if (extension != null 
                && !string.Equals(Path.GetExtension(fileName), "." + extension, StringComparison.OrdinalIgnoreCase)) {
            fileName = name + "." + extension;
        
        // Force an extension for older versions that don't provide one
        } else if (Path.GetExtension(fileName) == "") {
            fileName = name + ".pkg";
        }

        return fileName;
    }
}

/// <summary>
/// Index of available Unity versions.
/// </summary>
public class VersionsCache : IEnumerable<VersionMetadata>
{
    string dataFilePath;
    Cache cache;

    ILogger Logger = UnityInstaller.CreateLogger<VersionsCache>();

    /// <summary>
    /// Version of cache format.
    /// </summary>
    const int CACHE_FORMAT = 2;

    /// <summary>
    /// Data written out to JSON file.
    /// </summary>
    struct Cache
    {
        public int format;
        public List<VersionMetadata> versions;
        public Dictionary<UnityVersion.Type, DateTime> updated;
    }

    /// <summary>
    /// Create a new database.
    /// </summary>
    /// <param name="dataPath">Path to the database file.</param>
    public VersionsCache(string dataFilePath)
    {
        this.dataFilePath = dataFilePath;

        if (File.Exists(dataFilePath)) {
            try {
                var json = File.ReadAllText(dataFilePath);
                cache = JsonConvert.DeserializeObject<Cache>(json);
                if (cache.format != CACHE_FORMAT) {
                    Logger.LogInformation($"Cache format is outdated, resetting cache.");
                    cache = new Cache();
                } else {
                    SortVersions();
                    Logger.LogInformation($"Loaded versions cache from '{dataFilePath}'");
                }
            } catch (Exception e) {
                Console.Error.WriteLine("ERROR: Could not read versions database file: " + e.Message);
                Console.Error.WriteLine(e.InnerException);
            }
        }

        cache.format = CACHE_FORMAT;

        if (cache.versions == null) {
            Logger.LogInformation("Creating a new empty versions cache");
            cache.versions = new List<VersionMetadata>();
        }
        if (cache.updated == null) {
            cache.updated = new Dictionary<UnityVersion.Type, DateTime>();
        }
    }

    /// <summary>
    /// Sort versions in descending order.
    /// </summary>
    void SortVersions()
    {
        cache.versions.Sort((m1, m2) => m2.version.CompareTo(m1.version));
    }

    /// <summary>
    /// Save the versions database.
    /// </summary>
    /// <returns>Wether the database was saved successfully.</returns>
    public bool Save()
    {
        try {
            Directory.CreateDirectory(Path.GetDirectoryName(dataFilePath));
            var json = JsonConvert.SerializeObject(cache, Formatting.Indented);
            File.WriteAllText(dataFilePath, json);
            Logger.LogDebug($"Saved versions cache to '{dataFilePath}'");
            return true;
        } catch (Exception e) {
            Console.Error.WriteLine("ERROR: Could not save versions database file: " + e.Message);
            return false;
        }
    }

    /// <summary>
    /// Remove all versions in the database.
    /// </summary>
    public void Clear()
    {
        cache.versions.Clear();
        cache.updated.Clear();
        Logger.LogDebug("Cleared versions cache");
    }

    /// <summary>
    /// Add a version to the database. Existing version will be overwritten.
    /// </summary>
    /// <returns>True if the version didn't exist in the cache, false if it was only updated.</returns>
    public bool Add(VersionMetadata metadata)
    {
        for (int i = 0; i < cache.versions.Count; i++) {
            if (cache.versions[i].version == metadata.version) {
                UpdateVersion(i, metadata);
                Logger.LogDebug($"Updated version in cache: {metadata.version}");
                return false;
            }
        }

        cache.versions.Add(metadata);
        SortVersions();
        Logger.LogDebug($"Added version to cache: {metadata.version}");
        return true;
    }

    /// <summary>
    /// Add multiple version to the database. Existing version will be overwritten.
    /// </summary>
    /// <param name="newVersions">Pass in an optional IList, which gets filled with the added versions that weren't in the cache.</param>
    public void Add(IEnumerable<VersionMetadata> metadatas, IList<VersionMetadata> newVersions = null)
    {
        foreach (var metadata in metadatas) {
            for (int i = 0; i < cache.versions.Count; i++) {
                if (cache.versions[i].version == metadata.version) {
                    UpdateVersion(i, metadata);
                    Logger.LogDebug($"Updated version in cache: {metadata.version}");
                    goto continueOuter;
                }
            }
            cache.versions.Add(metadata);
            if (newVersions != null) newVersions.Add(metadata);
            Logger.LogDebug($"Added version to cache: {metadata.version}");
            continueOuter:;
        }

        SortVersions();
    }

    /// <summary>
    /// Update a version, merging its platform-specific data.
    /// </summary>
    void UpdateVersion(int index, VersionMetadata with)
    {
        var existing = cache.versions[index];
        if (with.baseUrl != null) existing.baseUrl = with.baseUrl;
        if (with.macPackages != null) existing.macPackages = with.macPackages;
        if (with.winPackages != null) existing.macPackages = with.winPackages;
        if (with.linuxPackages != null) existing.macPackages = with.linuxPackages;
        cache.versions[index] = existing;
    }

    /// <summary>
    /// Get a version from the databse.
    /// </summary>
    /// <remarks>
    /// If the version is incomplete, the latest version matching will be returned.
    /// </remarks>
    public VersionMetadata Find(UnityVersion version)
    {
        if (version.IsFullVersion) {
            // Do exact match
            foreach (var metadata in cache.versions) {
                if (version.MatchesVersionOrHash(metadata.version)) {
                    return metadata;
                }
            }
            return default;
        }

        // Do fuzzy match
        foreach (var metadata in cache.versions) {
            if (version.FuzzyMatches(metadata.version)) {
                return metadata;
            }
        }
        return default;
    }

    /// <summary>
    /// Get the time the cache was last updated.
    /// </summary>
    /// <param name="type">Release type to check for</param>
    /// <returns>The last update time or DateTime.MinValue if the cache was never updated.</returns>
    public DateTime GetLastUpdate(UnityVersion.Type type)
    {
        DateTime time;
        if (!cache.updated.TryGetValue(type, out time)) {
            return DateTime.MinValue;
        } else {
            return time;
        }
    }

    /// <summary>
    /// Set the time the cache was updated.
    /// </summary>
    public void SetLastUpdate(UnityVersion.Type type, DateTime time)
    {
        cache.updated[type] = time;
    }

    // -------- IEnumerable ------

    public IEnumerator<VersionMetadata> GetEnumerator()
    {
        return cache.versions.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return cache.versions.GetEnumerator();
    }
}

}