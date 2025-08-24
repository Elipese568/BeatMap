using PInvoke.Net;
using System;
using System.Runtime.CompilerServices;
using System.Text;

namespace BeatMap;

public struct Beatmap
{
    public string Name { get; set; }
    public string Artist { get; set; }
    public int Bpm { get; set; }
    public List<BeatRow> Rows { get; set; }
}

[InlineArray(4)]
public struct BeatRow
{
    private (bool IsKey, TimeSpan JudgeTime) _inner;
    public static BeatRow Empty
    {
        get
        { 
            var row = new BeatRow();
            row[0].IsKey = false;
            row[1].IsKey = false;
            row[2].IsKey = false;
            row[3].IsKey = false;
            return row;
        }
    }
}


public class BeatmapParser
{
    public static BeatRow ParseRow(string rowString, int bpm, int rowIndex)
    {
        if (!int.TryParse(rowString, out int integer) && !(rowString.StartsWith("0b") && int.TryParse(rowString.Replace("0b",""), style: System.Globalization.NumberStyles.AllowBinarySpecifier, null, out integer)))
            throw new ArgumentException("Invalid row format. Expected an integer value.");

        var row = new BeatRow();
        row[0] = ((integer & 0x1) != 0, TimeSpan.FromMilliseconds(60000 / bpm * rowIndex)); // First bit
        row[1] = ((integer & 0x2) != 0, TimeSpan.FromMilliseconds(60000 / bpm * rowIndex)); // Second bit
        row[2] = ((integer & 0x4) != 0, TimeSpan.FromMilliseconds(60000 / bpm * rowIndex)); // Third bit
        row[3] = ((integer & 0x8) != 0, TimeSpan.FromMilliseconds(60000 / bpm * rowIndex)); // Fourth bit
        return row;
    }

    public static List<BeatRow> ParseRows(string rowsString, int bpm)
    {
        List<BeatRow> rows = new List<BeatRow>();
        string[] rowStrings = rowsString.Split(",");
        int index = 0;
        Array.ForEach(rowStrings, x => 
        {
            rows.Add(ParseRow(x.Trim(), bpm, index));
        });
        return rows;
    }
    public static Beatmap Parse(string filePath)
    {
        string content = File.ReadAllText(filePath);
        string[] parts = content.Split(";");
        if (parts.Length < 4)
        {
            throw new ArgumentException("Invalid beatmap format.");
        }

        return new Beatmap
        {
            Name = parts[0].Trim(),
            Artist = parts[1].Trim(),
            Bpm = int.Parse(parts[2].Trim()),
            Rows = ParseRows(parts[3].Trim(), int.Parse(parts[2].Trim()))
        };
    }
}

internal class Program
{
    static void Main(string[] args)
    {
        Beatmap beatmap = BeatmapParser.Parse(args[0]);

        Console.WriteLine($"Name: {beatmap.Name}");
        Console.WriteLine($"Artist: {beatmap.Artist}");
        Console.WriteLine($"BPM: {beatmap.Bpm}");
        Console.WriteLine($"Rows: {beatmap.Rows.Count}, about {TimeSpan.FromMilliseconds(beatmap.Rows.Count * (60000 / beatmap.Bpm))}");
        Console.WriteLine("Press `Enter` to start");
        Console.WriteLine("Press `Esc` to exit");
        var key = Console.ReadKey(true);
        if (key.Key != ConsoleKey.Escape)
            Gaming(beatmap);
    }

