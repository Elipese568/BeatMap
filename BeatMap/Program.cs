using EUtility.ConsoleEx.Message;
using EUtility.StringEx.StringExtension;
using PInvoke.Net;
using System;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;


namespace BeatMap;

public enum NoteType
{
    Tap,
    Drag
}

public class Note
{
    public int Track;      // 0~8
    public double TimeMs;  // 判定时间
    public NoteType Type;
}

public class SpeedSegment
{
    public double StartTimeMs;
    public double Speed;
}

public class Beatmap
{
    public string Name { get; set; }
    public string Artist { get; set; }
    public double Bpm { get; set; }
    public int Keys { get; set; }
    public List<Note> Notes { get; set; } = new();
    public List<SpeedSegment> SpeedSegments { get; set; } = new();
    // ✅ 新增：与 Notes 同索引对齐的 floorUnits（时间∫速度比）
    // 单位：ms * ratio（无量纲“单位时间积”，渲染时再乘 (setting.Speed / 1000) 变成“屏幕行”）
    public List<double> NoteFloorUnits { get; set; } = new();

    // ✅ 新增：每个变速段起点的“累计 floorUnits”
    // 用来 O(1) 求 t 时刻的 floorUnits：cum[i] + (t - seg[i].Start)*seg[i].Speed
    public List<double> SegmentCumUnits { get; set; } = new();
}

[AttributeUsage(AttributeTargets.Property)]
public class SettingDisplayName : Attribute
{     
    public string Name { get; }
    public SettingDisplayName(string name) => Name = name;
}

public partial class BeatmapParser
{
    [GeneratedRegex(@"\[(.*?)\]")]
    public static partial Regex MatchAttributes();

    [GeneratedRegex(@"\{(.*?)\}")]
    public static partial Regex MatchPeriodUnitAttribute();

    [GeneratedRegex(@"\((.*?)\)")]
    public static partial Regex MatchBpmAttribute();

    [GeneratedRegex(@"d{0,1}(0b[01]+|\d+)")]
    public static partial Regex MatchNoteBinaryOrDecimal();

    public static Beatmap Parse(string filePath)
    {
        string content = File.ReadAllText(filePath);
        string[] parts = content.Split(";");
        if (parts.Length < 5)
            throw new ArgumentException("Invalid beatmap format.");
        
        Beatmap map = new()
        {
            Name = parts[0].Trim(),
            Artist = parts[1].Trim(),
            Bpm = double.Parse(parts[2].Trim()),
            Keys = int.Parse(parts[3].Trim())
        };

        double msPerBeat = 60000.0 / map.Bpm;
        string[] rowStrings = parts[4].Trim().Split(",");

        double lastJudgeTime = 0; // 用于检测空行间隔
        int currentNotePeriodF = 4;
        for (int rowIndex = 0; rowIndex < rowStrings.Length; rowIndex++)
        {
            string rowString = rowStrings[rowIndex].Trim();
            double judgeTime = lastJudgeTime + msPerBeat / currentNotePeriodF;
            lastJudgeTime = judgeTime;
            if (string.IsNullOrWhiteSpace(rowString)) continue;

            var attributes = MatchAttributes().Match(rowString);
            var periodAttr = MatchPeriodUnitAttribute().Match(rowString);
            var bpmAttr = MatchBpmAttribute().Match(rowString);
            string keyString = attributes.Success ? rowString.Replace(attributes.Value, "") : rowString;
            keyString = periodAttr.Success ? keyString.Replace(periodAttr.Value, "") : keyString;
            keyString = bpmAttr.Success ? keyString.Replace(bpmAttr.Value, "") : keyString;
            keyString = MatchNoteBinaryOrDecimal().Match(keyString).Value;
            
            foreach(var currentString in keyString.Split("/"))
            {
                int integer = -1;
                bool isDrag = currentString.StartsWith('d');
                string withoutDecoration = currentString[(isDrag?1:0)..];
                if (withoutDecoration.StartsWith(isDrag?"d0b":"0b"))
                {
                    integer = Convert.ToInt32(withoutDecoration[(isDrag?3:2)..], 2);
                }
                if (integer != -1 || int.TryParse(withoutDecoration, out integer))
                {
                    for (int track = 0; track < map.Keys; track++)
                    {
                        if ((integer & (1 << (map.Keys - 1 - track))) != 0)
                        {
                            map.Notes.Add(new Note { Track = track, TimeMs = judgeTime, Type = isDrag ? NoteType.Drag : NoteType.Tap });
                        }
                    }
                }
            }

            if(periodAttr.Success)
            {
                currentNotePeriodF = int.Parse(periodAttr.Value.Trim('{', '}'));
            }
            lastJudgeTime = judgeTime;

            if(bpmAttr.Success)
            {
                if(double.TryParse(bpmAttr.Value.Trim('(', ')'), out double newBpm) && newBpm > 0)
                {
                    map.Bpm = newBpm;
                    msPerBeat = 60000.0 / map.Bpm;
                }
            }
            // 解析属性
            if (attributes.Success)
            {
                foreach (var attrUnit in attributes.Value[1..^1].Split('&'))
                {
                    var kv = attrUnit.Split(':');
                    if (kv.Length == 2 && kv[0].Equals("s", StringComparison.CurrentCultureIgnoreCase))
                    {
                        if (double.TryParse(kv[1], out double speedRadio))
                        {
                            map.SpeedSegments.Add(new SpeedSegment
                            {
                                StartTimeMs = judgeTime,
                                Speed = speedRadio
                            });
                        }
                    }
                }
            }
        }

        // 如果没定义变速，默认一个
        if (map.SpeedSegments.Count == 0)
            map.SpeedSegments.Add(new SpeedSegment { StartTimeMs = 0, Speed = 1.0 });

        // ✅ 规范化变速段：按时间排序，确保 0ms 段存在，去除同一时间的重复定义（保留最后一个）
        map.SpeedSegments = map.SpeedSegments
            .GroupBy(s => s.StartTimeMs)
            .Select(g => g.Last())
            .OrderBy(s => s.StartTimeMs)
            .ToList();

        if (map.SpeedSegments[0].StartTimeMs > 0)
            map.SpeedSegments.Insert(0, new SpeedSegment { StartTimeMs = 0, Speed = 1.0 });

        // ✅ 预计算每个段起点的累计 floorUnits（∫ ratio dt）
        map.SegmentCumUnits.Clear();
        map.SegmentCumUnits.Capacity = map.SpeedSegments.Count;

        double cum = 0;
        map.SegmentCumUnits.Add(0); // 第 0 段起点累计为 0
        for (int i = 1; i < map.SpeedSegments.Count; i++)
        {
            var prev = map.SpeedSegments[i - 1];
            var cur = map.SpeedSegments[i];
            cum += (cur.StartTimeMs - prev.StartTimeMs) * prev.Speed; // 累计上一段的面积
            map.SegmentCumUnits.Add(cum);
        }

        // ✅ 为每个 Note 计算 floorUnits（与 Notes 对齐存储）
        map.NoteFloorUnits.Clear();
        map.NoteFloorUnits.Capacity = map.Notes.Count;

        // 二分工具：返回最后一个 StartTimeMs <= t 的段索引
        int FindSegmentIndex(double t)
        {
            int lo = 0, hi = map.SpeedSegments.Count - 1, ans = 0;
            while (lo <= hi)
            {
                int mid = (lo + hi) >> 1;
                if (map.SpeedSegments[mid].StartTimeMs <= t)
                {
                    ans = mid;
                    lo = mid + 1;
                }
                else hi = mid - 1;
            }
            return ans;
        }

        // 注意：保持与 Notes 原有顺序一致，floorUnits 用同样的索引
        foreach (var note in map.Notes)
        {
            int idx = FindSegmentIndex(note.TimeMs);
            var seg = map.SpeedSegments[idx];
            double baseCum = map.SegmentCumUnits[idx];
            double floorUnits = baseCum + (note.TimeMs - seg.StartTimeMs) * seg.Speed;
            map.NoteFloorUnits.Add(floorUnits);
        }

        return map;
    }
}


