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

using CycloneDX.Models;

namespace CycloneDX.MSBuildTask;

/// <summary>
/// Generates a CycloneDX BOM from MSBuild-resolved references and NuGet package data.
/// </summary>
public class SbomGenerator
{
    public Bom Generate(SbomInput input)
    {
        var bom = new Bom
        {
            SpecVersion = SpecificationVersion.v1_6,
            Version = 1,
            SerialNumber = $"urn:uuid:{Guid.NewGuid()}",
            Metadata = CreateMetadata(input),
        };

        var components = CreateComponents(input);
        bom.Components = components;
        bom.Dependencies = CreateDependencies(input, components, bom.Metadata.Component!.BomRef);

        return bom;
    }

    private static Metadata CreateMetadata(SbomInput input)
    {
        var toolComponent = new Component
        {
            Type = Component.Classification.Application,
            Name = "CycloneDX.MSBuildTask",
            Group = "CycloneDX",
            Description = "MSBuild task for CycloneDX SBOM generation",
        };

        return new Metadata
        {
            Timestamp = DateTime.UtcNow,
            Tools = new ToolChoices
            {
                Components = [toolComponent],
            },
            Component = new Component
            {
                Type = Component.Classification.Application,
                BomRef = input.ProjectName,
                Name = input.ProjectName,
                Version = input.ProjectVersion ?? "0.0.0",
                Properties = string.IsNullOrEmpty(input.TargetFramework) ? null :
                [
                    new Property { Name = "cdx:msbuild:targetFramework", Value = input.TargetFramework },
                ],
            },
        };
    }

    private static List<Component> CreateComponents(SbomInput input)
    {
        var components = new Dictionary<string, Component>(StringComparer.OrdinalIgnoreCase);
        // Collect resolved files grouped by package key for sub-component creation
        var filesByPackage = new Dictionary<string, List<ResolvedReferenceInfo>>(StringComparer.OrdinalIgnoreCase);

        // First pass: create components from resolved references that have NuGet metadata
        foreach (var resolved in input.ResolvedReferences)
        {
            if (string.IsNullOrEmpty(resolved.NuGetPackageId))
                continue;

            var key = $"{resolved.NuGetPackageId}/{resolved.NuGetPackageVersion}";

            // Skip framework reference assembly packs (compile-time only, not deployed).
            // These use the .Ref suffix convention (e.g. Microsoft.NETCore.App.Ref,
            // Microsoft.AspNetCore.App.Ref). Runtime packs for self-contained builds
            // (e.g. Microsoft.NETCore.App.Runtime.linux-x64) should be included.
            if (resolved.NuGetPackageId!.EndsWith(".Ref", StringComparison.OrdinalIgnoreCase))
                continue;

            if (!filesByPackage.TryGetValue(key, out var fileList))
            {
                fileList = [];
                filesByPackage[key] = fileList;
            }
            fileList.Add(resolved);

            if (components.ContainsKey(key))
                continue;

            var component = new Component
            {
                Type = Component.Classification.Library,
                BomRef = key,
                Name = resolved.NuGetPackageId,
                Version = resolved.NuGetPackageVersion ?? "",
                Purl = BuildPurl(resolved.NuGetPackageId, resolved.NuGetPackageVersion),
                Scope = Component.ComponentScope.Required,
            };

            components[key] = component;
        }

        // Attach file sub-components to each NuGet package component
        foreach (var (key, files) in filesByPackage)
        {
            if (!components.TryGetValue(key, out var parent))
                continue;

            parent.Components = files
                .Select(f => CreateFileSubComponent(key, f))
                .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        // Second pass: enrich with data from project.assets.json
        if (input.ProjectAssets is not null)
        {
            foreach (var (assetKey, assetInfo) in input.ProjectAssets.Packages)
            {
                if (components.TryGetValue(assetKey, out var existing))
                {
                    EnrichFromAssets(existing, assetInfo);
                }
                else
                {
                    // Transitive dependency not in ReferencePath — add it from assets
                    var component = new Component
                    {
                        Type = Component.Classification.Library,
                        BomRef = assetKey,
                        Name = assetInfo.Name,
                        Version = assetInfo.Version,
                        Purl = BuildPurl(assetInfo.Name, assetInfo.Version),
                        Scope = Component.ComponentScope.Required,
                    };

                    EnrichFromAssets(component, assetInfo);

                    components[assetKey] = component;
                }
            }
        }

        // Third pass: add framework/SDK references not from NuGet
        foreach (var resolved in input.ResolvedReferences)
        {
            if (!string.IsNullOrEmpty(resolved.NuGetPackageId))
                continue;

            // Skip if no meaningful identity
            if (string.IsNullOrEmpty(resolved.FileName))
                continue;

            var key = $"framework:{resolved.FileName}";
            if (components.ContainsKey(key))
                continue;

            var component = new Component
            {
                Type = Component.Classification.Framework,
                BomRef = key,
                Name = resolved.FileName,
                Version = resolved.Version ?? "",
                Properties =
                [
                    new Property { Name = "cdx:msbuild:resolvedFrom", Value = resolved.ResolvedFrom ?? "unknown" },
                    new Property { Name = "cdx:msbuild:hintPath", Value = resolved.HintPath ?? "" },
                ],
            };

            components[key] = component;
        }

        return [.. components.Values.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)];
    }

