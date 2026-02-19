using System.Diagnostics;
using Xunit.Abstractions;

namespace CycloneDX.MSBuildTask.IntegrationTests;

/// <summary>
/// Scaffolds temporary .NET projects wired up with the CycloneDX MSBuild task
/// and the BaGetter test NuGet registry. Used by integration test classes
/// to build projects and retrieve generated SBOMs.
/// </summary>
public class TestProjectHelper
{
    private readonly NuGetRegistryFixture _registry;
    private readonly ITestOutputHelper _output;
    private readonly string _tempDir;
    private readonly string _taskDll;
    private readonly string _taskPackageDir;
    private readonly string _repoRoot;

    public TestProjectHelper(
        NuGetRegistryFixture registry,
        ITestOutputHelper output,
        string tempDir,
        string taskPackagePath)
    {
        _registry = registry;
        _output = output;
        _tempDir = tempDir;
        _taskPackageDir = Path.GetDirectoryName(taskPackagePath)!;
        _repoRoot = FindRepoRoot();
        _taskDll = Path.Combine(_repoRoot, "src", "CycloneDX.MSBuildTask", "bin", "Debug", "net8.0", "CycloneDX.MSBuildTask.dll");
    }

    /// <summary>
    /// Creates a single test project, builds it, and returns the bom.json content.
    /// </summary>
    public async Task<string> BuildAndGetBomJson(string name, string packageReferences)
    {
        var projectDir = CreateTestProject(name, packageReferences);

        await RunDotnetAsync("restore", projectDir);
        var buildOutput = await RunDotnetAsync("build", projectDir, "--no-restore");
        _output.WriteLine(buildOutput);

        var bomJson = Path.Combine(projectDir, "bin", "Debug", "net10.0", "bom.json");
        if (!File.Exists(bomJson))
            throw new FileNotFoundException($"bom.json not found at {bomJson}");

        return await File.ReadAllTextAsync(bomJson);
    }

    /// <summary>
    /// Creates a single test project directory with a .csproj that references the
    /// compiled CycloneDX task and uses the BaGetter registry for test packages.
    /// </summary>
    public string CreateTestProject(string name, string packageReferences)
    {
        var projectDir = Path.Combine(_tempDir, name);
        Directory.CreateDirectory(projectDir);

        WriteNuGetConfig(projectDir);

        var csproj = $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net10.0</TargetFramework>
                <ImplicitUsings>enable</ImplicitUsings>
              </PropertyGroup>

              {packageReferences}

              {TaskImportFragment()}
            </Project>
            """;

        File.WriteAllText(Path.Combine(projectDir, $"{name}.csproj"), csproj);
        File.WriteAllText(Path.Combine(projectDir, "Program.cs"),
            """
            Console.WriteLine("Hello from integration test project");
            """);

        return projectDir;
    }

    /// <summary>
    /// Creates a multi-project setup: a root app with ProjectReferences to one or more
    /// class libraries. Each library can have its own NuGet package references.
    /// Returns the root project directory (restore and build from there).
    /// </summary>
    public string CreateMultiProjectSetup(
        string rootName,
        string rootPackageReferences,
        (string Name, string PackageReferences)[] libraries)
    {
        var slnDir = Path.Combine(_tempDir, $"{rootName}Sln");
        Directory.CreateDirectory(slnDir);

        var rootDir = Path.Combine(slnDir, rootName);
        Directory.CreateDirectory(rootDir);

        WriteNuGetConfig(slnDir);

        var projectRefs = new List<string>();
        foreach (var (libName, libPkgRefs) in libraries)
        {
            var libDir = Path.Combine(slnDir, libName);
            Directory.CreateDirectory(libDir);

            File.WriteAllText(Path.Combine(libDir, $"{libName}.csproj"), $"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <ImplicitUsings>enable</ImplicitUsings>
                  </PropertyGroup>
                  {libPkgRefs}
                </Project>
                """);

