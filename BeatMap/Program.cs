using EUtility.ConsoleEx.Message;
using EUtility.StringEx.StringExtension;
using PInvoke.Net;
using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Transactions;

namespace BeatMap;

public class Note
{
    public int Track;      // 0~3
    public double TimeMs;  // 判定时间
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
    public int Bpm { get; set; }
    public List<Note> Notes { get; set; } = new();
    public List<SpeedSegment> SpeedSegments { get; set; } = new();
    // ✅ 新增：与 Notes 同索引对齐的 floorUnits（时间∫速度比）
    // 单位：ms * ratio（无量纲“单位时间积”，渲染时再乘 (setting.Speed / 1000) 变成“屏幕行”）
    public List<double> NoteFloorUnits { get; set; } = new();

    // ✅ 新增：每个变速段起点的“累计 floorUnits”
    // 用来 O(1) 求 t 时刻的 floorUnits：cum[i] + (t - seg[i].Start)*seg[i].Speed
    public List<double> SegmentCumUnits { get; set; } = new();
}

public partial class BeatmapParser
{
    [GeneratedRegex(@"\[(.*?)\]")]
    public static partial Regex MatchAttributes();

    public static Beatmap Parse(string filePath)
    {
        string content = File.ReadAllText(filePath);
        string[] parts = content.Split(";");
        if (parts.Length < 4)
            throw new ArgumentException("Invalid beatmap format.");

        Beatmap map = new()
        {
            Name = parts[0].Trim(),
            Artist = parts[1].Trim(),
            Bpm = int.Parse(parts[2].Trim())
        };

        double msPerBeat = 60000.0 / map.Bpm;
        string[] rowStrings = parts[3].Trim().Split(",");
        for (int rowIndex = 0; rowIndex < rowStrings.Length; rowIndex++)
        {
            string rowString = rowStrings[rowIndex].Trim();
            if (string.IsNullOrWhiteSpace(rowString)) continue;

            var attributes = MatchAttributes().Match(rowString);
            string keyString = attributes.Success ? rowString.Replace(attributes.Value, "") : rowString;

            double judgeTime = rowIndex * msPerBeat;

            // 解析按键
            if (keyString.Contains('d')) map.Notes.Add(new Note { Track = 0, TimeMs = judgeTime });
            if (keyString.Contains('f')) map.Notes.Add(new Note { Track = 1, TimeMs = judgeTime });
            if (keyString.Contains('j')) map.Notes.Add(new Note { Track = 2, TimeMs = judgeTime });
            if (keyString.Contains('k')) map.Notes.Add(new Note { Track = 3, TimeMs = judgeTime });

            if (int.TryParse(keyString, out int integer))
            {
                if ((integer & 0x1) != 0) map.Notes.Add(new Note { Track = 3, TimeMs = judgeTime });
                if ((integer & 0x2) != 0) map.Notes.Add(new Note { Track = 2, TimeMs = judgeTime });
                if ((integer & 0x4) != 0) map.Notes.Add(new Note { Track = 1, TimeMs = judgeTime });
                if ((integer & 0x8) != 0) map.Notes.Add(new Note { Track = 0, TimeMs = judgeTime });
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
    public double AccLostScoreRadio { get; set; } = 0.24;
    public bool ShowMissMessage { get; set; } = false;
    public bool AutoPlay { get; set; } = false;
    public bool UnperfectAuto { get; set; } = false;
    public double UnperfectRadio { get; set; } = 0.2;
    public int KeyWidth { get; set; } = 6; // Width of each key display in characters
    public int PanelHeight { get; set; } = 16; // Height of the game panel in rows
}

internal class Program
{
    static void Main(string[] args)
    {
        Beatmap beatmap = BeatmapParser.Parse(args[0]);
        Console.WriteLine("BeatMap (Copyright) Eliamrity Team, Amlight 2025");
        Console.WriteLine();

        Console.WriteLine($"Name: {beatmap.Name}");
        Console.WriteLine($"Artist: {beatmap.Artist}");
        Console.WriteLine($"BPM: {beatmap.Bpm}");
        Console.WriteLine("Press `Enter` to start");
        Console.WriteLine("Press `Esc` to exit");
        Console.WriteLine("Press `F1` go to setting");

        Console.WriteLine();
        Console.WriteLine("Speed: 16");
        var key = Console.ReadKey(true);
        switch(key.Key)
        {
            case ConsoleKey.Enter:
                Gaming(beatmap, new PlaySetting());
                break;
            case ConsoleKey.F1:
                Gaming(beatmap, Setting<PlaySetting>(null));
                break;
        }
    }

    static T Setting<T>(Func<object, Type, object> OtherTypeProc, Func<object, Type, string> OtherTypeToString = null, T Default = default) where T : class, new()
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
            }
        };
        T result = Default == default ? new T() : Default;
        int select = 0, index = 0, selectcurleft = 0;
        PropertyInfo selectprop = default;
        Dictionary<PropertyInfo, string> stringStoragedPropertyValues = new();
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
                    if (new List<Type>() { typeof(int), typeof(string), typeof(bool), typeof(char), typeof(double) }.Contains(prop.PropertyType))
                        if(prop.PropertyType == typeof(double))
                        {
                            stringStoragedPropertyValues.TryAdd(prop, prop.GetValue(result)?.ToString() ?? "0.00");
                            wout = $"{prop.Name,-50}{stringStoragedPropertyValues[prop]}";
                        }
                        else
                        {
                            wout = $"{prop.Name,-50}{prop.GetValue(result)}";
                        }
                    else
                        wout = $"{prop.Name,-50}{OtherTypeToString(prop.GetValue(result), prop.PropertyType)}";
                    Console.WriteLine($"{wout}{new string(' ', Console.WindowWidth - wout.GetStringInConsoleGridWidth())}");
                    Console.ResetColor();
                    selectcurleft = wout.GetStringInConsoleGridWidth();
                }
                else
                {
                    string wout = "";
                    if (new List<Type>() { typeof(int), typeof(string), typeof(bool), typeof(char), typeof(double) }.Contains(prop.PropertyType))
                        if (prop.PropertyType == typeof(double))
                        {
                            stringStoragedPropertyValues.TryAdd(prop, prop.GetValue(result)?.ToString() ?? "0.00");
                            wout = $"{prop.Name,-50}{stringStoragedPropertyValues[prop]}";
                        }
                        else
                        {
                            wout = $"{prop.Name,-50}{prop.GetValue(result)}";
                        }
                    else
                        wout = $"{prop.Name,-50}{OtherTypeToString(prop.GetValue(result), prop.PropertyType)}";
                    Console.WriteLine($"{wout}{new string(' ', Console.WindowWidth - wout.GetStringInConsoleGridWidth())}");
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
                    foreach (var kvp in stringStoragedPropertyValues)
                    {
                        if(kvp.Key.PropertyType == typeof(double) && kvp.Value.EndsWith('.'))
                            kvp.Key.SetValue(result, Convert.ChangeType(kvp.Value + "0", kvp.Key.PropertyType));
                        else
                            kvp.Key.SetValue(result, Convert.ChangeType(kvp.Value, kvp.Key.PropertyType));
                    }
                    return result;
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
                        }),
                        SwitchHelper.Default<Type>((_) =>
                        {
                            selectprop.SetValue(result, OtherTypeProc(selectprop.GetValue(result), selectprop.PropertyType));
                        })
                    );
                    break;
            }
        }
    }


    static void Gaming(Beatmap beatmap, PlaySetting setting)
    {
        Console.Clear();
        Console.CursorVisible = false;

        int RowCountInScreen = setting.PanelHeight;
        const int FPS = 60;
        const int JudgeTime = 110; // ms
        int StatePanelWidth = 20; // Width of the state panel
        int KeyWidth = setting.KeyWidth;

        string GetKeyString(char keyForegeoundChar)
        {
            return new string(keyForegeoundChar, KeyWidth);
        }

        double msPerBeat = 60000 / beatmap.Bpm;
        bool updating = false;

        // 每个轨道的判定时间
        List<TimeSpan?>[] trackJudgeTimes = Enumerable.Range(0, 4)
            .Select(i => beatmap.Notes.Where(x => x.Track == i).Select(x => TimeSpan.FromMilliseconds(x.TimeMs)).Cast<TimeSpan?>().ToList())
            .ToArray();

        DateTime startJudgeTime;
        const int ClickedScore = 1000;
        int score = 0;
        double offsetLostScoreRadio = setting.AccLostScoreRadio;
        bool showMissMessage = setting.ShowMissMessage;
        int maxScore = beatmap.Notes.Count * ClickedScore;

        int notesCount = maxScore / ClickedScore;
        int clickedNoteCount = 0;

        // 键盘状态
        bool ods = false, ofs = false, ojs = false, oks = false;

        // 自动模式
        bool autoPlay = setting.AutoPlay;
        bool unperfectAuto = setting.UnperfectAuto;
        double unperfectRadio = setting.UnperfectRadio;
        int unperfectMax = (int)(200 * unperfectRadio) + 1;

        int usingSpeed = setting.Speed;
        int displayDuration = (int)((decimal)RowCountInScreen / usingSpeed * 1000);
        double rowDisplayDuration = (double)displayDuration / RowCountInScreen;
        startJudgeTime = DateTime.Now + TimeSpan.FromMilliseconds(displayDuration);
        CancellationTokenSource cts = new();

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
        void JudgeTrack(int trackIndex, int cursorLeft, string name, bool pressed)
        {
            if (trackJudgeTimes[trackIndex].FirstOrDefault(x =>
                Math.Abs(x.Value.TotalMilliseconds - nowTime.TotalMilliseconds) < JudgeTime) is TimeSpan judgeTS)
            {
                int lostScore = 0;
                if (autoPlay)
                {
                    lostScore = unperfectAuto ? (int)(Random.Shared.Next(Math.Abs(unperfectMax)) * offsetLostScoreRadio) : 0;
                }
                else if (pressed)
                {
                    var delta = (nowTime - judgeTS).TotalMilliseconds;
                    lostScore = (int)(Math.Abs(delta) * offsetLostScoreRadio);
                }

                if (autoPlay || pressed)
                {
                    score += ClickedScore - lostScore;
                    clickedNoteCount++;
                    trackJudgeTimes[trackIndex].Remove(judgeTS);
                    judgeStates.Add((trackIndex + 1, $"{name} Clicked!{(autoPlay ? " (Auto)" : "")} -{lostScore}"));
                }
            }
        }

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
                updating = false;
                return;
            }

            // 当前时间
            nowTime = DateTime.Now - startJudgeTime - pausedTimeSum;
            if (trackJudgeTimes.Sum(x => x.Count) == 0)
            {
                cts.Cancel();
                updating = false;
                return;
            }

            // 界面绘制
            List<string> paneContent = Enumerable.Repeat(new string(' ', KeyWidth * 4), RowCountInScreen).ToList();
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
                if (relRows <= 0 || relRows > RowCountInScreen) continue;

                int displayRowIndex = RowCountInScreen - 1 - (int)Math.Floor(relRows);
                var line = paneContent[displayRowIndex].ToCharArray();
                int pos = note.Track * KeyWidth;
                for (int k = 0; k < KeyWidth; k++) line[pos + k] = '#';
                paneContent[displayRowIndex] = new string(line);
            }

            Console.SetCursorPosition(0, 1);
            Console.Write(string.Join(Environment.NewLine, paneContent));
            Console.SetCursorPosition(0, RowCountInScreen + 1);



            Console.SetCursorPosition(0, 1);
            Console.Write(string.Join(Environment.NewLine, paneContent));
            Console.SetCursorPosition(0, RowCountInScreen + 1);

            // 判定逻辑
            if (autoPlay)
            {
                JudgeTrack(0, 0, "Track 1", true);
                JudgeTrack(1, KeyWidth, "Track 2", true);
                JudgeTrack(2, KeyWidth * 2, "Track 3", true);
                JudgeTrack(3, KeyWidth * 3, "Track 4", true);
            }
            else
            {
                var cds = DisplayKey(Keyboard.VirtualKeyStates.KB_D, GetKeyString('-'));
                var cfs = DisplayKey(Keyboard.VirtualKeyStates.KB_F, GetKeyString('-'));
                var cjs = DisplayKey(Keyboard.VirtualKeyStates.KB_J, GetKeyString('-'));
                var cks = DisplayKey(Keyboard.VirtualKeyStates.KB_K, GetKeyString('-'));

                if (!ods && cds) JudgeTrack(0, 0, "Track 1", true);
                if (!ofs && cfs) JudgeTrack(1, KeyWidth, "Track 2", true);
                if (!ojs && cjs) JudgeTrack(2, KeyWidth * 2, "Track 3", true);
                if (!oks && cks) JudgeTrack(3, KeyWidth * 3, "Track 4", true);

                ods = cds; ofs = cfs; ojs = cjs; oks = cks;
            }
            
            
            // Miss判定
            for (int t = 0; t < 4; t++)
            {
                var misses = trackJudgeTimes[t]
                    .Where(x => (nowTime - x.Value).TotalMilliseconds > JudgeTime)
                    .ToList();
                foreach (var miss in misses)
                {
                    trackJudgeTimes[t].Remove(miss);
                    if(showMissMessage)
                        judgeStates.Add((t + 1 + 8, $"Track {t + 1} Miss!"));
                }
            }

            Console.ResetColor();
            // 分数面板
            Console.SetCursorPosition(Console.WindowWidth - 7, 0);
            Console.Write(score.ToString().PadLeft(6, '0'));
            Console.SetCursorPosition(Console.WindowWidth - 7, 1);
            Console.Write($"{(double)score / maxScore * 100:F2}%".PadLeft(6, ' '));
            Console.SetCursorPosition(Console.WindowWidth - 11, 2);
            Console.Write("Max:" + maxScore.ToString().PadLeft(6, '0'));

            // 状态显示
            StatePanelWidth = judgeStates.Count > 0 ? Math.Max(20, judgeStates.Max(x => x.Item2.GetStringInConsoleGridWidth())) : 20;
            Console.SetCursorPosition(Console.WindowWidth - 1 - StatePanelWidth, Console.WindowHeight - 10);
            Console.ForegroundColor = ConsoleColor.Black;
            foreach (var judgeState in judgeStates.TakeLast(8))
            {
                Console.BackgroundColor = judgeState.Item1 switch
                {
                    1 => ConsoleColor.Blue,
                    2 => ConsoleColor.Green,
                    3 => ConsoleColor.Yellow,
                    4 => ConsoleColor.Gray,
                    _ => ConsoleColor.Red
                };
                Console.Write(CenterString(judgeState.Item2, StatePanelWidth));
                Console.SetCursorPosition(Console.WindowWidth - 1 - StatePanelWidth, Console.CursorTop + 1);
            }

            Console.ResetColor();

            Console.SetCursorPosition(0, Console.WindowHeight - 1 - 0);
            Console.Write(CenterString("Press `F1` to pause/resume ", Console.WindowWidth - 1));
            Console.SetCursorPosition(0, Console.WindowHeight - 1 - 1);
            Console.Write(CenterString("Press `F2` to skip to result view ", Console.WindowWidth - 1));
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
        Console.WriteLine("Press `F1` to restart");
        Console.WriteLine("Press `F2` to resetting");
        Console.WriteLine("Press `ESC` to exit");

        var key = Console.ReadKey(true);
        switch (key.Key)
        {
            case ConsoleKey.F1:
                Gaming(beatmap, setting);
                break;
            case ConsoleKey.F2:
                Gaming(beatmap, Setting<PlaySetting>(null, Default: setting));
                break;
            case ConsoleKey.Escape:
                Environment.Exit(0);
                break;
        }
    }

    public static double CalculateFinalAcc(
        int score,
        int maxScore,
        double offsetLostScoreRadio = 0.25,
        double powerK = 2.0) // 可以调节曲线陡峭度
    {
        if (maxScore <= 0) return 0;
        if (offsetLostScoreRadio < 0) offsetLostScoreRadio = 0;

        double acc = (double)score / maxScore * 100;

        // 使用sigmoid型曲线，让偏移更大时补偿明显
        double factor = Math.Pow(offsetLostScoreRadio, powerK);
        double compensation = (100 - acc) * factor / (factor + 0.5); // 归一化，避免过度补偿

        double finalAcc = acc + compensation;

        return finalAcc;
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
