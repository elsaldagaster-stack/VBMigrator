
    public static int SafeDivide(int a, int b)
    {
        if (b is null)
            return 0;
        return a / b;
    }



    public static double CalcPower(double base_, double exp)
    {
        return Math.Pow(base_, exp);
    }



    public static bool CompareIgnoreCase(string a, string b)
    {
        return string.Compare(a, b, true) == 0;
    }



    public static string GetSetting()
    {
        return Properties.Settings.Default.AppTitle;
    }



    public static void ProcessArray(ref int[] arr, int newSize)
    {
        Array.Resize(ref arr, newSize + 1);
    }



    public static string UseIIf(int x)
    {
        return (/* ⚠ VBMigrator: IIf evalúa ambos brazos en VB; ternario ?: no lo hace */ x > 0 ? "positive" : "non-positive");
    }



    public static void HandleError()
    {
        try
        {
            int x = (int)Math.Round(1d / 0d);
            return;
        }
        catch
        {

            Console.WriteLine(Err.Description);
        }
    }
