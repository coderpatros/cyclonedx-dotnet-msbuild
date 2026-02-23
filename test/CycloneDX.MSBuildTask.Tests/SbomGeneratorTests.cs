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
using System.Net.NetworkInformation;
using Xunit;

namespace CycloneDX.MSBuildTask.Tests;

public class SbomGeneratorTests
{
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


    private static Bom GenerateAndValidate(SbomInput input)
    {
        var bom = new SbomGenerator().Generate(input);
        Assert.True(IsValidBom(bom, out var problems), String.Join('\n', problems));
        return bom;
    }

    private static bool IsValidBom(Bom bom, out List<string> problems)
    {
        var json = CycloneDX.Json.Serializer.Serialize(bom);
        var xml = CycloneDX.Xml.Serializer.Serialize(bom);

        SpecificationVersion specVersion = SpecificationVersionHelpers.CurrentVersion;
        var validationResultJson = Json.Validator.Validate(json, specVersion);
        var validationResultXml = Xml.Validator.Validate(xml, specVersion);

       problems = [..validationResultJson.Messages, ..validationResultXml.Messages];


        return validationResultJson.Valid && validationResultXml.Valid;
    }

    [Fact]
    public void Generate_SetsSpecVersion()
    {
        var bom = GenerateAndValidate(CreateBasicInput());
        Assert.Equal(SpecificationVersion.v1_6, bom.SpecVersion);
    }

    [Fact]
    public void Generate_SetsSerialNumber()
    {
        var bom = GenerateAndValidate(CreateBasicInput());
        Assert.StartsWith("urn:uuid:", bom.SerialNumber);
    }

    [Fact]
    public void Generate_SetsMetadataTimestamp()
    {
        var before = DateTime.UtcNow;
        var bom = GenerateAndValidate(CreateBasicInput());
        var after = DateTime.UtcNow;

        Assert.NotNull(bom.Metadata?.Timestamp);
        Assert.InRange(bom.Metadata!.Timestamp!.Value, before, after);
    }

    [Fact]
    public void Generate_SetsMetadataToolComponent()
    {
        var bom = GenerateAndValidate(CreateBasicInput());

        Assert.NotNull(bom.Metadata?.Tools?.Components);
        var tool = Assert.Single(bom.Metadata!.Tools!.Components!);
        Assert.Equal("CycloneDX.MSBuildTask", tool.Name);
        Assert.Equal(Component.Classification.Application, tool.Type);
    }

    [Fact]
    public void Generate_SetsMetadataComponent()
    {
        var bom = GenerateAndValidate(CreateBasicInput());

        var component = bom.Metadata?.Component;
        Assert.NotNull(component);
        Assert.Equal("TestApp", component!.Name);
        Assert.Equal("1.2.3", component.Version);
        Assert.Equal(Component.Classification.Application, component.Type);
    }

    [Fact]
    public void Generate_SetsTargetFrameworkProperty()
    {
        var bom = GenerateAndValidate(CreateBasicInput());

        var prop = bom.Metadata?.Component?.Properties?
            .FirstOrDefault(p => p.Name == "cdx:msbuild:targetFramework");
        Assert.NotNull(prop);
        Assert.Equal("net10.0", prop!.Value);
    }

    [Fact]
    public void Generate_CreatesNuGetComponents()
    {
        var bom = GenerateAndValidate(CreateBasicInput());

        var component = bom.Components?.FirstOrDefault(c => c.Name == "Newtonsoft.Json");
        Assert.NotNull(component);
        Assert.Equal("13.0.3", component!.Version);
        Assert.Equal(Component.Classification.Library, component.Type);
        Assert.Equal("pkg:nuget/Newtonsoft.Json@13.0.3", component.Purl);
    }

    [Fact]
    public void Generate_SetsComponentBomRef()
    {
        var bom = GenerateAndValidate(CreateBasicInput());

        var component = bom.Components?.FirstOrDefault(c => c.Name == "Newtonsoft.Json");
        Assert.NotNull(component);
        Assert.Equal("Newtonsoft.Json/13.0.3", component!.BomRef);
    }

