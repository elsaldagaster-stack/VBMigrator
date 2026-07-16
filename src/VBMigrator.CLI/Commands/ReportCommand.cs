using System.CommandLine;
using Microsoft.Data.Sqlite;

namespace VBMigrator.CLI.Commands;

public static class ReportCommandBuilder
{
    public static Command Build()
    {
        var cmd    = new Command("report", "Generate HTML migration report");
        var outOpt = new Option<FileInfo>("--output") { Description = "Output HTML path" };
        outOpt.DefaultValueFactory = _ => new FileInfo("report.html");
        cmd.Add(outOpt);

        cmd.SetAction(async (ParseResult pr) =>
        {
            var output = pr.GetValue(outOpt)!;

            var dbPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "VBMigrator", "kb.sqlite");

            if (!File.Exists(dbPath))
            {
                Console.Error.WriteLine("No knowledge base found.");
                return;
            }

            using var conn = new SqliteConnection($"Data Source={dbPath}");
            await conn.OpenAsync();
            using var cmd2 = conn.CreateCommand();
            cmd2.CommandText = """
                SELECT file_path, method_name, confidence, compiler_passed, was_corrected
                FROM translation_log ORDER BY created_at DESC LIMIT 500
                """;

            var rows = new System.Text.StringBuilder();
            using var reader = await cmd2.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                rows.AppendLine($"<tr><td>{reader.GetString(0)}</td>" +
                    $"<td>{reader.GetString(1)}</td>" +
                    $"<td>{reader.GetDouble(2):F2}</td>" +
                    $"<td>{(reader.GetBoolean(3) ? "✓" : "✗")}</td>" +
                    $"<td>{(reader.GetBoolean(4) ? "yes" : "no")}</td></tr>");
            }

            var html = $"""
                <!DOCTYPE html><html><head><title>VBMigrator Report</title></head>
                <body><h1>Migration Log</h1>
                <table border="1">
                <tr><th>File</th><th>Method</th><th>Confidence</th><th>Compiled</th><th>Corrected</th></tr>
                {rows}
                </table></body></html>
                """;

            await File.WriteAllTextAsync(output.FullName, html);
            Console.WriteLine($"Report written to {output.FullName}");
        });

        return cmd;
    }
}
