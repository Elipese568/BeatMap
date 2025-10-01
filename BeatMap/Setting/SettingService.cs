using EUtility.ConsoleEx.Message;
using EUtility.StringEx.StringExtension;
using BeatMap.UI;
using System.Reflection;

namespace BeatMap.Setting;

public class SettingService<T> where T : class, new()
{
    private readonly ICustomTypeHandler _customTypeHandler;
    private readonly IPropertyActiveHandler _propertyActiveHandler;
    private readonly IMessageOutputer _messageOutputer;

    public SettingService(
        ICustomTypeHandler customTypeHandler = null,
        IPropertyActiveHandler propertyActiveHandler = null,
        IMessageOutputer messageOutputer = null)
    {
        _customTypeHandler = customTypeHandler;
        _propertyActiveHandler = propertyActiveHandler;
        _messageOutputer = messageOutputer ?? new MessageOutputerOnWindow();
    }

    /// <summary>
    /// 打开设置界面，返回配置结果
    /// </summary>
    public T Configure(T settingObj = null)
    {
        Console.Clear();
        Console.CursorVisible = false;

        var result = settingObj ?? new T();
        int selectedIndex = 0;
        Dictionary<PropertyInfo, string> tempDoubleValues = new();

        var knownTypes = new List<Type>() { typeof(int), typeof(string), typeof(bool), typeof(char), typeof(double) };
        bool IsKnownType(Type t) => knownTypes.Contains(t);

        while (true)
        {
            RenderUI(result, selectedIndex, tempDoubleValues, IsKnownType);

            var key = Console.ReadKey(true);

            switch (key.Key)
            {
                case ConsoleKey.DownArrow:
                    selectedIndex = Math.Min(selectedIndex + 1, typeof(T).GetProperties().Length - 1);
                    break;
                case ConsoleKey.UpArrow:
                    selectedIndex = Math.Max(selectedIndex - 1, 0);
                    break;
                case ConsoleKey.Escape:
                    ApplyTempValues(result, tempDoubleValues);
                    return result;

                case ConsoleKey.Enter:
                    HandleEnter(result, selectedIndex, tempDoubleValues, IsKnownType);
                    break;

                default:
                    HandleKeyInput(result, selectedIndex, key, tempDoubleValues);
                    break;
            }
        }
    }

    /// <summary>
    /// 渲染整个设置界面
    /// </summary>
    private void RenderUI(T result, int selectedIndex, Dictionary<PropertyInfo, string> tempDoubleValues, Func<Type, bool> IsKnownType)
    {
        Console.SetCursorPosition(0, 0);
        int index = 0;

        foreach (var prop in typeof(T).GetProperties())
        {
            bool isSelected = index == selectedIndex;
            RenderProperty(result, prop, isSelected, tempDoubleValues, IsKnownType);
            index++;
        }
    }

    /// <summary>
    /// 渲染单个属性
    /// </summary>
    private void RenderProperty(T result, PropertyInfo prop, bool isSelected,
                                Dictionary<PropertyInfo, string> tempDoubleValues,
                                Func<Type, bool> IsKnownType)
    {
        if (isSelected)
        {
            Console.BackgroundColor = ConsoleColor.White;
            Console.ForegroundColor = ConsoleColor.Black;
        }

        string name = prop.GetCustomAttribute<SettingDisplayName>()?.Name ?? prop.Name;
        Console.Write($"{name,-50}");

        string displayValue;
        if (IsKnownType(prop.PropertyType))
        {
            if (prop.PropertyType == typeof(double))
            {
                tempDoubleValues.TryAdd(prop, prop.GetValue(result)?.ToString() ?? "0.00");
                displayValue = tempDoubleValues[prop];
            }
            else
            {
                displayValue = prop.GetValue(result)?.ToString() ?? string.Empty;
            }
        }
        else
        {
            displayValue = _customTypeHandler?.CustomTypeToString(prop.GetValue(result), prop.PropertyType) ?? "<Custom>";
        }

        Console.WriteLine(GetTruncatedString(displayValue));
        Console.ResetColor();
    }

