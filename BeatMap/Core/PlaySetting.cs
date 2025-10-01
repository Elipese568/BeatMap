using BeatMap.Setting;

namespace BeatMap.Core;

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
