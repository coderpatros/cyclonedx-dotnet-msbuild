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
using CycloneDX.MSBuildTask;
using Xunit;

namespace CycloneDX.MSBuildTask.Tests;

public class SbomGeneratorTests
{
    private readonly SbomGenerator _generator = new();

    private static SbomInput CreateBasicInput() => new()
    {
        ProjectName = "TestApp",
        ProjectVersion = "1.2.3",
        TargetFramework = "net10.0",
        ResolvedReferences =
        [
            new ResolvedReferenceInfo
            {
                FileName = "Newtonsoft.Json",
                NuGetPackageId = "Newtonsoft.Json",
                NuGetPackageVersion = "13.0.3",
                HintPath = "/home/user/.nuget/packages/newtonsoft.json/13.0.3/lib/net6.0/Newtonsoft.Json.dll",
                ResolvedFrom = "{NuGetPackagesFallbackFolders}",
            },
        ],
        PackageReferences =
        [
            new PackageReferenceInfo { Name = "Newtonsoft.Json", Version = "13.0.3" },
        ],
    };

    [Fact]
    public void Generate_SetsSpecVersion()
    {
        var bom = _generator.Generate(CreateBasicInput());
        Assert.Equal(SpecificationVersion.v1_6, bom.SpecVersion);
    }

    [Fact]
    public void Generate_SetsSerialNumber()
    {
        var bom = _generator.Generate(CreateBasicInput());
        Assert.StartsWith("urn:uuid:", bom.SerialNumber);
    }

    [Fact]
    public void Generate_SetsMetadataTimestamp()
    {
        var before = DateTime.UtcNow;
        var bom = _generator.Generate(CreateBasicInput());
        var after = DateTime.UtcNow;

        Assert.NotNull(bom.Metadata?.Timestamp);
        Assert.InRange(bom.Metadata!.Timestamp!.Value, before, after);
    }

    [Fact]
    public void Generate_SetsMetadataToolComponent()
    {
        var bom = _generator.Generate(CreateBasicInput());

        Assert.NotNull(bom.Metadata?.Tools?.Components);
        var tool = Assert.Single(bom.Metadata!.Tools!.Components!);
        Assert.Equal("CycloneDX.MSBuildTask", tool.Name);
        Assert.Equal(Component.Classification.Application, tool.Type);
    }

    [Fact]
    public void Generate_SetsMetadataComponent()
    {
        var bom = _generator.Generate(CreateBasicInput());

        var component = bom.Metadata?.Component;
        Assert.NotNull(component);
        Assert.Equal("TestApp", component!.Name);
        Assert.Equal("1.2.3", component.Version);
        Assert.Equal(Component.Classification.Application, component.Type);
    }

    [Fact]
    public void Generate_SetsTargetFrameworkProperty()
    {
        var bom = _generator.Generate(CreateBasicInput());

        var prop = bom.Metadata?.Component?.Properties?
            .FirstOrDefault(p => p.Name == "cdx:msbuild:targetFramework");
        Assert.NotNull(prop);
        Assert.Equal("net10.0", prop!.Value);
    }

    [Fact]
    public void Generate_CreatesNuGetComponents()
    {
        var bom = _generator.Generate(CreateBasicInput());

        var component = bom.Components?.FirstOrDefault(c => c.Name == "Newtonsoft.Json");
        Assert.NotNull(component);
        Assert.Equal("13.0.3", component!.Version);
        Assert.Equal(Component.Classification.Library, component.Type);
        Assert.Equal("pkg:nuget/Newtonsoft.Json@13.0.3", component.Purl);
    }

    [Fact]
    public void Generate_SetsComponentBomRef()
    {
        var bom = _generator.Generate(CreateBasicInput());

        var component = bom.Components?.FirstOrDefault(c => c.Name == "Newtonsoft.Json");
        Assert.NotNull(component);
        Assert.Equal("Newtonsoft.Json/13.0.3", component!.BomRef);
    }

