Public Class SampleClass

    Public Function IsNullCheck(obj As Object) As Boolean
        Return obj Is Nothing
    End Function

    Public Function IsNotNullCheck(obj As Object) As Boolean
        Return obj IsNot Nothing
    End Function

    Public Function LogicalAnd(a As Boolean, b As Boolean) As Boolean
        Return a AndAlso b
    End Function

    Public Function LogicalOr(a As Boolean, b As Boolean) As Boolean
        Return a OrElse b
    End Function

    Public Function ConvertBool() As Integer
        Return CInt(True)
    End Function

    Public Function ConcatStrings(s1 As String, s2 As String) As String
        Return s1 & s2
    End Function

    Public Function MatchPattern(s As String) As Boolean
        Return s Like "A*"
    End Function

End Class
