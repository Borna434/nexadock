using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using NexaDock.Core;
using NexaDock.Core.Updates;

namespace NexaDock.Infrastructure.Updates;

/// <summary>
/// Reads the latest release of the NexaDock repository on GitHub. A release is offered only when it
/// carries the package (<see cref="UpdateRules.PackageName"/>) and its SHA-256: either GitHub's own
/// asset digest or a "<c>.sha256</c>" file next to it. Downloads are checked against that hash.
/// </summary>
public sealed class GitHubUpdateSource : IUpdateSource
{
    private static readonly HttpClient Http = CreateClient();
    private readonly ILogger<GitHubUpdateSource> _logger;

    public GitHubUpdateSource(ILogger<GitHubUpdateSource> logger) => _logger = logger;

    public async Task<UpdateInfo?> GetLatestAsync(CancellationToken cancellationToken)
    {
        var url = $"https://api.github.com/repos/{ProductInfo.UpdateRepository}/releases/latest";
        using var response = await Http.GetAsync(url, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            // 404: no release yet, or the repository is private.
            _logger.LogInformation("Update check: GitHub answered {Status}.", (int)response.StatusCode);
            return null;
        }

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
        return await ParseReleaseAsync(doc.RootElement, cancellationToken).ConfigureAwait(false);
    }

    private async Task<UpdateInfo?> ParseReleaseAsync(JsonElement release, CancellationToken cancellationToken)
    {
        if (release.TryGetProperty("draft", out var draft) && draft.GetBoolean()
            || release.TryGetProperty("prerelease", out var pre) && pre.GetBoolean())
        {
            return null;
        }

        var tag = release.TryGetProperty("tag_name", out var t) ? t.GetString() : null;
        var version = UpdateRules.ParseTag(tag);
        if (version is null || !release.TryGetProperty("assets", out var assets))
        {
            return null;
        }

        string? packageUrl = null, sha = null, shaFileUrl = null;
        foreach (var asset in assets.EnumerateArray())
        {
            var name = asset.TryGetProperty("name", out var n) ? n.GetString() : null;
            var download = asset.TryGetProperty("browser_download_url", out var d) ? d.GetString() : null;
            if (string.Equals(name, UpdateRules.PackageName, StringComparison.OrdinalIgnoreCase))
            {
                packageUrl = download;
                if (asset.TryGetProperty("digest", out var digest) && digest.GetString() is { } value
                    && value.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
                {
                    sha = value["sha256:".Length..];
                }
            }
            else if (string.Equals(name, UpdateRules.PackageName + ".sha256", StringComparison.OrdinalIgnoreCase))
            {
                shaFileUrl = download;
            }
        }

        if (packageUrl is null)
        {
            return null;
        }

        if (sha is null && shaFileUrl is not null)
        {
            var text = await Http.GetStringAsync(shaFileUrl, cancellationToken).ConfigureAwait(false);
            sha = text.Split((char[])[' ', '\t', (char)10, (char)13], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        }

        if (sha is null || sha.Length != 64)
        {
            _logger.LogWarning("Release {Tag} has no SHA-256 for its package; not offering it.", tag);
            return null;
        }

        var page = release.TryGetProperty("html_url", out var h) ? h.GetString() ?? string.Empty : string.Empty;
        return new UpdateInfo(version, tag!, packageUrl, sha.ToLowerInvariant(), page);
    }

    public async Task<string> DownloadAsync(UpdateInfo update, string folder, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(folder);
        var target = Path.Combine(folder, $"NexaDock-{update.Version}.zip");

        using (var response = await Http.GetAsync(update.PackageUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false))
        {
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength ?? 0;
            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await using var file = File.Create(target);
            var buffer = new byte[81920];
            long done = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                done += read;
                if (total > 0)
                {
                    progress?.Report((double)done / total);
                }
            }
        }

        string actual;
        await using (var check = File.OpenRead(target))
        {
            actual = Convert.ToHexString(await SHA256.HashDataAsync(check, cancellationToken).ConfigureAwait(false)).ToLowerInvariant();
        }

        if (!string.Equals(actual, update.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(target);
            throw new InvalidDataException("The downloaded update does not match its checksum.");
        }

        return target;
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue(ProductInfo.Name, ProductInfo.Version));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }
}
