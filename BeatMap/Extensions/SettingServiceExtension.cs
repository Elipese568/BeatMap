using BeatMap.Core;
using BeatMap.Helper;
using BeatMap.Setting;
using BeatMap.UI;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static BeatMap.Program;

namespace BeatMap.Extensions;

public static class SettingServiceExtension
{
    public static IServiceCollection UseSetting(this IServiceCollection services)
    {
        services.AddSingleton<ChartDrawer>(new ChartDrawer(
                    (i, max, lostAccSR, processEdge) => CalculationHelper.CalculateFinalAcc(i, max, lostAccSR, processEdge),
                    CalculationHelper.CalculateEdgeThresholeTime))
                .AddSingleton<KeyBindService>()
                .AddSingleton<SettingService<PlaySetting>>(new SettingService<PlaySetting>(new PlaySettingCustomTypeHandler(), new PlaySettingPropertyHandler()));

        return services;
    }
}
