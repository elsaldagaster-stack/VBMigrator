namespace VBMigrator.Core.Models;

public enum LlmFailureReason
{
    RateLimit,
    Timeout,
    ApiError,
    ContentFilter
}
