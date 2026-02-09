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

using CycloneDX.MSBuildTask;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using NSubstitute;
using Xunit;

namespace CycloneDX.MSBuildTask.Tests;

public class GenerateSbomTaskTests : IDisposable
{
    private readonly string _tempDir;
    private readonly IBuildEngine _buildEngine;

    public GenerateSbomTaskTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"cyclonedx-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _buildEngine = Substitute.For<IBuildEngine>();
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private GenerateSbomTask CreateTask() => new()
    {
        BuildEngine = _buildEngine,
        OutputDirectory = _tempDir,
        ProjectName = "TestProject",
        ProjectVersion = "1.0.0",
        TargetFramework = "net10.0",
    };

    private static TaskItem CreateResolvedReference(
        string itemSpec,
        string? nugetPackageId = null,
        string? nugetPackageVersion = null,
        string? hintPath = null,
        string? resolvedFrom = null)
    {
        var item = new TaskItem(itemSpec);
        if (nugetPackageId is not null)
            item.SetMetadata("NuGetPackageId", nugetPackageId);
        if (nugetPackageVersion is not null)
            item.SetMetadata("NuGetPackageVersion", nugetPackageVersion);
        if (hintPath is not null)
            item.SetMetadata("HintPath", hintPath);
        if (resolvedFrom is not null)
            item.SetMetadata("ResolvedFrom", resolvedFrom);
        return item;
    }

    [Fact]
    public void Execute_GeneratesJsonAndXml()
    {
        var task = CreateTask();
        task.ResolvedReferences =
        [
            CreateResolvedReference(
                "/path/to/Newtonsoft.Json.dll",
                nugetPackageId: "Newtonsoft.Json",
                nugetPackageVersion: "13.0.3",
                hintPath: "/path/to/Newtonsoft.Json.dll"),
        ];
        task.PackageReferences = [new TaskItem("Newtonsoft.Json")];

        var result = task.Execute();

        Assert.True(result);
        Assert.True(File.Exists(Path.Combine(_tempDir, "bom.json")));
        Assert.True(File.Exists(Path.Combine(_tempDir, "bom.xml")));
    }

    [Fact]
    public void Execute_JsonContainsComponents()
    {
        var task = CreateTask();
        task.ResolvedReferences =
        [
            CreateResolvedReference(
                "/path/to/Newtonsoft.Json.dll",
                nugetPackageId: "Newtonsoft.Json",
                nugetPackageVersion: "13.0.3"),
        ];
        task.PackageReferences = [new TaskItem("Newtonsoft.Json")];

        task.Execute();

        var json = File.ReadAllText(Path.Combine(_tempDir, "bom.json"));
        Assert.Contains("Newtonsoft.Json", json);
        Assert.Contains("13.0.3", json);
        Assert.Contains("pkg:nuget/Newtonsoft.Json@13.0.3", json);
    }

    [Fact]
    public void Execute_XmlContainsComponents()
    {
        var task = CreateTask();
        task.ResolvedReferences =
        [
            CreateResolvedReference(
                "/path/to/Newtonsoft.Json.dll",
                nugetPackageId: "Newtonsoft.Json",
                nugetPackageVersion: "13.0.3"),
        ];
        task.PackageReferences = [new TaskItem("Newtonsoft.Json")];

        task.Execute();

        var xml = File.ReadAllText(Path.Combine(_tempDir, "bom.xml"));
        Assert.Contains("Newtonsoft.Json", xml);
        Assert.Contains("13.0.3", xml);
    }

    [Fact]
    public void Execute_CreatesOutputDirectory()
    {
        var subDir = Path.Combine(_tempDir, "nested", "output");
        var task = CreateTask();
        task.OutputDirectory = subDir;

        var result = task.Execute();

        Assert.True(result);
        Assert.True(Directory.Exists(subDir));
        Assert.True(File.Exists(Path.Combine(subDir, "bom.json")));
    }

    [Fact]
    public void Execute_WithNoReferences_GeneratesEmptyBom()
    {
        var task = CreateTask();
        var result = task.Execute();

        Assert.True(result);

        var json = File.ReadAllText(Path.Combine(_tempDir, "bom.json"));
        Assert.Contains("TestProject", json);
    }

