using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BeatMap.UI;

public interface IContentBlock
{
    string Content { get; set; }
    ConsoleColor Background { get; set; }
    ConsoleColor Foreground { get; set; }
    public void Render()
    {
        Console.BackgroundColor = Background;
        Console.ForegroundColor = Foreground;
        Console.Write(Content);
        Console.ResetColor();
    }
}

public class LineBreakBlock : IContentBlock
{
    public string Content { get; set; } = Environment.NewLine;
    public ConsoleColor Background { get; set; } = ConsoleColor.Black;
    public ConsoleColor Foreground { get; set; } = ConsoleColor.White;
    public void Render()
    {
        Console.WriteLine();
    }
}

public class ContentBlock : IContentBlock
{
    public string Content { get; set; }
    public ConsoleColor Background { get; set; } = ConsoleColor.Black;
    public ConsoleColor Foreground { get; set; } = ConsoleColor.White;
}

public class ContentRender
{
    private List<IContentBlock> _contentBlocks = new();

    public void AppendLine()
    {
        _contentBlocks.Add(new LineBreakBlock());
    }

    public void Add(ContentBlock block)
    {
        _contentBlocks.Add(block);
    }

    public void Render()
    {
        foreach (var block in _contentBlocks)
            block.Render();
        _contentBlocks.Clear();
    }
}