            File.WriteAllText(Path.Combine(libDir, "Class1.cs"), $$"""
                namespace {{libName}};
                public class Class1 { }
                """);

            projectRefs.Add(
                $"""    <ProjectReference Include="..\\{libName}\\{libName}.csproj" />""");
        }

        var projectRefXml = projectRefs.Count > 0
            ? $"""
              <ItemGroup>
            {string.Join(Environment.NewLine, projectRefs)}
              </ItemGroup>
            """
            : "";

        File.WriteAllText(Path.Combine(rootDir, $"{rootName}.csproj"), $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net10.0</TargetFramework>
                <ImplicitUsings>enable</ImplicitUsings>
              </PropertyGroup>

              {rootPackageReferences}
              {projectRefXml}

              {TaskImportFragment()}
            </Project>
            """);

        File.WriteAllText(Path.Combine(rootDir, "Program.cs"), """
            Console.WriteLine("Hello from root app");
            """);

        return rootDir;
    }

    public async Task<string> RunDotnetAsync(string command, string workingDir, string? args = null)
    {
        var arguments = string.IsNullOrEmpty(args) ? command : $"{command} {args}";
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = arguments,
            WorkingDirectory = workingDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        psi.Environment["DOTNET_MULTILEVEL_LOOKUP"] = "0";

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start: dotnet {arguments}");

        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        var output = stdout + stderr;

        if (process.ExitCode != 0)
        {
            _output.WriteLine($"FAILED: dotnet {arguments}");
            _output.WriteLine(output);
            throw new InvalidOperationException(
                $"dotnet {arguments} failed with exit code {process.ExitCode}:\n{output}");
        }

        return output;
    }

    /// <summary>
    /// Builds the CycloneDX.MSBuildTask project and returns the path to the generated .nupkg.
    /// </summary>
    public static async Task<string> BuildTaskPackageAsync(ITestOutputHelper output)
    {
        var repoRoot = FindRepoRoot();
        var taskProject = Path.Combine(repoRoot, "src", "CycloneDX.MSBuildTask");

        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = "build -c Debug",
            WorkingDirectory = taskProject,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.Environment["DOTNET_MULTILEVEL_LOOKUP"] = "0";

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start: dotnet build");

        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            output.WriteLine(stdout + stderr);
            throw new InvalidOperationException($"Task build failed:\n{stdout}{stderr}");
        }

        return Directory.GetFiles(
                Path.Combine(taskProject, "bin", "Debug"),
                "*.nupkg", SearchOption.TopDirectoryOnly)
            .FirstOrDefault()
            ?? throw new InvalidOperationException("Failed to find .nupkg after building the task project");
    }

    private void WriteNuGetConfig(string directory)
    {
        File.WriteAllText(Path.Combine(directory, "nuget.config"), $"""
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <packageSources>
                <clear />
                <add key="test-registry" value="{_registry.NuGetSourceUrl}" allowInsecureConnections="true" />
                <add key="local-task" value="{_taskPackageDir}" />
                <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
              </packageSources>
            </configuration>
            """);
    }

    private string TaskImportFragment() => $"""
              <!-- Reference the local CycloneDX.MSBuildTask directly (source-tree build output) -->
              <PropertyGroup>
                <CycloneDxMSBuildTaskAssembly>{_taskDll}</CycloneDxMSBuildTaskAssembly>
              </PropertyGroup>
              <Import Project="{Path.Combine(_repoRoot, "src", "CycloneDX.MSBuildTask", "build", "CycloneDX.MSBuildTask.props")}" />
              <Import Project="{Path.Combine(_repoRoot, "src", "CycloneDX.MSBuildTask", "build", "CycloneDX.MSBuildTask.targets")}" />
            """;

    private static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "CycloneDX.MSBuildTask.sln")))
            dir = Path.GetDirectoryName(dir);
        return dir ?? throw new InvalidOperationException("Could not find repo root");
    }
}