    [Fact]
    public void Execute_WithAssetsFile_IncludesHashes()
    {
        var assetsPath = Path.Combine(
            AppContext.BaseDirectory, "Fixtures", "project.assets.json");

        var task = CreateTask();
        task.ProjectAssetsFile = assetsPath;
        task.ResolvedReferences =
        [
            CreateResolvedReference(
                "/path/to/Newtonsoft.Json.dll",
                nugetPackageId: "Newtonsoft.Json",
                nugetPackageVersion: "13.0.3"),
        ];
        task.PackageReferences = [new TaskItem("Newtonsoft.Json")];

        task.Execute();

        var json = File.ReadAllText(Path.Combine(_tempDir, "bom.json"));
        // SHA-512 hash from the fixture
        Assert.Contains("HdHQRBnCnKscn3WDJmO0C8Rg", json);
    }

    [Fact]
    public void BuildInput_MapsTaskItemsCorrectly()
    {
        var task = CreateTask();
        task.ResolvedReferences =
        [
            CreateResolvedReference(
                "/path/to/MyLib.dll",
                nugetPackageId: "MyLib",
                nugetPackageVersion: "2.0.0",
                hintPath: "/path/to/MyLib.dll",
                resolvedFrom: "{NuGetPackagesFallbackFolders}"),
        ];
        task.PackageReferences =
        [
            new TaskItem("MyLib"),
        ];

        var input = task.BuildInput();

        Assert.Equal("TestProject", input.ProjectName);
        Assert.Equal("1.0.0", input.ProjectVersion);
        Assert.Equal("net10.0", input.TargetFramework);

        var resolved = Assert.Single(input.ResolvedReferences);
        Assert.Equal("MyLib", resolved.NuGetPackageId);
        Assert.Equal("2.0.0", resolved.NuGetPackageVersion);
        Assert.Equal("/path/to/MyLib.dll", resolved.HintPath);

        var pkgRef = Assert.Single(input.PackageReferences);
        Assert.Equal("MyLib", pkgRef.Name);
    }

    [Fact]
    public void BuildInput_HandlesNullReferences()
    {
        var task = CreateTask();
        // Leave ResolvedReferences and PackageReferences null

        var input = task.BuildInput();

        Assert.Empty(input.ResolvedReferences);
        Assert.Empty(input.PackageReferences);
    }

    [Fact]
    public void BuildInput_HandlesEmptyMetadata()
    {
        var task = CreateTask();
        task.ResolvedReferences =
        [
            new TaskItem("/path/to/Something.dll"),
        ];

        var input = task.BuildInput();

        var resolved = Assert.Single(input.ResolvedReferences);
        Assert.Null(resolved.NuGetPackageId);
        Assert.Null(resolved.NuGetPackageVersion);
    }

    [Fact]
    public void ComputeFileHash_ReturnsNullForMissingFile()
    {
        var hash = GenerateSbomTask.ComputeFileHash("/nonexistent/file.dll");
        Assert.Null(hash);
    }

    [Fact]
    public void ComputeFileHash_ReturnsBase64Sha256ForExistingFile()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, "test content");
            var hash = GenerateSbomTask.ComputeFileHash(tempFile);

            Assert.NotNull(hash);
            // Verify it's valid base64 and the right length for SHA-256 (32 bytes = 44 base64 chars)
            var bytes = Convert.FromBase64String(hash!);
            Assert.Equal(32, bytes.Length);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void ComputeFileHash_ReturnsDeterministicResult()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, "deterministic content");
            var hash1 = GenerateSbomTask.ComputeFileHash(tempFile);
            var hash2 = GenerateSbomTask.ComputeFileHash(tempFile);

            Assert.Equal(hash1, hash2);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void Execute_JsonContainsFileSubComponents()
    {
        // Create a real file so it can be hashed
        var dllPath = Path.Combine(_tempDir, "FakeLib.dll");
        File.WriteAllText(dllPath, "fake dll content");

        var task = CreateTask();
        task.ResolvedReferences =
        [
            CreateResolvedReference(
                dllPath,
                nugetPackageId: "FakeLib",
                nugetPackageVersion: "1.0.0",
                hintPath: dllPath),
        ];
        task.PackageReferences = [new TaskItem("FakeLib")];

        task.Execute();

        var json = File.ReadAllText(Path.Combine(_tempDir, "bom.json"));
        // Should contain the file sub-component
        Assert.Contains("FakeLib.dll", json);
        Assert.Contains("\"type\": \"file\"", json);
        // Should contain a SHA-256 hash for the file
        Assert.Contains("SHA-256", json);
    }
}
