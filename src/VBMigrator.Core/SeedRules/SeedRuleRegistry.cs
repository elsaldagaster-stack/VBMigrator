using VBMigrator.Core.SeedRules.Rules;

namespace VBMigrator.Core.SeedRules;

public static class SeedRuleRegistry
{
    public static IReadOnlyList<ISeedRule> GetAll() =>
    [
        new IsNothingRule(),
        new IsNotNothingRule(),
        new AndAlsoRule(),
        new OrElseRule(),
        new CintBoolRule(),
        new IntegerDivisionRule(),
        new ExponentiationRule(),
        new RedimPreserveRule(),
        new EraseArrayRule(),
        new StringConcatRule(),
        new StringComparisonRule(),
        new IifFunctionRule(),
        new LikeOperatorRule(),
        new ByValParamRule(),
        new OptionalParamRule(),
        new DateLiteralRule(),
        new OnErrorGotoRule(),
        new OnErrorResumeRule(),
        new ByteLoopRule(),
        new MySettingsRule(),
        new MyFilesystemReadRule(),
        new MyFilesystemWriteRule(),
        new MyAppVersionRule(),
        new MyUserRule(),
        new NothingValueTypeRule()
    ];
}
