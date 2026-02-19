using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Xunit;

namespace CycloneDX.MSBuildTask.IntegrationTests;

/// <summary>
/// Shared fixture that manages a BaGetter NuGet registry container and provides
/// helpers to create and push test packages.
/// </summary>
public class NuGetRegistryFixture : IAsyncLifetime
{
    private IContainer _container = null!;

    public string NuGetSourceUrl => $"http://{_container.Hostname}:{_container.GetMappedPublicPort(8080)}/v3/index.json";
    public string PushUrl => $"http://{_container.Hostname}:{_container.GetMappedPublicPort(8080)}/api/v2/package";
    public const string ApiKey = "TestApiKey";

    public async Task InitializeAsync()
    {
        _container = new ContainerBuilder("bagetter/bagetter:latest")
            .WithPortBinding(8080, true)
            .WithEnvironment("ApiKey", ApiKey)
            .WithEnvironment("Storage__Type", "FileSystem")
            .WithEnvironment("Storage__Path", "/data")
            .WithEnvironment("Database__Type", "Sqlite")
            .WithEnvironment("Database__ConnectionString", "Data Source=/data/bagetter.db")
            .WithEnvironment("Search__Type", "Database")
            .WithWaitStrategy(Wait.ForUnixContainer()
                .UntilHttpRequestIsSucceeded(r => r.ForPort(8080).ForPath("/v3/index.json")))
            .Build();

        await _container.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await _container.DisposeAsync();
    }

    /// <summary>
    /// Creates a minimal .nupkg in memory and pushes it to the BaGetter registry.
    /// </summary>
    public async Task PushTestPackageAsync(TestPackage package)
    {
        var nupkgBytes = CreateNupkg(
            package.Id, package.Version, package.LicenseExpression,
            package.Description, package.Authors,
            package.Dependencies.Length > 0 ? package.Dependencies : null);
        await PushNupkgAsync(nupkgBytes);
    }

    private async Task PushNupkgAsync(byte[] nupkgBytes)
    {
        using var client = new HttpClient();
        using var content = new ByteArrayContent(nupkgBytes);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");

        using var request = new HttpRequestMessage(HttpMethod.Put, PushUrl);
        request.Headers.Add("X-NuGet-ApiKey", ApiKey);
        request.Content = content;

        var response = await client.SendAsync(request);

        // 409 Conflict = package already exists (idempotent push)
        if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
            return;

        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Creates a minimal .nupkg (ZIP with .nuspec and a placeholder lib dll).
    /// </summary>
    private static byte[] CreateNupkg(
        string packageId,
        string version,
        string? licenseExpression,
        string? description,
        string? authors,
        (string id, string versionRange)[]? dependencies)
    {
        var nuspec = BuildNuspec(packageId, version, licenseExpression, description, authors, dependencies);

        using var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            // Add .nuspec
            var nuspecEntry = archive.CreateEntry($"{packageId}.nuspec");
            using (var writer = new StreamWriter(nuspecEntry.Open(), Encoding.UTF8))
            {
                writer.Write(nuspec);
            }

            // Add a placeholder DLL so the package has runtime content
            var dllEntry = archive.CreateEntry($"lib/net8.0/{packageId}.dll");
            using (var dllStream = dllEntry.Open())
            {
                var placeholder = Encoding.UTF8.GetBytes($"placeholder-{packageId}-{version}");
                dllStream.Write(placeholder, 0, placeholder.Length);
            }

            // Also add for net10.0 compatibility
            var dll10Entry = archive.CreateEntry($"lib/net10.0/{packageId}.dll");
            using (var dll10Stream = dll10Entry.Open())
            {
                var placeholder = Encoding.UTF8.GetBytes($"placeholder-{packageId}-{version}");
                dll10Stream.Write(placeholder, 0, placeholder.Length);
            }
        }

        return ms.ToArray();
    }

    private static string BuildNuspec(
        string packageId,
        string version,
        string? licenseExpression,
        string? description,
        string? authors,
        (string id, string versionRange)[]? dependencies)
    {
        XNamespace ns = "http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd";

        var metadata = new XElement(ns + "metadata",
            new XElement(ns + "id", packageId),
            new XElement(ns + "version", version),
            new XElement(ns + "description", description ?? $"Test package {packageId}"),
            new XElement(ns + "authors", authors ?? "TestAuthor"));

        if (!string.IsNullOrEmpty(licenseExpression))
        {
            metadata.Add(new XElement(ns + "license",
                new XAttribute("type", "expression"),
                licenseExpression));
        }

        if (dependencies is { Length: > 0 })
        {
            var depGroup = new XElement(ns + "group",
                new XAttribute("targetFramework", ".NETCoreApp3.1"));
            foreach (var (id, versionRange) in dependencies)
            {
                depGroup.Add(new XElement(ns + "dependency",
                    new XAttribute("id", id),
                    new XAttribute("version", versionRange)));
            }
            metadata.Add(new XElement(ns + "dependencies", depGroup));
        }

        var doc = new XDocument(
            new XElement(ns + "package", metadata));

        return doc.ToString();
    }
}

[CollectionDefinition("NuGetRegistry")]
public class NuGetRegistryCollection : ICollectionFixture<NuGetRegistryFixture>
{
}
