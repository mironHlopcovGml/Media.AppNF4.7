using Microsoft.Extensions.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ModularMediaApp.Modules.VideoProcessing;

namespace Media.VideoProcessing
{
    public static class DependencyInjection
    {
        /// <summary>
        /// Регистрация конвертера видео (FFmpeg)
        /// </summary>
        public static IServiceCollection AddVideoProcessingModule(this IServiceCollection services, IConfiguration config)
        {
            var ffmpegOptions = new FFmpegOptions();
            config.GetSection("Storage").Bind(ffmpegOptions);
            services.AddSingleton(Microsoft.Extensions.Options.Options.Create(ffmpegOptions));
            services.AddSingleton<IVideoConverter, Implementations.FFmpegVideoConverter>();
            return services;
        }

        /// <summary>
        /// Регистрация генератора осциллограмм (FFmpeg)
        /// </summary>
        public static IServiceCollection AddWaveformGeneratorModule(this IServiceCollection services, IConfiguration config)
        {
            var ffmpegOptions = new FFmpegOptions();
            config.GetSection("FFmpeg").Bind(ffmpegOptions);
            services.AddSingleton(Microsoft.Extensions.Options.Options.Create(ffmpegOptions));
            services.AddSingleton<IWaveformGenerator, Implementations.FFmpegWaveformGenerator>();
            return services;
        }
    }
}