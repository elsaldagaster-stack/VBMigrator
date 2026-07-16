using System.CommandLine;

namespace VBMigrator.CLI.Commands;

public static class ReportCommandBuilder
{
    public static Command Build() => new Command("report", "Generate HTML migration report");
}