public class PlaySetting
{
    public int Speed { get; set; } = 16;
    [SettingDisplayName("AccLostScoreRadio(Enter to view compensation arc)")]
    public double AccLostScoreRadio { get; set; } = 0.24;
    public int JudgeOffset { get; set; } = -40;
    public bool ShowMissMessage { get; set; } = false;
    public bool AutoPlay { get; set; } = false;
    public bool UnperfectAuto { get; set; } = false;
    public double UnperfectRadio { get; set; } = 0.2;
    public int KeyWidth { get; set; } = 6; // Width of each key display in characters
    public int PanelHeight { get; set; } = 16; // Height of the game panel in rows
    public Dictionary<int, string> KeyBinding { get; set; } = new()
    {
        [2] = "FJ",
        [3] = "D K",
        [4] = "DFJK",
        [5] = "DF JK",
        [6] = "SDFJKL",
        [7] = "SDF JKL",
        [8] = "ASDFHJKL",
        [9] = "ASDF HJKL"
    };
}

public static class Menu
{
    static MessageOutputerOnWindow menuguide = new()
        {
            new MessageUnit()
            {
                Title = "↑",
                Description = "上一个选项"
            },
            new MessageUnit()
            {
                Title = "↓",
                Description = "下一个选项"
            },
            new MessageUnit()
            {
                Title = "Enter",
                Description = "确认选项"
            }
        };
    static Menu()
    {

    }
    public static int WriteMenu(Dictionary<string, string> menuitem, int curstartindex = 2, bool descriptionEnabled = true)
    {
        int select = 0;
        Console.CursorTop = curstartindex;
        for (int i = 0; i < Console.WindowHeight - curstartindex - 1; i++)
        {
            Console.WriteLine(new string(' ', Console.WindowWidth));
        }
        while (true)
        {
            menuguide.Write();
            Console.CursorTop = curstartindex;
            int index = 0;
            foreach (var item in menuitem)
            {
                if (index == select)
                {
                    Console.BackgroundColor = ConsoleColor.White;
                    Console.ForegroundColor = ConsoleColor.Black;
                    Console.WriteLine("   -->" + item.Key + new string(' ', Console.WindowWidth - (item.Key + "   -->").GetStringInConsoleGridWidth()));
                    Console.ResetColor();

                    if(descriptionEnabled)
                    {
                        var ct = Console.CursorTop;
                        Console.CursorTop = menuitem.Count + 2 + curstartindex;
                        Console.WriteLine("说明：");
                        var dct = Console.CursorTop;
                        for (int i = 0; i < Console.WindowHeight - Console.CursorTop - 1; i++)
                        {
                            Console.WriteLine(new string(' ', Console.WindowWidth));
                        }
                        Console.CursorTop = dct;
                        Console.WriteLine(item.Value);
                        Console.CursorTop = ct;
                    }
                }
                else
                {
                    Console.WriteLine(item.Key + new string(' ', Console.WindowWidth - item.Key.GetStringInConsoleGridWidth()));
                }
                index++;
            }
            Console.CursorVisible = false;
            var key = Console.ReadKey(true);
            if (key.Key == ConsoleKey.UpArrow)
            {
                select--;
                if (select < 0)
                {
                    select = menuitem.Count - 1;
                }
            }
            else if (key.Key == ConsoleKey.DownArrow)
            {
                select++;
                select %= menuitem.Count;
            }
            else if (key.Key == ConsoleKey.Enter)
            {
                return select;
            }
        }
    }

    public static int WriteLargerMenu(Dictionary<string, string> menuitem, int maxitems = 8, int curstartindex = 2)
    {
        int select = 0, startitem = 0, enditem = maxitems;
        void WriteItems()
        {
            int index = startitem;
            foreach (var item in menuitem.Skip(startitem).SkipLast(menuitem.Count - startitem - enditem))
            {
                if (index == select)
                {
                    Console.BackgroundColor = ConsoleColor.White;
                    Console.ForegroundColor = ConsoleColor.Black;
                    Console.WriteLine("   -->" + item.Key + new string(' ', Console.WindowWidth - (item.Key + "   -->").GetStringInConsoleGridWidth()));
                    Console.ResetColor();

                    var ct = Console.CursorTop;
                    Console.CursorTop = maxitems + 2 + curstartindex;
                    Console.WriteLine("说明：");
                    var dct = Console.CursorTop;
                    for (int i = 0; i < Console.WindowHeight - Console.CursorTop - 1; i++)
                    {
                        Console.WriteLine(new string(' ', Console.WindowWidth));
                    }
                    Console.CursorTop = dct;
                    Console.WriteLine(item.Value);
                    Console.CursorTop = ct;
                }
                else
                {
                    Console.WriteLine(item.Key + new string(' ', Console.WindowWidth - item.Key.GetStringInConsoleGridWidth()));
                }
                index++;
            }
        }


        while (true)
        {
            menuguide.Write();
            Console.CursorTop = curstartindex;
            WriteItems();

            Console.CursorVisible = false;
            var key = Console.ReadKey(true);
            if (key.Key == ConsoleKey.UpArrow)
            {
                select--;
                if (select < 0)
                    select = 0;
                if (select < startitem)
                {
                    startitem = select;
                    enditem--;
                    if (startitem < 0)
                    {
                        startitem = menuitem.Count - enditem;
                        enditem = menuitem.Count - 1;
                        select = enditem;
                    }

                }
            }
            else if (key.Key == ConsoleKey.DownArrow)
            {
                select++;
                if (select > menuitem.Count - 1)
                    select = menuitem.Count - 1;
                if (select > enditem)
                {
                    enditem = select;
                    startitem++;
                    if (enditem > menuitem.Count)
                    {
                        enditem = maxitems;
                        select = 0;
                        startitem = 0;
                    }
                }
            }
            else if (key.Key == ConsoleKey.Enter)
            {
                return select;
            }
        }
    }
}
internal class Program
{
    private const string EdgeProcessThresholeDisplayHeader = "Edge Process Threshole";

