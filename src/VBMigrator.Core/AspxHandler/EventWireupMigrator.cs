using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.VisualBasic;
using Microsoft.CodeAnalysis.VisualBasic.Syntax;

namespace VBMigrator.Core.AspxHandler;

public static class EventWireupMigrator
{
    public static IReadOnlyList<string> ExtractSubscriptions(SyntaxTree vbTree)
    {
        var subscriptions = new List<string>();
        var root = vbTree.GetRoot();

        foreach (var method in root.DescendantNodes().OfType<MethodBlockSyntax>())
        {
            var stmt         = method.SubOrFunctionStatement;
            var handlesClause = stmt.HandlesClause;
            if (handlesClause == null) continue;

            var methodName = stmt.Identifier.Text;
            foreach (var handlesItem in handlesClause.Events)
            {
                var eventText = handlesItem.ToString().Trim();
                eventText = eventText.Replace("Me.", "this.");
                subscriptions.Add($"{eventText} += {methodName};");
            }
        }

        return subscriptions;
    }
}
