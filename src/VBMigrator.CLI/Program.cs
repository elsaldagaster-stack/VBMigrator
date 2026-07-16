using System.CommandLine;
using VBMigrator.CLI.Commands;

var rootCommand = new RootCommand("VBMigrator — VB.NET to C# migration tool");

rootCommand.Add(ConvertCommandBuilder.Build());
rootCommand.Add(KbCommandBuilder.Build());
rootCommand.Add(ReportCommandBuilder.Build());

return await rootCommand.Parse(args).InvokeAsync();