    static void Main(string[] args)
    {
        Beatmap beatmap = BeatmapParser.Parse(args[0]);
        PlaySetting setting;
        if (!Path.Exists("setting.json"))
        {
            setting = new PlaySetting();
            using var settingFs = File.Create("setting.json");
        }
        else
        {
            setting = JsonSerializer.Deserialize<PlaySetting>(File.ReadAllText("setting.json"));
        }
        Console.WriteLine("BeatMap (Copyright) Eliamrity Team, Amlight 2025");
        Console.WriteLine();

        Console.WriteLine($"Name: {beatmap.Name}");
        Console.WriteLine($"Artist: {beatmap.Artist}");
        Console.WriteLine($"BPM: {beatmap.Bpm}");
        Console.WriteLine($"Keys: {beatmap.Keys}");

        Console.WriteLine();
        Console.WriteLine($"Speed: {setting.Speed}");

        Console.WriteLine();
        int select = Menu.WriteMenu(new Dictionary<string, string>()
        {
            ["Start"] = "以当前谱面和设置开始游戏",
            ["Setting"] = "进入游戏设置界面",
            ["Exit"] = "退出游戏"
        }, Console.CursorTop);
        switch (select)
        {
            case 0:
                Gaming(beatmap, setting);
                break;
            case 1:
                setting = Setting<PlaySetting>(OtherOptionTypeProcessor, OtherOptionStringConverter, OnPropertrySelectActive, Default: setting);
                Gaming(beatmap, setting);
                break;
        }

        File.WriteAllText("setting.json", JsonSerializer.Serialize(setting, new JsonSerializerOptions() { WriteIndented = true }));
    }

    private static void OnPropertrySelectActive(string propertyName, object propertyObject)
    {
        if(propertyName == "AccLostScoreRadio")
        {
            DrawLostScoreAccArc((double)propertyObject);
        }
    }

    private static void DrawLostScoreAccArc(double lostAccSR)
    {
        const int Rsg = 20; // 行数（图高）
        
        bool showEdge = false;

        while (true)
        {
            int ett = CalculateEdgeThresholeTime(lostAccSR / 2);
            ett = ett > 99 ? 99 : ett < 10 ? 10 : ett;
            int displayETT = (int)(ett / 100.0 * (Console.WindowWidth));

            Console.Clear();

            // 提示栏
            Console.WriteLine(" Lost Score Acc Arc");
            Console.WriteLine($" Mode: {(showEdge ? "Edge Enabled" : "No Edge")}");
            Console.WriteLine(" ─────────────────────────────");
            Console.WriteLine(" Controls: [E]dge / [N]oEdge / [Q]uit");
            Console.WriteLine();

            int width = Math.Max(1, Console.WindowWidth);

            // 每次根据当前宽度重算数据（当窗口改变大小时能自适应）
            double[] noEdgeResults = Enumerable.Range(0, width)
                .Select(i => CalculateFinalAcc(i, width - 1, lostAccSR, processEdge: false))
                .ToArray();
            double[] edgeResults = Enumerable.Range(0, width)
                .Select(i => CalculateFinalAcc(i, width - 1, lostAccSR, processEdge: true))
                .ToArray();

            double neMax = noEdgeResults.Max();
            double eMax = edgeResults.Max();

            double[] results = showEdge ? edgeResults : noEdgeResults;
            double max = showEdge ? eMax : neMax;

            // 构建字符网格（Rsg x width），先全部空格
            char[][] grid = new char[Rsg][];
            for (int row = 0; row < Rsg; row++)
            {
                grid[row] = Enumerable.Repeat(' ', width).ToArray();
                double threshold = max * (1 - (row + 1) / (double)Rsg); // 从上往下
                for (int x = 0; x < width; x++)
                {
                    if (results[x] >= threshold)
                        grid[row][x] = showEdge ? '#' : '*';
                }
            }

            // 准备两个 header 文本（你程序中应已有 EdgeProcessThresholeDisplayHeader 常量/变量）
            string header1 = EdgeProcessThresholeDisplayHeader; // 保留原名
            string header2 = $"(t = {ett}, FinalAcc = {(displayETT >= 0 && displayETT < results.Length ? results[displayETT] : 0.0):F2})";

            // 按策略把 header 叠加到网格（优先左侧，不够则右侧，不行则截断或右对齐）
            int ettClamped = Math.Min(Math.Max(0, displayETT), width - 1);
            PlaceHeaderInGrid(grid, 0, header1, ettClamped);
            PlaceHeaderInGrid(grid, 1, header2, ettClamped);

            // 最后在 ett 列标记竖线（覆盖其上字符，保证可见）
            if (ettClamped >= 0 && ettClamped < width)
            {
                for (int row = 0; row < Rsg; row++)
                    grid[row][ettClamped] = '|';
            }

            // 一次性输出整张图（不会改变行数）
            for (int row = 0; row < Rsg; row++)
                Console.WriteLine(new string(grid[row]));

            // 等待按键
            ConsoleKey key = Console.ReadKey(true).Key;
            if (key == ConsoleKey.Q) break;
            if (key == ConsoleKey.E) showEdge = true;
            if (key == ConsoleKey.N) showEdge = false;
        }
    }

    private static void PlaceHeaderInGrid(char[][] grid, int row, string text, int ett)
    {
        if (grid == null || row < 0 || row >= grid.Length || string.IsNullOrEmpty(text))
            return;

        int width = grid[row].Length;
        int len = text.Length;
        if (len == 0) return;

        // 优先左侧放置
        int leftStart = ett - len;
        if (leftStart >= 0)
        {
            for (int i = 0; i < len; i++) grid[row][leftStart + i] = text[i];
            return;
        }

        // 尝试右侧放置
        int rightStart = ett + 1;
        if (rightStart + len <= width)
        {
            for (int i = 0; i < len; i++) grid[row][rightStart + i] = text[i];
            return;
        }

        // 无法完整放置：如果 header 比宽度还长，保留尾部 width 个字符；否则右对齐显示
        if (len >= width)
        {
            string tail = text.Substring(len - width, width);
            for (int i = 0; i < width; i++) grid[row][i] = tail[i];
            return;
        }

        int start = Math.Max(0, width - len); // 右对齐
        for (int i = 0; i < len; i++) grid[row][start + i] = text[i];
    }

    private static string OtherOptionStringConverter(object obj, Type t)
    {
        if (obj is Dictionary<int, string> keyBinding)
        {
            return string.Join(", ", keyBinding.Select(kv => $"{kv.Key}:{kv.Value.Replace(" ", "[SPACE]")}"));
        }
        return "";
    }

    private static object OtherOptionTypeProcessor(object obj, Type t)
    {
        if (obj is Dictionary<int, string> keyBinding)
        {
            return KeyBind(keyBinding);
        }
        return null;
    }

