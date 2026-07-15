using Microsoft.Data.Sqlite;
using System.IO;
using System.Reflection;

namespace VBMigrator.Core.Learning;

public class CorrectionStore(string dbPath)
{
    public async Task InitializeAsync()
    {
        var asm  = typeof(CorrectionStore).Assembly;
        var name = asm.GetManifestResourceNames()
                      .First(n => n.EndsWith("001_initial.sql"));
        using var stream = asm.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream);
        var sql = await reader.ReadToEndAsync();

        using var conn = OpenConnection();
        using var cmd  = conn.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task SaveCorrectionAsync(string vbInput, string csCorrection, string tag)
    {
        var (vbTemplate, map) = PatternNormalizer.NormalizeVb(vbInput);
        var (csTemplate, _)   = PatternNormalizer.NormalizeCs(csCorrection, map);

        using var conn = OpenConnection();
        using var cmd  = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO patterns (id, tag, vb_template, cs_template, source, applied, successes, created_at, updated_at)
            VALUES ($id, $tag, $vbt, $cst, 'human', 0, 1, $now, $now)
            ON CONFLICT(tag, vb_template) DO UPDATE
            SET successes = successes + 1, updated_at = $now
            """;
        cmd.Parameters.AddWithValue("$id",  Guid.NewGuid().ToString());
        cmd.Parameters.AddWithValue("$tag", tag);
        cmd.Parameters.AddWithValue("$vbt", vbTemplate);
        cmd.Parameters.AddWithValue("$cst", csTemplate);
        cmd.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("o"));
        await cmd.ExecuteNonQueryAsync();
    }

    public virtual async Task<string?> GetFewShotAsync(string tag)
    {
        using var conn = OpenConnection();
        using var cmd  = conn.CreateCommand();
        cmd.CommandText = """
            SELECT vb_template || ' → ' || cs_template
            FROM patterns
            WHERE tag = $tag AND source = 'human'
            ORDER BY successes DESC
            LIMIT 1
            """;
        cmd.Parameters.AddWithValue("$tag", tag);
        return (await cmd.ExecuteScalarAsync()) as string;
    }

    public virtual Task StoreAsync(string tag, string vbTemplate, string csTemplate)
        => SaveCorrectionAsync(vbTemplate, csTemplate, tag);

    private SqliteConnection OpenConnection()
    {
        var conn = new SqliteConnection($"Data Source={dbPath}");
        conn.Open();
        return conn;
    }
}
