/// <summary>
/// DatabaseConnectionFactory.cs
/// Provides SQLite connection strings for dev and production environments.
/// </summary>
/// <remarks>
/// Usage:
/// - Dev:  data/record.db (relative to working directory)
/// - Prod: \\rtdnas2\OE\record.db (network mount)
/// </remarks>

using System.IO;
using Microsoft.Data.Sqlite;

namespace DrawingTree.Data;

public static class DatabaseConnectionFactory
{
    private static readonly string DevDbPath  = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\..\..\data\record.db");
    private static readonly string ProdDbPath = @"\\rtdnas2\OE\record.db";

    /// <summary>
    /// Opens and returns a SQLite connection using the dev database path.
    /// Caller is responsible for disposing the connection.
    /// </summary>
    public static SqliteConnection OpenDevConnection()
        => OpenConnection(DevDbPath);

    /// <summary>
    /// Opens and returns a SQLite connection using the production database path.
    /// Caller is responsible for disposing the connection.
    /// </summary>
    public static SqliteConnection OpenProdConnection()
        => OpenConnection(ProdDbPath);

    private static SqliteConnection OpenConnection(string path)
    {
        string fullPath = Path.GetFullPath(path);
        var conn = new SqliteConnection($"Data Source={fullPath}");
        conn.Open();
        return conn;
    }
}
