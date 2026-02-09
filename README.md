# EXPERIMENTAL CycloneDX MSBuild Task

An MSBuild task that automatically generates [CycloneDX](https://cyclonedx.org/) Software Bill of Materials (SBOM) during build. It combines NuGet package manifest data with MSBuild's actual resolved assembly references to produce an accurate, build-time SBOM.

This tool is currently in an experimental state, is not an officially supported CycloneDX project, and uses unofficial property taxonomy entries.

The officially recommended way to generate SBOMs for .NET projects remains the CycloneDX .NET tool.

NOTE: This is a different tool to the unofficial community created CycloneDX.MSBuild package. The CycloneDX.MSBuild package works by calling the CycloneDX .NET tool. This MS Build task works by integrating into the build process by extending the 
`Microsoft.Build.Utilities.Task` class and generating an SBOM directly from the build process.

## What It Does

When you build your project, this task:

1. **Captures resolved references** - the actual DLLs that MSBuild resolves during `ResolveAssemblyReferences`, including where each file came from (NuGet cache, framework directory, hint path, etc.)
2. **Reads NuGet package data** - parses `project.assets.json` to extract transitive dependencies, SHA-512 hashes, and the full dependency graph
3. **Correlates the two** - matches resolved files to their originating NuGet packages, capturing both declared and transitive dependencies
4. **Emits CycloneDX SBOM** - generates both `bom.json` and `bom.xml` in the output directory

The result is an SBOM that reflects what MSBuild *actually* resolved, not just what was declared.

## Output Format

The generated SBOM follows CycloneDX v1.6 and includes:

- **Metadata** - project name, version, target framework, build timestamp, tool identification
- **Components** - each NuGet package as a `library` component with Package URL (purl), SHA-512 hash, and resolved file paths; framework references as `framework` components
- **Dependencies** - full dependency graph linking the project to direct dependencies and transitive relationships
- **Build evidence** - custom properties (`cdx:msbuild:resolvedFile`, `cdx:msbuild:resolvedFrom`) showing exactly which DLLs were resolved and from where

## Installation

### From NuGet Package

`dotnet add package CycloneDX.MSBuildTask`

The SBOM generates automatically on every build in JSON and XML formats -- no additional configuration needed.

### From Source (Local Development)

```xml
<!-- Set the task assembly path to the local build output -->
<PropertyGroup>
  <CycloneDxMSBuildTaskAssembly>path/to/CycloneDX.MSBuildTask.dll</CycloneDxMSBuildTaskAssembly>
</PropertyGroup>
<Import Project="path/to/build/CycloneDX.MSBuildTask.props" />
<Import Project="path/to/build/CycloneDX.MSBuildTask.targets" />
```

## Configuration

| MSBuild Property | Default | Description |
|---|---|---|
| `CycloneDxOutputDirectory` | `$(OutputPath)` | Directory where `bom.json` and `bom.xml` are written |
| `CycloneDxMSBuildTaskAssembly` | *(auto from NuGet)* | Path to the task DLL (override for local development) |

### Example: Custom Output Directory

```xml
<PropertyGroup>
  <CycloneDxOutputDirectory>$(MSBuildProjectDirectory)/sbom</CycloneDxOutputDirectory>
</PropertyGroup>
```

### Try the Sample

```bash
dotnet build samples/SampleApp/SampleApp.csproj
cat samples/SampleApp/bin/Debug/net10.0/bom.json
```

