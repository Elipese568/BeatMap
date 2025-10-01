using EUtility.ConsoleEx.Message;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BeatMap.UI;

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
