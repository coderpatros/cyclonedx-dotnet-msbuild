using Xunit.Abstractions;

namespace CycloneDX.MSBuildTask.IntegrationTests;

[Collection("NuGetRegistry")]
public class SbomIntegrationTests(NuGetRegistryFixture registry, ITestOutputHelper output) : IAsyncLifetime
{
    private string _tempDir = null!;
    private TestProjectHelper _helper = null!;

    public async Task InitializeAsync()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"cyclonedx-integ-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        var taskPackagePath = await TestProjectHelper.BuildTaskPackageAsync(output);

        foreach (var package in TestPackages.All)
            await registry.PushTestPackageAsync(package);

        _helper = new TestProjectHelper(registry, output, _tempDir, taskPackagePath);
    }

    public Task DisposeAsync()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Build_WithSinglePackage()
    {
        var bomJson = await _helper.BuildAndGetBomJson("SinglePkg",
            new PackageReferenceBuilder()
                .Add("TestLib.Core", "1.0.0")
                .Build());

        await Verifier.Verify(bomJson)
            .ScrubBomDynamicFields();
    }

    [Fact]
    public async Task Build_WithTransitiveDependency()
    {
        var bomJson = await _helper.BuildAndGetBomJson("TransitiveDep",
            new PackageReferenceBuilder()
                .Add("TestLib.Logging", "2.1.0")
                .Build());

        await Verifier.Verify(bomJson)
            .ScrubBomDynamicFields();
    }

    [Fact]
    public async Task Build_WithPrereleasePackage()
    {
        var bomJson = await _helper.BuildAndGetBomJson("PrereleasePkg",
            new PackageReferenceBuilder()
                .Add("TestLib.Utils", "3.0.0-beta.1")
                .Build());

        await Verifier.Verify(bomJson)
            .ScrubBomDynamicFields();
    }

    [Fact]
    public async Task Build_WithMultiplePackages()
    {
        var bomJson = await _helper.BuildAndGetBomJson("MultiPkg",
            new PackageReferenceBuilder()
                .Add("TestLib.Core", "1.0.0")
                .Add("TestLib.Utils", "3.0.0-beta.1")
                .Build());

        await Verifier.Verify(bomJson)
            .ScrubBomDynamicFields();
    }

    [Fact]
    public async Task Build_GeneratesBothJsonAndXml()
    {
        var projectDir = _helper.CreateTestProject("BothFormats",
            new PackageReferenceBuilder()
                .Add("TestLib.Core", "1.0.0")
                .Build());

        await _helper.RunDotnetAsync("restore", projectDir);
        await _helper.RunDotnetAsync("build", projectDir, "--no-restore");

        var outputDir = Path.Combine(projectDir, "bin", "Debug", "net10.0");
        Assert.True(File.Exists(Path.Combine(outputDir, "bom.json")), "bom.json missing");
        Assert.True(File.Exists(Path.Combine(outputDir, "bom.xml")), "bom.xml missing");
    }

    [Fact]
    public async Task Build_WithProjectReference()
    {
        var rootDir = _helper.CreateMultiProjectSetup(
            rootName: "WebApp",
            rootPackageReferences: "",
            libraries: [
                ("Infrastructure", new PackageReferenceBuilder()
                    .Add("TestLib.Core", "1.0.0")
                    .Add("TestLib.Logging", "2.1.0")
                    .Build()),
            ]);

        await _helper.RunDotnetAsync("restore", rootDir);
        await _helper.RunDotnetAsync("build", rootDir, "--no-restore");

        var bomJson = await File.ReadAllTextAsync(
            Path.Combine(rootDir, "bin", "Debug", "net10.0", "bom.json"));

        await Verifier.Verify(bomJson)
            .ScrubBomDynamicFields();
    }

    [Fact]
    public async Task Build_WithDiamondDependency()
    {
        // ProjA → TestLib.Data (depends on TestLib.Common >= 1.0.0), ProjectReference ProjB
        // ProjB → TestLib.Messaging (depends on TestLib.Common >= 2.0.0)
        // NuGet should resolve TestLib.Common to 2.0.0 (highest requested minimum)
        var rootDir = _helper.CreateMultiProjectSetup(
            rootName: "AppHost",
            rootPackageReferences: new PackageReferenceBuilder()
                .Add("TestLib.Data", "1.0.0")
                .Build(),
            libraries: [
                ("Services", new PackageReferenceBuilder()
                    .Add("TestLib.Messaging", "1.0.0")
                    .Build()),
            ]);

        await _helper.RunDotnetAsync("restore", rootDir);
        await _helper.RunDotnetAsync("build", rootDir, "--no-restore");

        var bomJson = await File.ReadAllTextAsync(
            Path.Combine(rootDir, "bin", "Debug", "net10.0", "bom.json"));

        await Verifier.Verify(bomJson)
            .ScrubBomDynamicFields();
    }

    [Fact]
    public async Task Build_WithRealPackage_VerifiesHashes()
    {
        // Uses a real NuGet package from nuget.org to verify hash generation is stable.
        // Hashes are NOT scrubbed so any change in hash computation will be caught.
        var bomJson = await _helper.BuildAndGetBomJson("RealPkg",
            new PackageReferenceBuilder()
                .Add("CycloneDX.Core", "11.0.0")
                .Build());

        await Verifier.Verify(bomJson)
            .ScrubBomMetadataOnly();
    }
}