    [Fact]
    public void Generate_CreatesFileSubComponents()
    {
        var bom = _generator.Generate(CreateBasicInput());

        var component = bom.Components?.FirstOrDefault(c => c.Name == "Newtonsoft.Json");
        Assert.NotNull(component?.Components);
        var fileComp = Assert.Single(component!.Components!);
        Assert.Equal(Component.Classification.File, fileComp.Type);
        Assert.Equal("Newtonsoft.Json.dll", fileComp.Name);
        Assert.Equal("Newtonsoft.Json/13.0.3#Newtonsoft.Json.dll", fileComp.BomRef);
    }

    [Fact]
    public void Generate_FileSubComponentsHaveHintPath()
    {
        var bom = _generator.Generate(CreateBasicInput());

        var component = bom.Components?.FirstOrDefault(c => c.Name == "Newtonsoft.Json");
        var fileComp = component!.Components!.First();
        var hintProp = fileComp.Properties?.FirstOrDefault(p => p.Name == "cdx:msbuild:hintPath");
        Assert.NotNull(hintProp);
        Assert.Contains("Newtonsoft.Json.dll", hintProp!.Value);
    }

    [Fact]
    public void Generate_FileSubComponentsHaveResolvedFrom()
    {
        var bom = _generator.Generate(CreateBasicInput());

        var component = bom.Components?.FirstOrDefault(c => c.Name == "Newtonsoft.Json");
        var fileComp = component!.Components!.First();
        var resolvedProp = fileComp.Properties?.FirstOrDefault(p => p.Name == "cdx:msbuild:resolvedFrom");
        Assert.NotNull(resolvedProp);
        Assert.Equal("{NuGetPackagesFallbackFolders}", resolvedProp!.Value);
    }

    [Fact]
    public void Generate_FileSubComponentsHaveIndividualHash()
    {
        var input = new SbomInput
        {
            ProjectName = "TestApp",
            ResolvedReferences =
            [
                new ResolvedReferenceInfo
                {
                    FileName = "Newtonsoft.Json",
                    NuGetPackageId = "Newtonsoft.Json",
                    NuGetPackageVersion = "13.0.3",
                    HintPath = "/path/to/Newtonsoft.Json.dll",
                    FileHash = "abc123filehash==",
                },
            ],
        };

        var bom = _generator.Generate(input);
        var component = bom.Components?.FirstOrDefault(c => c.Name == "Newtonsoft.Json");
        var fileComp = component!.Components!.First();

        Assert.NotNull(fileComp.Hashes);
        var hash = Assert.Single(fileComp.Hashes!);
        Assert.Equal(Hash.HashAlgorithm.SHA_256, hash.Alg);
        Assert.Equal("abc123filehash==", hash.Content);
    }

    [Fact]
    public void Generate_EnrichesWithAssetsHashes()
    {
        var input = CreateBasicInput() with
        {
            ProjectAssets = new ProjectAssetsData
            {
                Packages = new Dictionary<string, PackageAssetInfo>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Newtonsoft.Json/13.0.3"] = new PackageAssetInfo
                    {
                        Name = "Newtonsoft.Json",
                        Version = "13.0.3",
                        Sha512 = "abc123hash==",
                    },
                },
            },
        };

        var bom = _generator.Generate(input);
        var component = bom.Components?.FirstOrDefault(c => c.Name == "Newtonsoft.Json");

