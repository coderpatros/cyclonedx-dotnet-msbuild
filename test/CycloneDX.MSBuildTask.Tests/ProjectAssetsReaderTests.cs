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
using Xunit;

namespace CycloneDX.MSBuildTask.Tests;

public class ProjectAssetsReaderTests
{
    private static string FixturePath =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "project.assets.json");

    [Fact]
    public void Read_ParsesLibraries()
    {
        var data = ProjectAssetsReader.Read(FixturePath, "net10.0");

        Assert.Equal(3, data.Packages.Count);
        Assert.True(data.Packages.ContainsKey("Newtonsoft.Json/13.0.3"));
        Assert.True(data.Packages.ContainsKey("Serilog/4.0.0"));
        Assert.True(data.Packages.ContainsKey("Serilog.Formatting.Compact/3.0.0"));
    }

    [Fact]
    public void Read_ExtractsPackageMetadata()
    {
        var data = ProjectAssetsReader.Read(FixturePath, "net10.0");
        var pkg = data.Packages["Newtonsoft.Json/13.0.3"];

        Assert.Equal("Newtonsoft.Json", pkg.Name);
        Assert.Equal("13.0.3", pkg.Version);
        Assert.NotNull(pkg.Sha512Hex);
        Assert.Equal("1eb0b9057765d3420ff73795fb467ce3c4163c0a02afd3f76c311982e23e8242dc04a00ec62c7fb4b1000070be52f0cd3efe1ad9dd7c94e45e1cc39a80f6becd", pkg.Sha512Hex);
    }

    [Fact]
    public void Read_ExtractsDependencyGraph()
    {
        var data = ProjectAssetsReader.Read(FixturePath, "net10.0");

        Assert.True(data.DependencyGraph.ContainsKey("Serilog/4.0.0"));
        var deps = data.DependencyGraph["Serilog/4.0.0"];
        Assert.Single(deps);
        Assert.Equal("Serilog.Formatting.Compact/3.0.0", deps[0]);
    }

    [Fact]
    public void Read_ExtractsRuntimeAssemblies()
    {
        var data = ProjectAssetsReader.Read(FixturePath, "net10.0");
        var pkg = data.Packages["Newtonsoft.Json/13.0.3"];

        Assert.Single(pkg.RuntimeAssemblies);
        Assert.Contains("lib/net6.0/Newtonsoft.Json.dll", pkg.RuntimeAssemblies);
    }

    [Fact]
    public void Read_ExtractsCompileAssemblies()
    {
        var data = ProjectAssetsReader.Read(FixturePath, "net10.0");
        var pkg = data.Packages["Serilog/4.0.0"];

        Assert.Single(pkg.CompileAssemblies);
        Assert.Contains("lib/net8.0/Serilog.dll", pkg.CompileAssemblies);
    }

    [Fact]
    public void Read_ExtractsResourceAssemblies()
    {
        var json = """
        {
          "version": 3,
          "targets": {
            "net10.0": {
              "System.CommandLine/2.0.0-beta4": {
                "type": "package",
                "runtime": { "lib/net6.0/System.CommandLine.dll": {} },
                "resource": {
                  "lib/net6.0/cs/System.CommandLine.resources.dll": { "locale": "cs" },
                  "lib/net6.0/de/System.CommandLine.resources.dll": { "locale": "de" },
                  "lib/net6.0/fr/System.CommandLine.resources.dll": { "locale": "fr" }
                }
              }
            }
          },
          "libraries": {
            "System.CommandLine/2.0.0-beta4": {
              "sha512": "abc123==",
              "type": "package"
            }
          }
        }
        """;

        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, json);
            var data = ProjectAssetsReader.Read(tempFile, "net10.0");
            var pkg = data.Packages["System.CommandLine/2.0.0-beta4"];

            Assert.Equal(3, pkg.ResourceAssemblies.Count);
            Assert.Contains("lib/net6.0/cs/System.CommandLine.resources.dll", pkg.ResourceAssemblies);
            Assert.Contains("lib/net6.0/de/System.CommandLine.resources.dll", pkg.ResourceAssemblies);
            Assert.Contains("lib/net6.0/fr/System.CommandLine.resources.dll", pkg.ResourceAssemblies);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void Read_ResourceAssembliesEmptyWhenNoResourceSection()
    {
        var data = ProjectAssetsReader.Read(FixturePath, "net10.0");
        var pkg = data.Packages["Newtonsoft.Json/13.0.3"];

        Assert.Empty(pkg.ResourceAssemblies);
    }

    [Fact]
    public void Read_ReturnsEmptyForMissingFile()
    {
        var data = ProjectAssetsReader.Read("/nonexistent/path/project.assets.json");

        Assert.Empty(data.Packages);
        Assert.Empty(data.DependencyGraph);
    }

    [Fact]
    public void Read_IgnoresNonPackageLibraries()
    {
        var json = """
        {
          "version": 3,
          "targets": { "net10.0": {} },
          "libraries": {
            "MyProject/1.0.0": {
              "type": "project",
              "path": "myproject/1.0.0"
            }
          }
        }
        """;

        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, json);
            var data = ProjectAssetsReader.Read(tempFile);
            Assert.Empty(data.Packages);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void Read_SelectsCorrectTargetFramework()
    {
        var json = """
        {
          "version": 3,
          "targets": {
            "net9.0": {
              "PackageA/1.0.0": {
                "type": "package",
                "runtime": { "lib/net9.0/PackageA.dll": {} }
              }
            },
            "net10.0": {
              "PackageA/1.0.0": {
                "type": "package",
                "runtime": { "lib/net10.0/PackageA.dll": {} }
              }
            }
          },
          "libraries": {
            "PackageA/1.0.0": {
              "sha512": "abc123==",
              "type": "package",
              "path": "packagea/1.0.0",
              "files": [ "lib/net9.0/PackageA.dll", "lib/net10.0/PackageA.dll" ]
            }
          }
        }
        """;

        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, json);

            var dataN9 = ProjectAssetsReader.Read(tempFile, "net9.0");
            var pkgN9 = dataN9.Packages["PackageA/1.0.0"];
            Assert.Single(pkgN9.RuntimeAssemblies);
            Assert.Contains("lib/net9.0/PackageA.dll", pkgN9.RuntimeAssemblies);

            var dataN10 = ProjectAssetsReader.Read(tempFile, "net10.0");
            var pkgN10 = dataN10.Packages["PackageA/1.0.0"];
            Assert.Single(pkgN10.RuntimeAssemblies);
            Assert.Contains("lib/net10.0/PackageA.dll", pkgN10.RuntimeAssemblies);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void Read_SelectsRidSpecificTarget()
    {
        var json = """
        {
          "version": 3,
          "targets": {
            "net10.0": {
              "PackageA/1.0.0": {
                "type": "package",
                "runtime": { "lib/net10.0/PackageA.dll": {} }
              }
            },
            "net10.0/linux-x64": {
              "PackageA/1.0.0": {
                "type": "package",
                "runtime": { "lib/net10.0/PackageA.dll": {} }
              },
              "RuntimePack/10.0.0": {
                "type": "package",
                "runtime": { "runtimes/linux-x64/lib/net10.0/RuntimeLib.dll": {} }
              }
            }
          },
          "libraries": {
            "PackageA/1.0.0": {
              "sha512": "abc123==",
              "type": "package"
            },
            "RuntimePack/10.0.0": {
              "sha512": "def456==",
              "type": "package"
            }
          }
        }
        """;

        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, json);

            // With RID: should pick net10.0/linux-x64 and get RuntimePack assemblies
            var dataWithRid = ProjectAssetsReader.Read(tempFile, "net10.0", "linux-x64");
            var runtimePack = dataWithRid.Packages["RuntimePack/10.0.0"];
            Assert.Single(runtimePack.RuntimeAssemblies);
            Assert.Contains("runtimes/linux-x64/lib/net10.0/RuntimeLib.dll", runtimePack.RuntimeAssemblies);

            // Without RID: should pick net10.0 and RuntimePack has no assemblies
            var dataNoRid = ProjectAssetsReader.Read(tempFile, "net10.0");
            var runtimePackNoRid = dataNoRid.Packages["RuntimePack/10.0.0"];
            Assert.Empty(runtimePackNoRid.RuntimeAssemblies);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void Read_FallsBackToFirstFrameworkWhenNoMatch()
    {
        var json = """
        {
          "version": 3,
          "targets": {
            "net10.0": {
              "PackageA/1.0.0": {
                "type": "package",
                "runtime": { "lib/net10.0/PackageA.dll": {} }
              }
            }
          },
          "libraries": {
            "PackageA/1.0.0": {
              "sha512": "abc123==",
              "type": "package",
              "path": "packagea/1.0.0",
              "files": [ "lib/net10.0/PackageA.dll" ]
            }
          }
        }
        """;

        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, json);
            var data = ProjectAssetsReader.Read(tempFile, "net8.0");
            var pkg = data.Packages["PackageA/1.0.0"];
            Assert.Single(pkg.RuntimeAssemblies);
            Assert.Contains("lib/net10.0/PackageA.dll", pkg.RuntimeAssemblies);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void Read_ExtractsLicenseExpressionFromNuspec()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"nuget-test-{Guid.NewGuid():N}");
        var packageFolder = Path.Combine(tempDir, "packages") + Path.DirectorySeparatorChar;
        var nuspecDir = Path.Combine(packageFolder, "packagea", "1.0.0");
        Directory.CreateDirectory(nuspecDir);

        var nuspec = """
        <?xml version="1.0" encoding="utf-8"?>
        <package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
          <metadata>
            <id>PackageA</id>
            <version>1.0.0</version>
            <license type="expression">MIT</license>
          </metadata>
        </package>
        """;
        File.WriteAllText(Path.Combine(nuspecDir, "packagea.nuspec"), nuspec);

        var json = $$"""
        {
          "version": 3,
          "targets": { "net10.0": {} },
          "packageFolders": { "{{packageFolder.Replace("\\", "\\\\")}}": {} },
          "libraries": {
            "PackageA/1.0.0": {
              "type": "package",
              "path": "packagea/1.0.0"
            }
          }
        }
        """;

        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, json);
            var data = ProjectAssetsReader.Read(tempFile);
            var pkg = data.Packages["PackageA/1.0.0"];

            Assert.Equal("MIT", pkg.LicenseExpression);
            Assert.Null(pkg.LicenseUrl);
        }
        finally
        {
            File.Delete(tempFile);
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void Read_ExtractsLicenseUrlFromLegacyNuspec()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"nuget-test-{Guid.NewGuid():N}");
        var packageFolder = Path.Combine(tempDir, "packages") + Path.DirectorySeparatorChar;
        var nuspecDir = Path.Combine(packageFolder, "oldpkg", "2.0.0");
        Directory.CreateDirectory(nuspecDir);

        var nuspec = """
        <?xml version="1.0" encoding="utf-8"?>
        <package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
          <metadata>
            <id>OldPkg</id>
            <version>2.0.0</version>
            <licenseUrl>https://example.com/license</licenseUrl>
          </metadata>
        </package>
        """;
        File.WriteAllText(Path.Combine(nuspecDir, "oldpkg.nuspec"), nuspec);

        var json = $$"""
        {
          "version": 3,
          "targets": { "net10.0": {} },
          "packageFolders": { "{{packageFolder.Replace("\\", "\\\\")}}": {} },
          "libraries": {
            "OldPkg/2.0.0": {
              "type": "package",
              "path": "oldpkg/2.0.0"
            }
          }
        }
        """;

        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, json);
            var data = ProjectAssetsReader.Read(tempFile);
            var pkg = data.Packages["OldPkg/2.0.0"];

            Assert.Null(pkg.LicenseExpression);
            Assert.Equal("https://example.com/license", pkg.LicenseUrl);
        }
        finally
        {
            File.Delete(tempFile);
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void Read_HandlesCompoundLicenseExpression()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"nuget-test-{Guid.NewGuid():N}");
        var packageFolder = Path.Combine(tempDir, "packages") + Path.DirectorySeparatorChar;
        var nuspecDir = Path.Combine(packageFolder, "duallicense", "1.0.0");
        Directory.CreateDirectory(nuspecDir);

        var nuspec = """
        <?xml version="1.0" encoding="utf-8"?>
        <package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
          <metadata>
            <id>DualLicense</id>
            <version>1.0.0</version>
            <license type="expression">MIT OR Apache-2.0</license>
          </metadata>
        </package>
        """;
        File.WriteAllText(Path.Combine(nuspecDir, "duallicense.nuspec"), nuspec);

        var json = $$"""
        {
          "version": 3,
          "targets": { "net10.0": {} },
          "packageFolders": { "{{packageFolder.Replace("\\", "\\\\")}}": {} },
          "libraries": {
            "DualLicense/1.0.0": {
              "type": "package",
              "path": "duallicense/1.0.0"
            }
          }
        }
        """;

        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, json);
            var data = ProjectAssetsReader.Read(tempFile);
            var pkg = data.Packages["DualLicense/1.0.0"];

            Assert.Equal("MIT OR Apache-2.0", pkg.LicenseExpression);
            Assert.Null(pkg.LicenseUrl);
        }
        finally
        {
            File.Delete(tempFile);
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void Read_GracefullyHandlesMissingNuspec()
    {
        var json = """
        {
          "version": 3,
          "targets": { "net10.0": {} },
          "packageFolders": { "/nonexistent/folder/": {} },
          "libraries": {
            "PackageA/1.0.0": {
              "type": "package",
              "path": "packagea/1.0.0"
            }
          }
        }
        """;

        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, json);
            var data = ProjectAssetsReader.Read(tempFile);
            var pkg = data.Packages["PackageA/1.0.0"];

            Assert.Null(pkg.LicenseExpression);
            Assert.Null(pkg.LicenseUrl);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void Read_NoLicenseDataWhenNoPackageFolders()
    {
        var data = ProjectAssetsReader.Read(FixturePath, "net10.0");
        var pkg = data.Packages["Newtonsoft.Json/13.0.3"];

        Assert.Null(pkg.LicenseExpression);
        Assert.Null(pkg.LicenseUrl);
    }

    [Fact]
    public void Read_ExtractsDescriptionAuthorsAndCopyright()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"nuget-test-{Guid.NewGuid():N}");
        var packageFolder = Path.Combine(tempDir, "packages") + Path.DirectorySeparatorChar;
        var nuspecDir = Path.Combine(packageFolder, "richpkg", "1.0.0");
        Directory.CreateDirectory(nuspecDir);

        var nuspec = """
        <?xml version="1.0" encoding="utf-8"?>
        <package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
          <metadata>
            <id>RichPkg</id>
            <version>1.0.0</version>
            <description>A feature-rich package for testing</description>
            <authors>Alice,Bob</authors>
            <copyright>Copyright 2025 Example Corp</copyright>
            <license type="expression">MIT</license>
          </metadata>
        </package>
        """;
        File.WriteAllText(Path.Combine(nuspecDir, "richpkg.nuspec"), nuspec);

        var json = $$"""
        {
          "version": 3,
          "targets": { "net10.0": {} },
          "packageFolders": { "{{packageFolder.Replace("\\", "\\\\")}}": {} },
          "libraries": {
            "RichPkg/1.0.0": {
              "type": "package",
              "path": "richpkg/1.0.0"
            }
          }
        }
        """;

        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, json);
            var data = ProjectAssetsReader.Read(tempFile);
            var pkg = data.Packages["RichPkg/1.0.0"];

            Assert.Equal("A feature-rich package for testing", pkg.Description);
            Assert.Equal("Alice,Bob", pkg.Authors);
            Assert.Equal("Copyright 2025 Example Corp", pkg.Copyright);
        }
        finally
        {
            File.Delete(tempFile);
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void Read_ExtractsProjectUrlAndRepository()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"nuget-test-{Guid.NewGuid():N}");
        var packageFolder = Path.Combine(tempDir, "packages") + Path.DirectorySeparatorChar;
        var nuspecDir = Path.Combine(packageFolder, "repopkg", "2.0.0");
        Directory.CreateDirectory(nuspecDir);

        var nuspec = """
        <?xml version="1.0" encoding="utf-8"?>
        <package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
          <metadata>
            <id>RepoPkg</id>
            <version>2.0.0</version>
            <description>Package with repository metadata</description>
            <authors>Dev Team</authors>
            <projectUrl>https://example.com/repopkg</projectUrl>
            <repository type="git" url="https://github.com/example/repopkg.git" commit="abc123def456" />
          </metadata>
        </package>
        """;
        File.WriteAllText(Path.Combine(nuspecDir, "repopkg.nuspec"), nuspec);

        var json = $$"""
        {
          "version": 3,
          "targets": { "net10.0": {} },
          "packageFolders": { "{{packageFolder.Replace("\\", "\\\\")}}": {} },
          "libraries": {
            "RepoPkg/2.0.0": {
              "type": "package",
              "path": "repopkg/2.0.0"
            }
          }
        }
        """;

        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, json);
            var data = ProjectAssetsReader.Read(tempFile);
            var pkg = data.Packages["RepoPkg/2.0.0"];

            Assert.Equal("https://example.com/repopkg", pkg.ProjectUrl);
            Assert.Equal("https://github.com/example/repopkg.git", pkg.RepositoryUrl);
            Assert.Equal("git", pkg.RepositoryType);
            Assert.Equal("abc123def456", pkg.RepositoryCommit);
        }
        finally
        {
            File.Delete(tempFile);
            Directory.Delete(tempDir, recursive: true);
        }
    }
}
