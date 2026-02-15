namespace Monity.Infrastructure.Persistence;

/// <summary>
/// Provides the path to the SQLite database file.
/// </summary>
public interface IDatabasePathProvider
{
    string GetDatabasePath();
}
