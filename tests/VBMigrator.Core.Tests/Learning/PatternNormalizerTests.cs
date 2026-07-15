using VBMigrator.Core.Learning;
using Xunit;

namespace VBMigrator.Core.Tests.Learning;

public class PatternNormalizerTests
{
    [Fact]
    public void NormalizeVb_ReplacesIdentifiersAndTypes()
    {
        const string vb = "Dim result As Integer = x + y";
        var (template, map) = PatternNormalizer.NormalizeVb(vb);

        Assert.Contains("__var1__", template);
        Assert.Contains("__Type1__", template);
        Assert.DoesNotContain("result", template);
        Assert.DoesNotContain("Integer", template);
    }

    [Fact]
    public void NormalizeCs_VbOriginVarsGetSameSlot_CsIntroducedGetNewSlot()
    {
        const string vb = "On Error GoTo ErrHandler";
        const string cs = "try { } catch (Exception ex) { }";
        var (_, vbMap) = PatternNormalizer.NormalizeVb(vb);
        var (csTemplate, _) = PatternNormalizer.NormalizeCs(cs, vbMap);

        // 'ex' has no VB counterpart → __new1__ or __new2__
        Assert.Contains("__new1__", csTemplate);
        Assert.DoesNotContain(" ex ", csTemplate);
    }

    [Fact]
    public void NormalizeVb_TwoCallsWithSameInput_ProduceSameTemplate()
    {
        const string vb = "Dim count As Integer = 0";
        var (t1, _) = PatternNormalizer.NormalizeVb(vb);
        var (t2, _) = PatternNormalizer.NormalizeVb(vb);
        Assert.Equal(t1, t2);
    }
}