    static Dictionary<int, string> KeyBind(Dictionary<int,string> origin)
    {
        Console.Clear();
        IMessageOutputer message = new MessageOutputerOnWindow();
        message.Add(new MessageUnit()
        {
            Title = "F1",
            Description = "重绑定"
        });
        message.Add(new MessageUnit()
        {
            Title = "F2",
            Description = "测试模式"
        }); 
        message.Add(new MessageUnit()
        {
            Title = "← / →",
            Description = "选择需要绑定的轨道模式"
        });
        message.Add(new MessageUnit()
        {
            Title = "ESC",
            Description = "退出并返回设置的结果"
        });

        message.Write(new WarpMessageFormatter());

        int currentSettingTrack = 4;
        int heightMid = (Console.WindowHeight - 1) / 2;
        int triWidth = ((Console.WindowWidth - 1) - ((Console.WindowWidth - 1) % 3)) / 3;
        int mainTriWidth = Console.WindowWidth - 1 - triWidth * 2;

        while (true)
        {
            
            Console.SetCursorPosition(0, heightMid - 2);
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.Write(CenterString(currentSettingTrack > 2 ? (currentSettingTrack - 1).ToString() : "", triWidth));
            Console.ForegroundColor = ConsoleColor.White;
            Console.Write('<');
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.Write(CenterString(currentSettingTrack.ToString(), mainTriWidth - 2));
            Console.ForegroundColor = ConsoleColor.White;
            Console.Write('>');
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.Write(CenterString(currentSettingTrack < 9 ? (currentSettingTrack + 1).ToString() : "", triWidth));

            Console.SetCursorPosition(0, heightMid - 3);
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.Write(CenterString("Previous", triWidth));
            Console.Write(CenterString("Current", mainTriWidth));
            Console.Write(CenterString("Next", triWidth));
            Console.SetCursorPosition(0, heightMid);

            Console.Write(CenterString(string.Join('|', Enumerable.Repeat("#########", currentSettingTrack)), Console.WindowWidth - 1));
            Console.SetCursorPosition(0, heightMid + 1);
            Console.Write(new string(' ', Console.WindowWidth - 1));
            Console.CursorLeft = 0;
            int keyViewWidth = currentSettingTrack * 10 - 1;
            int leftPadding = (Console.WindowWidth - 1 - keyViewWidth) / 2;
            Console.SetCursorPosition(leftPadding, heightMid + 1);
            for(int currentKey = 0; currentKey < currentSettingTrack; currentKey++)
            {
                char keyChar = origin[currentSettingTrack][currentKey];
                Console.Write(CenterString(keyChar != ' ' ? keyChar.ToString() : "SPACE", 9));
                if(currentKey != currentSettingTrack - 1)
                    Console.Write('|');
            }
            Console.SetCursorPosition(0, heightMid + 3);
            Console.Write(new string(' ',Console.WindowWidth - 1));
            Console.SetCursorPosition(0, heightMid + 4);
            Console.Write(new string(' ', Console.WindowWidth - 1));
            Console.ResetColor();

            var key = Console.ReadKey(true);
            switch (key.Key)
            {
                case ConsoleKey.LeftArrow:
                    currentSettingTrack--;
                    if (currentSettingTrack < 2)
                        currentSettingTrack = 2;
                    break;
                case ConsoleKey.RightArrow:
                    currentSettingTrack++;
                    if (currentSettingTrack > 9)
                        currentSettingTrack = 9;
                    break;
                case ConsoleKey.F1:
                    {
                        Console.SetCursorPosition(0, heightMid);
                        StringBuilder newBinding = new();
                        bool canceled = false;
                        for(int currentBindingTrack = 0; currentBindingTrack < currentSettingTrack; currentBindingTrack++)
                        {
                            if(canceled)
                                break;
                            while (true)
                            {
                                if(canceled)
                                    break;
                                Console.SetCursorPosition(leftPadding, heightMid);
                                for (int writingTrack = 0; writingTrack < currentSettingTrack; writingTrack++)
                                {
                                    Console.Write(writingTrack == currentBindingTrack ? "vvvvvvvvv" : "#########");
                                    if(writingTrack != currentSettingTrack - 1)
                                        Console.Write('|');
                                }
                                Console.SetCursorPosition(leftPadding, heightMid + 1);
                                for (int writingDescriptionTrack = 0; writingDescriptionTrack < currentSettingTrack; writingDescriptionTrack++)
                                {
                                    if(newBinding.Length > writingDescriptionTrack)
                                    {
                                        char keyChar = newBinding[writingDescriptionTrack];
                                        Console.Write(CenterString(keyChar != ' ' ? keyChar.ToString().ToUpper() : "SPACE", 9));
                                    }
                                    else
                                    {
                                        Console.Write(CenterString("", 9));
                                    }
                                    if (writingDescriptionTrack != currentSettingTrack - 1)
                                        Console.Write('|');
                                }
                                Console.SetCursorPosition(0, heightMid + 3);
                                Console.Write(CenterString($"Press a key to bind, or Backspace to clear, or ESC to cancel for track{currentBindingTrack + 1}.", Console.WindowWidth - 1));
                                var key2 = Console.ReadKey(true);
                                if(key2.KeyChar is ' ' or (>= '0' and <= '9') or (>= 'a' and <= 'z') or (>= 'A' and <= 'Z'))
                                {
                                    newBinding.Append(key2.KeyChar.ToString().ToUpper());
                                    break;
                                }
                                else if(key2.Key == ConsoleKey.Backspace)
                                {
                                    if (currentBindingTrack == 0)
                                        continue;
                                    newBinding.Remove(currentBindingTrack - 1, 1);
                                    currentBindingTrack--;
                                }
                                else if(key2.Key == ConsoleKey.Escape)
                                {
                                    canceled = true;
                                    newBinding.Clear();
                                    break;
                                }
                                else
                                {
                                    Console.SetCursorPosition(0, heightMid + 4);
                                    Console.Write(CenterString($"Input must be a number or a letter, mustn't be a symbol", Console.WindowWidth - 1));
                                }
                            }
                        }

                        if(!canceled)
                        {
                            origin[currentSettingTrack] = newBinding.ToString();
                        }
                    }
                    break;
                case ConsoleKey.F2:
                    {
                        Console.SetCursorPosition(0, heightMid + 3);
                        Console.Write(CenterString("Testing mode: Press the bound keys to see the effect. Press ESC to exit.", Console.WindowWidth - 1));
                        while (true)
                        {
                            bool exitTesting = false;
                            if(Keyboard.IsKeyPressed(Keyboard.VirtualKeyStates.VK_ESCAPE))
                            {
                                exitTesting = true;
                            }

                            Console.SetCursorPosition(leftPadding, heightMid);
                            for(int i = 0; i < currentSettingTrack; i++)
                            {
                                char keyChar = origin[currentSettingTrack][i];
                                Keyboard.VirtualKeyStates vk;
                                if (keyChar == ' ')
                                    vk = Keyboard.VirtualKeyStates.VK_SPACE;
                                else if (char.IsLetter(keyChar))
                                    vk = (Keyboard.VirtualKeyStates)Enum.Parse(typeof(Keyboard.VirtualKeyStates), "VK_" + char.ToUpper(keyChar));
                                else if (char.IsDigit(keyChar))
                                    vk = (Keyboard.VirtualKeyStates)Enum.Parse(typeof(Keyboard.VirtualKeyStates), "VK_" + keyChar);
                                else
                                    continue;

                                DisplayKey(vk, "         ");
                                Console.ResetColor();
                                if(i != currentSettingTrack - 1)
                                    Console.Write('|');
                            }
                            if (exitTesting)
                                break;
                            Thread.Sleep(50);
                        }
                        while(Console.ReadKey(true).Key != ConsoleKey.Escape) ;
                    }
                    break;
                case ConsoleKey.Escape:
                    return origin;
            }
        }
    }
    private static string GetTruncatedString(string output)
    {
        int avaliableWidth = Console.WindowWidth - 1 - Console.CursorLeft;
        if(output.GetStringInConsoleGridWidth() > avaliableWidth)
        {
            StringBuilder sb = new();
            int currentWidth = 0;
            foreach (var ch in output)
            {
                int charWidth = ch.ToString().GetStringInConsoleGridWidth();
                if (currentWidth + charWidth > avaliableWidth - 3) // 3 for "..."
                    break;
                sb.Append(ch);
                currentWidth += charWidth;
            }
            sb.Append("...");
            return sb.ToString();
        }

        return output + new string(' ', avaliableWidth - output.GetStringInConsoleGridWidth());
    }

