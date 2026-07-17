using Microsoft.Data.Sqlite;

namespace VBMigrator.CLI;

public class TranslationLogStore
{
    private readonly string _dbPath;

    public TranslationLogStore()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "VBMigrator");
        Directory.CreateDirectory(dir);
        _dbPath = Path.Combine(dir, "kb.sqlite");
    }

    public async Task InitializeAsync()
    {
        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS translation_log (
                id              INTEGER PRIMARY KEY AUTOINCREMENT,
                file_path       TEXT    NOT NULL,
                method_name     TEXT    NOT NULL DEFAULT '(file)',
                vb_source       TEXT,
                cs_source       TEXT,
                confidence      REAL    NOT NULL DEFAULT 0,
                route           TEXT    NOT NULL DEFAULT 'SeedRule',
                compiler_passed INTEGER NOT NULL DEFAULT 0,
                tag             TEXT,
                was_corrected   INTEGER NOT NULL DEFAULT 0,
                created_at      TEXT    NOT NULL DEFAULT (datetime('now'))
            )
            """;
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task WriteFileResultAsync(
        string filePath, string vbSource, string csSource,
        double confidence, string route, bool compilerPassed, string? tag)
    {
        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO translation_log
                (file_path, vb_source, cs_source, confidence, route, compiler_passed, tag)
            VALUES (@fp, @vb, @cs, @conf, @route, @cp, @tag)
            """;
        cmd.Parameters.AddWithValue("@fp",    filePath);
        cmd.Parameters.AddWithValue("@vb",    vbSource);
        cmd.Parameters.AddWithValue("@cs",    csSource);
        cmd.Parameters.AddWithValue("@conf",  confidence);
        cmd.Parameters.AddWithValue("@route", route);
        cmd.Parameters.AddWithValue("@cp",    compilerPassed ? 1 : 0);
        cmd.Parameters.AddWithValue("@tag",   tag ?? (object)DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<List<QueueItemRecord>> GetHumanQueueItemsAsync()
    {
        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, file_path, vb_source, cs_source, confidence, tag
            FROM translation_log
            WHERE route = 'HumanQueue' AND was_corrected = 0
            ORDER BY created_at DESC
            """;
        var items = new List<QueueItemRecord>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            items.Add(new QueueItemRecord(
                Id:         reader.GetInt32(0),
                FilePath:   reader.GetString(1),
                VbSource:   reader.IsDBNull(2) ? "" : reader.GetString(2),
                CsSource:   reader.IsDBNull(3) ? "" : reader.GetString(3),
                Confidence: reader.GetDouble(4),
                Tag:        reader.IsDBNull(5) ? null : reader.GetString(5)));
        }
        return items;
    }

    public async Task AcceptItemAsync(int id)
    {
        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE translation_log SET route='Accepted', was_corrected=1 WHERE id=@id";
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task DismissItemAsync(int id)
    {
        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE translation_log SET route='Manual' WHERE id=@id";
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync();
    }
}

public record QueueItemRecord(
    int Id, string FilePath, string VbSource, string CsSource,
    double Confidence, string? Tag);
