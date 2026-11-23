namespace NexaDock.Core.Updates;

/// <summary>A newer NexaDock published as a GitHub release.</summary>
/// <param name="Sha256">Expected hash of the package (hex); updates without one are refused.</param>
public sealed record UpdateInfo(Version Version, string Tag, string PackageUrl, string Sha256, string PageUrl);

public interface IUpdateSource
{
    /// <summary>The latest release, or null when there is none (or it can't be reached).</summary>
    Task<UpdateInfo?> GetLatestAsync(CancellationToken cancellationToken);

    /// <summary>Downloads the package and checks its hash; returns the local file.</summary>
    Task<string> DownloadAsync(UpdateInfo update, string folder, IProgress<double>? progress, CancellationToken cancellationToken);
}

public static class UpdateRules
{
    /// <summary>Name of the release asset that holds the app (a zip of the install folder).</summary>
    public const string PackageName = "NexaDock-win-x64.zip";

    /// <summary>"v0.4.0", "0.4.0" or "v0.4" become a version; anything else is not a release.</summary>
    public static Version? ParseTag(string? tag)
    {
        var text = (tag ?? string.Empty).Trim().TrimStart('v', 'V');
        var dash = text.IndexOf('-');
        if (dash >= 0)
        {
            return null; // pre-releases ("0.5.0-beta") are not offered
        }

        return Version.TryParse(text, out var v) ? Normalize(v) : null;
    }

    public static Version Current => ParseTag(ProductInfo.Version) ?? new Version(0, 0, 0);

    public static bool IsNewer(Version candidate, Version current) => Normalize(candidate) > Normalize(current);

    private static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(0, v.Build));
}
