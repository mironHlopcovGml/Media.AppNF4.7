using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace ModularMediaApp.Modules.VideoProcessing
{

    public interface IVideoConverter
    {
        /// <summary>
        /// Конвертирует видео в MP4 с FLAC, возвращает поток (dispose caller'ом).
        /// </summary>
        Task<(Stream OutputStream, Task CompletionTask)> ConvertToMp4WithFlacAsync(string inputFile, CancellationToken ct = default);

        /// <summary>
        /// Конвертирует с waveform, возвращает поток и completion task.
        /// </summary>
        Task<(Stream OutputStream, Task CompletionTask)> ConvertToMp4WithFlacAsync(
            string inputFile, string saveWaveformPath, CancellationToken ct = default);
    }
}