    static T Setting<T>(Func<object, Type, object> OtherTypeProc = null, Func<object, Type, string> OtherTypeToString = null, Action<string,object> PropertiesActiveProc = null, T Default = default) where T : class, new()
    {
        Console.Clear();
        Console.CursorVisible = false;
        IMessageOutputer message = new MessageOutputerOnWindow()
        {
            new MessageUnit()
            {
                Title = "↑",
                Description = "上一个属性"
            },
            new MessageUnit()
            {
                Title = "↓",
                Description = "下一个属性"
            },
            new MessageUnit()
            {
                Title = "Other",
                Description = "设置属性值"
            },
            new MessageUnit()
            {
                Title = "ESC",
                Description = "退出并返回设置的结果"
            },
            new MessageUnit()
            {
                Title = "+",
                Description = "将当前值提高"
            },
            new MessageUnit()
            {
                Title = "-",
                Description = "将当前值减小"
            },
            new MessageUnit()
            {
                Title = "T/F",
                Description = "将当前值设置为True/False"
            },
            new MessageUnit()
            {
                Title = "Enter",
                Description = "进入非基本类型属性设置界面 / 进入下一级预览"
            }
        };
        T result = Default == default ? new T() : Default;
        int select = 0, index = 0, selectcurleft = 0;
        PropertyInfo selectprop = default;
        Dictionary<PropertyInfo, string> stringStoragedPropertyValues = new();
        var KnownTypes = new List<Type>() { typeof(int), typeof(string), typeof(bool), typeof(char), typeof(double) };
        bool IsKnownType(Type t) => KnownTypes.Contains(t);
        var wmf = new WarpMessageFormatter();
        while (true)
        {
            message.Write(wmf);
            Console.SetCursorPosition(0, 0);
            
            index = 0;
            foreach (var prop in typeof(T).GetProperties())
            {
                if (index == select)
                {
                    selectprop = prop;
                    Console.BackgroundColor = ConsoleColor.White;
                    Console.ForegroundColor = ConsoleColor.Black;
                    string wout = "";
                    string name;
                    if(prop.GetCustomAttribute<SettingDisplayName>() is SettingDisplayName displayName)
                        name = displayName.Name;
                    else
                        name = prop.Name;
                    Console.Write($"{name,-50}");
                    if (IsKnownType(prop.PropertyType))
                        if(prop.PropertyType == typeof(double))
                        {
                            stringStoragedPropertyValues.TryAdd(prop, prop.GetValue(result)?.ToString() ?? "0.00");
                            wout = $"{GetTruncatedString(stringStoragedPropertyValues[prop])}";
                        }
                        else
                        {
                            wout = $"{GetTruncatedString(prop.GetValue(result).ToString())}";
                        }
                    else
                        wout = $"{GetTruncatedString(OtherTypeToString(prop.GetValue(result), prop.PropertyType))}";
                    Console.WriteLine($"{wout}");
                    Console.ResetColor();
                    selectcurleft = wout.GetStringInConsoleGridWidth();
                }
                else
                {
                    string wout = "";
                    string name;
                    if (prop.GetCustomAttribute<SettingDisplayName>() is SettingDisplayName displayName)
                        name = displayName.Name;
                    else
                        name = prop.Name;
                    Console.Write($"{name,-50}");
                    if (IsKnownType(prop.PropertyType))
                        if (prop.PropertyType == typeof(double))
                        {
                            stringStoragedPropertyValues.TryAdd(prop, prop.GetValue(result)?.ToString() ?? "0.00");
                            wout = $"{GetTruncatedString(stringStoragedPropertyValues[prop])}";
                        }
                        else
                        {
                            wout = $"{GetTruncatedString(prop.GetValue(result).ToString())}";
                        }
                    else
                        wout = $"{GetTruncatedString(OtherTypeToString(prop.GetValue(result), prop.PropertyType))}";
                    Console.WriteLine($"{wout}");
                    Console.ResetColor();
                }
                index++;
            }
            
            var key = Console.ReadKey(true);
            switch (key.Key)
            {
                case ConsoleKey.DownArrow:
                    select++;
                    if (select == typeof(T).GetProperties().Length)
                    {
                        select = typeof(T).GetProperties().Length - 1;
                    }
                    break;
                case ConsoleKey.UpArrow:
                    select--;
                    if (select == -1)
                    {
                        select = 0;
                    }
                    break;
                case ConsoleKey.Escape:
                    ApplyStringStoragedValue(result, stringStoragedPropertyValues);
                    return result;
                case ConsoleKey.Enter:
                    if(!IsKnownType(selectprop.PropertyType))
                        selectprop.SetValue(result, OtherTypeProc(selectprop.GetValue(result), selectprop.PropertyType));
                    else
                    {
                        ApplyStringStoragedValue(result, stringStoragedPropertyValues);
                        PropertiesActiveProc?.Invoke(selectprop.Name, selectprop.GetValue(result));
                    }
                    Console.Clear();
                    break;
                default:
                    SwitchHelper.Switch<Type>(
                        selectprop.PropertyType,
                        SwitchHelper.Case<Type>(typeof(int), (_) =>
                        {
                            switch (key.Key)
                            {
                                case ConsoleKey.Backspace:
                                    selectprop.SetValue(result, (int)((int)(selectprop.GetValue(result) ?? 0) / 10));
                                    break;
                                case ConsoleKey.Add:
                                    selectprop.SetValue(result, (int)(selectprop.GetValue(result) ?? 0) + 1);
                                    break;
                                case ConsoleKey.Subtract:
                                    selectprop.SetValue(result, (int)(selectprop.GetValue(result) ?? 0) - 1);
                                    break;
                                default:
                                    if (key.Key >= ConsoleKey.D0 && key.Key <= ConsoleKey.D9 ||
                                    key.Key >= ConsoleKey.NumPad0 && key.Key <= ConsoleKey.NumPad9)
                                        selectprop.SetValue(result, (int)(selectprop.GetValue(result) ?? 0) * 10 + (key.KeyChar - 48));
                                    break;
                            }
                        }),
                        SwitchHelper.Case<Type>(typeof(double), (_) =>
                        {
                            switch (key.Key)
                            {
                                case ConsoleKey.Backspace:
                                    if (stringStoragedPropertyValues[selectprop].Length == 0)
                                    {
                                        stringStoragedPropertyValues[selectprop] = "0";
                                        break;
                                    }
                                    stringStoragedPropertyValues[selectprop] = stringStoragedPropertyValues[selectprop][..^1];
                                    break;
                                case ConsoleKey.Add:
                                    if (stringStoragedPropertyValues[selectprop].Length == 0)
                                        stringStoragedPropertyValues[selectprop] = "0.05";
                                    else if (stringStoragedPropertyValues[selectprop] == "-")
                                        break;
                                    stringStoragedPropertyValues[selectprop] = (double.Parse(stringStoragedPropertyValues[selectprop]) + 0.05).ToString("F2");
                                    break;
                                case ConsoleKey.Subtract:
                                    if (stringStoragedPropertyValues[selectprop].Length == 0)
                                        goto default; // start with '-' meaning this is a negative number
                                    else if (stringStoragedPropertyValues[selectprop] == "-")
                                        stringStoragedPropertyValues[selectprop] = "-0.05";
                                    else 
                                        stringStoragedPropertyValues[selectprop] = (double.Parse(stringStoragedPropertyValues[selectprop]) - 0.05).ToString("F2");
                                    break;
                                default:
                                    if (((key.KeyChar > '9' || key.KeyChar < '0') && key.KeyChar != '.' && key.KeyChar != '-') || (key.KeyChar == '.' && (stringStoragedPropertyValues[selectprop].Contains('.') || !stringStoragedPropertyValues[selectprop].Any(x => '0' <= x && x <= '9'))))
                                        return;
                                    else if ((key.KeyChar == '-' && stringStoragedPropertyValues[selectprop].StartsWith('-')))
                                        goto case ConsoleKey.Subtract;

                                    if ((key.KeyChar != '.' && stringStoragedPropertyValues[selectprop] == "0"))
                                        stringStoragedPropertyValues[selectprop] = key.KeyChar.ToString();
                                    else
                                        stringStoragedPropertyValues[selectprop] += key.KeyChar;
                                    break;
                            }
                        }),
                        SwitchHelper.Case<Type>(typeof(string), (_) =>
                        {
                            switch (key.Key)
                            {
                                case ConsoleKey.Backspace:
                                    if (((string)selectprop.GetValue(result)).Length == 0)
                                        break;
                                    selectprop.SetValue(result, new string(((string)selectprop.GetValue(result)).ToCharArray()[..^1]));
                                    break;
                                default:
                                    if (key.KeyChar < 32)
                                        return;
                                    var string2list = ((string)selectprop.GetValue(result) ?? "").ToList();
                                    string2list.Add(key.KeyChar);
                                    StringBuilder sb = new StringBuilder();
                                    string2list.ForEach(x => sb.Append(x));
                                    selectprop.SetValue(result, sb.ToString());
                                    break;
                            }
                        }),
                        SwitchHelper.Case<Type>(typeof(char), (_) =>
                        {
                            switch (key.Key)
                            {
                                case ConsoleKey.Backspace:
                                    selectprop.SetValue(result, ' ');
                                    break;
                                default:
                                    selectprop.SetValue(result, key.KeyChar);
                                    break;
                            }
                        }),
                        SwitchHelper.Case<Type>(typeof(bool), (_) =>
                        {
                            switch (key.Key)
                            {
                                case ConsoleKey.T:
                                    selectprop.SetValue(result, true);
                                    break;
                                case ConsoleKey.F:
                                    selectprop.SetValue(result, false);
                                    break;
                            }
                        })
                    );
                    break;
            }
        }

        static void ApplyStringStoragedValue<T>(T result, Dictionary<PropertyInfo, string> stringStoragedPropertyValues) where T : class, new()
        {
            foreach (var kvp in stringStoragedPropertyValues)
            {
                if (kvp.Key.PropertyType == typeof(double) && kvp.Value.EndsWith('.'))
                    kvp.Key.SetValue(result, Convert.ChangeType(kvp.Value + "0", kvp.Key.PropertyType));
                else
                    kvp.Key.SetValue(result, Convert.ChangeType(kvp.Value, kvp.Key.PropertyType));
            }
        }
    }

