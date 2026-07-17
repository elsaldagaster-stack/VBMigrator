
    public bool IsNullCheck(object obj)
    {
        return obj is null;
    }



    public bool IsNotNullCheck(object obj)
    {
        return obj is not null;
    }



    public bool LogicalAnd(bool a, bool b)
    {
        return a && b;
    }



    public bool LogicalOr(bool a, bool b)
    {
        return a || b;
    }



    public int ConvertBool()
    {
        return Conversions.ToInteger(true);
    }



    public string ConcatStrings(string s1, string s2)
    {
        return s1 + s2;
    }



    public bool MatchPattern(string s)
    {
        return System.Text.RegularExpressions.Regex.IsMatch(s, "^A.*$");
    }
