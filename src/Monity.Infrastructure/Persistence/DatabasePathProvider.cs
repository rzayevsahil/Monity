namespace Monity.Infrastructure.Persistence;

/// <summary>
/// Default path: %LocalAppData%\Monity\monity.db
/// </summary>
public sealed class DatabasePathProvider : IDatabasePathProvider
{
    private const string DbFileName = "monity.db";

    public string GetDatabasePath()
    {
        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Monity");
        Directory.CreateDirectory(folder);
        return Path.Combine(folder, DbFileName);
    }
}
