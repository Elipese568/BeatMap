using BeatMap.Input;
using BeatMap.UI;
using EUtility.ConsoleEx.Message;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;

namespace BeatMap.Setting;

/// <summary>
/// 负责键位绑定功能，包含选择轨道、重新绑定、测试模式等
/// </summary>
public class KeyBindService
{
    /// <summary>
    /// 轨道键位绑定流程
    /// </summary>
    /// <param name="origin">原始绑定配置，例如 [4] = "DFJK"</param>
    /// <returns>更新后的绑定配置</returns>
    public Dictionary<int, string> ConfigureKeyBinding(Dictionary<int, string> origin)
    {
        Console.Clear();
        IMessageOutputer message = new MessageOutputerOnWindow();
        message.Add(new MessageUnit() { Title = "F1", Description = "重绑定" });
        message.Add(new MessageUnit() { Title = "F2", Description = "测试模式" });
        message.Add(new MessageUnit() { Title = "← / →", Description = "切换轨道数量" });
        message.Add(new MessageUnit() { Title = "ESC", Description = "退出并保存" });
        message.Write(new WarpMessageFormatter());

        int currentTrack = 4;
        int heightMid = (Console.WindowHeight - 1) / 2;
        int triWidth = ((Console.WindowWidth - 1) - ((Console.WindowWidth - 1) % 3)) / 3;
        int mainTriWidth = Console.WindowWidth - 1 - triWidth * 2;

        while (true)
        {
            RenderTrackSelector(origin, currentTrack, heightMid, triWidth, mainTriWidth);

            var key = Console.ReadKey(true);
            switch (key.Key)
            {
                case ConsoleKey.LeftArrow:
                    currentTrack = Math.Max(2, currentTrack - 1);
                    break;

                case ConsoleKey.RightArrow:
                    currentTrack = Math.Min(9, currentTrack + 1);
                    break;

                case ConsoleKey.F1: // 进入重绑定模式
                    origin[currentTrack] = RebindKeysForTrack(currentTrack, heightMid);
                    break;

                case ConsoleKey.F2: // 进入测试模式
                    RunTestingMode(origin[currentTrack], heightMid);
                    break;

                case ConsoleKey.Escape:
                    return origin;
            }
        }
    }

    #region --- 渲染 ---

    /// <summary>
    /// 渲染当前轨道的选择界面
    /// </summary>
    private void RenderTrackSelector(Dictionary<int, string> origin, int currentTrack, int heightMid, int triWidth, int mainTriWidth)
    {
        Console.SetCursorPosition(0, heightMid - 2);
        Console.ForegroundColor = ConsoleColor.Gray;
        Console.Write(CenterString(currentTrack > 2 ? (currentTrack - 1).ToString() : "", triWidth));
        Console.ForegroundColor = ConsoleColor.White;
        Console.Write('<');
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.Write(CenterString(currentTrack.ToString(), mainTriWidth - 2));
        Console.ForegroundColor = ConsoleColor.White;
        Console.Write('>');
        Console.ForegroundColor = ConsoleColor.Gray;
        Console.Write(CenterString(currentTrack < 9 ? (currentTrack + 1).ToString() : "", triWidth));

        Console.SetCursorPosition(0, heightMid - 3);
        Console.ForegroundColor = ConsoleColor.Gray;
        Console.Write(CenterString("Previous", triWidth));
        Console.Write(CenterString("Current", mainTriWidth));
        Console.Write(CenterString("Next", triWidth));

        // 显示轨道对应的键位
        Console.SetCursorPosition(0, heightMid);
        Console.Write(CenterString(string.Join('|', Enumerable.Repeat("#########", currentTrack)), Console.WindowWidth - 1));

        int keyViewWidth = currentTrack * 10 - 1;
        int leftPadding = (Console.WindowWidth - 1 - keyViewWidth) / 2;

        Console.SetCursorPosition(0, heightMid + 1);
        Console.Write(new string(' ', Console.WindowWidth - 1));
        Console.CursorLeft = leftPadding;

        for (int i = 0; i < currentTrack; i++)
        {
            char keyChar = origin[currentTrack][i];
            Console.Write(CenterString(keyChar != ' ' ? keyChar.ToString() : "SPACE", 9));
            if (i != currentTrack - 1) Console.Write('|');
        }

        Console.ResetColor();
    }

    #endregion

    #region --- 重绑定模式 ---

