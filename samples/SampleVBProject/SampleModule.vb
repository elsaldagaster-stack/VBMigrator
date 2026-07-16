Module SampleModule

    Public Function SafeDivide(a As Integer, b As Integer) As Integer
        If b Is Nothing Then Return 0
        Return a \ b
    End Function

    Public Function CalcPower(base_ As Double, exp As Double) As Double
        Return base_ ^ exp
    End Function

    Public Function CompareIgnoreCase(a As String, b As String) As Boolean
        Return String.Compare(a, b, True) = 0
    End Function

    Public Function GetSetting() As String
        Return My.Settings.AppTitle
    End Function

    Public Sub ProcessArray(ByRef arr() As Integer, newSize As Integer)
        ReDim Preserve arr(newSize)
    End Sub

    Public Function UseIIf(x As Integer) As String
        Return IIf(x > 0, "positive", "non-positive")
    End Function

    Public Sub HandleError()
        On Error GoTo ErrHandler
        Dim x As Integer = 1 / 0
        Exit Sub
ErrHandler:
        Console.WriteLine(Err.Description)
    End Sub

End Module
