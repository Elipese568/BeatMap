using EUtility.ConsoleEx.Message;
using EUtility.StringEx.StringExtension;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BeatMap;

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

public class MessageOutputerOnWindow : MessageOutputer
{
    public override void Write(IMessageFormatter messageFormater)
    {
        var (left, top) = Console.GetCursorPosition();
        string text = messageFormater.FormatMessage(_messageUnits);
        var tr = new StringReader(text);
        int lineCount = 0;
        while(tr.ReadLine() is not null)
        {
            lineCount++;
        }
        Console.SetCursorPosition(0, Console.WindowHeight - 1 - lineCount);
        Console.Write(text);
        Console.SetCursorPosition(left, top);
    }
}
