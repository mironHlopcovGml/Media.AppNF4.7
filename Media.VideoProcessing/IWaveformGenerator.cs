using System.Threading;
using System.Threading.Tasks;

namespace Media.VideoProcessing
{
    public interface IWaveformGenerator
    {
        /// <summary>
        /// Генерирует осциллограмму и возвращает путь к temp файлу (caller должен delete).
        /// </summary>
        Task<string> GenerateWaveformAsync(string inputFile, CancellationToken ct = default);
    }
}