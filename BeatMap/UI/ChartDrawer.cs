using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BeatMap.UI;

using System;
using System.Linq;

/// <summary>
/// 负责绘制控制台图形，比如 LostScoreAccArc
/// </summary>
public class ChartDrawer
{
    private readonly Func<int, int, double, bool, double> _calculateFinalAcc;
    private readonly Func<double, int> _calculateEdgeThresholeTime;

    private const string EdgeProcessThresholeDisplayHeader = "Edge Process Threshole";

    /// <summary>
    /// 构造 Drawer
    /// </summary>
    /// <param name="calculateFinalAcc">计算最终准确度的函数</param>
    /// <param name="calculateEdgeThresholeTime">计算边缘阈值时间的函数</param>
    public ChartDrawer(
        Func<int, int, double, bool, double> calculateFinalAcc,
        Func<double, int> calculateEdgeThresholeTime)
    {
        _calculateFinalAcc = calculateFinalAcc;
        _calculateEdgeThresholeTime = calculateEdgeThresholeTime;
    }

    /// <summary>
    /// 绘制 LostScoreAccArc
    /// </summary>
    /// <param name="lostAccSR">AccLostScoreRadio</param>
    public void DrawLostScoreAccArc(double lostAccSR)
    {
        const int Rsg = 20; // 行数（图高）

        bool showEdge = false;

        while (true)
        {
            int ett = _calculateEdgeThresholeTime(lostAccSR / 2);
            ett = Math.Clamp(ett, 10, 99); // 限制在 10~99
            int width = Math.Max(1, Console.WindowWidth);

            // 计算 ett 在屏幕上的位置
            int displayETT = (int)(ett / 100.0 * width);

            Console.Clear();

            // 提示栏
            Console.WriteLine(" Lost Score Acc Arc");
            Console.WriteLine($" Mode: {(showEdge ? "Edge Enabled" : "No Edge")}");
            Console.WriteLine(" ─────────────────────────────");
            Console.WriteLine(" Controls: [E]dge / [N]oEdge / [Q]uit");
            Console.WriteLine();

            // 每次根据当前宽度重算数据（当窗口改变大小时能自适应）
            double[] noEdgeResults = Enumerable.Range(0, width)
                .Select(i => _calculateFinalAcc(i, width - 1, lostAccSR, false))
                .ToArray();
            double[] edgeResults = Enumerable.Range(0, width)
                .Select(i => _calculateFinalAcc(i, width - 1, lostAccSR, true))
                .ToArray();

            double max = showEdge ? edgeResults.Max() : noEdgeResults.Max();
            double[] results = showEdge ? edgeResults : noEdgeResults;

            // 构建字符网格（Rsg x width），先全部空格
            char[][] grid = CreateGrid(Rsg, width, results, max, showEdge);

            // 添加 Header 文本
            PlaceHeaderInGrid(grid, 0, EdgeProcessThresholeDisplayHeader, displayETT);
            PlaceHeaderInGrid(grid, 1, $"(t = {ett}, FinalAcc = {(displayETT >= 0 && displayETT < results.Length ? results[displayETT] : 0.0):F2})", displayETT);

            // 最后在 ett 列标记竖线
            MarkVerticalLine(grid, displayETT);

            // 一次性输出整张图
            for (int row = 0; row < Rsg; row++)
                Console.WriteLine(new string(grid[row]));

            // 等待按键
            ConsoleKey key = Console.ReadKey(true).Key;
            if (key == ConsoleKey.Q) break;
            if (key == ConsoleKey.E) showEdge = true;
            if (key == ConsoleKey.N) showEdge = false;
        }
    }

    /// <summary>
    /// 创建字符网格
    /// </summary>
    private static char[][] CreateGrid(int rows, int width, double[] results, double max, bool showEdge)
    {
        char[][] grid = new char[rows][];

        for (int row = 0; row < rows; row++)
        {
            grid[row] = Enumerable.Repeat(' ', width).ToArray();
            double threshold = max * (1 - (row + 1) / (double)rows); // 从上往下
            for (int x = 0; x < width; x++)
            {
                if (results[x] >= threshold)
                    grid[row][x] = showEdge ? '#' : '*';
            }
        }

        return grid;
    }

    /// <summary>
    /// 在网格中添加 Header
    /// </summary>
    private static void PlaceHeaderInGrid(char[][] grid, int row, string text, int ett)
    {
        if (grid == null || row < 0 || row >= grid.Length || string.IsNullOrEmpty(text))
            return;

        int width = grid[row].Length;
        int len = text.Length;

        // 优先左侧放置
        int leftStart = ett - len;
        if (leftStart >= 0)
        {
            for (int i = 0; i < len; i++) grid[row][leftStart + i] = text[i];
            return;
        }

        // 尝试右侧放置
        int rightStart = ett + 1;
        if (rightStart + len <= width)
        {
            for (int i = 0; i < len; i++) grid[row][rightStart + i] = text[i];
            return;
        }

        // 无法完整放置：如果 header 比宽度还长，保留尾部 width 个字符；否则右对齐显示
        if (len >= width)
        {
            string tail = text.Substring(len - width, width);
            for (int i = 0; i < width; i++) grid[row][i] = tail[i];
            return;
        }

        int start = Math.Max(0, width - len); // 右对齐
        for (int i = 0; i < len; i++) grid[row][start + i] = text[i];
    }

    /// <summary>
    /// 在指定列绘制一条竖线
    /// </summary>
    private static void MarkVerticalLine(char[][] grid, int ettClamped)
    {
        if (ettClamped < 0 || ettClamped >= grid[0].Length) return;

        for (int row = 0; row < grid.Length; row++)
            grid[row][ettClamped] = '|';
    }
}

