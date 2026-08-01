namespace Nop.Plugin.Misc.SqlReports.Services;

public static class SqlReportDataType
{
    public const string Text = "Text";
    public const string Number = "Number";
    public const string TextList = "TextList";
    public const string NumberList = "NumberList";

    public static IList<string> All => new List<string> { Text, Number, TextList, NumberList };

    public static bool IsList(string dataType)
    {
        return string.Equals(dataType, TextList, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(dataType, NumberList, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsNumber(string dataType)
    {
        return string.Equals(dataType, Number, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(dataType, NumberList, StringComparison.OrdinalIgnoreCase);
    }
}
