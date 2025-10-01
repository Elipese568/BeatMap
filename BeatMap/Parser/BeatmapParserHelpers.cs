using System.Text.RegularExpressions;

namespace BeatMap;

internal static partial class BeatmapParserHelpers
{
    [GeneratedRegex(@"\[(.*?)\]")]
    public static partial Regex MatchAttributes();

    [GeneratedRegex(@"\((.*?)\)")]
    public static partial Regex MatchBpmAttribute();

    [GeneratedRegex(@"d{0,1}(0b[01]+|\d+)")]
    public static partial Regex MatchNoteBinaryOrDecimal();

    [GeneratedRegex(@"\{(.*?)\}")]
    public static partial Regex MatchPeriodUnitAttribute();
}