    private static Component CreateFileSubComponent(string parentBomRef, ResolvedReferenceInfo resolved)
    {
        var fileName = Path.GetFileName(resolved.HintPath ?? resolved.FileName);
        var subComponent = new Component
        {
            Type = Component.Classification.File,
            BomRef = $"{parentBomRef}#{fileName}",
            Name = fileName,
        };

        if (!string.IsNullOrEmpty(resolved.FileHashHex))
        {
            subComponent.Hashes =
            [
                new Hash
                {
                    Alg = Hash.HashAlgorithm.SHA_256,
                    Content = resolved.FileHashHex,
                },
            ];
        }

        var properties = new List<Property>();
        if (!string.IsNullOrEmpty(resolved.HintPath))
        {
            properties.Add(new Property
            {
                Name = "cdx:msbuild:hintPath",
                Value = resolved.HintPath,
            });
        }
        if (!string.IsNullOrEmpty(resolved.ResolvedFrom))
        {
            properties.Add(new Property
            {
                Name = "cdx:msbuild:resolvedFrom",
                Value = resolved.ResolvedFrom,
            });
        }

        if (properties.Count > 0)
            subComponent.Properties = properties;

        return subComponent;
    }

    private static List<Dependency> CreateDependencies(
        SbomInput input,
        List<Component> components,
        string rootBomRef)
    {
        var dependencies = new List<Dependency>();
        var componentRefs = new HashSet<string>(
            components.Select(c => c.BomRef),
            StringComparer.OrdinalIgnoreCase);

        // Root project depends on all direct package references
        var rootDeps = new List<Dependency>();
        foreach (var pkgRef in input.PackageReferences)
        {
            // Find the matching component
            var matchingComponent = components.FirstOrDefault(c =>
                string.Equals(c.Name, pkgRef.Name, StringComparison.OrdinalIgnoreCase)
                && c.Type == Component.Classification.Library);
            if (matchingComponent is not null)
            {
                rootDeps.Add(new Dependency { Ref = matchingComponent.BomRef });
            }
        }

        dependencies.Add(new Dependency
        {
            Ref = rootBomRef,
            Dependencies = rootDeps,
        });

        // Transitive dependencies from project.assets.json
        if (input.ProjectAssets is not null)
        {
            foreach (var (pkgKey, depList) in input.ProjectAssets.DependencyGraph)
            {
                if (!componentRefs.Contains(pkgKey))
                    continue;

                var resolved = ResolveDependencyRefs(depList, components);
                dependencies.Add(new Dependency
                {
                    Ref = pkgKey,
                    Dependencies = resolved,
                });
            }
        }

        // Components without explicit dependency entries get empty dependency lists
        foreach (var component in components)
        {
            if (!dependencies.Any(d => string.Equals(d.Ref, component.BomRef, StringComparison.OrdinalIgnoreCase)))
            {
                dependencies.Add(new Dependency { Ref = component.BomRef });
            }
        }

        return [.. dependencies.OrderBy(d => d.Ref, StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>
    /// Resolves dependency version ranges from project.assets.json to actual component BomRefs.
    /// The assets file may list dependencies with version ranges (e.g. "1.0.0" meaning ">= 1.0.0"),
    /// so we match by name and find the best matching component.
    /// </summary>
    private static List<Dependency> ResolveDependencyRefs(
        List<string> depKeys,
        List<Component> components)
    {
        var result = new List<Dependency>();
        foreach (var depKey in depKeys)
        {
            var parts = depKey.Split('/', 2);
            if (parts.Length != 2) continue;
            var depName = parts[0];

            // Find component by name (there should typically be only one version)
            var match = components.FirstOrDefault(c =>
                string.Equals(c.Name, depName, StringComparison.OrdinalIgnoreCase)
                && c.Type == Component.Classification.Library);
            if (match is not null)
            {
                result.Add(new Dependency { Ref = match.BomRef });
            }
        }
        return result;
    }

    private static void EnrichFromAssets(Component component, PackageAssetInfo assetInfo)
    {
        if (!string.IsNullOrEmpty(assetInfo.Sha512Hex))
        {
            component.Hashes =
            [
                new Hash
                {
                    Alg = Hash.HashAlgorithm.SHA_512,
                    Content = assetInfo.Sha512Hex,
                },
            ];
        }

        component.Licenses = CreateLicenses(assetInfo);
        component.Description = assetInfo.Description;
        component.Authors = CreateAuthors(assetInfo.Authors);
        component.Copyright = assetInfo.Copyright;
        component.ExternalReferences = CreateExternalReferences(assetInfo);
    }

    private static List<OrganizationalContact>? CreateAuthors(string? authors)
    {
        if (string.IsNullOrEmpty(authors))
            return null;

        return authors.Split(',')
            .Select(a => a.Trim())
            .Where(a => a.Length > 0)
            .Select(a => new OrganizationalContact { Name = a })
            .ToList() is { Count: > 0 } list ? list : null;
    }

    private static List<LicenseChoice>? CreateLicenses(PackageAssetInfo assetInfo)
    {
        if (!string.IsNullOrEmpty(assetInfo.LicenseExpression))
            return [new LicenseChoice { Expression = assetInfo.LicenseExpression }];
        if (!string.IsNullOrEmpty(assetInfo.LicenseUrl))
            return [new LicenseChoice { License = new License { Name = "See URL", Url = assetInfo.LicenseUrl } }];
        return null;
    }

    private static List<ExternalReference>? CreateExternalReferences(PackageAssetInfo assetInfo)
    {
        var refs = new List<ExternalReference>();

        if (!string.IsNullOrEmpty(assetInfo.ProjectUrl))
        {
            refs.Add(new ExternalReference
            {
                Type = ExternalReference.ExternalReferenceType.Website,
                Url = assetInfo.ProjectUrl,
            });
        }

        if (!string.IsNullOrEmpty(assetInfo.RepositoryUrl))
        {
            var vcsRef = new ExternalReference
            {
                Type = ExternalReference.ExternalReferenceType.Vcs,
                Url = assetInfo.RepositoryUrl,
            };

            // Include commit hash and repo type as comment if available
            var details = new List<string>();
            if (!string.IsNullOrEmpty(assetInfo.RepositoryType))
                details.Add(assetInfo.RepositoryType);
            if (!string.IsNullOrEmpty(assetInfo.RepositoryCommit))
                details.Add($"commit: {assetInfo.RepositoryCommit}");
            if (details.Count > 0)
                vcsRef.Comment = string.Join(", ", details);

            refs.Add(vcsRef);
        }

        return refs.Count > 0 ? refs : null;
    }

    private static string? BuildPurl(string? name, string? version)
    {
        if (string.IsNullOrEmpty(name)) return null;
        return string.IsNullOrEmpty(version)
            ? $"pkg:nuget/{name}"
            : $"pkg:nuget/{name}@{version}";
    }
}

public record SbomInput
{
    public required string ProjectName { get; init; }
    public string? ProjectVersion { get; init; }
    public string? TargetFramework { get; init; }
    public IReadOnlyList<ResolvedReferenceInfo> ResolvedReferences { get; init; } = [];
    public IReadOnlyList<PackageReferenceInfo> PackageReferences { get; init; } = [];
    public ProjectAssetsData? ProjectAssets { get; init; }
}

public class ResolvedReferenceInfo
{
    public required string FileName { get; init; }
    public string? Version { get; init; }
    public string? NuGetPackageId { get; init; }
    public string? NuGetPackageVersion { get; init; }
    public string? HintPath { get; init; }
    public string? ResolvedFrom { get; init; }
    public string? FusionName { get; init; }

    /// <summary>
    /// SHA-256 hash of the actual resolved file on disk (hex-encoded).
    /// Computed by the MSBuild task from the file at HintPath.
    /// </summary>
    public string? FileHashHex { get; init; }
}

public class PackageReferenceInfo
{
    public required string Name { get; init; }
    public string? Version { get; init; }
}
