namespace BeatMap.Core;

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
