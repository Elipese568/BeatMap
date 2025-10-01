using EUtility.ConsoleEx.Message;
using EUtility.StringEx.StringExtension;
using System.Text;

namespace BeatMap.UI;

public class WarpMessageFormatter : IMessageFormatter
{
    public string FormatMessage(ICollection<IMessageUnit> messageunits)
    {
        StringBuilder sb = new();
        var lastElement = messageunits.LastOrDefault();
        int currentLineCount = 0;
        foreach(var messageunit in messageunits)
        {
            string unitString = $"{messageunit.Title} {messageunit.Description} ";
            if (sb.ToString().GetStringInConsoleGridWidth() + unitString.GetStringInConsoleGridWidth() + 4 >= Console.BufferWidth * (currentLineCount + 1) - 1)
            {
                sb.AppendLine();
                currentLineCount++;
            }
            sb.Append(unitString);
            if (messageunit != lastElement && sb.ToString().GetStringInConsoleGridWidth() <= Console.BufferWidth * (currentLineCount + 1) - 1)
                sb.Append("    ");
        }

        return sb.ToString();
    }
}
