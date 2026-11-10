using System.Data.Common;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using Npgsql;

namespace DouYing.Server;

public sealed class DataStore
{
    private static readonly Regex Placeholder = new(@"\?", RegexOptions.Compiled);
    private readonly string _connectionString;
    private readonly bool _postgres;

    public DataStore(string root)
    {
        var driver = Environment.GetEnvironmentVariable("DB_DRIVER")?.Trim().ToLowerInvariant();
        var databaseUrl = Environment.GetEnvironmentVariable("DATABASE_URL");
        _postgres = driver is "postgres" or "postgresql" || (string.IsNullOrEmpty(driver) && !string.IsNullOrEmpty(databaseUrl));
        Dialect = _postgres ? "postgres" : "sqlite";

        if (_postgres)
        {
            var builder = string.IsNullOrEmpty(databaseUrl)
                ? new NpgsqlConnectionStringBuilder
                {
                    Host = Env("PGHOST", "127.0.0.1"),
                    Port = EnvInt("PGPORT", 5432),
                    Username = Env("PGUSER", "postgres"),
                    Password = Env("PGPASSWORD", ""),
                    Database = Env("PGDATABASE", "douying"),
                    Pooling = true,
                    MaxPoolSize = EnvInt("PGPOOL_SIZE", 10)
                }
                : new NpgsqlConnectionStringBuilder(databaseUrl);
            if (EnvBool("PGSSL") || EnvBool("DATABASE_SSL"))
                builder.SslMode = SslMode.Require;
            _connectionString = builder.ConnectionString;
        }
        else
        {
            var path = Environment.GetEnvironmentVariable("DB_PATH");
            if (string.IsNullOrWhiteSpace(path))
                path = Path.Combine(root, "data", "douying.db");
            if (!Path.IsPathRooted(path))
                path = Path.GetFullPath(Path.Combine(root, path));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            _connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = path,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Cache = SqliteCacheMode.Shared
            }.ConnectionString;
        }
    }

    public string Dialect { get; }
    public object Timestamp(DateTimeOffset value) =>
        _postgres ? value.ToUniversalTime() : value.ToUniversalTime().ToString("O");

    public async Task InitializeAsync()
    {
        await using var connection = await OpenAsync();
        var schema = _postgres ? PostgresSchema : SqliteSchema;
        await using var command = connection.CreateCommand();
        command.CommandText = schema;
        await command.ExecuteNonQueryAsync();
        if (_postgres)
        {
            command.CommandText = "ALTER TABLE videos ADD COLUMN IF NOT EXISTS music_name TEXT NOT NULL DEFAULT ''";
            await command.ExecuteNonQueryAsync();
        }
        else
        {
            command.CommandText = "PRAGMA table_info(videos)";
            var hasMusicName = false;
            await using (var reader = await command.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                    hasMusicName |= reader.GetString(1).Equals("music_name", StringComparison.OrdinalIgnoreCase);
            }
            if (!hasMusicName)
            {
                command.CommandText = "ALTER TABLE videos ADD COLUMN music_name TEXT NOT NULL DEFAULT ''";
                await command.ExecuteNonQueryAsync();
            }
        }
        if (!_postgres)
        {
            command.CommandText = "PRAGMA journal_mode = WAL; PRAGMA foreign_keys = ON; PRAGMA busy_timeout = 5000;";
            await command.ExecuteNonQueryAsync();
        }
    }

    public async Task<List<Dictionary<string, object?>>> QueryAsync(string sql, params object?[] values)
    {
        await using var connection = await OpenAsync();
        await using var command = CreateCommand(connection, null, sql, values);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<Dictionary<string, object?>>();
        while (await reader.ReadAsync())
        {
            var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < reader.FieldCount; i++)
                row[reader.GetName(i)] = reader.IsDBNull(i) ? null : Normalize(reader.GetValue(i));
            rows.Add(row);
        }
        return rows;
    }

    public async Task<Dictionary<string, object?>?> FirstAsync(string sql, params object?[] values) =>
        (await QueryAsync(sql, values)).FirstOrDefault();

    public async Task<int> ExecuteAsync(string sql, params object?[] values)
    {
        await using var connection = await OpenAsync();
        await using var command = CreateCommand(connection, null, sql, values);
        return await command.ExecuteNonQueryAsync();
    }

    public async Task<long> InsertIdAsync(string sql, params object?[] values)
    {
        await using var connection = await OpenAsync();
        await using var command = CreateCommand(connection, null, sql, values);
        var result = await command.ExecuteScalarAsync();
        return Convert.ToInt64(result);
    }

    public async Task<T> TransactionAsync<T>(Func<DbConnection, DbTransaction, Task<T>> action)
    {
        await using var connection = await OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        try
        {
            var result = await action(connection, transaction);
            await transaction.CommitAsync();
            return result;
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public static async Task<int> ExecuteAsync(
        DbConnection connection, DbTransaction transaction, string sql, params object?[] values)
    {
        await using var command = CreateCommand(connection, transaction, sql, values);
        return await command.ExecuteNonQueryAsync();
    }

    public static async Task<long> InsertIdAsync(
        DbConnection connection, DbTransaction transaction, string sql, params object?[] values)
    {
        await using var command = CreateCommand(connection, transaction, sql, values);
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private async Task<DbConnection> OpenAsync()
    {
        DbConnection connection = _postgres
            ? new NpgsqlConnection(_connectionString)
            : new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        if (!_postgres)
        {
            await using var pragma = connection.CreateCommand();
            pragma.CommandText = "PRAGMA foreign_keys = ON; PRAGMA busy_timeout = 5000;";
            await pragma.ExecuteNonQueryAsync();
        }
        return connection;
    }

    private static DbCommand CreateCommand(
        DbConnection connection, DbTransaction? transaction, string sql, object?[] values)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        var index = 0;
        command.CommandText = Placeholder.Replace(sql, _ => $"@p{index++}");
        for (var i = 0; i < values.Length; i++)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = $"p{i}";
            parameter.Value = values[i] ?? DBNull.Value;
            command.Parameters.Add(parameter);
        }
        return command;
    }

    private static object? Normalize(object value) =>
        value is DateTime dateTime ? dateTime.ToUniversalTime().ToString("O") :
        value is DateTimeOffset dateTimeOffset ? dateTimeOffset.ToUniversalTime().ToString("O") :
        value;

    private static string Env(string name, string fallback) =>
        Environment.GetEnvironmentVariable(name) is { Length: > 0 } value ? value : fallback;

    private static int EnvInt(string name, int fallback) =>
        int.TryParse(Environment.GetEnvironmentVariable(name), out var value) ? value : fallback;

    private static bool EnvBool(string name) =>
        Environment.GetEnvironmentVariable(name)?.Trim().ToLowerInvariant() is "1" or "true" or "yes" or "on";

    private const string SqliteSchema = """
        CREATE TABLE IF NOT EXISTS users (
          id INTEGER PRIMARY KEY AUTOINCREMENT, username TEXT NOT NULL UNIQUE,
          password_hash TEXT NOT NULL, nickname TEXT NOT NULL, avatar_url TEXT,
          bio TEXT NOT NULL DEFAULT '', created_at TEXT NOT NULL
        );
        CREATE TABLE IF NOT EXISTS videos (
          id INTEGER PRIMARY KEY AUTOINCREMENT, user_id INTEGER NOT NULL REFERENCES users(id) ON DELETE CASCADE,
          title TEXT NOT NULL, description TEXT NOT NULL DEFAULT '', video_url TEXT NOT NULL, cover_url TEXT,
          music_name TEXT NOT NULL DEFAULT '',
          duration REAL NOT NULL DEFAULT 0, width INTEGER NOT NULL DEFAULT 0, height INTEGER NOT NULL DEFAULT 0,
          view_count INTEGER NOT NULL DEFAULT 0, like_count INTEGER NOT NULL DEFAULT 0,
          comment_count INTEGER NOT NULL DEFAULT 0, share_count INTEGER NOT NULL DEFAULT 0,
          status TEXT NOT NULL DEFAULT 'published', created_at TEXT NOT NULL
        );
        CREATE TABLE IF NOT EXISTS likes (
          user_id INTEGER NOT NULL REFERENCES users(id) ON DELETE CASCADE,
          video_id INTEGER NOT NULL REFERENCES videos(id) ON DELETE CASCADE,
          created_at TEXT NOT NULL, PRIMARY KEY (user_id, video_id)
        );
        CREATE TABLE IF NOT EXISTS comments (
          id INTEGER PRIMARY KEY AUTOINCREMENT, video_id INTEGER NOT NULL REFERENCES videos(id) ON DELETE CASCADE,
          user_id INTEGER NOT NULL REFERENCES users(id) ON DELETE CASCADE, content TEXT NOT NULL,
          like_count INTEGER NOT NULL DEFAULT 0, created_at TEXT NOT NULL
        );
        CREATE TABLE IF NOT EXISTS follows (
          follower_id INTEGER NOT NULL REFERENCES users(id) ON DELETE CASCADE,
          following_id INTEGER NOT NULL REFERENCES users(id) ON DELETE CASCADE,
          created_at TEXT NOT NULL, PRIMARY KEY (follower_id, following_id)
        );
        CREATE TABLE IF NOT EXISTS video_views (
          user_id INTEGER NOT NULL REFERENCES users(id) ON DELETE CASCADE,
          video_id INTEGER NOT NULL REFERENCES videos(id) ON DELETE CASCADE,
          viewed_at TEXT NOT NULL, PRIMARY KEY (user_id, video_id)
        );
        CREATE INDEX IF NOT EXISTS idx_videos_user_created ON videos(user_id, created_at DESC);
        CREATE INDEX IF NOT EXISTS idx_videos_created ON videos(created_at DESC);
        CREATE INDEX IF NOT EXISTS idx_likes_video ON likes(video_id);
        CREATE INDEX IF NOT EXISTS idx_comments_video ON comments(video_id, created_at DESC);
        CREATE INDEX IF NOT EXISTS idx_follows_following ON follows(following_id);
        CREATE INDEX IF NOT EXISTS idx_views_video ON video_views(video_id);
        """;

    private const string PostgresSchema = """
        CREATE TABLE IF NOT EXISTS users (
          id BIGSERIAL PRIMARY KEY, username TEXT NOT NULL UNIQUE,
          password_hash TEXT NOT NULL, nickname TEXT NOT NULL, avatar_url TEXT,
          bio TEXT NOT NULL DEFAULT '', created_at TIMESTAMPTZ NOT NULL
        );
        CREATE TABLE IF NOT EXISTS videos (
          id BIGSERIAL PRIMARY KEY, user_id BIGINT NOT NULL REFERENCES users(id) ON DELETE CASCADE,
          title TEXT NOT NULL, description TEXT NOT NULL DEFAULT '', video_url TEXT NOT NULL, cover_url TEXT,
          music_name TEXT NOT NULL DEFAULT '',
          duration DOUBLE PRECISION NOT NULL DEFAULT 0, width INTEGER NOT NULL DEFAULT 0, height INTEGER NOT NULL DEFAULT 0,
          view_count BIGINT NOT NULL DEFAULT 0, like_count BIGINT NOT NULL DEFAULT 0,
          comment_count BIGINT NOT NULL DEFAULT 0, share_count BIGINT NOT NULL DEFAULT 0,
          status TEXT NOT NULL DEFAULT 'published', created_at TIMESTAMPTZ NOT NULL
        );
        CREATE TABLE IF NOT EXISTS likes (
          user_id BIGINT NOT NULL REFERENCES users(id) ON DELETE CASCADE,
          video_id BIGINT NOT NULL REFERENCES videos(id) ON DELETE CASCADE,
          created_at TIMESTAMPTZ NOT NULL, PRIMARY KEY (user_id, video_id)
        );
        CREATE TABLE IF NOT EXISTS comments (
          id BIGSERIAL PRIMARY KEY, video_id BIGINT NOT NULL REFERENCES videos(id) ON DELETE CASCADE,
          user_id BIGINT NOT NULL REFERENCES users(id) ON DELETE CASCADE, content TEXT NOT NULL,
          like_count BIGINT NOT NULL DEFAULT 0, created_at TIMESTAMPTZ NOT NULL
        );
        CREATE TABLE IF NOT EXISTS follows (
          follower_id BIGINT NOT NULL REFERENCES users(id) ON DELETE CASCADE,
          following_id BIGINT NOT NULL REFERENCES users(id) ON DELETE CASCADE,
          created_at TIMESTAMPTZ NOT NULL, PRIMARY KEY (follower_id, following_id)
        );
        CREATE TABLE IF NOT EXISTS video_views (
          user_id BIGINT NOT NULL REFERENCES users(id) ON DELETE CASCADE,
          video_id BIGINT NOT NULL REFERENCES videos(id) ON DELETE CASCADE,
          viewed_at TIMESTAMPTZ NOT NULL, PRIMARY KEY (user_id, video_id)
        );
        CREATE INDEX IF NOT EXISTS idx_videos_user_created ON videos(user_id, created_at DESC);
        CREATE INDEX IF NOT EXISTS idx_videos_created ON videos(created_at DESC);
        CREATE INDEX IF NOT EXISTS idx_likes_video ON likes(video_id);
        CREATE INDEX IF NOT EXISTS idx_comments_video ON comments(video_id, created_at DESC);
        CREATE INDEX IF NOT EXISTS idx_follows_following ON follows(following_id);
        CREATE INDEX IF NOT EXISTS idx_views_video ON video_views(video_id);
        """;

}