    /// <summary>
    /// 处理 Enter 键逻辑
    /// </summary>
    private void HandleEnter(T result, int selectedIndex, Dictionary<PropertyInfo, string> tempDoubleValues, Func<Type, bool> IsKnownType)
    {
        var prop = typeof(T).GetProperties()[selectedIndex];

        if (!IsKnownType(prop.PropertyType))
        {
            var newValue = _customTypeHandler?.HandleCustomType(prop.GetValue(result), prop.PropertyType);
            prop.SetValue(result, newValue);
        }
        else
        {
            ApplyTempValues(result, tempDoubleValues);
            _propertyActiveHandler?.OnPropertyActivated(prop.Name, prop.GetValue(result));
        }

        Console.Clear();
    }

    /// <summary>
    /// 处理用户输入修改值
    /// </summary>
    private void HandleKeyInput(T result, int selectedIndex, ConsoleKeyInfo key,
                                Dictionary<PropertyInfo, string> tempDoubleValues)
    {
        var prop = typeof(T).GetProperties()[selectedIndex];
        var type = prop.PropertyType;

        if (type == typeof(int))
        {
            HandleIntInput(result, prop, key);
        }
        else if (type == typeof(double))
        {
            HandleDoubleInput(tempDoubleValues, prop, key);
        }
        else if (type == typeof(string))
        {
            HandleStringInput(result, prop, key);
        }
        else if (type == typeof(char))
        {
            if (key.Key == ConsoleKey.Backspace)
                prop.SetValue(result, ' ');
            else
                prop.SetValue(result, key.KeyChar);
        }
        else if (type == typeof(bool))
        {
            if (key.Key == ConsoleKey.T) prop.SetValue(result, true);
            if (key.Key == ConsoleKey.F) prop.SetValue(result, false);
        }
    }

    private void HandleIntInput(T result, PropertyInfo prop, ConsoleKeyInfo key)
    {
        int current = (int)(prop.GetValue(result) ?? 0);
        if (key.Key >= ConsoleKey.D0 && key.Key <= ConsoleKey.D9)
            prop.SetValue(result, current * 10 + (key.KeyChar - '0'));
        else if (key.Key == ConsoleKey.Backspace)
            prop.SetValue(result, current / 10);
        else if (key.Key == ConsoleKey.Add)
            prop.SetValue(result, current + 1);
        else if (key.Key == ConsoleKey.Subtract)
            prop.SetValue(result, current - 1);
    }

    private void HandleDoubleInput(Dictionary<PropertyInfo, string> tempValues, PropertyInfo prop, ConsoleKeyInfo key)
    {
        if (!tempValues.ContainsKey(prop))
            tempValues[prop] = "0.00";

        var current = tempValues[prop];
        if (key.Key == ConsoleKey.Backspace && current.Length > 0)
        {
            tempValues[prop] = current[..^1];
            return;
        }

        if (char.IsDigit(key.KeyChar) || key.KeyChar == '.' || key.KeyChar == '-')
        {
            tempValues[prop] += key.KeyChar;
        }
    }

    private void HandleStringInput(T result, PropertyInfo prop, ConsoleKeyInfo key)
    {
        var current = (prop.GetValue(result) as string) ?? string.Empty;
        if (key.Key == ConsoleKey.Backspace && current.Length > 0)
        {
            prop.SetValue(result, current[..^1]);
        }
        else if (!char.IsControl(key.KeyChar))
        {
            prop.SetValue(result, current + key.KeyChar);
        }
    }

    /// <summary>
    /// 应用临时 double 值到对象属性
    /// </summary>
    private static void ApplyTempValues(T result, Dictionary<PropertyInfo, string> tempValues)
    {
        foreach (var kvp in tempValues)
        {
            if (kvp.Key.PropertyType == typeof(double))
            {
                var finalValue = kvp.Value.EndsWith('.') ? kvp.Value + "0" : kvp.Value;
                kvp.Key.SetValue(result, Convert.ChangeType(finalValue, kvp.Key.PropertyType));
            }
        }
    }

    /// <summary>
    /// 自动截断过长的字符串
    /// </summary>
    private string GetTruncatedString(string output)
    {
        int availableWidth = Console.WindowWidth - 1 - Console.CursorLeft;
        if (output.GetStringInConsoleGridWidth() > availableWidth)
        {
            return output.Substring(0, Math.Max(availableWidth - 3, 0)) + "...";
        }
        return output.PadRight(availableWidth);
    }
}