    static int GetMaxComboScore(int noteCount)
    {
        int result = 0;
        while (--noteCount>=0)
            result += noteCount + 1;

        return result;
    }
    static void Gaming(Beatmap beatmap, PlaySetting setting)
    {
        Console.Clear();
        Console.CursorVisible = false;

        int RowCountInScreen = setting.PanelHeight;
        const int FPS = 60;
        const int JudgeTime = 200;// ms
        int StatePanelWidth = 20; // Width of the state panel
        int KeyWidth = setting.KeyWidth;

        string GetKeyString(char keyForegeoundChar)
        {
            return new string(keyForegeoundChar, KeyWidth);
        }

        double msPerBeat = 60000 / beatmap.Bpm;
        bool updating = false;

        // 每个轨道的判定时间
        List<(TimeSpan,NoteType)?>[] trackJudgeTimes = [.. Enumerable.Range(0, beatmap.Keys).Select(i => beatmap.Notes.Where(x => x.Track == i).Select(x => (TimeSpan.FromMilliseconds(x.TimeMs), x.Type)).Cast<(TimeSpan,NoteType)?>().ToList()).Reverse()];
        int keys = beatmap.Keys;

        DateTime startJudgeTime;
        const int ClickedScore = 1000;
        const int AccScore = 500;
        int score = 0;
        double offsetLostScoreRadio = setting.AccLostScoreRadio;
        bool showMissMessage = setting.ShowMissMessage;
        int maxScore = beatmap.Notes.Count * ClickedScore + beatmap.Notes.Where(x => x.Type == NoteType.Tap).Count() * AccScore + GetMaxComboScore(beatmap.Notes.Count);

        int notesCount = beatmap.Notes.Count;
        int clickedNoteCount = 0;
        int missedNoteCount = 0;
        int combo = 0;
        int maxCombo = 0;

        List<bool> oldKeyStates = Enumerable.Repeat(false, keys).ToList();

        // 自动模式
        bool autoPlay = setting.AutoPlay;
        bool unperfectAuto = setting.UnperfectAuto;
        double unperfectRadio = setting.UnperfectRadio;
        int unperfectMax = (int)(200 * unperfectRadio) + 1;

        int usingSpeed = setting.Speed;
        int displayDuration = (int)((decimal)RowCountInScreen / usingSpeed * 1000);
        double rowDisplayDuration = (double)displayDuration / RowCountInScreen;
        startJudgeTime = DateTime.Now + TimeSpan.FromMilliseconds(displayDuration);

        int judgeOffset = setting.JudgeOffset;

        CancellationTokenSource cts = new();
        bool canceling = false;

        TimeSpan pausedTimeSum = new(0);
        DateTime pauseTime = default;
        TimeSpan nowTime = default;
        bool oldF1Pressed = false;
        bool isPaused = false;

        
        List<(int, string)> judgeStates = new();
        // 当前时刻 nowMs 对应的判定线 floorUnits（与 NoteFloorUnits 同量纲）
        double CurrentFloorUnits(double nowMs, List<SpeedSegment> segs, List<double> cum)
        {
            // 段二分查找
            int lo = 0, hi = segs.Count - 1, idx = 0;
            while (lo <= hi)
            {
                int mid = (lo + hi) >> 1;
                if (segs[mid].StartTimeMs <= nowMs) { idx = mid; lo = mid + 1; }
                else hi = mid - 1;
            }
            var s = segs[idx];
            return cum[idx] + (nowMs - s.StartTimeMs) * s.Speed;
        }

        // 判定方法（统一处理 Auto/手动）
        bool JudgeTrack(int trackIndex, bool isPressed, string name)
        {
            if (trackJudgeTimes[trackIndex].FirstOrDefault(x =>
                Math.Abs(x.Value.Item1.TotalMilliseconds - nowTime.TotalMilliseconds - judgeOffset) < JudgeTime && ((x.Value.Item2 == NoteType.Tap && !isPressed) || (x.Value.Item2 == NoteType.Drag))) is (TimeSpan judgeTS, NoteType type))
            {
                int lostScore = 0;
                if (autoPlay)
                {
                    lostScore = unperfectAuto ? (int)(Random.Shared.Next(Math.Abs(unperfectMax)) * offsetLostScoreRadio) : 0;
                }
                else
                {
                    var delta = (nowTime - judgeTS).TotalMilliseconds;
                    lostScore = (int)(Math.Abs(delta) * offsetLostScoreRadio);
                }

                score += ClickedScore + (type == NoteType.Tap? Math.Max(0,AccScore - lostScore) : 0);
                clickedNoteCount++;
                trackJudgeTimes[trackIndex].Remove((judgeTS,type));
                judgeStates.Add((trackIndex, $"{name} {(isPressed?"Holded!":"Clicked")}{(autoPlay ? " (Auto)" : "")} -{(type == NoteType.Tap ? (Math.Max(0, AccScore - lostScore) == 0? 500 : lostScore) : 0)}"));
                combo++;
                if (combo > maxCombo) maxCombo = combo;
                score += combo;
                return true;
            }

            return false;
        }

        var keybindings = setting.KeyBinding[beatmap.Keys];

        // 定时器渲染
        var updater = new Timer((_) =>
        {
            if (updating)
                return;
            updating = true;
            // 暂停 / 跳过
            var F1Pressed = Keyboard.IsKeyPressed(Keyboard.VirtualKeyStates.VK_F1);
            if (!oldF1Pressed && F1Pressed)
            {
                isPaused = !isPaused;
                if (isPaused)
                    pauseTime = DateTime.Now;
                else
                    pausedTimeSum += DateTime.Now - pauseTime;
            }
            oldF1Pressed = F1Pressed;

            if (Keyboard.IsKeyPressed(Keyboard.VirtualKeyStates.VK_F2))
            {
                lock (cts)
                    cts.Cancel();
            }

            if (isPaused)
            {
                Console.SetCursorPosition(0, Console.WindowHeight - 1 - 0);
                Console.Write(CenterString("Pausing - Press `F1` to resume ", Console.WindowWidth - 1));
                updating = false;
                return;
            }

            // 当前时间
            nowTime = DateTime.Now - startJudgeTime - pausedTimeSum;
            if (trackJudgeTimes.Sum(x => x.Count) == 0 && !canceling)
            {
                lock(cts)
                    cts.CancelAfter(1000);
                canceling = true;
                updating = false;
                return;
            }

            // 界面绘制

            // 连击
            Console.SetCursorPosition(0, (Console.WindowHeight - 1) / 2);
            var comboString = combo.ToString();
            if (comboString.Length % 2 == 0)
                comboString = comboString.Insert(comboString.Length / 2, " ");
            Console.Write(CenterString(comboString, Console.WindowWidth - 1));
            Console.SetCursorPosition(0, (Console.WindowHeight - 1) / 2 + 1);
            Console.Write(CenterString("Combo", Console.WindowWidth - 1));


            List<string> paneContent = Enumerable.Repeat(new string(' ', KeyWidth * keys), RowCountInScreen).ToList();
            // === 面板绘制：基于 floorUnits 的流动渲染 ===

            double currentFloor = CurrentFloorUnits(nowTime.TotalMilliseconds, beatmap.SpeedSegments, beatmap.SegmentCumUnits);

            // ratioUnits -> 屏幕行换算因子（注意 setting.Speed 单位为“行/秒”）
            double unitsToRows = setting.Speed / 1000.0;

            for (int i = 0; i < beatmap.Notes.Count; i++)
            {
                var note = beatmap.Notes[i];

                // 相对判定线的行数（>0 表示在判定线上方、会往下掉）
                double relRows = (beatmap.NoteFloorUnits[i] - currentFloor) * unitsToRows;

                // 仅渲染落在屏幕内的音符
                if (relRows <= 0 || relRows > RowCountInScreen || note.TimeMs < nowTime.TotalMilliseconds) continue;

                int displayRowIndex = RowCountInScreen - 1 - (int)Math.Floor(relRows);
                var line = paneContent[displayRowIndex].ToCharArray();
                int pos = (keys - note.Track - 1) * KeyWidth;
                for (int k = 0; k < KeyWidth; k++) line[pos + k] = note.Type == NoteType.Drag?'-':'#';
                paneContent[displayRowIndex] = new string(line);
            }

            Console.SetCursorPosition(0, 1);
            Console.Write(string.Join(Environment.NewLine, paneContent));
            Console.SetCursorPosition(0, RowCountInScreen + 1);

            // 判定逻辑
            if (autoPlay)
            {
                Console.ResetColor();
                Enumerable.Range(0, beatmap.Keys)
                          .Select(ti => JudgeTrack(ti, false, $"Track {ti + 1}"))
                          .Aggregate(new ContentRender(), (x,y) =>
                          {
                              x.Add(new ContentBlock() { Content = GetKeyString('-'), Background = y ? ConsoleColor.White : ConsoleColor.Black });
                              return x;
                          })
                          .Render();
            }
            else
            {
                var currentState = new List<bool>(keys);
                for (int i = 0; i < keys; i++)
                {
                    var key = (Keyboard.VirtualKeyStates)(int)keybindings[i];
                    currentState.Add(DisplayKey(key, GetKeyString('-')));
                }
                for (int i = 0; i < keys; i++)
                {
                    if (!oldKeyStates[i] && currentState[i])
                    {
                        Debug.WriteLine($"Track{i + 1} Pressed!");
                        JudgeTrack(i, false, $"Track {i + 1}");
                    }
                    if (oldKeyStates[i]&&currentState[i])
                    {
                        Debug.WriteLine($"Track{i + 1} Holded!");
                        JudgeTrack(i, true, $"Track {i + 1}");
                    }
                }
                oldKeyStates = currentState;
            }
            
            
            // Miss判定
            for (int t = 0; t < keys; t++)
            {
                var misses = trackJudgeTimes[t]
                    .Where(x => (nowTime - x.Value.Item1).TotalMilliseconds - judgeOffset > JudgeTime)
                    .ToList();
                foreach (var miss in misses)
                {
                    trackJudgeTimes[t].Remove(miss);
                    missedNoteCount++;
                    combo = 0;
                    if (showMissMessage)
                        judgeStates.Add((-t, $"Track {t + 1} Miss!"));
                }
            }

            Console.ResetColor();
            // 分数面板
            Console.SetCursorPosition(Console.WindowWidth - 10, 1);
            Console.Write(score.ToString().PadLeft(9, '0'));
            Console.SetCursorPosition(Console.WindowWidth - 10, 2);
            Console.Write($"{(double)score / maxScore * 100:F2}%".PadLeft(9, ' '));
            Console.SetCursorPosition(Console.WindowWidth - 14, 3);
            Console.Write("Max:" + maxScore.ToString().PadLeft(9, '0'));

            Console.ResetColor();

            Console.SetCursorPosition(0, Console.WindowHeight - 1 - 0);
            Console.Write(CenterString("Playing - Press `F1` to pause ", Console.WindowWidth - 1));
            Console.SetCursorPosition(0, Console.WindowHeight - 1 - 1);
            Console.Write(CenterString("Press `F2` to skip to result view ", Console.WindowWidth - 1));
            
            Console.SetCursorPosition(0, 0);
            int halfWidth = (Console.WindowWidth - 1) / 2;
            Console.ForegroundColor = ConsoleColor.Black;
            Console.BackgroundColor = ConsoleColor.Yellow;
            Console.Write(CenterString("Clicked: " + clickedNoteCount.ToString(), halfWidth));
            Console.BackgroundColor = ConsoleColor.Red;
            Console.Write(CenterString("Missed: " + missedNoteCount.ToString(), Console.WindowWidth - 1 - halfWidth));
            Console.ResetColor();

            // 状态显示
            StatePanelWidth = judgeStates.Count > 0 ? Math.Max(20, judgeStates.Max(x => x.Item2.GetStringInConsoleGridWidth())) : 20;
            Console.SetCursorPosition(Console.WindowWidth - 1 - StatePanelWidth, Console.WindowHeight - 10);
            Console.ForegroundColor = ConsoleColor.Black;
            foreach (var judgeState in judgeStates.TakeLast(8))
            {
                Console.BackgroundColor = (judgeState.Item1 + 1) switch
                {
                    <= 0 => ConsoleColor.Red,
                    1 => ConsoleColor.Blue,
                    2 => ConsoleColor.Green,
                    3 => ConsoleColor.Yellow,
                    4 => ConsoleColor.Gray,
                    5 => ConsoleColor.Cyan,
                    6 => ConsoleColor.Magenta,
                    7 => ConsoleColor.White,
                    8 => ConsoleColor.DarkBlue,
                    9 => ConsoleColor.DarkCyan,
                };
                Console.Write(CenterString(judgeState.Item2, StatePanelWidth));
                Console.SetCursorPosition(Console.WindowWidth - 1 - StatePanelWidth, Console.CursorTop + 1);
            }

            Console.ResetColor();
            updating = false;
        }, null, 0, (int)(1000.0 / FPS * 1.5));

        while (!cts.IsCancellationRequested) { }
        updater.Dispose();

        Thread.Sleep(100);
        Console.SetCursorPosition(0, Console.WindowHeight - 1 - 1);
        Console.Write(new string(' ', Console.WindowWidth - 1));
        Console.SetCursorPosition(0, Console.WindowHeight - 1);
        Console.Write(CenterString("Press `F4` to exit... ", Console.WindowWidth - 1));

        while (Console.ReadKey(true).Key != ConsoleKey.F4) { }
        Console.Clear();
        ShowResult(beatmap, setting, score, offsetLostScoreRadio, maxScore, notesCount, clickedNoteCount);
    }

