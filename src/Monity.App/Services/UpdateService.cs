using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Monity.App.Services;

public sealed class UpdateService
{
    private static readonly HttpClient HttpClient = new()
    {
        DefaultRequestHeaders = { { "User-Agent", "Monity-Updater/1.0" } }
    };

    private static string MonityBasePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Monity");

    private static string UpdaterPath => Path.Combine(MonityBasePath, "Updater.exe");
    private static string UpdateFolderPath => Path.Combine(MonityBasePath, "Update");

    public async Task<UpdateCheckResult?> CheckForUpdateAsync(CancellationToken ct = default)
    {
        var currentStr = AppVersion.Current;
        try
        {
            var response = await HttpClient.GetStringAsync(UpdateCheckConfig.LatestReleaseApiUrl, ct);
            var release = JsonSerializer.Deserialize<GitHubRelease>(response);
            if (release?.TagName == null)
            {
                Serilog.Log.Debug("Update check: no release or tag_name");
                return null;
            }

            var latestVersion = release.TagName.TrimStart('v').Trim();
            if (string.IsNullOrEmpty(latestVersion))
            {
                Serilog.Log.Debug("Update check: tag_name empty after trim");
                return null;
            }

            if (!Version.TryParse(latestVersion, out var latest))
            {
                Serilog.Log.Debug("Update check: could not parse latest version {Tag}", release.TagName);
                return null;
            }
            if (!Version.TryParse(currentStr, out var current))
            {
                Serilog.Log.Debug("Update check: could not parse current version {Current}", currentStr);
                return null;
            }

            if (latest <= current)
            {
                Serilog.Log.Debug("Update check: no update (current={Current}, latest={Latest})", currentStr, latestVersion);
                return null;
            }

            // Uygulama zip'ini seç (win-x64); "Source code (zip)" gibi asset'leri atla
            var zipAsset = release.Assets?.FirstOrDefault(a =>
                a.Name?.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) == true &&
                a.Name.Contains("win-x64", StringComparison.OrdinalIgnoreCase));
            var downloadUrl = zipAsset?.BrowserDownloadUrl;
            if (string.IsNullOrEmpty(downloadUrl))
            {
                Serilog.Log.Debug("Update check: no win-x64 zip asset in release (assets: {Names})",
                    release.Assets == null ? "" : string.Join(", ", release.Assets.Select(x => x.Name ?? "")));
                return null;
            }

            Serilog.Log.Information("Update check: update available (current={Current}, latest={Latest})", currentStr, latestVersion);
            return new UpdateCheckResult(latestVersion, downloadUrl);
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Update check failed");
            return null;
        }
    }

    public void EnsureUpdaterInPlace()
    {
        if (File.Exists(UpdaterPath)) return;

        var appDir = Path.GetDirectoryName(Environment.ProcessPath);
        if (string.IsNullOrEmpty(appDir)) return;

        var localUpdater = Path.Combine(appDir, "Updater.exe");
        if (!File.Exists(localUpdater)) return;

        try
        {
            Directory.CreateDirectory(MonityBasePath);
            File.Copy(localUpdater, UpdaterPath);
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Could not copy Updater to LocalAppData");
        }
    }

    public async Task<bool> DownloadAndApplyUpdateAsync(string version, string downloadUrl, IProgress<int>? progress, CancellationToken ct = default)
    {
        EnsureUpdaterInPlace();
        if (!File.Exists(UpdaterPath))
        {
            Serilog.Log.Error("Updater.exe not found");
            return false;
        }

        var appDir = Path.GetDirectoryName(Environment.ProcessPath);
        if (string.IsNullOrEmpty(appDir))
        {
            Serilog.Log.Error("Could not determine app directory");
            return false;
        }

        var extractPath = Path.Combine(UpdateFolderPath, version);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(extractPath)!);
        }
        catch
        {
            return false;
        }

        try
        {
            using var response = await HttpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength ?? 0L;
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            var zipPath = Path.Combine(Path.GetTempPath(), $"Monity-{version}.zip");
            await using (var file = File.Create(zipPath))
            {
                var buffer = new byte[81920];
                long read = 0;
                int count;
                while ((count = await stream.ReadAsync(buffer, ct)) > 0)
                {
                    await file.WriteAsync(buffer.AsMemory(0, count), ct);
                    read += count;
                    if (total > 0) progress?.Report((int)(100 * read / total));
                }
            }

            if (Directory.Exists(extractPath))
            {
                foreach (var f in Directory.GetFiles(extractPath))
                    try { File.Delete(f); } catch { }
                foreach (var d in Directory.GetDirectories(extractPath))
                    try { Directory.Delete(d, true); } catch { }
            }
            else
                Directory.CreateDirectory(extractPath);

            ZipFile.ExtractToDirectory(zipPath, extractPath, true);
            try { File.Delete(zipPath); } catch { }

            var updaterArgs = $"\"{Path.Combine(extractPath)}\" \"{appDir}\"";
            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = UpdaterPath,
                Arguments = updaterArgs,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            System.Diagnostics.Process.Start(startInfo);
            return true;
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Download or apply update failed");
            return false;
        }
    }

    public record UpdateCheckResult(string Version, string DownloadUrl);

    private class GitHubRelease
    {
        [JsonPropertyName("tag_name")]
        public string? TagName { get; set; }

        [JsonPropertyName("assets")]
        public List<GitHubAsset>? Assets { get; set; }
    }

    private class GitHubAsset
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("browser_download_url")]
        public string? BrowserDownloadUrl { get; set; }
    }
}
