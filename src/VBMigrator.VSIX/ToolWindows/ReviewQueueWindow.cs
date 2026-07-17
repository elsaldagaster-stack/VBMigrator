using System.Runtime.InteropServices;
using Microsoft.VisualStudio.Shell;

namespace VBMigrator.VSIX.ToolWindows;

[Guid("c2d3e4f5-a6b7-8901-cdef-012345678902")]
public class ReviewQueueWindow : ToolWindowPane
{
    public ReviewQueueWindow() : base(null)
    {
        Caption = "VBMigrator — Review Queue";
        Content = new ReviewQueueWindowControl();
    }

    public ReviewQueueWindowControl Control => (ReviewQueueWindowControl)Content;
}