    private static void ShowResult(Beatmap beatmap, PlaySetting setting, int score, double offsetLostScoreRadio, int maxScore, int notesCount, int clickedNoteCount)
    {
        var acc = (double)score / maxScore * 100;
        var finalAcc = CalculateFinalAcc(score, maxScore, offsetLostScoreRadio);

        Console.WriteLine(finalAcc > 80 ? "Track Completed" : "Track Lost");
        Console.WriteLine();
        Console.WriteLine(beatmap.Name);
        Console.WriteLine(beatmap.Artist);
        Console.WriteLine($"{score} : {finalAcc:F2}%");
        Console.WriteLine($"Rank {finalAcc switch
        {
            < 0 => "Auto Fail",
            < 60.0 => "C",
            < 70.0 => "B",
            < 75.0 => "BB",
            < 77.0 => "BBB",
            < 80.0 => "A",
            < 94.0 => "AA",
            < 97.0 => "AAA",
            < 98.0 => "S",
            < 98.5 => "S+",
            < 99 => "SS",
            < 99.5 => "SS+",
            < 100.0 => "SSS",
            _ => "SSS+"
        }}");
        Console.WriteLine($"Clicked {clickedNoteCount}  Missed {notesCount - clickedNoteCount} Accuracy {acc:F2}%");
        Console.WriteLine();
        int select = Menu.WriteMenu(new Dictionary<string, string>()
        {
            ["Restart"] = "重新游玩这张谱面",
            ["Setting"] = "重新设置选项后游玩",
            ["Exit"] = "退出"
        }, Console.CursorTop);

        switch (select)
        {
            case 0:
                Gaming(beatmap, setting);
                return;
            case 1:
                setting = Setting<PlaySetting>(OtherOptionTypeProcessor, OtherOptionStringConverter, OnPropertrySelectActive, Default: setting);
                Gaming(beatmap, setting);
                return;
        }
    }

