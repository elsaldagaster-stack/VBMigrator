namespace VBMigrator.Core.Validator;

public record ValidationResult(bool Success, IReadOnlyList<string> Errors)
{
    public static ValidationResult Ok() => new(true, Array.Empty<string>());
}
