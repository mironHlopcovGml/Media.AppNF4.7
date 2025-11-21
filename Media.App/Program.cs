using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Media.Analitics;
using Media.Storage;
using Media.VideoProcessing;
using ModularMediaApp.Modules.VideoProcessing;

namespace Media.App
{
    class Program
    {
        static void Main(string[] args)
        {
            MainAsync(args).GetAwaiter().GetResult();
        }

        static async Task MainAsync(string[] args)
        {
            // 1. Настройка конфигурации
            var config = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .Build();

            // 2. Настройка DI и логирования
            var services = new ServiceCollection();
            services.AddSingleton<IConfiguration>(config);
            services.AddLogging(builder => builder.AddConsole());

            // Подключаем модули
            services.AddStorageModule(config);
            services.AddVideoProcessingModule(config);
            services.AddMediaAnalysisModule(config);
            services.AddWaveformGeneratorModule(config);

            using (var provider = services.BuildServiceProvider())
            {
                var storage = provider.GetRequiredService<IStorageService>();
                var converter = provider.GetRequiredService<IVideoConverter>();
                var mediaInfo = provider.GetRequiredService<IMediaInfoService>();
                var logger = provider.GetRequiredService<ILogger<Program>>();

                var cts = new CancellationTokenSource(TimeSpan.FromMinutes(30));
                var ct = cts.Token;

                try
                {
                    string inputFile = args.Length > 0 ?
                        args[0] :
                        "C:\\Users\\administrator\\Desktop\\ffmpeg-2025-07-31-git-119d127d05-full_build\\bin\\Test1.avi";

                    if (!File.Exists(inputFile))
                    {
                        logger.LogError("Файл не найден: {InputFile}", inputFile);
                        Console.WriteLine($"Файл не найден: {inputFile}");
                        return;
                    }
                    Console.WriteLine($"Входной файл найден: {inputFile}");

                    var info = await mediaInfo.GetMediaInfoAsync(inputFile, ct);
                    Console.WriteLine("------ Media Info ------");
                    Console.WriteLine($"Формат: {info.ContainerFormat}");
                    Console.WriteLine($"Длительность: {info.Duration}");
                    Console.WriteLine($"Видео: {info.VideoCodec} {info.Width}x{info.Height}");
                    Console.WriteLine($"Аудио: {info.AudioCodec} {info.AudioChannels}");
                    Console.WriteLine("------------------------");

                    if (string.IsNullOrEmpty(info.AudioCodec))
                    {
                        logger.LogWarning("Видео без аудио — FLAC пропускается");
                        throw new InvalidOperationException("Нет аудио");
                    }

                    Console.WriteLine("Конвертация видео в MP4 с FLAC...");
                    string waveformPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".png");
                    string targetVideoPath = "converted/output.mp4";
                    string targetWaveformPath = "waveforms/" + Path.GetFileName(waveformPath);

                    Stream videoStream = null;
                    Task completion = null;

                    try
                    {
                        (videoStream, completion) = await converter
                            .ConvertToMp4WithFlacAsync(inputFile, waveformPath, ct);

                        await storage.UploadStreamAsync(targetVideoPath, videoStream, ct);
                        logger.LogInformation("Видео загружено: {Path}", targetVideoPath);

                        await completion.ConfigureAwait(false);

                        using (var wfStream = File.OpenRead(waveformPath))
                        {
                            await storage.UploadStreamAsync(targetWaveformPath, wfStream, ct);
                        }

                        logger.LogInformation("Осциллограмма загружена: {Path}", targetWaveformPath);
                    }
                    finally
                    {
                        videoStream?.Dispose();
                        if (File.Exists(waveformPath))
                        {
                            File.Delete(waveformPath);
                        }
                    }

                    Console.WriteLine("Все операции успешно завершены");
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    logger.LogError("Операция отменена");
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Ошибка выполнения: {Message}", ex.Message);
                    Console.WriteLine("Ошибка: " + ex.Message);
                }
            }
        }
    }
}
