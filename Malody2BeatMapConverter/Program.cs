using Malody2BeatMapConverter;
using System;
using System.Collections;
using System.Diagnostics;
using System.Reflection.PortableExecutable;

namespace Malody2BeatMapConverter;

public class Song
{
    /// <summary>
    /// 
    /// </summary>
    public string title { get; set; }
    /// <summary>
    /// 
    /// </summary>
    public string artist { get; set; }
    /// <summary>
    /// 
    /// </summary>
    public int id { get; set; }
}

public class Mode_ext
{
    /// <summary>
    /// 
    /// </summary>
    public int column { get; set; }
}

public class Meta
{
    /// <summary>
    /// 
    /// </summary>
    public int ver { get; set; }
/// <summary>
/// 
/// </summary>
public string creator { get; set; }
    /// <summary>
    /// 
    /// </summary>
    public string background { get; set; }
    /// <summary>
    /// 
    /// </summary>
    public string version { get; set; }
    /// <summary>
    /// 
    /// </summary>
    public int preview { get; set; }
    /// <summary>
    /// 
    /// </summary>
    public int id { get; set; }
    /// <summary>
    /// 
    /// </summary>
    public int mode { get; set; }
    /// <summary>
    /// 
    /// </summary>
    public int time { get; set; }
    /// <summary>
    /// 
    /// </summary>
    public Song song { get; set; }
    /// <summary>
    /// 
    /// </summary>
    public Mode_ext mode_ext { get; set; }
}

public class TimedElement
{
    public List<int> beat { get; set; }
}

public class Time : TimedElement
{
/// <summary>
/// 
/// </summary>
public double bpm { get; set; }
}

public class Note : TimedElement
{
/// <summary>
/// 
/// </summary>
public int column { get; set; }
}

public class Test
{
/// <summary>
/// 
/// </summary>
public int divide { get; set; }
/// <summary>
/// 
/// </summary>
public int speed { get; set; }
/// <summary>
/// 
/// </summary>
public int save { get; set; }
/// <summary>
/// 
/// </summary>
public int @lock { get; set; }
/// <summary>
/// 
/// </summary>
public int edit_mode { get; set; }
}

public class Extra
{
/// <summary>
/// 
/// </summary>
public Test test { get; set; }
}

public class Effect : TimedElement
{
    public double scroll { get; set; }
    public double jump { get; set; }
    public double sign { get; set; }
}

public class Root
{
/// <summary>
/// 
/// </summary>
public Meta meta { get; set; }
/// <summary>
/// 
/// </summary>
public List<Time> time { get; set; }
/// <summary>
/// 
/// </summary>
public List<Effect> effect { get; set; }
/// <summary>
/// 
/// </summary>
public List<Note> note { get; set; }
/// <summary>
/// 
/// </summary>
public Extra extra { get; set; }
}

public static class MathEx
{
    // 计算最大公约数
    public static long GCD(long a, long b)
    {
        while (b != 0)
        {
            long t = b;
            b = a % b;
            a = t;
        }
        return Math.Abs(a);
    }

    public static int GCD(int a, int b)
    {
        while (b != 0)
        {
            int t = b;
            b = a % b;
            a = t;
        }
        return Math.Abs(a);
    }

    // 计算最小公倍数
    public static long LCM(long a, long b)
    {
        if (a == 0 || b == 0) return 0;
        return Math.Abs(a / GCD(a, b) * b); // 先除后乘防止溢出
    }

    public static int LCM(int a, int b)
    {
        if (a == 0 || b == 0) return 0;
        return Math.Abs(a / GCD(a, b) * b); // 先除后乘防止溢出
    }

    // IEnumerable 的最小公倍数
    public static long LCM(IEnumerable<long> numbers)
    {
        return numbers.Aggregate(1L, (lcm, n) => LCM(lcm, n));
    }

    public static int LCM(IEnumerable<int> numbers)
    {
        return numbers.Aggregate(1, (lcm, n) => LCM(lcm, n));
    }
}

public class DynamicList<T> : ICollection<T>
{
    private T[] _items;

    public int Count => ((ICollection<T>)_items).Count;

    public bool IsReadOnly => ((ICollection<T>)_items).IsReadOnly;

    private void Adp(int index)
    {
        _items ??= new T[index + 1];
        if (index >= _items.Length)
            Array.Resize(ref _items, index + 1);
    }

    public void Add(T item)
    {
        ((ICollection<T>)_items).Add(item);
    }

    public void Clear()
    {
        ((ICollection<T>)_items).Clear();
    }

    public bool Contains(T item)
    {
        return ((ICollection<T>)_items).Contains(item);
    }

    public void CopyTo(T[] array, int arrayIndex)
    {
        ((ICollection<T>)_items).CopyTo(array, arrayIndex);
    }

    public bool Remove(T item)
    {
        return ((ICollection<T>)_items).Remove(item);
    }

    public IEnumerator<T> GetEnumerator()
    {
        return ((IEnumerable<T>)_items).GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return _items.GetEnumerator();
    }

    public new T this[int index]
    {
        get
        {
            Adp(index);
            return _items[index];
        }
        set
        {
            Adp(index);
            _items[index] = value;
        }
    }
}

