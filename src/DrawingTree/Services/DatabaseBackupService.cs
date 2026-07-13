/// <summary>
/// DatabaseBackupService.cs
/// Creates a timestamped backup of the dev database before OE sync writes begin, using SQLite's
/// online backup API (safe even while other connections/journals are active).
/// </summary>

using System.IO;
using DrawingTree.Data;
using DrawingTree.Logging;
using Microsoft.Data.Sqlite;

namespace DrawingTree.Services;

public static class DatabaseBackupService
{
    /// <summary>
    /// Copies the dev database to "record.db.backup-yyyyMMdd-HHmmss" next to it.
    /// </summary>
    /// <returns>The full path of the created backup file.</returns>
    public static string BackupDevDatabase()
    {
        var sourcePath = DatabaseConnectionFactory.GetDevDbFullPath();
        var backupPath = Path.Combine(
            Path.GetDirectoryName(sourcePath)!,
            $"record.db.backup-{DateTime.Now:yyyyMMdd-HHmmss}");

        using var source = new SqliteConnection($"Data Source={sourcePath}");
        using var destination = new SqliteConnection($"Data Source={backupPath}");
        source.Open();
        destination.Open();
        source.BackupDatabase(destination);

        Logger.Instance.Info($"DatabaseBackupService.BackupDevDatabase: created {backupPath}");
        return backupPath;
    }
}
