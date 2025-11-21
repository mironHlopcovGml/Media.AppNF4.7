using MediaInfo;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Media.Analitics.Implementations
{
    public class MediaInfoService : IMediaInfoService
    {
        private readonly ILogger<MediaInfoService> _logger;

        public MediaInfoService(ILogger<MediaInfoService> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<MediaInfoResult> GetMediaInfoAsync(string filePath, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("Путь к файлу не может быть пустым", nameof(filePath));

            if (!File.Exists(filePath))
                throw new FileNotFoundException("Файл не найден", filePath);

            return await Task.Factory.StartNew(() =>
            {
                ct.ThrowIfCancellationRequested();

                var mi = new MediaInfo.MediaInfo();
                try
                {
                    var openResult = mi.Open(filePath);

                    if (openResult == IntPtr.Zero)
                    {
                        throw new InvalidOperationException(
                            $"MediaInfo не смог открыть файл: {filePath}");
                    }

                    ct.ThrowIfCancellationRequested();

                    string format = mi.Get(StreamKind.General, 0, "Format");

                    if (string.IsNullOrEmpty(format))
                    {
                        _logger.LogWarning("MediaInfo не распознал формат файла {FilePath}", filePath);
                        throw new InvalidOperationException(
                            $"MediaInfo: Неизвестный формат: {filePath}");
                    }

                    int? TryInt(string v) => int.TryParse(v, out var x) ? x : (int?)null;
                    long? TryLong(string v) => long.TryParse(v, out var x) ? x : (long?)null;

                    string durationStr = mi.Get(StreamKind.General, 0, "Duration");
                    TimeSpan? duration = null;

                    if (long.TryParse(durationStr, out long durMs))
                        duration = TimeSpan.FromMilliseconds(durMs);

                    var result = new MediaInfoResult
                    {
                        ContainerFormat = format,
                        Duration = duration?.ToString(@"hh\:mm\:ss\.fff"),

                        VideoCodec = mi.Get(StreamKind.Video, 0, "Format"),
                        Width = TryInt(mi.Get(StreamKind.Video, 0, "Width")),
                        Height = TryInt(mi.Get(StreamKind.Video, 0, "Height")),
                        VideoBitrate = mi.Get(StreamKind.Video, 0, "BitRate/String"),
                        FrameRate = mi.Get(StreamKind.Video, 0, "FrameRate/String"),

                        AudioCodec = mi.Get(StreamKind.Audio, 0, "Format"),
                        AudioChannels = mi.Get(StreamKind.Audio, 0, "Channel(s)/String"),
                        AudioSampleRate = mi.Get(StreamKind.Audio, 0, "SamplingRate/String"),
                        AudioBitrate = mi.Get(StreamKind.Audio, 0, "BitRate/String"),

                        FileSize = TryLong(mi.Get(StreamKind.General, 0, "FileSize")),
                        FileName = Path.GetFileName(filePath),
                        FullPath = Path.GetFullPath(filePath)
                    };

                    _logger.LogInformation(
                        "MediaInfo успешно прочитан: {File} (V:{V}, A:{A})",
                        result.FileName, result.VideoCodec, result.AudioCodec);

                    return result;
                }
                finally
                {
                    try { mi.Close(); } catch { }
                    try { mi.Dispose(); } catch { }
                }

            }, ct, TaskCreationOptions.LongRunning, TaskScheduler.Default)
            .ConfigureAwait(false);
        }

        public Task<MediaInfoResult> GetMediaInfoAsync(string filePath)
        {
            throw new NotImplementedException();
        }
    }
}
