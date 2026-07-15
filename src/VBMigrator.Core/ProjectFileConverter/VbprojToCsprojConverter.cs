using System.Xml.Linq;

namespace VBMigrator.Core.ProjectFileConverter;

public static class VbprojToCsprojConverter
{
    public static string Convert(string vbprojXml)
    {
        var doc  = XDocument.Parse(vbprojXml);
        var root = doc.Root!;
        var ns   = root.Name.Namespace;

        return root.Attribute("Sdk") != null
            ? ConvertSdkStyle(doc)
            : ConvertOldStyle(doc, ns);
    }

    private static string ConvertSdkStyle(XDocument doc)
    {
        var root    = doc.Root!;
        var sdkAttr = root.Attribute("Sdk");
        if (sdkAttr != null)
            sdkAttr.Value = "Microsoft.NET.Sdk";

        foreach (var tf in root.Descendants("TargetFramework"))
            tf.Value = NormalizeTargetFramework(tf.Value);

        return doc.ToString();
    }

    private static string ConvertOldStyle(XDocument doc, XNamespace ns)
    {
        var root = doc.Root!;

        root.SetAttributeValue("Sdk", "Microsoft.NET.Sdk");
        root.Attributes().Where(a => a.Name != "Sdk").Remove();

        // TargetFrameworkVersion → TargetFramework
        foreach (var el in root.Descendants(ns + "TargetFrameworkVersion").ToList())
            el.ReplaceWith(new XElement("TargetFramework", NormalizeTargetFramework(el.Value)));

        // .vb → .cs in Compile includes
        foreach (var compile in root.Descendants(ns + "Compile").ToList())
        {
            var inc = compile.Attribute("Include");
            if (inc != null && inc.Value.EndsWith(".vb", StringComparison.OrdinalIgnoreCase))
                inc.Value = inc.Value[..^3] + ".cs";
        }

        // Remove VB-specific imports
        foreach (var import in root.Descendants(ns + "Import").ToList())
        {
            if ((import.Attribute("Project")?.Value ?? "")
                .Contains("Microsoft.VisualBasic", StringComparison.OrdinalIgnoreCase))
                import.Remove();
        }

        // Remove VB-specific references
        foreach (var refEl in root.Descendants(ns + "Reference").ToList())
        {
            if ((refEl.Attribute("Include")?.Value ?? "")
                .StartsWith("Microsoft.VisualBasic", StringComparison.OrdinalIgnoreCase))
                refEl.Remove();
        }

        // COM references: prepend comment
        foreach (var com in root.Descendants(ns + "COMReference").ToList())
            com.AddBeforeSelf(new XComment(" VBMigrator: COM reference — verificar interop assembly "));

        // Strip namespace from all elements (SDK-style has no xmlns)
        foreach (var el in root.DescendantsAndSelf())
            el.Name = el.Name.LocalName;

        return doc.ToString();
    }

    private static string NormalizeTargetFramework(string value)
    {
        if (value.StartsWith('v'))
            return $"net{value[1..].Replace(".", "")}";
        return value;
    }
}
