using MediaInfo; // пространство имён из нового пакета
﻿using MediaInfo; // пространство имён из нового пакета
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Threading.Tasks;
using System.Threading;

namespace Media.Analitics.Implementations
{
    public class MediaInfoService : IMediaInfoService
    {
        private readonly ILogger<MediaInfoService> _logger;

        public MediaInfoService(ILogger<MediaInfoService> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public Task<MediaInfoResult> GetMediaInfoAsync(string filePath, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                throw new ArgumentException("Файл не найден или путь некорректен.", nameof(filePath));

            return Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();
                using (var mi = new MediaInfo.MediaInfo())
                {
                    mi.Open(filePath);
                    ct.ThrowIfCancellationRequested();

                    // Проверяем, удалось ли открыть файл
                    if (string.IsNullOrEmpty(mi.Get(StreamKind.General, 0, "Format")))
                    {
                        _logger.LogWarning("MediaInfo не смог проанализировать файл {FilePath}", filePath);
                        throw new InvalidOperationException("Не удалось получить информацию о медиафайле.");
                    }
                    
                    var result = new MediaInfoResult
                    {
                        ContainerFormat = mi.Get(StreamKind.General, 0, "Format"),
                        Duration = mi.Get(StreamKind.General, 0, "Duration/String3"),
                        
                        VideoCodec = mi.Get(StreamKind.Video, 0, "Format"),
                        Width = int.TryParse(mi.Get(StreamKind.Video, 0, "Width"), out var w) ? w : (int?)null,
                        Height = int.TryParse(mi.Get(StreamKind.Video, 0, "Height"), out var h) ? h : (int?)null,
                        VideoBitrate = mi.Get(StreamKind.Video, 0, "BitRate/String"),
                        FrameRate = mi.Get(StreamKind.Video, 0, "FrameRate/String"),
                        
                        AudioCodec = mi.Get(StreamKind.Audio, 0, "Format"),
                        AudioChannels = mi.Get(StreamKind.Audio, 0, "Channel(s)/String"),
                        AudioSampleRate = mi.Get(StreamKind.Audio, 0, "SamplingRate/String"),
                        AudioBitrate = mi.Get(StreamKind.Audio, 0, "BitRate/String"),
                        
                        FileSize = long.TryParse(mi.Get(StreamKind.General, 0, "FileSize"), out var size) ? size : (long?)null,
                        FileName = Path.GetFileName(filePath),
                        FullPath = Path.GetFullPath(filePath)
                    };

                    _logger.LogInformation("Медиаинформация успешно получена для {FilePath}: {Video} {Audio}",
                        filePath, result.VideoCodec, result.AudioCodec);

                    return result;
                }
            }, ct);
        }

        public Task<MediaInfoResult> GetMediaInfoAsync(string filePath)
        {
            throw new NotImplementedException();
        }
    }
}