    /// <summary>
    /// 针对单个轨道进行重绑定
    /// </summary>
    private string RebindKeysForTrack(int trackCount, int heightMid)
    {
        int leftPadding = (Console.WindowWidth - 1 - (trackCount * 10 - 1)) / 2;
        StringBuilder newBinding = new();
        bool canceled = false;

        for (int currentTrackIndex = 0; currentTrackIndex < trackCount; currentTrackIndex++)
        {
            if (canceled) break;

            while (true)
            {
                if (canceled) break;

                RenderRebindArrow(trackCount, currentTrackIndex, leftPadding, heightMid, newBinding);

                Console.SetCursorPosition(0, heightMid + 3);
                Console.Write(CenterString($"Press a key to bind, or Backspace to clear, or ESC to cancel for track {currentTrackIndex + 1}.", Console.WindowWidth - 1));

                var keyInput = Console.ReadKey(true);

                // 允许的按键：字母、数字、空格
                if (keyInput.KeyChar is ' ' or (>= '0' and <= '9') or (>= 'a' and <= 'z') or (>= 'A' and <= 'Z'))
                {
                    newBinding.Append(keyInput.KeyChar.ToString().ToUpper());
                    break;
                }
                else if (keyInput.Key == ConsoleKey.Backspace)
                {
                    if (currentTrackIndex > 0)
                    {
                        newBinding.Remove(currentTrackIndex - 1, 1);
                        currentTrackIndex--;
                    }
                }
                else if (keyInput.Key == ConsoleKey.Escape)
                {
                    canceled = true;
                    newBinding.Clear();
                    break;
                }
                else
                {
                    Console.SetCursorPosition(0, heightMid + 4);
                    Console.Write(CenterString("Input must be a number or letter, symbols are not allowed.", Console.WindowWidth - 1));
                }
            }
        }

        return canceled ? string.Empty : newBinding.ToString();
    }

    /// <summary>
    /// 渲染重绑定模式的箭头和输入状态
    /// </summary>
    private void RenderRebindArrow(int trackCount, int currentIndex, int leftPadding, int heightMid, StringBuilder currentBinding)
    {
        Console.SetCursorPosition(leftPadding, heightMid);
        for (int i = 0; i < trackCount; i++)
        {
            Console.Write(i == currentIndex ? "vvvvvvvvv" : "#########");
            if (i != trackCount - 1) Console.Write('|');
        }

        Console.SetCursorPosition(leftPadding, heightMid + 1);
        for (int i = 0; i < trackCount; i++)
        {
            if (currentBinding.Length > i)
            {
                char keyChar = currentBinding[i];
                Console.Write(CenterString(keyChar != ' ' ? keyChar.ToString().ToUpper() : "SPACE", 9));
            }
            else
            {
                Console.Write(CenterString("", 9));
            }

            if (i != trackCount - 1) Console.Write('|');
        }
    }

    #endregion

    #region --- 测试模式 ---

    /// <summary>
    /// 测试当前轨道绑定效果
    /// </summary>
    private void RunTestingMode(string binding, int heightMid)
    {
        int leftPadding = (Console.WindowWidth - 1 - (binding.Length * 10 - 1)) / 2;

        Console.SetCursorPosition(0, heightMid + 3);
        Console.Write(CenterString("Testing mode: Press bound keys to see the effect. Press ESC to exit.", Console.WindowWidth - 1));

        while (true)
        {
            if (Keyboard.IsKeyPressed(Keyboard.VirtualKeyStates.VK_ESCAPE))
                break;

            Console.SetCursorPosition(leftPadding, heightMid);

            for (int i = 0; i < binding.Length; i++)
            {
                char keyChar = binding[i];
                var vk = ParseKeyToVirtualKey(keyChar);
                DisplayKey(vk, "         ");
                Console.ResetColor();
                if (i != binding.Length - 1) Console.Write('|');
            }

            Thread.Sleep(50);
        }

        while (Console.ReadKey(true).Key != ConsoleKey.Escape) ;
    }

    /// <summary>
    /// 将字符转换为虚拟键枚举
    /// </summary>
    private Keyboard.VirtualKeyStates ParseKeyToVirtualKey(char keyChar)
    {
        if (keyChar == ' ')
            return Keyboard.VirtualKeyStates.VK_SPACE;
        else if (char.IsLetter(keyChar))
            return (Keyboard.VirtualKeyStates)Enum.Parse(typeof(Keyboard.VirtualKeyStates), "VK_" + char.ToUpper(keyChar));
        else if (char.IsDigit(keyChar))
            return (Keyboard.VirtualKeyStates)Enum.Parse(typeof(Keyboard.VirtualKeyStates), "VK_" + keyChar);

        throw new ArgumentException("Unsupported key character: " + keyChar);
    }

    #endregion

    #region --- 辅助方法 ---

    private string CenterString(string str, int width, char fillChar = ' ')
    {
        if (str.Length > width)
            return str;
        int leftPadding = (width - str.Length) / 2;
        int rightPadding = width - str.Length - leftPadding;
        return $"{new string(fillChar, leftPadding)}{str}{new string(fillChar, rightPadding)}";
    }

    private bool DisplayKey(Keyboard.VirtualKeyStates key, string keyDisplayString)
    {
        bool isPressed = Keyboard.IsKeyPressed(key);
        Console.BackgroundColor = isPressed ? ConsoleColor.White : ConsoleColor.Black;
        Console.Write(keyDisplayString);
        return isPressed;
    }

    #endregion
}
