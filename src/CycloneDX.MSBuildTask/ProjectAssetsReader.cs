// This file is part of CycloneDX MSBuild Task for .NET
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
// http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
//
// SPDX-License-Identifier: Apache-2.0
// Copyright (c) Patrick Dwyer. All Rights Reserved.

using System.Text.Json;
using System.Xml.Linq;

namespace CycloneDX.MSBuildTask;

/// <summary>
/// Reads and parses a project.assets.json file produced by NuGet restore,
/// extracting package metadata, hashes, and the dependency graph.
/// </summary>
public static class ProjectAssetsReader
{
    public static ProjectAssetsData Read(string projectAssetsPath, string? targetFramework = null, string? runtimeIdentifier = null)
    {
        if (!File.Exists(projectAssetsPath))
            return new ProjectAssetsData();

        var json = File.ReadAllText(projectAssetsPath);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var packages = new Dictionary<string, PackageAssetInfo>(StringComparer.OrdinalIgnoreCase);
        var dependencyGraph = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        var packageFolders = ReadPackageFolders(root);
        ReadLibraries(root, packages, packageFolders);
        ReadTargets(root, packages, dependencyGraph, targetFramework, runtimeIdentifier);

        return new ProjectAssetsData
        {
            Packages = packages,
            DependencyGraph = dependencyGraph,
        };
    }

    private static List<string> ReadPackageFolders(JsonElement root)
    {
        var folders = new List<string>();
        if (!root.TryGetProperty("packageFolders", out var packageFolders))
            return folders;

        foreach (var folder in packageFolders.EnumerateObject())
            folders.Add(folder.Name);

        return folders;
    }

    private static void ReadLibraries(JsonElement root, Dictionary<string, PackageAssetInfo> packages, List<string> packageFolders)
    {
        if (!root.TryGetProperty("libraries", out var libraries))
            return;

        foreach (var lib in libraries.EnumerateObject())
        {
            // Key format: "PackageName/Version"
            var parts = lib.Name.Split('/', 2);
            if (parts.Length != 2) continue;

            var name = parts[0];
            var version = parts[1];

            var type = lib.Value.TryGetProperty("type", out var typeProp)
                ? typeProp.GetString() ?? ""
                : "";

            if (!string.Equals(type, "package", StringComparison.OrdinalIgnoreCase))
                continue;

            var sha512 = lib.Value.TryGetProperty("sha512", out var hashProp)
                ? hashProp.GetString()
                : null;

            var path = lib.Value.TryGetProperty("path", out var pathProp)
                ? pathProp.GetString()
                : null;

            var nuspecData = path is not null
                ? ReadNuspecMetadata(packageFolders, path, name)
                : null;

            var key = $"{name}/{version}";
            packages[key] = new PackageAssetInfo
            {
                Name = name,
                Version = version,
                Sha512 = sha512,
                LicenseExpression = nuspecData?.LicenseExpression,
                LicenseUrl = nuspecData?.LicenseUrl,
                Description = nuspecData?.Description,
                Authors = nuspecData?.Authors,
                Copyright = nuspecData?.Copyright,
                ProjectUrl = nuspecData?.ProjectUrl,
                RepositoryUrl = nuspecData?.RepositoryUrl,
                RepositoryType = nuspecData?.RepositoryType,
                RepositoryCommit = nuspecData?.RepositoryCommit,
            };
        }
    }

    private static NuspecData? ReadNuspecMetadata(
        List<string> packageFolders, string packagePath, string packageName)
    {
        foreach (var folder in packageFolders)
        {
            var nuspecPath = Path.Combine(folder, packagePath, $"{packageName.ToLowerInvariant()}.nuspec");
            if (!File.Exists(nuspecPath))
                continue;

            try
            {
                var doc = XDocument.Load(nuspecPath);
                var ns = doc.Root?.GetDefaultNamespace() ?? XNamespace.None;
                var metadata = doc.Root?.Element(ns + "metadata");
                if (metadata is null)
                    continue;

                var data = new NuspecData();

                // License
                var licenseEl = metadata.Element(ns + "license");
                if (licenseEl is not null)
                {
                    var typeAttr = licenseEl.Attribute("type")?.Value;
                    if (string.Equals(typeAttr, "expression", StringComparison.OrdinalIgnoreCase))
                        data.LicenseExpression = licenseEl.Value.Trim();
                }
                else
                {
                    var licenseUrlEl = metadata.Element(ns + "licenseUrl");
                    if (licenseUrlEl is not null)
                    {
                        var url = licenseUrlEl.Value.Trim();
                        if (!string.IsNullOrEmpty(url))
                            data.LicenseUrl = url;
                    }
                }

                // Description, Authors, Copyright
                data.Description = metadata.Element(ns + "description")?.Value.Trim();
                data.Authors = metadata.Element(ns + "authors")?.Value.Trim();
                data.Copyright = metadata.Element(ns + "copyright")?.Value.Trim();

                // ProjectUrl
                var projectUrlEl = metadata.Element(ns + "projectUrl");
                if (projectUrlEl is not null)
                {
                    var url = projectUrlEl.Value.Trim();
                    if (!string.IsNullOrEmpty(url))
                        data.ProjectUrl = url;
                }

                // Repository
                var repoEl = metadata.Element(ns + "repository");
                if (repoEl is not null)
                {
                    data.RepositoryUrl = repoEl.Attribute("url")?.Value;
                    data.RepositoryType = repoEl.Attribute("type")?.Value;
                    data.RepositoryCommit = repoEl.Attribute("commit")?.Value;
                }

                return data;
            }
            catch
            {
                // Nuspec parsing failed — skip this folder, try next
            }
        }

        return null;
    }

