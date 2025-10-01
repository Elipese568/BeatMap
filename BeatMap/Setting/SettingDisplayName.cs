namespace BeatMap.Setting;

[AttributeUsage(AttributeTargets.Property)]
public class SettingDisplayName : Attribute
{     
    public string Name { get; }
    public SettingDisplayName(string name) => Name = name;
}
