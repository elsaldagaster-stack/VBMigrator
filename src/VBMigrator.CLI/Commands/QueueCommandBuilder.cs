using System.CommandLine;
using System.Text.Json;

namespace VBMigrator.CLI.Commands;

public static class QueueCommandBuilder
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static Command Build()
    {
        var cmd = new Command("queue", "Review queue operations");

        var listCmd = new Command("list", "List HUMAN_QUEUE items as JSON");
        listCmd.SetAction(async (ParseResult _) =>
        {
            var store = new TranslationLogStore();
            await store.InitializeAsync();
            var items = await store.GetHumanQueueItemsAsync();
            Console.WriteLine(JsonSerializer.Serialize(items, JsonOpts));
        });

        var acceptCmd = new Command("accept", "Mark item as accepted");
        var acceptId  = new Option<int>("--id") { Required = true };
        acceptCmd.Add(acceptId);
        acceptCmd.SetAction(async (ParseResult pr) =>
        {
            var store = new TranslationLogStore();
            await store.InitializeAsync();
            await store.AcceptItemAsync(pr.GetValue(acceptId));
        });

        var dismissCmd = new Command("dismiss", "Mark item for manual review");
        var dismissId  = new Option<int>("--id") { Required = true };
        dismissCmd.Add(dismissId);
        dismissCmd.SetAction(async (ParseResult pr) =>
        {
            var store = new TranslationLogStore();
            await store.InitializeAsync();
            await store.DismissItemAsync(pr.GetValue(dismissId));
        });

        cmd.Add(listCmd);
        cmd.Add(acceptCmd);
        cmd.Add(dismissCmd);
        return cmd;
    }
}