    private class NuspecData
    {
        public string? LicenseExpression { get; set; }
        public string? LicenseUrl { get; set; }
        public string? Description { get; set; }
        public string? Authors { get; set; }
        public string? Copyright { get; set; }
        public string? ProjectUrl { get; set; }
        public string? RepositoryUrl { get; set; }
        public string? RepositoryType { get; set; }
        public string? RepositoryCommit { get; set; }
    }

    private static void ReadTargets(
        JsonElement root,
        Dictionary<string, PackageAssetInfo> packages,
        Dictionary<string, List<string>> dependencyGraph,
        string? targetFramework,
        string? runtimeIdentifier)
    {
        if (!root.TryGetProperty("targets", out var targets))
            return;

        var frameworkEntry = FindTargetFramework(targets, targetFramework, runtimeIdentifier);
        if (frameworkEntry == null)
            return;

        foreach (var pkg in frameworkEntry.Value.EnumerateObject())
        {
            var parts = pkg.Name.Split('/', 2);
            if (parts.Length != 2) continue;

            var name = parts[0];
            var version = parts[1];
            var key = $"{name}/{version}";

            // Extract dependency list
            if (pkg.Value.TryGetProperty("dependencies", out var deps))
            {
                var depList = new List<string>();
                foreach (var dep in deps.EnumerateObject())
                {
                    var depVersion = dep.Value.GetString() ?? "";
                    depList.Add($"{dep.Name}/{depVersion}");
                }
                dependencyGraph[key] = depList;
            }

            // Extract runtime assemblies
            if (packages.TryGetValue(key, out var pkgInfo))
            {
                if (pkg.Value.TryGetProperty("runtime", out var runtime))
                {
                    foreach (var asm in runtime.EnumerateObject())
                    {
                        pkgInfo.RuntimeAssemblies.Add(asm.Name);
                    }
                }
                if (pkg.Value.TryGetProperty("compile", out var compile))
                {
                    foreach (var asm in compile.EnumerateObject())
                    {
                        pkgInfo.CompileAssemblies.Add(asm.Name);
                    }
                }
            }
        }
    }

    private static JsonElement? FindTargetFramework(JsonElement targets, string? targetFramework, string? runtimeIdentifier)
    {
        if (!string.IsNullOrEmpty(targetFramework))
        {
            // Try RID-specific target first (e.g. "net10.0/linux-x64")
            if (!string.IsNullOrEmpty(runtimeIdentifier))
            {
                var ridKey = $"{targetFramework}/{runtimeIdentifier}";
                foreach (var entry in targets.EnumerateObject())
                {
                    if (entry.Name.Equals(ridKey, StringComparison.OrdinalIgnoreCase))
                        return entry.Value;
                }
            }

            foreach (var entry in targets.EnumerateObject())
            {
                if (entry.Name.Equals(targetFramework, StringComparison.OrdinalIgnoreCase))
                    return entry.Value;
            }
        }

        // Fall back to the first entry
        foreach (var entry in targets.EnumerateObject())
            return entry.Value;

        return null;
    }
}

public class ProjectAssetsData
{
    /// <summary>
    /// Package metadata keyed by "Name/Version".
    /// </summary>
    public Dictionary<string, PackageAssetInfo> Packages { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Dependency graph: key is "Name/Version", value is list of dependency "Name/Version" entries.
    /// Note: dependency versions from project.assets.json may be version ranges or resolved versions.
    /// </summary>
    public Dictionary<string, List<string>> DependencyGraph { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

public class PackageAssetInfo
{
    public required string Name { get; init; }
    public required string Version { get; init; }
    public string? Sha512 { get; init; }
    public string? LicenseExpression { get; init; }
    public string? LicenseUrl { get; init; }
    public string? Description { get; init; }
    public string? Authors { get; init; }
    public string? Copyright { get; init; }
    public string? ProjectUrl { get; init; }
    public string? RepositoryUrl { get; init; }
    public string? RepositoryType { get; init; }
    public string? RepositoryCommit { get; init; }
    public List<string> RuntimeAssemblies { get; } = [];
    public List<string> CompileAssemblies { get; } = [];
}
