namespace Monity.App;

public static class UpdateCheckConfig
{
    public const string GitHubOwner = "rzayevsahil";
    public const string GitHubRepo = "Monity";

    public static string LatestReleaseApiUrl =>
        $"https://api.github.com/repos/{GitHubOwner}/{GitHubRepo}/releases/latest";

    public static string ReleasesPageUrl =>
        $"https://github.com/{GitHubOwner}/{GitHubRepo}/releases";
}
