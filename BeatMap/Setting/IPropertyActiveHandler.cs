namespace BeatMap.Setting;

/// <summary>
/// 属性选中后触发的回调（例如显示图表等）
/// </summary>
public interface IPropertyActiveHandler
{
    void OnPropertyActivated(string propertyName, object propertyValue);
}
