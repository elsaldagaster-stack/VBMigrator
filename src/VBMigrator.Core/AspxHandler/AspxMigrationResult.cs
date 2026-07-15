namespace VBMigrator.Core.AspxHandler;

public record AspxMigrationResult(
    string RewrittenAspx,
    IReadOnlyList<string> EventSubscriptions);