        Assert.NotNull(component?.Hashes);
        var hash = Assert.Single(component!.Hashes!);
        Assert.Equal(Hash.HashAlgorithm.SHA_512, hash.Alg);
        Assert.Equal("abc123hash==", hash.Content);
    }

    [Fact]
    public void Generate_IncludesTransitiveDependenciesFromAssets()
    {
        var input = CreateBasicInput() with
        {
            ProjectAssets = new ProjectAssetsData
            {
                Packages = new Dictionary<string, PackageAssetInfo>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Newtonsoft.Json/13.0.3"] = new PackageAssetInfo
                    {
                        Name = "Newtonsoft.Json",
                        Version = "13.0.3",
                        Sha512 = "abc==",
                    },
                    ["System.Buffers/4.5.1"] = new PackageAssetInfo
                    {
                        Name = "System.Buffers",
                        Version = "4.5.1",
                        Sha512 = "def==",
                    },
                },
            },
        };

        var bom = _generator.Generate(input);
        var transitive = bom.Components?.FirstOrDefault(c => c.Name == "System.Buffers");

        Assert.NotNull(transitive);
        Assert.Equal("4.5.1", transitive!.Version);
        Assert.Equal("pkg:nuget/System.Buffers@4.5.1", transitive.Purl);
    }

    [Fact]
    public void Generate_CreatesFrameworkComponents()
    {
        var input = new SbomInput
        {
            ProjectName = "TestApp",
            ResolvedReferences =
            [
                new ResolvedReferenceInfo
                {
                    FileName = "System.Runtime",
                    Version = "10.0.0",
                    ResolvedFrom = "{TargetFrameworkDirectory}",
                    HintPath = "/usr/share/dotnet/shared/Microsoft.NETCore.App/10.0.0/System.Runtime.dll",
                },
            ],
        };

        var bom = _generator.Generate(input);
        var component = bom.Components?.FirstOrDefault(c => c.Name == "System.Runtime");

        Assert.NotNull(component);
        Assert.Equal(Component.Classification.Framework, component!.Type);
    }

    [Fact]
    public void Generate_CreatesDependencyGraph()
    {
        var input = CreateBasicInput();
        var bom = _generator.Generate(input);

        Assert.NotNull(bom.Dependencies);
        var rootDep = bom.Dependencies.FirstOrDefault(d => d.Ref == "TestApp");
        Assert.NotNull(rootDep);
        Assert.Contains(rootDep!.Dependencies!, d => d.Ref == "Newtonsoft.Json/13.0.3");
    }

    [Fact]
    public void Generate_CreatesDependencyGraphWithTransitives()
    {
        var input = CreateBasicInput() with
        {
            ProjectAssets = new ProjectAssetsData
            {
                Packages = new Dictionary<string, PackageAssetInfo>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Newtonsoft.Json/13.0.3"] = new PackageAssetInfo
                    {
                        Name = "Newtonsoft.Json",
                        Version = "13.0.3",
                    },
                    ["System.Buffers/4.5.1"] = new PackageAssetInfo
                    {
                        Name = "System.Buffers",
                        Version = "4.5.1",
                    },
                },
                DependencyGraph = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Newtonsoft.Json/13.0.3"] = ["System.Buffers/4.5.1"],
                },
            },
        };

        var bom = _generator.Generate(input);
        var newtonsoftDep = bom.Dependencies?.FirstOrDefault(d => d.Ref == "Newtonsoft.Json/13.0.3");

        Assert.NotNull(newtonsoftDep);
        Assert.Contains(newtonsoftDep!.Dependencies!, d => d.Ref == "System.Buffers/4.5.1");
    }

    [Fact]
    public void Generate_ComponentsAreSortedByName()
    {
        var input = new SbomInput
        {
            ProjectName = "TestApp",
            ResolvedReferences =
            [
                new ResolvedReferenceInfo { FileName = "Zebra", NuGetPackageId = "Zebra", NuGetPackageVersion = "1.0.0" },
                new ResolvedReferenceInfo { FileName = "Alpha", NuGetPackageId = "Alpha", NuGetPackageVersion = "2.0.0" },
                new ResolvedReferenceInfo { FileName = "Middle", NuGetPackageId = "Middle", NuGetPackageVersion = "3.0.0" },
            ],
        };

        var bom = _generator.Generate(input);
        var names = bom.Components!.Select(c => c.Name).ToList();

        Assert.Equal(["Alpha", "Middle", "Zebra"], names);
    }

    [Fact]
    public void Generate_DeduplicatesPackageButKeepsAllFileSubComponents()
    {
        var input = new SbomInput
        {
            ProjectName = "TestApp",
            ResolvedReferences =
            [
                new ResolvedReferenceInfo
                {
                    FileName = "Newtonsoft.Json",
                    NuGetPackageId = "Newtonsoft.Json",
                    NuGetPackageVersion = "13.0.3",
                    HintPath = "/path/to/Newtonsoft.Json.dll",
                    FileHash = "hash1==",
                },
                new ResolvedReferenceInfo
                {
                    FileName = "Newtonsoft.Json.Bson",
                    NuGetPackageId = "Newtonsoft.Json",
                    NuGetPackageVersion = "13.0.3",
                    HintPath = "/path/to/Newtonsoft.Json.Bson.dll",
                    FileHash = "hash2==",
                },
            ],
        };

        var bom = _generator.Generate(input);
        var components = bom.Components!.Where(c => c.Name == "Newtonsoft.Json").ToList();

        // One package component
        Assert.Single(components);
        // Two file sub-components
        Assert.Equal(2, components[0].Components!.Count);
        Assert.All(components[0].Components!, fc => Assert.Equal(Component.Classification.File, fc.Type));
        var fileNames = components[0].Components!.Select(c => c.Name).OrderBy(n => n).ToList();
        Assert.Equal(["Newtonsoft.Json.Bson.dll", "Newtonsoft.Json.dll"], fileNames);
    }

    [Fact]
    public void Generate_ExcludesFrameworkRefPacks()
    {
        var input = new SbomInput
        {
            ProjectName = "TestApp",
            ResolvedReferences =
            [
                new ResolvedReferenceInfo
                {
                    FileName = "System.Runtime",
                    NuGetPackageId = "Microsoft.NETCore.App.Ref",
                    NuGetPackageVersion = "10.0.0",
                    HintPath = "/usr/share/dotnet/packs/Microsoft.NETCore.App.Ref/10.0.0/ref/net10.0/System.Runtime.dll",
                },
                new ResolvedReferenceInfo
                {
                    FileName = "Microsoft.AspNetCore.Http",
                    NuGetPackageId = "Microsoft.AspNetCore.App.Ref",
                    NuGetPackageVersion = "10.0.0",
                    HintPath = "/usr/share/dotnet/packs/Microsoft.AspNetCore.App.Ref/10.0.0/ref/net10.0/Microsoft.AspNetCore.Http.dll",
                },
            ],
        };

        var bom = _generator.Generate(input);

        Assert.DoesNotContain(bom.Components!, c => c.Name == "Microsoft.NETCore.App.Ref");
        Assert.DoesNotContain(bom.Components!, c => c.Name == "Microsoft.AspNetCore.App.Ref");
    }

    [Fact]
    public void Generate_IncludesRuntimePacksForSelfContained()
    {
        var input = new SbomInput
        {
            ProjectName = "TestApp",
            ResolvedReferences =
            [
                new ResolvedReferenceInfo
                {
                    FileName = "System.Runtime",
                    NuGetPackageId = "Microsoft.NETCore.App.Runtime.linux-x64",
                    NuGetPackageVersion = "10.0.0",
                    HintPath = "/usr/share/dotnet/packs/Microsoft.NETCore.App.Runtime.linux-x64/10.0.0/runtimes/linux-x64/lib/net10.0/System.Runtime.dll",
                },
            ],
        };

        var bom = _generator.Generate(input);

        Assert.Contains(bom.Components!, c => c.Name == "Microsoft.NETCore.App.Runtime.linux-x64");
    }

    [Fact]
    public void Generate_HandlesEmptyInput()
    {
        var input = new SbomInput { ProjectName = "EmptyProject" };

        var bom = _generator.Generate(input);

        Assert.NotNull(bom.Metadata);
        Assert.NotNull(bom.Components);
        Assert.Empty(bom.Components);
        Assert.NotNull(bom.Dependencies);
    }

    [Fact]
    public void Generate_UsesDefaultVersionWhenNull()
    {
        var input = new SbomInput { ProjectName = "NoVersion" };
        var bom = _generator.Generate(input);

        Assert.Equal("0.0.0", bom.Metadata?.Component?.Version);
    }

    [Fact]
    public void Generate_SetsLicenseExpressionOnEnrichedComponent()
    {
        var input = CreateBasicInput() with
        {
            ProjectAssets = new ProjectAssetsData
            {
                Packages = new Dictionary<string, PackageAssetInfo>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Newtonsoft.Json/13.0.3"] = new PackageAssetInfo
                    {
                        Name = "Newtonsoft.Json",
                        Version = "13.0.3",
                        LicenseExpression = "MIT",
                    },
                },
            },
        };

        var bom = _generator.Generate(input);
        var component = bom.Components?.FirstOrDefault(c => c.Name == "Newtonsoft.Json");

        Assert.NotNull(component?.Licenses);
        var choice = Assert.Single(component!.Licenses!);
        Assert.Equal("MIT", choice.Expression);
        Assert.Null(choice.License);
    }

    [Fact]
    public void Generate_SetsLicenseUrlOnEnrichedComponent()
    {
        var input = CreateBasicInput() with
        {
            ProjectAssets = new ProjectAssetsData
            {
                Packages = new Dictionary<string, PackageAssetInfo>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Newtonsoft.Json/13.0.3"] = new PackageAssetInfo
                    {
                        Name = "Newtonsoft.Json",
                        Version = "13.0.3",
                        LicenseUrl = "https://example.com/license",
                    },
                },
            },
        };

        var bom = _generator.Generate(input);
        var component = bom.Components?.FirstOrDefault(c => c.Name == "Newtonsoft.Json");

        Assert.NotNull(component?.Licenses);
        var choice = Assert.Single(component!.Licenses!);
        Assert.NotNull(choice.License);
        Assert.Equal("https://example.com/license", choice.License!.Url);
        Assert.Null(choice.Expression);
    }

    [Fact]
    public void Generate_SetsLicenseOnTransitiveComponent()
    {
        var input = CreateBasicInput() with
        {
            ProjectAssets = new ProjectAssetsData
            {
                Packages = new Dictionary<string, PackageAssetInfo>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Newtonsoft.Json/13.0.3"] = new PackageAssetInfo
                    {
                        Name = "Newtonsoft.Json",
                        Version = "13.0.3",
                    },
                    ["System.Buffers/4.5.1"] = new PackageAssetInfo
                    {
                        Name = "System.Buffers",
                        Version = "4.5.1",
                        LicenseExpression = "MIT",
                    },
                },
            },
        };

        var bom = _generator.Generate(input);
        var transitive = bom.Components?.FirstOrDefault(c => c.Name == "System.Buffers");

        Assert.NotNull(transitive?.Licenses);
        var choice = Assert.Single(transitive!.Licenses!);
        Assert.Equal("MIT", choice.Expression);
    }

    [Fact]
    public void Generate_NoLicensesWhenNeitherExpressionNorUrl()
    {
        var input = CreateBasicInput() with
        {
            ProjectAssets = new ProjectAssetsData
            {
                Packages = new Dictionary<string, PackageAssetInfo>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Newtonsoft.Json/13.0.3"] = new PackageAssetInfo
                    {
                        Name = "Newtonsoft.Json",
                        Version = "13.0.3",
                    },
                },
            },
        };

        var bom = _generator.Generate(input);
        var component = bom.Components?.FirstOrDefault(c => c.Name == "Newtonsoft.Json");

        Assert.Null(component?.Licenses);
    }

    [Fact]
    public void Generate_SetsDescriptionAuthorAndCopyright()
    {
        var input = CreateBasicInput() with
        {
            ProjectAssets = new ProjectAssetsData
            {
                Packages = new Dictionary<string, PackageAssetInfo>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Newtonsoft.Json/13.0.3"] = new PackageAssetInfo
                    {
                        Name = "Newtonsoft.Json",
                        Version = "13.0.3",
                        Description = "Popular JSON framework",
                        Authors = "James Newton-King",
                        Copyright = "Copyright 2007 James Newton-King",
                    },
                },
            },
        };

        var bom = _generator.Generate(input);
        var component = bom.Components?.FirstOrDefault(c => c.Name == "Newtonsoft.Json");

        Assert.Equal("Popular JSON framework", component!.Description);
        Assert.NotNull(component.Authors);
        var author = Assert.Single(component.Authors!);
        Assert.Equal("James Newton-King", author.Name);
        Assert.Equal("Copyright 2007 James Newton-King", component.Copyright);
    }

    [Fact]
    public void Generate_SetsWebsiteExternalReference()
    {
        var input = CreateBasicInput() with
        {
            ProjectAssets = new ProjectAssetsData
            {
                Packages = new Dictionary<string, PackageAssetInfo>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Newtonsoft.Json/13.0.3"] = new PackageAssetInfo
                    {
                        Name = "Newtonsoft.Json",
                        Version = "13.0.3",
                        ProjectUrl = "https://www.newtonsoft.com/json",
                    },
                },
            },
        };

        var bom = _generator.Generate(input);
        var component = bom.Components?.FirstOrDefault(c => c.Name == "Newtonsoft.Json");

        Assert.NotNull(component?.ExternalReferences);
        var websiteRef = component!.ExternalReferences!
            .FirstOrDefault(r => r.Type == ExternalReference.ExternalReferenceType.Website);
        Assert.NotNull(websiteRef);
        Assert.Equal("https://www.newtonsoft.com/json", websiteRef!.Url);
    }

    [Fact]
    public void Generate_SetsVcsExternalReference()
    {
        var input = CreateBasicInput() with
        {
            ProjectAssets = new ProjectAssetsData
            {
                Packages = new Dictionary<string, PackageAssetInfo>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Newtonsoft.Json/13.0.3"] = new PackageAssetInfo
                    {
                        Name = "Newtonsoft.Json",
                        Version = "13.0.3",
                        RepositoryUrl = "https://github.com/JamesNK/Newtonsoft.Json.git",
                        RepositoryType = "git",
                        RepositoryCommit = "0a2c4effbbfe9ebd36a0fbb3ea6fe587823aa98b",
                    },
                },
            },
        };

        var bom = _generator.Generate(input);
        var component = bom.Components?.FirstOrDefault(c => c.Name == "Newtonsoft.Json");

        Assert.NotNull(component?.ExternalReferences);
        var vcsRef = component!.ExternalReferences!
            .FirstOrDefault(r => r.Type == ExternalReference.ExternalReferenceType.Vcs);
        Assert.NotNull(vcsRef);
        Assert.Equal("https://github.com/JamesNK/Newtonsoft.Json.git", vcsRef!.Url);
        Assert.Contains("git", vcsRef.Comment!);
        Assert.Contains("0a2c4effbbfe9ebd36a0fbb3ea6fe587823aa98b", vcsRef.Comment!);
    }

    [Fact]
    public void Generate_NoExternalReferencesWhenNoneProvided()
    {
        var input = CreateBasicInput() with
        {
            ProjectAssets = new ProjectAssetsData
            {
                Packages = new Dictionary<string, PackageAssetInfo>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Newtonsoft.Json/13.0.3"] = new PackageAssetInfo
                    {
                        Name = "Newtonsoft.Json",
                        Version = "13.0.3",
                    },
                },
            },
        };

        var bom = _generator.Generate(input);
        var component = bom.Components?.FirstOrDefault(c => c.Name == "Newtonsoft.Json");

        Assert.Null(component?.ExternalReferences);
    }

    [Fact]
    public void Generate_SetsMetadataOnTransitiveComponent()
    {
        var input = CreateBasicInput() with
        {
            ProjectAssets = new ProjectAssetsData
            {
                Packages = new Dictionary<string, PackageAssetInfo>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Newtonsoft.Json/13.0.3"] = new PackageAssetInfo
                    {
                        Name = "Newtonsoft.Json",
                        Version = "13.0.3",
                    },
                    ["System.Buffers/4.5.1"] = new PackageAssetInfo
                    {
                        Name = "System.Buffers",
                        Version = "4.5.1",
                        Description = "Buffer utilities",
                        Authors = "Microsoft",
                        ProjectUrl = "https://dot.net",
                    },
                },
            },
        };

        var bom = _generator.Generate(input);
        var transitive = bom.Components?.FirstOrDefault(c => c.Name == "System.Buffers");

        Assert.Equal("Buffer utilities", transitive!.Description);
        var author = Assert.Single(transitive.Authors!);
        Assert.Equal("Microsoft", author.Name);
        var websiteRef = transitive.ExternalReferences!
            .FirstOrDefault(r => r.Type == ExternalReference.ExternalReferenceType.Website);
        Assert.NotNull(websiteRef);
        Assert.Equal("https://dot.net", websiteRef!.Url);
    }
}