internal class Program
{
    static void Main(string[] args)
    {
        string content = File.ReadAllText(args[0]);
        Root? root = System.Text.Json.JsonSerializer.Deserialize<Root>(content);
        Console.WriteLine(root.meta.song.title);
        Console.WriteLine(root.meta.song.artist);
        Console.WriteLine("Is this chart? [Y/N]");
        while (true)
        {
            var key = Console.ReadKey(true);
            if (key.Key == ConsoleKey.Y)
            {
                ConvertToBeatMap(root);
                break;
            }
            else if (key.Key == ConsoleKey.N)
            {
                return;
            }
        }
    }

    /// <summary>
    /// //最大公约数
    /// </summary>
    static int GetGreatestCommonDivisor(int a, int b)
    {
        if (a < b)
        {
            a = a + b;
            b = a - b;
            a = a - b;
        }
        return (a % b == 0) ? b : GetGreatestCommonDivisor(a % b, b);
    }

    /// <summary>
    /// //最小公倍数
    /// </summary>
    static int GetMinimumCommonMultiple(int a, int b)
    {
        return a * b / GetGreatestCommonDivisor(a, b);
    }

    static void ConvertToBeatMap(Root root)
    {
        Console.WriteLine("Converting...");
        var result = new StreamWriter(File.Create("output.bm"));
        
        result.Write(root.meta.song.title);
        result.Write(';');
        result.Write(root.meta.song.artist);
        result.Write(';');
        result.Write(root.time[0].bpm);
        result.Write(";");
        result.Write(root.meta.mode_ext.column);
        result.WriteLine(";");

        // note effect and bpm groups
        var nebTimes =
            root.note.Select(x => x.beat)
                     .Concat(root.time?.Select(x => x.beat) ?? [])
                     .Concat(root.effect?.Select(x => x.beat) ?? [])
                     .Select(x => (x, x[0] + (double)x[1] / x[2]))
                     .DistinctBy(x => x.Item2)
                     .Order(Comparer<(List<int>, double)>.Create((x, y) => x.Item2.CompareTo(y.Item2)))
                     .Select(x => x.Item1)
                     .ToList();

        var allSignsLCM = MathEx.LCM(nebTimes.Select(x => x[2]));

        var uSignL = nebTimes.Select(x => (List<int>)[x[0], x[1] * allSignsLCM / x[2], allSignsLCM]).ToList();

        var oriUniTimes = nebTimes.Zip(uSignL);

        var outputResultCollection = new DynamicList<List<TimedElement>>();
        var noteGroups = (root.note ?? []).GroupBy(x => x.beat).ToList();
        var timeGroups = (root.time ?? []).GroupBy(x => x.beat).ToList();
        var effectGroups = (root.effect ?? []).GroupBy(x => x.beat).ToList();
        foreach(var i in oriUniTimes)
        {
            var noteMatchElements = noteGroups.Count > 0 ?
                noteGroups.Where(x => x.Key.SequenceEqual(i.First))
                          .Select(x => x.Cast<TimedElement>())
                          .Aggregate(new List<TimedElement>(), (x, y) => [.. x, .. y])
                : [];

            var effectMatchElements = effectGroups.Count > 0 ?
                effectGroups.Where(x => x.Key.SequenceEqual(i.First))
                          .Select(x => x.Cast<TimedElement>())
                          .Aggregate(new List<TimedElement>(), (x, y) => [.. x, .. y])
                : [];

            var timeMatchElements = timeGroups.Count > 0 ?
                timeGroups.Where(x => x.Key.SequenceEqual(i.First))
                          .Select(x => x.Cast<TimedElement>())
                          .Aggregate(new List<TimedElement>(),(x, y) => [..x, ..y])
                : [];

            outputResultCollection[i.Second[0] * allSignsLCM + i.Second[1]] = [..noteMatchElements,..effectMatchElements,..timeMatchElements];
        }

        Console.WriteLine($"{outputResultCollection.Count} Rows");

        result.Write($"({root.time[0].bpm}){{{allSignsLCM}}}");

        foreach(var row in outputResultCollection)
        {
            if(row == null)
            {
                result.Write(',');
                continue;
            }
            if (row.LastOrDefault(x => x is Time, null) is Time time)
            {
                result.Write($"({time.bpm})");
            }
            var notes = row.Where(x => x is Note).Cast<Note>().ToList();
            int keyValue = 0;
            foreach (var note in notes)
            {
                keyValue |= 0x1 << (note.column);
            }
            result.Write(keyValue.ToString());
            
            var effects = row.Where(x => x is Effect).Cast<Effect>().ToList();
            if(effects.Count > 0)
            {
                result.Write('[');
                foreach (var effect in effects)
                {
                    if(effect.scroll != null)
                    {
                        result.Write("s:");
                        result.Write(effect.scroll.ToString());
                    }
                    if (effects.Last() != effect)
                        result.Write(',');
                }
                result.Write(']');
            }

            result.Write(',');
        }

        result.Close();
        Console.WriteLine("Convertion completed.");
        Console.ReadLine();
    }
}
