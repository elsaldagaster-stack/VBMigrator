using Microsoft.CodeAnalysis.VisualBasic;
using VBMigrator.Core.AspxHandler;
using Xunit;

namespace VBMigrator.Core.Tests.AspxHandler;

public class AspxHandlerTests
{
    [Fact]
    public void AspxDirectiveRewriter_ReplacesLanguageAndCodeBehind()
    {
        const string aspx = """
            <%@ Page Language="VB" AutoEventWireup="false" CodeBehind="Default.aspx.vb" Inherits="MyApp.Default" %>
            """;

        var result = AspxDirectiveRewriter.Rewrite(aspx);

        Assert.Contains("Language=\"C#\"", result);
        Assert.Contains("Default.aspx.cs", result);
        Assert.DoesNotContain(".aspx.vb", result);
    }

    [Fact]
    public void EventWireupMigrator_GeneratesSubscriptions_FromHandlesDeclarations()
    {
        const string vbCodeBehind = """
            Public Class Default
                Inherits System.Web.UI.Page

                Protected Sub Button1_Click(sender As Object, e As EventArgs) Handles Button1.Click
                    Label1.Text = "clicked"
                End Sub

                Protected Sub Page_Load(sender As Object, e As EventArgs) Handles Me.Load
                    Label1.Text = "loaded"
                End Sub
            End Class
            """;

        var vbTree = VisualBasicSyntaxTree.ParseText(vbCodeBehind);
        var subscriptions = EventWireupMigrator.ExtractSubscriptions(vbTree);

        Assert.Contains(subscriptions, s => s.Contains("Button1.Click += Button1_Click"));
        Assert.Contains(subscriptions, s => s.Contains("Load += Page_Load") || s.Contains("this.Load += Page_Load"));
    }
}
