using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BeatMap.Helper;

public static class CalculationHelper
{
    /// <summary>
    /// 计算最终准确度
    /// </summary>
    public static double CalculateFinalAcc(
        int score,
        int maxScore,
        double offsetLostScoreRadio = 0.25,
        bool processEdge = true)
    {
        offsetLostScoreRadio /= 2;
        int thresholdTime = CalculateEdgeThresholeTime(offsetLostScoreRadio);
        thresholdTime = Math.Clamp(thresholdTime, 10, 99);

        double edgeThreshold = processEdge ? CalculateFinalAccByAcc(thresholdTime, offsetLostScoreRadio, 10, false, thresholdTime, 0) : 0;
        if (maxScore <= 0) return 0;
        if (offsetLostScoreRadio < 0) offsetLostScoreRadio = 0;

        double acc = (double)score / maxScore * 100;
        return CalculateFinalAccByAcc(acc, offsetLostScoreRadio, 10, processEdge, thresholdTime, edgeThreshold);
    }

    private static double CalculateFinalAccByAcc(double acc, double offsetLostScoreRadio, double powerK, bool processEdge, int thresholdTime, double edgeThreshold)
    {
        double factor = Math.Pow(offsetLostScoreRadio, powerK);
        double compensation = (100 - acc) * factor / (factor + 0.5);

        double finalAcc = acc + compensation;

        if (processEdge && acc <= thresholdTime)
        {
            finalAcc = LinearDuration(0, edgeThreshold, acc, thresholdTime);
        }
        return finalAcc;
    }

    public static int CalculateEdgeThresholeTime(double offsetLostScoreRadio)
    {
        return (int)(Math.Pow((2 * offsetLostScoreRadio), 2) * 30);
    }

    private static double LinearDuration(double start, double end, double step, double totalStep)
    {
        return start + (end - start) * (step / totalStep);
    }
}
