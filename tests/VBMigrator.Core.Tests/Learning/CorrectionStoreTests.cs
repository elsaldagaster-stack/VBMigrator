using Microsoft.Data.Sqlite;
using VBMigrator.Core.Learning;
using Xunit;

namespace VBMigrator.Core.Tests.Learning;

public class CorrectionStoreTests : IDisposable
{
    private readonly string _dbPath = Path.GetTempFileName();
    private readonly CorrectionStore _store;

    public CorrectionStoreTests()
    {
        _store = new CorrectionStore(_dbPath);
        _store.InitializeAsync().Wait();
    }

    [Fact]
    public async Task SaveCorrection_ThenGetFewShot_ReturnsSavedPattern()
    {
        await _store.SaveCorrectionAsync(
            vbInput: "If x Is Nothing Then",
            csCorrection: "if (x is null) {",
            tag: "is_nothing");

        var fewShot = await _store.GetFewShotAsync("is_nothing");

        Assert.NotNull(fewShot);
        Assert.Contains("__", fewShot); // normalized template
    }

    [Fact]
    public async Task SaveCorrection_DuplicateTemplate_UpdatesSuccesses()
    {
        await _store.SaveCorrectionAsync("If x Is Nothing Then", "if (x is null) {", "is_nothing");
        await _store.SaveCorrectionAsync("If x Is Nothing Then", "if (x is null) {", "is_nothing");

        // Should have 1 pattern with successes > 0, not 2 patterns
        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM patterns WHERE tag='is_nothing'";
        var count = (long)(await cmd.ExecuteScalarAsync())!;
        Assert.Equal(1, count);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        File.Delete(_dbPath);
    }
}
