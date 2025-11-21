using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Media.Analitics
{
    public static class DependencyInjection
    {
        public static IServiceCollection AddMediaAnalysisModule(this IServiceCollection services, IConfiguration config)
        {
            services.AddSingleton<IMediaInfoService, Implementations.MediaInfoService>();
            return services;
        }
    }
}