using System.CommandLine;

namespace VBMigrator.CLI.Commands;

public static class KbCommandBuilder
{
    public static Command Build() => new Command("kb", "Knowledge base operations");
}