    static void Gaming(Beatmap beatmap)
    {
        Console.Clear();
        Console.CursorVisible = false;
        const int RowCountInScreen = 16;
        const int FPS = 60;

        BeatRow currentJudgeRow = default;
        double msPerBeat = 60000 / beatmap.Bpm;
        Queue<BeatRow> rowsQueue = new Queue<BeatRow>();
        var rowsEnumer = beatmap.Rows.GetEnumerator();
        CancellationTokenSource cts = new(TimeSpan.FromMilliseconds((beatmap.Rows.Count + 16 + 2) * msPerBeat + 2000));
        bool updating = false;
        int currentRowGeneration = 0;
        int oldRowGeneration = -1;

        DateTime startJudgeTime = default;

        const int ClickedScore = 1000;
        int score = 0;
        const double offsetLostScoreRadio = 0.37;
        int maxScore = beatmap.Rows.Sum(x =>
        {
            int rowScore = 0;
            for (int i = 0; i < 4; i++)
            {
                if (x[i].IsKey)
                    rowScore += ClickedScore;
            }
            return rowScore;
        });
        int notesCount = maxScore / ClickedScore;
        int clickedNoteCount = 0;

        bool ods, ofs, ojs, oks;
        ods = ofs = ojs = oks = false;

        bool autoPlay = true;
        bool unperfectAuto = false;
        double unperfectRadio = 0.2;
        int unperfectMax = (int)(200 * unperfectRadio);

        var updater = new Timer((_) =>
        {
            if (startJudgeTime == default)
                startJudgeTime = DateTime.Now;
            while (updating) { }
            updating = true;

            if (rowsEnumer.MoveNext())
                rowsQueue.Enqueue(rowsEnumer.Current);
            else
                rowsQueue.Enqueue(BeatRow.Empty);

            if (rowsQueue.Count > RowCountInScreen)
            {
                rowsQueue.Dequeue();
            }
            if (rowsQueue.Count == RowCountInScreen)
            {
                currentJudgeRow = rowsQueue.Peek();
                currentRowGeneration++;
            }
            Console.ResetColor();
            Console.SetCursorPosition(0, 0);

            StringBuilder screenContent = new();

            foreach (var item in rowsQueue)
            {
                foreach (var key in item)
                {
                    if (key.IsKey)
                        screenContent.Insert(0, '#');
                    else
                        screenContent.Insert(0, ' ');
                }
                screenContent.Insert(0, Environment.NewLine);
            }
            Console.SetCursorPosition(0, 0);
            Console.Write(screenContent.ToString().TrimStart('\n').TrimStart('\r'));
            updating = false;
        }, null, 1000, (int)msPerBeat);
        bool stjed, ndjed, rdjed, fthjed;
        stjed = ndjed = rdjed = fthjed = false;
        BeatRow oldRow = default;
        var keyStateUpdater = new Timer((_) =>
        {
            if (updating)
                return;
            updating = true;
            Console.SetCursorPosition(0, RowCountInScreen + 1);

            if(autoPlay)
            {
                if(currentRowGeneration != oldRowGeneration)
                {
                    oldRowGeneration = currentRowGeneration;
                    if (currentJudgeRow[3].IsKey)
                    {
                        Console.BackgroundColor = ConsoleColor.White;
                        Console.Write(' ');
                        Console.ResetColor();
                        Console.SetCursorPosition(0, RowCountInScreen);
                        Console.BackgroundColor = ConsoleColor.Yellow;
                        Console.Write(' ');
                        Console.ResetColor();
                        int lostScore1 = unperfectAuto? Random.Shared.Next(unperfectMax) : 0;//(int)(Math.Abs((currentJudgeRow[3].JudgeTime - (DateTime.Now - startJudgeTime)).Milliseconds) * offsetLostScoreRadio);
                        score += ClickedScore - lostScore1;
                        Console.SetCursorPosition(0, RowCountInScreen + 2 + 1);
                        Console.Write($"{"",30}");
                        Console.CursorLeft = 0;
                        Console.Write($"Track 1: -{lostScore1} {(double)lostScore1 / maxScore * 100:F2}%");
                        clickedNoteCount++;
                    }
                    else
                    {
                        Console.BackgroundColor = ConsoleColor.Black;
                        Console.Write(' ');
                        Console.ResetColor();
                    }
                    Console.SetCursorPosition(1, RowCountInScreen + 1);
                    if (currentJudgeRow[2].IsKey)
                    {
                        Console.BackgroundColor = ConsoleColor.White;
                        Console.Write(' ');
                        Console.ResetColor();
                        Console.SetCursorPosition(1, RowCountInScreen);
                        Console.BackgroundColor = ConsoleColor.Yellow;
                        Console.Write(' ');
                        Console.ResetColor();
                        int lostScore2 = unperfectAuto ? Random.Shared.Next(unperfectMax) : 0;//(int)(Math.Abs((currentJudgeRow[2].JudgeTime - (DateTime.Now - startJudgeTime)).Milliseconds) * offsetLostScoreRadio);
                        score += ClickedScore - lostScore2;
                        Console.SetCursorPosition(0, RowCountInScreen + 2 + 2);
                        Console.Write($"{"",30}");
                        Console.CursorLeft = 0;
                        Console.Write($"Track 2: -{lostScore2} {(double)lostScore2 / maxScore * 100:F2}%");
                        clickedNoteCount++;
                    }
                    else
                    {
                        Console.BackgroundColor = ConsoleColor.Black;
                        Console.Write(' ');
                        Console.ResetColor();
                    }
                    Console.SetCursorPosition(2, RowCountInScreen + 1);
                    if (currentJudgeRow[1].IsKey)
                    {
                        Console.BackgroundColor = ConsoleColor.White;
                        Console.Write(' ');
                        Console.ResetColor();
                        Console.SetCursorPosition(2, RowCountInScreen);
                        Console.BackgroundColor = ConsoleColor.Yellow;
                        Console.Write(' ');
                        Console.ResetColor();
                        int lostScore3 = unperfectAuto ? Random.Shared.Next(unperfectMax) : 0;//(int)(Math.Abs((currentJudgeRow[1].JudgeTime - (DateTime.Now - startJudgeTime)).Milliseconds) * offsetLostScoreRadio);
                        score += ClickedScore - lostScore3;
                        Console.SetCursorPosition(0, RowCountInScreen + 2 + 3);
                        Console.Write($"{"",30}");
                        Console.CursorLeft = 0;
                        Console.Write($"Track 3: -{lostScore3} {(double)lostScore3 / maxScore * 100:F2}%");
                        clickedNoteCount++;
                    }
                    else
                    {
                        Console.BackgroundColor = ConsoleColor.Black;
                        Console.Write(' ');
                        Console.ResetColor();
                    }
                    Console.SetCursorPosition(3, RowCountInScreen + 1);
                    if (currentJudgeRow[0].IsKey)
                    {
                        Console.BackgroundColor = ConsoleColor.White;
                        Console.Write(' ');
                        Console.ResetColor();
                        Console.SetCursorPosition(3, RowCountInScreen);
                        Console.BackgroundColor = ConsoleColor.Yellow;
                        Console.Write(' ');
                        Console.ResetColor();
                        int lostScore4 = unperfectAuto ? Random.Shared.Next(unperfectMax) : 0;// (int)(Math.Abs((currentJudgeRow[0].JudgeTime - (DateTime.Now - startJudgeTime)).Milliseconds) * offsetLostScoreRadio);
                        score += ClickedScore - lostScore4;
                        Console.SetCursorPosition(0, RowCountInScreen + 2 + 4);
                        Console.Write($"{"",30}");
                        Console.CursorLeft = 0;
                        Console.Write($"Track 4: -{lostScore4} {(double)lostScore4 / maxScore * 100:F2}%");
                        clickedNoteCount++;
                    }
                    else
                    {
                        Console.BackgroundColor = ConsoleColor.Black;
                        Console.Write(' ');
                        Console.ResetColor();
                    }
                }
            }
            else
            {
                var cds = DisplayKey(Keyboard.VirtualKeyStates.KB_D);
                var cfs = DisplayKey(Keyboard.VirtualKeyStates.KB_F);
                var cjs = DisplayKey(Keyboard.VirtualKeyStates.KB_J);
                var cks = DisplayKey(Keyboard.VirtualKeyStates.KB_K);
                if (!ods && cds && currentJudgeRow[3].IsKey)
                {
                    Console.SetCursorPosition(0, RowCountInScreen);
                    Console.BackgroundColor = ConsoleColor.Yellow;
                    Console.Write(' ');
                    Console.ResetColor();
                    int lostScore1 = (int)((currentJudgeRow[3].JudgeTime - (DateTime.Now - startJudgeTime)).Milliseconds * offsetLostScoreRadio);
                    score += ClickedScore - lostScore1;
                    Console.SetCursorPosition(0, RowCountInScreen + 2 + 1);
                    Console.Write($"{"",30}");
                    Console.CursorLeft = 0;
                    Console.Write($"Track 1: -{lostScore1} {(double)lostScore1 / maxScore * 100:F2}%");
                    clickedNoteCount++;
                }
                if (!ofs && cfs && currentJudgeRow[2].IsKey)
                {
                    Console.SetCursorPosition(1, RowCountInScreen);
                    Console.BackgroundColor = ConsoleColor.Yellow;
                    Console.Write(' ');
                    Console.ResetColor();
                    int lostScore2 = (int)((currentJudgeRow[2].JudgeTime - (DateTime.Now - startJudgeTime)).Milliseconds * offsetLostScoreRadio);
                    score += ClickedScore - lostScore2;
                    Console.SetCursorPosition(0, RowCountInScreen + 2 + 2);
                    Console.Write($"{"",30}");
                    Console.CursorLeft = 0;
                    Console.Write($"Track 2: -{lostScore2} {(double)lostScore2 / maxScore * 100:F2}%");
                    clickedNoteCount++;
                }
                if (!ojs && cjs && currentJudgeRow[1].IsKey)
                {
                    Console.SetCursorPosition(2, RowCountInScreen);
                    Console.BackgroundColor = ConsoleColor.Yellow;
                    Console.Write(' ');
                    Console.ResetColor();
                    int lostScore3 = (int)((currentJudgeRow[1].JudgeTime - (DateTime.Now - startJudgeTime)).Milliseconds * offsetLostScoreRadio);
                    score += ClickedScore - lostScore3;
                    Console.SetCursorPosition(0, RowCountInScreen + 2 + 3);
                    Console.Write($"{"",30}");
                    Console.CursorLeft = 0;
                    Console.Write($"Track 3: -{lostScore3} {(double)lostScore3 / maxScore * 100:F2}%");
                    clickedNoteCount++;
                }
                if (!oks && cks && currentJudgeRow[0].IsKey)
                {
                    Console.SetCursorPosition(3, RowCountInScreen);
                    Console.BackgroundColor = ConsoleColor.Yellow;
                    Console.Write(' ');
                    Console.ResetColor();
                    int lostScore4 = (int)((currentJudgeRow[0].JudgeTime - (DateTime.Now - startJudgeTime)).Milliseconds * offsetLostScoreRadio);
                    score += ClickedScore - lostScore4;
                    Console.SetCursorPosition(0, RowCountInScreen + 2 + 4);
                    Console.Write($"{"",30}");
                    Console.CursorLeft = 0;
                    Console.Write($"Track 4: -{lostScore4} {(double)lostScore4 / maxScore * 100:F2}%");
                    clickedNoteCount++;
                }
                ods = cds;
                ofs = cfs;
                ojs = cjs;
                oks = cks;
            }
            
            Console.ResetColor();

            Console.SetCursorPosition(Console.WindowWidth - 1 - 6, 0);
            Console.Write(score.ToString().PadLeft(6, '0'));
            Console.SetCursorPosition(Console.WindowWidth - 1 - 6, 1);
            Console.Write($"{(double)score / maxScore * 100:F2}%".PadLeft(6, ' '));
            Console.SetCursorPosition(Console.WindowWidth - 1 - 10, 2);
            Console.Write("Max:" + maxScore.ToString().PadLeft(6, '0'));

            updating = false;
        }, null, 00, 1000 / FPS);

        while (!cts.IsCancellationRequested)
        {
        }

        updater.Dispose();
        keyStateUpdater.Dispose();
        Console.In.ReadToEnd();

        Console.Clear();
        var acc = (double)score / maxScore * 100;
        var compensatoryRadio = 0.4;
        Console.WriteLine(acc * (1 / offsetLostScoreRadio) * 0.3 > 80 ? "Track Completed" : "Track Lost");
        Console.WriteLine();
        Console.WriteLine(beatmap.Name);
        Console.WriteLine(beatmap.Artist);
        Console.WriteLine($"{score} : {acc * (1 / offsetLostScoreRadio) * compensatoryRadio:F2}%");
        Console.WriteLine($"Rank {(acc * (1 / offsetLostScoreRadio) * compensatoryRadio) switch
        {
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
        Console.ReadLine();
    }

    static bool DisplayKey(Keyboard.VirtualKeyStates k)
    {
        bool pressState = Keyboard.IsKeyPressed(k);
        Console.BackgroundColor = pressState ? ConsoleColor.White : ConsoleColor.Black;
        Console.Write('-');

        return pressState;
    }
}
