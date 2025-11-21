using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Media.Storage
{

    public interface IStorageService
    {
        /// <summary>
        /// Загружает поток (multipart для S3, обычный для Disk).
        /// </summary>
        Task UploadStreamAsync(string path, Stream data, CancellationToken ct = default);

        /// <summary>
        /// Устаревший — используйте UploadStreamAsync.
        /// </summary>
        Task UploadAsync(string path, Stream data, CancellationToken ct = default);

        Task<Stream> DownloadAsync(string path, CancellationToken ct = default);
        Task DeleteAsync(string path, CancellationToken ct = default);
        Task<bool> ExistsAsync(string path, CancellationToken ct = default);
    }
}