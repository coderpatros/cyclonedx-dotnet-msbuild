using System.Text.RegularExpressions;
using VerifyXunit;

namespace CycloneDX.MSBuildTask.IntegrationTests;

internal static class VerifyExtensions
{
    private static readonly Regex SerialNumberRegex = new(
        @"""serialNumber""\s*:\s*""[^""]+""",
        RegexOptions.Compiled);

    private static readonly Regex TimestampRegex = new(
        @"""timestamp""\s*:\s*""[^""]+""",
        RegexOptions.Compiled);

    private static readonly Regex TempGuidRegex = new(
        @"cyclonedx-integ-[a-f0-9]{32}",
        RegexOptions.Compiled);

    // Hash content varies across environments (NuGet cache, ZIP impl differences)
    private static readonly Regex HashContentRegex = new(
        @"(""alg"":\s*""[^""]+"",\s*""content"":\s*"")[a-f0-9]+("")",
        RegexOptions.Compiled);

    /// <summary>
    /// Scrubs all environment-dependent fields including hash content.
    /// Use for tests with synthetic test packages whose hashes vary across environments.
    /// </summary>
    public static SettingsTask ScrubBomDynamicFields(this SettingsTask task)
    {
        return task.ScrubBomMetadataOnly().AddScrubber(sb =>
        {
            var text = sb.ToString();
            text = HashContentRegex.Replace(text, "${1}SCRUBBED${2}");
            sb.Clear();
            sb.Append(text);
        });
    }

    /// <summary>
    /// Scrubs serialNumber, timestamp, temp paths, and metadata component file hashes
    /// (build outputs like .dll/.pdb that aren't deterministic) — keeps NuGet package hashes intact.
    /// Use for tests with real NuGet packages whose hashes are deterministic.
    /// </summary>
    public static SettingsTask ScrubBomMetadataOnly(this SettingsTask task)
    {
        return task.AddScrubber(sb =>
        {
            var text = sb.ToString();
            text = SerialNumberRegex.Replace(text, @"""serialNumber"": ""SCRUBBED""");
            text = TimestampRegex.Replace(text, @"""timestamp"": ""SCRUBBED""");
            text = TempGuidRegex.Replace(text, "cyclonedx-integ-GUID");

            // Scrub hashes only within the metadata section (build output files are not deterministic).
            // The top-level "components" array starts at 2-space indent after the metadata section.
            var topLevelComponents = text.IndexOf("\n  \"components\":", StringComparison.Ordinal);
            if (topLevelComponents > 0)
            {
                var metadataSection = text[..topLevelComponents];
                metadataSection = HashContentRegex.Replace(metadataSection, "${1}SCRUBBED${2}");
                text = metadataSection + text[topLevelComponents..];
            }

            sb.Clear();
            sb.Append(text);
        });
    }
}
