Public Class DefaultPage
    Inherits System.Web.UI.Page

    Protected Sub Button1_Click(sender As Object, e As System.EventArgs) Handles Button1.Click
        Label1.Text = "Clicked"
    End Sub

    Protected Sub Page_Load(sender As Object, e As System.EventArgs) Handles Me.Load
        Label1.Text = "Loaded"
    End Sub
End Class
