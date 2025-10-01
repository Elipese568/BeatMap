using BeatMap.Parser;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BeatMap.Extensions;

public static class BeatmapServiceExtension
{
    public static IServiceCollection UseBeatmapService(this IServiceCollection services)
    {
        services.AddSingleton<BeatmapParser>();
        return services;
    }
}
