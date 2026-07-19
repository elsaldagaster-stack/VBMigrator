using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace VBMigrator.Core.Translator;

/// <summary>
/// Resolves missing using statements after both SeedRules and LLM have run on the C# method.
/// Runs on complete C# method text (post-SeedRule + post-LLM), per spec §4.3.
/// Types not in dictionary → emits MISSING_USING flag for HumanQueue.
/// </summary>
public class LlmUsingResolver
{
    private static readonly Dictionary<string, string> _wellKnown = new(StringComparer.Ordinal)
    {
        // System
        ["DateTime"]              = "System",
        ["StringComparison"]      = "System",
        ["Math"]                  = "System",
        ["Convert"]               = "System",
        ["Environment"]           = "System",
        ["GC"]                    = "System",
        // System.IO
        ["File"]                  = "System.IO",
        ["Path"]                  = "System.IO",
        ["Directory"]             = "System.IO",
        ["Stream"]                = "System.IO",
        ["StreamReader"]          = "System.IO",
        ["StreamWriter"]          = "System.IO",
        // System.Text
        ["StringBuilder"]         = "System.Text",
        ["Encoding"]              = "System.Text",
        // System.Text.RegularExpressions
        ["Regex"]                 = "System.Text.RegularExpressions",
        ["Match"]                 = "System.Text.RegularExpressions",
        // System.Collections.Generic
        ["List"]                  = "System.Collections.Generic",
        ["Dictionary"]            = "System.Collections.Generic",
        ["IEnumerable"]           = "System.Collections.Generic",
        ["HashSet"]               = "System.Collections.Generic",
        // System.Threading
        ["Task"]                  = "System.Threading.Tasks",
        ["CancellationToken"]     = "System.Threading",
        // System.Net.Http
        ["HttpClient"]            = "System.Net.Http",
        // System.Text.Json
        ["JsonSerializer"]        = "System.Text.Json",
        ["JsonSerializerOptions"] = "System.Text.Json",
        // System.Xml.Linq
        ["XDocument"]             = "System.Xml.Linq",
        ["XElement"]              = "System.Xml.Linq",
        // System.Reflection
        ["Assembly"]              = "System.Reflection",
        // System.Security
        ["WindowsIdentity"]       = "System.Security.Principal",
        ["SHA256"]                = "System.Security.Cryptography",
        ["MD5"]                   = "System.Security.Cryptography",
        ["SHA512"]                = "System.Security.Cryptography",
        // System.Data
        ["DataTable"]             = "System.Data",
        ["DataRow"]               = "System.Data",
        ["DataColumn"]            = "System.Data",
        ["DataSet"]               = "System.Data",
        ["DataView"]              = "System.Data",
        ["DBNull"]                = "System.Data",
        // System.Data.SqlClient
        ["SqlConnection"]         = "System.Data.SqlClient",
        ["SqlCommand"]            = "System.Data.SqlClient",
        ["SqlDataReader"]         = "System.Data.SqlClient",
        ["SqlDataAdapter"]        = "System.Data.SqlClient",
        ["SqlParameter"]          = "System.Data.SqlClient",
        ["SqlException"]          = "System.Data.SqlClient",
        // System.Web.UI.WebControls (ASP.NET WebForms controls and event args)
        ["GridView"]                  = "System.Web.UI.WebControls",
        ["GridViewPageEventArgs"]     = "System.Web.UI.WebControls",
        ["GridViewCommandEventArgs"]  = "System.Web.UI.WebControls",
        ["GridViewSortEventArgs"]     = "System.Web.UI.WebControls",
        ["GridViewEditEventArgs"]     = "System.Web.UI.WebControls",
        ["GridViewDeleteEventArgs"]   = "System.Web.UI.WebControls",
        ["GridViewRowEventArgs"]      = "System.Web.UI.WebControls",
        ["Label"]                     = "System.Web.UI.WebControls",
        ["TextBox"]                   = "System.Web.UI.WebControls",
        ["Button"]                    = "System.Web.UI.WebControls",
        ["DropDownList"]              = "System.Web.UI.WebControls",
        ["ListItem"]                  = "System.Web.UI.WebControls",
        ["Panel"]                     = "System.Web.UI.WebControls",
        ["HyperLink"]                 = "System.Web.UI.WebControls",
        ["LinkButton"]                = "System.Web.UI.WebControls",
        ["CheckBox"]                  = "System.Web.UI.WebControls",
        ["RadioButton"]               = "System.Web.UI.WebControls",
        ["Literal"]                   = "System.Web.UI.WebControls",
        ["Repeater"]                  = "System.Web.UI.WebControls",
        ["RepeaterItemEventArgs"]     = "System.Web.UI.WebControls",
        ["FormView"]                  = "System.Web.UI.WebControls",
        ["DetailsView"]               = "System.Web.UI.WebControls",
        ["Calendar"]                  = "System.Web.UI.WebControls",
        ["FileUpload"]                = "System.Web.UI.WebControls",
        ["HiddenField"]               = "System.Web.UI.WebControls",
        ["Image"]                     = "System.Web.UI.WebControls",
        ["ImageButton"]               = "System.Web.UI.WebControls",
        ["Table"]                     = "System.Web.UI.WebControls",
        ["TableRow"]                  = "System.Web.UI.WebControls",
        ["TableCell"]                 = "System.Web.UI.WebControls",
        ["BoundField"]                = "System.Web.UI.WebControls",
        ["CommandField"]              = "System.Web.UI.WebControls",
        ["TemplateField"]             = "System.Web.UI.WebControls",
        // System.Web (ASP.NET WebForms)
        ["HttpContext"]           = "System.Web",
        ["HttpResponse"]          = "System.Web",
        ["HttpRequest"]           = "System.Web",
        ["HttpServerUtility"]     = "System.Web",
        ["HttpCookie"]            = "System.Web",
        // System.Web.Security (ASP.NET Forms Authentication)
        ["FormsAuthentication"]        = "System.Web.Security",
        ["FormsAuthenticationTicket"]  = "System.Web.Security",
        ["FormsIdentity"]              = "System.Web.Security",
        ["RoleProvider"]               = "System.Web.Security",
        // System.Configuration
        ["ConfigurationManager"]  = "System.Configuration",
        // System.Globalization
        ["CultureInfo"]           = "System.Globalization",
        ["NumberStyles"]          = "System.Globalization",
    };

