namespace CycloneDX.MSBuildTask.IntegrationTests;

/// <summary>
/// Describes a test NuGet package to be pushed to the BaGetter registry.
/// </summary>
public record TestPackage
{
    public required string Id { get; init; }
    public required string Version { get; init; }
    public string? LicenseExpression { get; init; }
    public string Description { get; init; } = "Test package";
    public string Authors { get; init; } = "IntegrationTest";
    public (string Id, string VersionRange)[] Dependencies { get; init; } = [];
}

/// <summary>
/// Fluent builder for <see cref="TestPackage"/>.
/// </summary>
public class TestPackageBuilder
{
    private readonly string _id;
    private readonly string _version;
    private string? _license;
    private string _description = "Test package";
    private string _authors = "IntegrationTest";
    private readonly List<(string Id, string VersionRange)> _dependencies = [];

    public TestPackageBuilder(string id, string version)
    {
        _id = id;
        _version = version;
    }

    public TestPackageBuilder WithLicense(string expression)
    {
        _license = expression;
        return this;
    }

    public TestPackageBuilder WithDescription(string description)
    {
        _description = description;
        return this;
    }

    public TestPackageBuilder WithAuthors(string authors)
    {
        _authors = authors;
        return this;
    }

    public TestPackageBuilder DependsOn(string id, string versionRange = "[1.0.0, )")
    {
        _dependencies.Add((id, versionRange));
        return this;
    }

    public TestPackage Build() => new()
    {
        Id = _id,
        Version = _version,
        LicenseExpression = _license,
        Description = _description,
        Authors = _authors,
        Dependencies = [.. _dependencies],
    };
}

/// <summary>
/// Fluent builder that produces an MSBuild <c>&lt;ItemGroup&gt;</c> with
/// <c>&lt;PackageReference&gt;</c> elements for use in generated test .csproj files.
/// </summary>
public class PackageReferenceBuilder
{
    private readonly List<(string Id, string Version)> _refs = [];

    public PackageReferenceBuilder Add(string id, string version)
    {
        _refs.Add((id, version));
        return this;
    }

    public string Build()
    {
        var lines = _refs
            .Select(r => $"""      <PackageReference Include="{r.Id}" Version="{r.Version}" />""");
        return $"""
            <ItemGroup>
            {string.Join(Environment.NewLine, lines)}
            </ItemGroup>
            """;
    }
}

/// <summary>
/// Central catalog of all test packages used by integration tests.
/// Add new packages here — they are automatically pushed to the
/// BaGetter registry before tests run.
/// </summary>
public static class TestPackages
{
    public static readonly TestPackage Core = new TestPackageBuilder("TestLib.Core", "1.0.0")
        .WithLicense("MIT")
        .WithDescription("A core test library")
        .Build();

    public static readonly TestPackage Logging = new TestPackageBuilder("TestLib.Logging", "2.1.0")
        .WithLicense("Apache-2.0")
        .WithDescription("A logging test library")
        .DependsOn("TestLib.Core")
        .Build();

    public static readonly TestPackage Utils = new TestPackageBuilder("TestLib.Utils", "3.0.0-beta.1")
        .WithDescription("A prerelease utility library")
        .Build();

    public static readonly TestPackage CommonV1 = new TestPackageBuilder("TestLib.Common", "1.0.0")
        .WithLicense("MIT")
        .WithDescription("A shared common library v1")
        .Build();

    public static readonly TestPackage CommonV2 = new TestPackageBuilder("TestLib.Common", "2.0.0")
        .WithLicense("MIT")
        .WithDescription("A shared common library v2")
        .Build();

    public static readonly TestPackage Data = new TestPackageBuilder("TestLib.Data", "1.0.0")
        .WithLicense("MIT")
        .WithDescription("A data access library")
        .DependsOn("TestLib.Common", "[1.0.0, )")
        .Build();

    public static readonly TestPackage Messaging = new TestPackageBuilder("TestLib.Messaging", "1.0.0")
        .WithLicense("Apache-2.0")
        .WithDescription("A messaging library")
        .DependsOn("TestLib.Common", "[2.0.0, )")
        .Build();

    /// <summary>All packages to push to the registry before tests run.</summary>
    public static IReadOnlyList<TestPackage> All =>
        [Core, Logging, Utils, CommonV1, CommonV2, Data, Messaging];
}
