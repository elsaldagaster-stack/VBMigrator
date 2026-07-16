using System.CommandLine;
using System.Text.Json;
using System.Text.Json.Serialization;
using VBMigrator.Core.Models;
using VBMigrator.Core.Translator;

namespace VBMigrator.CLI.Commands;

public static class ConvertCommandBuilder
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public static Command Build()
    {
        var cmd = new Command("convert", "Convert VB.NET files to C#");

        var fileOpt     = new Option<FileInfo?>("--file")       { Description = "Single .vb file to convert" };
        var solutionOpt = new Option<FileInfo?>("--solution")  { Description = "Solution .sln to convert" };
        var outputOpt   = new Option<DirectoryInfo?>("--output") { Description = "Output directory" };
        var jsonOpt     = new Option<bool>("--json-output")    { Description = "Emit JSON to stdout (--file mode only)" };
        var dryRunOpt   = new Option<bool>("--dry-run")        { Description = "Report only, do not write files" };
        var reportOpt   = new Option<FileInfo?>("--report")    { Description = "Report HTML output path" };

        cmd.Add(fileOpt);
        cmd.Add(solutionOpt);
        cmd.Add(outputOpt);
        cmd.Add(jsonOpt);
        cmd.Add(dryRunOpt);
        cmd.Add(reportOpt);

        cmd.SetAction(async (ParseResult pr) =>
        {
            var file       = pr.GetValue(fileOpt);
            var solution   = pr.GetValue(solutionOpt);
            var output     = pr.GetValue(outputOpt);
            var jsonOutput = pr.GetValue(jsonOpt);
            var dryRun     = pr.GetValue(dryRunOpt);

            if (file != null)
                await ConvertFile(file, jsonOutput);
            else if (solution != null)
                await ConvertSolution(solution, output, dryRun);
        });

        return cmd;
    }

    private static async Task ConvertFile(FileInfo file, bool jsonOutput)
    {
        var vbSource = await File.ReadAllTextAsync(file.FullName);
        var pipeline = PipelineFactory.Build();
        var results  = await pipeline.ProcessFileAsync(vbSource, file.FullName);

        if (results.Count == 0) return;

        var csSource       = string.Join("\n\n", results.Select(r => r.CsSource));
        var confidence     = results.Min(r => r.Confidence);
        var worstRoute     = results.Any(r => r.Route == TranslationRoute.HumanQueue)
                                ? TranslationRoute.HumanQueue
                                : results.Any(r => r.Route == TranslationRoute.Llm)
                                    ? TranslationRoute.Llm
                                    : results.First().Route;
        var compilerPassed = results.All(r => r.CompilerPassed);
        var compilerErrors = results.SelectMany(r => r.CompilerErrors).Distinct().ToList();

        if (jsonOutput)
        {
            var dto = new TranslationResultDto(
                filePath:         file.FullName,
                csSource:         csSource,
                confidence:       confidence,
                route:            worstRoute.ToString(),
                compilerPassed:   compilerPassed,
                compilerErrors:   compilerErrors,
                patternTag:       results.FirstOrDefault(r => r.PatternTag != null)?.PatternTag,
                llmFailureReason: results.FirstOrDefault(r => r.LlmFailureReason != null)?.LlmFailureReason?.ToString());

            Console.WriteLine(JsonSerializer.Serialize(dto, JsonOpts));
        }
        else
        {
            var outPath = Path.ChangeExtension(file.FullName, ".cs");
            await File.WriteAllTextAsync(outPath, csSource);
        }
    }

    private static async Task ConvertSolution(FileInfo sln, DirectoryInfo? output, bool dryRun)
    {
        var baseDir  = sln.Directory!;
        var vbFiles  = baseDir.GetFiles("*.vb", SearchOption.AllDirectories);
        var pipeline = PipelineFactory.Build();

        if (output != null && !output.Exists)
            output.Create();

        foreach (var vbFile in vbFiles)
        {
            var vbSource = await File.ReadAllTextAsync(vbFile.FullName);
            var results  = await pipeline.ProcessFileAsync(vbSource, vbFile.FullName);
            var status   = results.All(r => r.Route != TranslationRoute.HumanQueue) ? "OK" : "HUMAN_QUEUE";

            Console.Error.WriteLine($"FILE: {vbFile.Name} {status}");

            if (!dryRun)
            {
                var outPath = output != null
                    ? Path.Combine(output.FullName, Path.ChangeExtension(vbFile.Name, ".cs"))
                    : Path.ChangeExtension(vbFile.FullName, ".cs");

                var csSource = string.Join("\n\n", results.Select(r => r.CsSource));
                await File.WriteAllTextAsync(outPath, csSource);
            }
        }
    }
}

public record TranslationResultDto(
    string filePath,
    string csSource,
    double confidence,
    string route,
    bool compilerPassed,
    List<string> compilerErrors,
    string? patternTag,
    string? llmFailureReason);
