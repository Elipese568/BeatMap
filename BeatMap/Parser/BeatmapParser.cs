using BeatMap.Core;

namespace BeatMap.Parser;

public partial class BeatmapParser
{
    public Beatmap Parse(string filePath)
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

            var attributes = BeatmapParserHelpers.MatchAttributes().Match(rowString);
            var periodAttr = BeatmapParserHelpers.MatchPeriodUnitAttribute().Match(rowString);
            var bpmAttr = BeatmapParserHelpers.MatchBpmAttribute().Match(rowString);
            string keyString = attributes.Success ? rowString.Replace(attributes.Value, "") : rowString;
            keyString = periodAttr.Success ? keyString.Replace(periodAttr.Value, "") : keyString;
            keyString = bpmAttr.Success ? keyString.Replace(bpmAttr.Value, "") : keyString;
            keyString = BeatmapParserHelpers.MatchNoteBinaryOrDecimal().Match(keyString).Value;
            
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
                        if ((integer & 1 << map.Keys - 1 - track) != 0)
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
                int mid = lo + hi >> 1;
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
