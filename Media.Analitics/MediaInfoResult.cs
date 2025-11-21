using System;

namespace Media.Analitics
{
    public class MediaInfoResult
    {
        // Общая информация о контейнере
        public string ContainerFormat { get; set; }
        public string Duration { get; set; }

        // Видео
        public string VideoCodec { get; set; }
        public int? Width { get; set; }
        public int? Height { get; set; }
        public string VideoBitrate { get; set; }
        public string FrameRate { get; set; }

        // Аудио
        public string AudioCodec { get; set; }
        public string AudioChannels { get; set; }
        public string AudioSampleRate { get; set; }
        public string AudioBitrate { get; set; }

        // Прочие метаданные
        public long? FileSize { get; set; }
        public string FileName { get; set; }
        public string FullPath { get; set; }
    }
}