    public static double LinearDuration(double start, double end, double step, double totalStep)
        => start + (end - start) * (step / totalStep);

    public static double CalculateFinalAcc(
        int score,
        int maxScore,
        double offsetLostScoreRadio = 0.25,
        double powerK = 2, // 可以调节曲线陡峭度
        bool processEdge = true)
    {
        offsetLostScoreRadio /= 2;
        int ThresholeTime = CalculateEdgeThresholeTime(offsetLostScoreRadio);
        ThresholeTime = ThresholeTime > 99 ? 99 : ThresholeTime < 10 ? 10 : ThresholeTime;
        double edgeThreshold;
        if (processEdge)
            edgeThreshold = CalculateFinalAccByAcc((double)ThresholeTime, offsetLostScoreRadio, powerK, false, ThresholeTime, 0);
        else
            edgeThreshold = 0;
        if (maxScore <= 0) return 0;
        if (offsetLostScoreRadio < 0) offsetLostScoreRadio = 0;
        double acc = (double)score / maxScore * 100;
        return CalculateFinalAccByAcc(acc, offsetLostScoreRadio, powerK, processEdge, ThresholeTime, edgeThreshold);
    }

    private static double CalculateFinalAccByAcc(double acc, double offsetLostScoreRadio, double powerK, bool processEdge, int ThresholeTime, double edgeThreshold)
    {
        // 使用sigmoid型曲线，让偏移更大时补偿明显
        double factor = Math.Pow(offsetLostScoreRadio, powerK);
        double compensation = (100 - acc) * factor / (factor + 0.5); // 归一化，避免过度补偿

        double finalAcc = acc + compensation;

        if (processEdge && acc <= ThresholeTime)
        {
            finalAcc = LinearDuration(0, edgeThreshold, acc, ThresholeTime);
        }
        return finalAcc;
    }

    private static int CalculateEdgeThresholeTime(double offsetLostScoreRadio)
    {
        return (int)(Math.Pow((2 * offsetLostScoreRadio), 2) * 30);
    }

    static string CenterString(string str, int width, char fillChar = ' ')
    {
        if (str.Length > width)
            return str;
        int leftPadding = (width - str.Length) / 2;
        int rightPadding = width - str.Length - leftPadding;
        return $"{new string(fillChar, leftPadding)}{str}{new string(fillChar, rightPadding)}";
    }
    static bool DisplayKey(Keyboard.VirtualKeyStates k, string keyDisplayString)
    {
        bool pressState = Keyboard.IsKeyPressed(k);
        Console.BackgroundColor = pressState ? ConsoleColor.White : ConsoleColor.Black;
        Console.Write(keyDisplayString);

        return pressState;
    }
}
