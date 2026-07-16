using System.CommandLine;
using VBMigrator.Core.Learning;

namespace VBMigrator.CLI.Commands;

public static class KbCommandBuilder
{
    public static Command Build()
    {
        var cmd = new Command("kb", "Knowledge base operations");

        // kb save
        var saveCmd = new Command("save", "Save a human correction");
        var vbOpt   = new Option<string>("--vb")  { Description = "Original VB snippet",   Required = true };
        var csOpt   = new Option<string>("--cs")  { Description = "Corrected C# snippet",  Required = true };
        var tagOpt  = new Option<string>("--tag") { Description = "Pattern tag",            Required = true };
        saveCmd.Add(vbOpt);
        saveCmd.Add(csOpt);
        saveCmd.Add(tagOpt);
        saveCmd.SetAction(async (ParseResult pr) =>
        {
            var vb  = pr.GetValue(vbOpt)!;
            var cs  = pr.GetValue(csOpt)!;
            var tag = pr.GetValue(tagOpt)!;

            var store = new CorrectionStore(GetDbPath());
            await store.InitializeAsync();
            await store.SaveCorrectionAsync(vb, cs, tag);
            Console.WriteLine($"Saved pattern for tag '{tag}'");
        });

        // kb stats
        var statsCmd = new Command("stats", "Show pattern statistics");
        statsCmd.SetAction(async (ParseResult _) =>
        {
            var store = new CorrectionStore(GetDbPath());
            await store.InitializeAsync();
            var stats = await store.GetStatsAsync();
            foreach (var s in stats)
                Console.WriteLine($"{s.Tag}: {s.TotalPatterns} patterns, {s.SuccessRate:P0} success rate");
        });

        cmd.Add(saveCmd);
        cmd.Add(statsCmd);
        return cmd;
    }

    private static string GetDbPath()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "VBMigrator");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "kb.sqlite");
    }
}
