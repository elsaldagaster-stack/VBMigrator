using Microsoft.CodeAnalysis.VisualBasic;
using VBMigrator.Core.Analyzer;
using VBMigrator.Core.Analyzer.FlagDetectors;
using VBMigrator.Core.Models;
using Xunit;

namespace VBMigrator.Core.Tests.Analyzer;

public class DifficultyAnalyzerTests
{
    private readonly DifficultyAnalyzer _analyzer = new();

    [Fact]
    public void Analyze_CleanMethod_NoFlags_ReturnsScoreZero()
    {
        var vb   = "Module M\nSub F()\nDim x As Integer = 1\nEnd Sub\nEnd Module";
        var tree = SyntaxFactory.ParseSyntaxTree(vb);
        var map  = _analyzer.Analyze(tree, "test.vb");
        var fn   = Assert.Single(map.Functions);
        Assert.Empty(fn.Flags);
        Assert.Equal(0, fn.Score);
    }

    [Fact]
    public void Analyze_OnErrorGoTo_SetsOnErrorFlag()
    {
        var vb   = "Module M\nSub F()\nOn Error GoTo ErrH\nErrH:\nEnd Sub\nEnd Module";
        var tree = SyntaxFactory.ParseSyntaxTree(vb);
        var map  = _analyzer.Analyze(tree, "test.vb");
        var fn   = Assert.Single(map.Functions);
        Assert.Contains(OnErrorDetector.FlagOnError, fn.Flags);
    }

    [Fact]
    public void Analyze_OnErrorResumeNext_SetsOnErrorResumeFlag()
    {
        var vb   = "Module M\nSub F()\nOn Error Resume Next\nEnd Sub\nEnd Module";
        var tree = SyntaxFactory.ParseSyntaxTree(vb);
        var map  = _analyzer.Analyze(tree, "test.vb");
        var fn   = Assert.Single(map.Functions);
        Assert.Contains(OnErrorDetector.FlagOnErrorResume, fn.Flags);
    }

    [Fact]
    public void Analyze_LikeOperator_SetsLikeOpFlag()
    {
        var vb   = "Module M\nSub F()\nDim r = s Like \"A*\"\nEnd Sub\nEnd Module";
        var tree = SyntaxFactory.ParseSyntaxTree(vb);
        var map  = _analyzer.Analyze(tree, "test.vb");
        var fn   = Assert.Single(map.Functions);
        Assert.Contains(OperatorDetector.FlagLikeOp, fn.Flags);
    }

    [Fact]
    public void Analyze_Exponentiation_SetsExponOpFlag()
    {
        var vb   = "Module M\nSub F()\nDim r = x ^ y\nEnd Sub\nEnd Module";
        var tree = SyntaxFactory.ParseSyntaxTree(vb);
        var map  = _analyzer.Analyze(tree, "test.vb");
        var fn   = Assert.Single(map.Functions);
        Assert.Contains(OperatorDetector.FlagExponOp, fn.Flags);
    }

    [Fact]
    public void Analyze_ByteLoop255_SetsByteLoopFlag()
    {
        var vb   = "Module M\nSub F()\nFor i As Byte = 0 To 255\nNext\nEnd Sub\nEnd Module";
        var tree = SyntaxFactory.ParseSyntaxTree(vb);
        var map  = _analyzer.Analyze(tree, "test.vb");
        var fn   = Assert.Single(map.Functions);
        Assert.Contains(OperatorDetector.FlagByteLoop, fn.Flags);
    }

    [Fact]
    public void Analyze_MyNamespace_SetsMyNamespaceFlag()
    {
        var vb   = "Module M\nSub F()\nDim v = My.Settings.Foo\nEnd Sub\nEnd Module";
        var tree = SyntaxFactory.ParseSyntaxTree(vb);
        var map  = _analyzer.Analyze(tree, "test.vb");
        var fn   = Assert.Single(map.Functions);
        Assert.Contains(MyNamespaceDetector.Flag, fn.Flags);
    }

    [Fact]
    public void Analyze_MultipleFlags_ScoreIsProportional()
    {
        // OnError + LikeOp + ExponOp = 3 flags → score 30
        var vb   = "Module M\nSub F()\nOn Error GoTo E\nDim r = s Like \"A*\"\nDim p = x ^ y\nE:\nEnd Sub\nEnd Module";
        var tree = SyntaxFactory.ParseSyntaxTree(vb);
        var map  = _analyzer.Analyze(tree, "test.vb");
        var fn   = Assert.Single(map.Functions);
        Assert.Equal(30, fn.Score);
    }

    [Fact]
    public void Analyze_FilePath_IsPreservedInMap()
    {
        var tree = SyntaxFactory.ParseSyntaxTree("Module M\nSub F()\nEnd Sub\nEnd Module");
        var map  = _analyzer.Analyze(tree, "MyFile.vb");
        Assert.Equal("MyFile.vb", map.FilePath);
    }
}