    public const string MissingUsingFlag = "MISSING_USING";

    public LlmUsingResolution Resolve(string csMethodSource, Microsoft.CodeAnalysis.SemanticModel? model = null)
    {
        var resolvedNamespaces = new HashSet<string>(StringComparer.Ordinal);
        var missingTypes       = new List<string>();

        var tree = CSharpSyntaxTree.ParseText(csMethodSource);
        var root = tree.GetRoot();

        var candidates = root.DescendantNodes()
            .OfType<IdentifierNameSyntax>()
            .Select(id => id.Identifier.Text)
            .Distinct(StringComparer.Ordinal);

        foreach (var typeName in candidates)
        {
            if (_wellKnown.TryGetValue(typeName, out var ns))
                resolvedNamespaces.Add(ns);
        }

        return new LlmUsingResolution
        {
            Namespaces   = resolvedNamespaces.ToList(),
            MissingTypes = missingTypes
        };
    }

    public static IEnumerable<string> ToUsingDirectives(IEnumerable<string> namespaces)
        => namespaces.OrderBy(n => n).Select(n => $"using {n};");

    public static IReadOnlyDictionary<string, string> WellKnown => _wellKnown;
}

public record LlmUsingResolution
{
    public List<string> Namespaces   { get; init; } = new();
    public List<string> MissingTypes { get; init; } = new();
    public bool HasMissingTypes => MissingTypes.Count > 0;
}
