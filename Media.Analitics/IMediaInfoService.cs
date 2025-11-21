using System.Threading;
using System.Threading.Tasks;

namespace Media.Analitics
{

    public interface IMediaInfoService
    {
        Task<MediaInfoResult> GetMediaInfoAsync(string filePath);
        Task<MediaInfoResult> GetMediaInfoAsync(string filePath, CancellationToken ct = default);
    }
}