    [Fact]
    public void Generate_CreatesFileSubComponents()
    {
        var bom = GenerateAndValidate(CreateBasicInput());

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
        var bom = GenerateAndValidate(CreateBasicInput());

        var component = bom.Components?.FirstOrDefault(c => c.Name == "Newtonsoft.Json");
        var fileComp = component!.Components!.First();
        var hintProp = fileComp.Properties?.FirstOrDefault(p => p.Name == "cdx:msbuild:hintPath");
        Assert.NotNull(hintProp);
        Assert.Contains("Newtonsoft.Json.dll", hintProp!.Value);
    }

    [Fact]
    public void Generate_FileSubComponentsHaveResolvedFrom()
    {
        var bom = GenerateAndValidate(CreateBasicInput());

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
                    FileHashHex = "cf83e1357eefb8bdf1542850d66d8007d620e4050b5715dc83f4a921d36ce9ce47d0d13c5d85f2b0ff8318d2877eec2f63b931bd47417a81a538327af927da3e",
                },
            ],
        };

        var bom = GenerateAndValidate(input);
        var component = bom.Components?.FirstOrDefault(c => c.Name == "Newtonsoft.Json");
        var fileComp = component!.Components!.First();

        Assert.NotNull(fileComp.Hashes);
        var hash = Assert.Single(fileComp.Hashes!);
        Assert.Equal(Hash.HashAlgorithm.SHA_256, hash.Alg);
        Assert.Equal("cf83e1357eefb8bdf1542850d66d8007d620e4050b5715dc83f4a921d36ce9ce47d0d13c5d85f2b0ff8318d2877eec2f63b931bd47417a81a538327af927da3e", hash.Content);
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
                        Sha512Hex = "cf83e1357eefb8bdf1542850d66d8007d620e4050b5715dc83f4a921d36ce9ce47d0d13c5d85f2b0ff8318d2877eec2f63b931bd47417a81a538327af927da3e",
                    },
                },
            },
        };

        var bom = GenerateAndValidate(input);
        var component = bom.Components?.FirstOrDefault(c => c.Name == "Newtonsoft.Json");

        Assert.NotNull(component?.Hashes);
        var hash = Assert.Single(component!.Hashes!);
        Assert.Equal(Hash.HashAlgorithm.SHA_512, hash.Alg);
        Assert.Equal("cf83e1357eefb8bdf1542850d66d8007d620e4050b5715dc83f4a921d36ce9ce47d0d13c5d85f2b0ff8318d2877eec2f63b931bd47417a81a538327af927da3e", hash.Content);
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
                        Sha512Hex = "9b71d224bd62f3785d96d46ad3ea3d73319bfbc2890caadae2dff72519673ca72323c3d99ba5c11d7c7acc6e14b8c5da0c4663475c2e5c3adef46f73bcdec043",
                    },
                    ["System.Buffers/4.5.1"] = new PackageAssetInfo
                    {
                        Name = "System.Buffers",
                        Version = "4.5.1",
                        Sha512Hex = "07e547d9586f6a73f73fbac0435ed76951218fb7d0c8d788a309d785436bbb642e93a252a954f23912547d1e8a3b5ed6e1bfd7097821233fa0538f3db854fee6",
                    },
                },
            },
        };

        var bom = GenerateAndValidate(input);
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

        var bom = GenerateAndValidate(input);
        var component = bom.Components?.FirstOrDefault(c => c.Name == "System.Runtime");

        Assert.NotNull(component);
        Assert.Equal(Component.Classification.Framework, component!.Type);
    }

    [Fact]
    public void Generate_CreatesDependencyGraph()
    {
        var input = CreateBasicInput();
        var bom = GenerateAndValidate(input);

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

        var bom = GenerateAndValidate(input);
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

        var bom = GenerateAndValidate(input);
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
                    FileHashHex = "ddaf35a193617abacc417349ae20413112e6fa4e89a97ea20a9eeee64b55d39a2192992a274fc1a836ba3c23a3feebbd454d4423643ce80e2a9ac94fa54ca49f",
                },
                new ResolvedReferenceInfo
                {
                    FileName = "Newtonsoft.Json.Bson",
                    NuGetPackageId = "Newtonsoft.Json",
                    NuGetPackageVersion = "13.0.3",
                    HintPath = "/path/to/Newtonsoft.Json.Bson.dll",
                    FileHashHex = "1f40fc92da241694750979ee6cf582f2d5d7d28e18335de05abc54d0560e0f5302860c652bf08d560252aa5e74210546f369fbbbce8c12cfc7957b2652fe9a75",
                },
            ],
        };

        var bom = GenerateAndValidate(input);
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

        var bom = GenerateAndValidate(input);

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

        var bom = GenerateAndValidate(input);

        Assert.Contains(bom.Components!, c => c.Name == "Microsoft.NETCore.App.Runtime.linux-x64");
    }

    [Fact]
    public void Generate_HandlesEmptyInput()
    {
        var input = new SbomInput { ProjectName = "EmptyProject" };

        var bom = GenerateAndValidate(input);

        Assert.NotNull(bom.Metadata);
        Assert.NotNull(bom.Components);
        Assert.Empty(bom.Components);
        Assert.NotNull(bom.Dependencies);
    }

    [Fact]
    public void Generate_UsesDefaultVersionWhenNull()
    {
        var input = new SbomInput { ProjectName = "NoVersion" };
        var bom = GenerateAndValidate(input);

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

        var bom = GenerateAndValidate(input);
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

        var bom = GenerateAndValidate(input);
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

        var bom = GenerateAndValidate(input);
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

        var bom = GenerateAndValidate(input);
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

        var bom = GenerateAndValidate(input);
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

        var bom = GenerateAndValidate(input);
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

        var bom = GenerateAndValidate(input);
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

        var bom = GenerateAndValidate(input);
        var component = bom.Components?.FirstOrDefault(c => c.Name == "Newtonsoft.Json");

        Assert.Null(component?.ExternalReferences);
    }

    [Fact]
    public void Generate_SatelliteAssemblyHasCulturePrefixedName()
    {
        var input = new SbomInput
        {
            ProjectName = "TestApp",
            ResolvedReferences =
            [
                new ResolvedReferenceInfo
                {
                    FileName = "System.CommandLine",
                    NuGetPackageId = "System.CommandLine",
                    NuGetPackageVersion = "2.0.0-beta4",
                    HintPath = "/path/to/System.CommandLine.dll",
                },
                new ResolvedReferenceInfo
                {
                    FileName = "System.CommandLine.resources",
                    NuGetPackageId = "System.CommandLine",
                    NuGetPackageVersion = "2.0.0-beta4",
                    HintPath = "/path/to/cs/System.CommandLine.resources.dll",
                    CultureName = "cs",
                },
                new ResolvedReferenceInfo
                {
                    FileName = "System.CommandLine.resources",
                    NuGetPackageId = "System.CommandLine",
                    NuGetPackageVersion = "2.0.0-beta4",
                    HintPath = "/path/to/de/System.CommandLine.resources.dll",
                    CultureName = "de",
                },
            ],
        };

        var bom = GenerateAndValidate(input);
        var component = bom.Components?.FirstOrDefault(c => c.Name == "System.CommandLine");

        Assert.NotNull(component?.Components);
        Assert.Equal(3, component!.Components!.Count);

        var csFile = component.Components.FirstOrDefault(c => c.Name == "cs/System.CommandLine.resources.dll");
        Assert.NotNull(csFile);
        Assert.Equal("System.CommandLine/2.0.0-beta4#cs/System.CommandLine.resources.dll", csFile!.BomRef);

        var deFile = component.Components.FirstOrDefault(c => c.Name == "de/System.CommandLine.resources.dll");
        Assert.NotNull(deFile);
        Assert.Equal("System.CommandLine/2.0.0-beta4#de/System.CommandLine.resources.dll", deFile!.BomRef);
    }

    [Fact]
    public void Generate_SatelliteAssemblyHasCultureNameProperty()
    {
        var input = new SbomInput
        {
            ProjectName = "TestApp",
            ResolvedReferences =
            [
                new ResolvedReferenceInfo
                {
                    FileName = "System.CommandLine",
                    NuGetPackageId = "System.CommandLine",
                    NuGetPackageVersion = "2.0.0-beta4",
                    HintPath = "/path/to/System.CommandLine.dll",
                },
                new ResolvedReferenceInfo
                {
                    FileName = "System.CommandLine.resources",
                    NuGetPackageId = "System.CommandLine",
                    NuGetPackageVersion = "2.0.0-beta4",
                    HintPath = "/path/to/fr/System.CommandLine.resources.dll",
                    CultureName = "fr",
                },
            ],
        };

        var bom = GenerateAndValidate(input);
        var component = bom.Components?.FirstOrDefault(c => c.Name == "System.CommandLine");
        var frFile = component!.Components!.FirstOrDefault(c => c.Name == "fr/System.CommandLine.resources.dll");

        Assert.NotNull(frFile);
        var cultureProp = frFile!.Properties?.FirstOrDefault(p => p.Name == "cdx:msbuild:cultureName");
        Assert.NotNull(cultureProp);
        Assert.Equal("fr", cultureProp!.Value);
    }

    [Fact]
    public void Generate_SatelliteAssemblyBomRefsAreUnique()
    {
        var input = new SbomInput
        {
            ProjectName = "TestApp",
            ResolvedReferences =
            [
                new ResolvedReferenceInfo
                {
                    FileName = "MyLib.resources",
                    NuGetPackageId = "MyLib",
                    NuGetPackageVersion = "1.0.0",
                    HintPath = "/path/to/cs/MyLib.resources.dll",
                    CultureName = "cs",
                },
                new ResolvedReferenceInfo
                {
                    FileName = "MyLib.resources",
                    NuGetPackageId = "MyLib",
                    NuGetPackageVersion = "1.0.0",
                    HintPath = "/path/to/de/MyLib.resources.dll",
                    CultureName = "de",
                },
                new ResolvedReferenceInfo
                {
                    FileName = "MyLib.resources",
                    NuGetPackageId = "MyLib",
                    NuGetPackageVersion = "1.0.0",
                    HintPath = "/path/to/fr/MyLib.resources.dll",
                    CultureName = "fr",
                },
            ],
        };

        var bom = GenerateAndValidate(input);
        var component = bom.Components?.FirstOrDefault(c => c.Name == "MyLib");
        var bomRefs = component!.Components!.Select(c => c.BomRef).ToList();

        Assert.Equal(3, bomRefs.Count);
        Assert.Equal(3, bomRefs.Distinct().Count());
    }

    [Fact]
    public void Generate_ResourceAssembliesFromResolvedRefsCreateSubComponents()
    {
        // Resource assemblies are added to ResolvedReferences by BuildInput() after
        // resolving paths from project.assets.json — SbomGenerator sees them as normal refs
        var input = new SbomInput
        {
            ProjectName = "TestApp",
            ResolvedReferences =
            [
                new ResolvedReferenceInfo
                {
                    FileName = "System.CommandLine",
                    NuGetPackageId = "System.CommandLine",
                    NuGetPackageVersion = "2.0.0-beta4",
                    HintPath = "/path/to/System.CommandLine.dll",
                },
                new ResolvedReferenceInfo
                {
                    FileName = "System.CommandLine.resources",
                    NuGetPackageId = "System.CommandLine",
                    NuGetPackageVersion = "2.0.0-beta4",
                    HintPath = "/path/to/cs/System.CommandLine.resources.dll",
                    CultureName = "cs",
                    FileHashHex = "abcdef1234567890abcdef1234567890abcdef1234567890abcdef1234567890",
                },
                new ResolvedReferenceInfo
                {
                    FileName = "System.CommandLine.resources",
                    NuGetPackageId = "System.CommandLine",
                    NuGetPackageVersion = "2.0.0-beta4",
                    HintPath = "/path/to/de/System.CommandLine.resources.dll",
                    CultureName = "de",
                    FileHashHex = "1234567890abcdef1234567890abcdef1234567890abcdef1234567890abcdef",
                },
                new ResolvedReferenceInfo
                {
                    FileName = "System.CommandLine.resources",
                    NuGetPackageId = "System.CommandLine",
                    NuGetPackageVersion = "2.0.0-beta4",
                    HintPath = "/path/to/fr/System.CommandLine.resources.dll",
                    CultureName = "fr",
                    FileHashHex = "567890abcdef1234567890abcdef1234567890abcdef1234567890abcdef1234",
                },
            ],
        };

        var bom = GenerateAndValidate(input);
        var component = bom.Components?.FirstOrDefault(c => c.Name == "System.CommandLine");

        Assert.NotNull(component?.Components);
        // 1 main DLL + 3 resource DLLs
        Assert.Equal(4, component!.Components!.Count);

        var csFile = component.Components.FirstOrDefault(c => c.Name == "cs/System.CommandLine.resources.dll");
        Assert.NotNull(csFile);
        Assert.Equal("System.CommandLine/2.0.0-beta4#cs/System.CommandLine.resources.dll", csFile!.BomRef);
        Assert.NotNull(csFile.Hashes);
        var cultureProp = csFile.Properties?.FirstOrDefault(p => p.Name == "cdx:msbuild:cultureName");
        Assert.NotNull(cultureProp);
        Assert.Equal("cs", cultureProp!.Value);

        var deFile = component.Components.FirstOrDefault(c => c.Name == "de/System.CommandLine.resources.dll");
        Assert.NotNull(deFile);
        Assert.NotNull(deFile!.Hashes);

        var frFile = component.Components.FirstOrDefault(c => c.Name == "fr/System.CommandLine.resources.dll");
        Assert.NotNull(frFile);
        Assert.NotNull(frFile!.Hashes);
    }

    [Fact]
    public void Generate_ResourceAssembliesFromAssetsFallbackIncludesHashes()
    {
        // When resource assemblies are only in ProjectAssets (not in ResolvedReferences),
        // the fallback path resolves files from package folders and computes hashes
        var tempDir = Path.Combine(Path.GetTempPath(), $"sbom-test-{Guid.NewGuid():N}");
        var packageFolder = Path.Combine(tempDir, "packages") + Path.DirectorySeparatorChar;
        var csDir = Path.Combine(packageFolder, "mylib", "1.0.0", "lib", "net6.0", "cs");
        Directory.CreateDirectory(csDir);
        File.WriteAllText(Path.Combine(csDir, "MyLib.resources.dll"), "fake cs resource dll");

        try
        {
            var assetInfo = new PackageAssetInfo
            {
                Name = "MyLib",
                Version = "1.0.0",
                PackagePath = "mylib/1.0.0",
            };
            assetInfo.ResourceAssemblies.Add("lib/net6.0/cs/MyLib.resources.dll");

            var input = new SbomInput
            {
                ProjectName = "TestApp",
                ResolvedReferences =
                [
                    new ResolvedReferenceInfo
                    {
                        FileName = "MyLib",
                        NuGetPackageId = "MyLib",
                        NuGetPackageVersion = "1.0.0",
                        HintPath = "/path/to/MyLib.dll",
                    },
                ],
                ProjectAssets = new ProjectAssetsData
                {
                    PackageFolders = [packageFolder],
                    Packages = new Dictionary<string, PackageAssetInfo>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["MyLib/1.0.0"] = assetInfo,
                    },
                },
            };

            var bom = GenerateAndValidate(input);
            var component = bom.Components?.FirstOrDefault(c => c.Name == "MyLib");

            Assert.NotNull(component?.Components);
            Assert.Equal(2, component!.Components!.Count);

            var csFile = component.Components.First(c => c.Name == "cs/MyLib.resources.dll");
            Assert.NotNull(csFile.Hashes);
            var hash = Assert.Single(csFile.Hashes!);
            Assert.Equal(Hash.HashAlgorithm.SHA_256, hash.Alg);
            Assert.NotEmpty(hash.Content);

            var hintProp = csFile.Properties?.FirstOrDefault(p => p.Name == "cdx:msbuild:hintPath");
            Assert.NotNull(hintProp);
            Assert.Contains("MyLib.resources.dll", hintProp!.Value);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void Generate_ResourceAssembliesFromAssetsFallbackDoesNotDuplicateExisting()
    {
        // When a satellite assembly is already in ResolvedReferences (e.g. from
        // @(ReferenceSatellitePaths)), the assets fallback should skip it
        var input = new SbomInput
        {
            ProjectName = "TestApp",
            ResolvedReferences =
            [
                new ResolvedReferenceInfo
                {
                    FileName = "MyLib",
                    NuGetPackageId = "MyLib",
                    NuGetPackageVersion = "1.0.0",
                    HintPath = "/path/to/MyLib.dll",
                },
                new ResolvedReferenceInfo
                {
                    FileName = "MyLib.resources",
                    NuGetPackageId = "MyLib",
                    NuGetPackageVersion = "1.0.0",
                    HintPath = "/path/to/cs/MyLib.resources.dll",
                    CultureName = "cs",
                    FileHashHex = "abcdef1234567890abcdef1234567890abcdef1234567890abcdef1234567890",
                },
            ],
            ProjectAssets = new ProjectAssetsData
            {
                Packages = new Dictionary<string, PackageAssetInfo>(StringComparer.OrdinalIgnoreCase)
                {
                    ["MyLib/1.0.0"] = new PackageAssetInfo
                    {
                        Name = "MyLib",
                        Version = "1.0.0",
                        ResourceAssemblies = { "lib/net6.0/cs/MyLib.resources.dll" },
                    },
                },
            },
        };

        var bom = GenerateAndValidate(input);
        var component = bom.Components?.FirstOrDefault(c => c.Name == "MyLib");

        Assert.NotNull(component?.Components);
        // 1 main DLL + 1 cs resource (not duplicated)
        Assert.Equal(2, component!.Components!.Count);

        // The one from ResolvedReferences should be kept (has hash)
        var csFile = component.Components.First(c => c.Name == "cs/MyLib.resources.dll");
        Assert.NotNull(csFile.Hashes);
    }

    [Fact]
    public void Generate_RegularAssemblyHasNoCultureProperty()
    {
        var input = CreateBasicInput();

        var bom = GenerateAndValidate(input);
        var component = bom.Components?.FirstOrDefault(c => c.Name == "Newtonsoft.Json");
        var fileComp = component!.Components!.First();

        var cultureProp = fileComp.Properties?.FirstOrDefault(p => p.Name == "cdx:msbuild:cultureName");
        Assert.Null(cultureProp);
    }

    [Fact]
    public void Generate_TopLevelFilesAddedToMetadataComponent()
    {
        var input = new SbomInput
        {
            ProjectName = "MyApp",
            TopLevelFiles =
            [
                new TopLevelFileInfo
                {
                    FileName = "MyApp.dll",
                    FullPath = "/out/MyApp.dll",
                    FileHashHex = "abcdef1234567890abcdef1234567890abcdef1234567890abcdef1234567890",
                },
                new TopLevelFileInfo
                {
                    FileName = "MyApp.pdb",
                    FullPath = "/out/MyApp.pdb",
                    FileHashHex = "1234567890abcdef1234567890abcdef1234567890abcdef1234567890abcdef",
                },
            ],
        };

        var bom = GenerateAndValidate(input);
        var metaComponent = bom.Metadata?.Component;

        Assert.NotNull(metaComponent?.Components);
        Assert.Equal(2, metaComponent!.Components!.Count);
        Assert.All(metaComponent.Components, c => Assert.Equal(Component.Classification.File, c.Type));
    }

    [Fact]
    public void Generate_TopLevelFilesHaveCorrectBomRefs()
    {
        var input = new SbomInput
        {
            ProjectName = "MyApp",
            TopLevelFiles =
            [
                new TopLevelFileInfo
                {
                    FileName = "MyApp.dll",
                    FullPath = "/out/MyApp.dll",
                    FileHashHex = "abcdef1234567890abcdef1234567890abcdef1234567890abcdef1234567890",
                },
            ],
        };

        var bom = GenerateAndValidate(input);
        var fileComp = bom.Metadata!.Component!.Components!.First();

        Assert.Equal("MyApp#MyApp.dll", fileComp.BomRef);
        Assert.Equal("MyApp.dll", fileComp.Name);
    }

    [Fact]
    public void Generate_TopLevelFilesHaveSha256Hashes()
    {
        var input = new SbomInput
        {
            ProjectName = "MyApp",
            TopLevelFiles =
            [
                new TopLevelFileInfo
                {
                    FileName = "MyApp.dll",
                    FullPath = "/out/MyApp.dll",
                    FileHashHex = "abcdef1234567890abcdef1234567890abcdef1234567890abcdef1234567890",
                },
            ],
        };

        var bom = GenerateAndValidate(input);
        var fileComp = bom.Metadata!.Component!.Components!.First();

        Assert.NotNull(fileComp.Hashes);
        var hash = Assert.Single(fileComp.Hashes!);
        Assert.Equal(Hash.HashAlgorithm.SHA_256, hash.Alg);
        Assert.Equal("abcdef1234567890abcdef1234567890abcdef1234567890abcdef1234567890", hash.Content);
    }

    [Fact]
    public void Generate_TopLevelFilesSortedByName()
    {
        var input = new SbomInput
        {
            ProjectName = "MyApp",
            TopLevelFiles =
            [
                new TopLevelFileInfo { FileName = "MyApp.pdb", FullPath = "/out/MyApp.pdb" },
                new TopLevelFileInfo { FileName = "MyApp.deps.json", FullPath = "/out/MyApp.deps.json" },
                new TopLevelFileInfo { FileName = "MyApp.dll", FullPath = "/out/MyApp.dll" },
            ],
        };

        var bom = GenerateAndValidate(input);
        var names = bom.Metadata!.Component!.Components!.Select(c => c.Name).ToList();

        Assert.Equal(["MyApp.deps.json", "MyApp.dll", "MyApp.pdb"], names);
    }

    [Fact]
    public void Generate_NoTopLevelSubComponentsWhenEmpty()
    {
        var input = new SbomInput
        {
            ProjectName = "MyApp",
            TopLevelFiles = [],
        };

        var bom = GenerateAndValidate(input);

        Assert.Null(bom.Metadata?.Component?.Components);
    }

    [Fact]
    public void Generate_TopLevelFileWithNullHashOmitsHashes()
    {
        var input = new SbomInput
        {
            ProjectName = "MyApp",
            TopLevelFiles =
            [
                new TopLevelFileInfo
                {
                    FileName = "MyApp.dll",
                    FullPath = "/out/MyApp.dll",
                    FileHashHex = null,
                },
            ],
        };

        var bom = GenerateAndValidate(input);
        var fileComp = bom.Metadata!.Component!.Components!.First();

        Assert.Null(fileComp.Hashes);
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

        var bom = GenerateAndValidate(input);
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
