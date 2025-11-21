using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Media.Storage.Implementations
{
    public class DiskStorageService : IStorageService
    {
        private readonly StorageOptions _options;
        private readonly ILogger<DiskStorageService> _logger;

        public DiskStorageService(IOptions<StorageOptions> options, ILogger<DiskStorageService> logger)
        {
            _options = options.Value;
            _logger = logger;
        }

        private string FullPath(string path)
        {
            var root = Path.GetFullPath(_options.RootPath ?? ".");
            var combined = Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar));
            var full = Path.GetFullPath(combined);

            if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new UnauthorizedAccessException("Path traversal attempt detected.");

            return full;
        }

        public Task UploadAsync(string path, Stream data, CancellationToken ct)
        {
            return UploadStreamAsync(path, data, ct);
        }

        public async Task UploadStreamAsync(string path, Stream data, CancellationToken ct)
        {
            var full = FullPath(path);
            var dir = Path.GetDirectoryName(full);

            if (dir == null)
                throw new InvalidOperationException("Cannot determine directory for target path: " + full);

            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            // Используем using (или try/finally) вместо await using
            using (var fs = new FileStream(full, FileMode.Create, FileAccess.Write, FileShare.None, 4096, true))
                try
                {
                    await data.CopyToAsync(fs, 81920, ct).ConfigureAwait(false);
                    await fs.FlushAsync(ct).ConfigureAwait(false);
                }
                catch
                {
                    // fs будет автоматически закрыт и освобожден здесь
                }

            _logger.LogInformation("Saved to disk: {Path}", full);
        }

        public Task<Stream> DownloadAsync(string path, CancellationToken ct)
        {
            var full = FullPath(path);

            if (!File.Exists(full))
                throw new FileNotFoundException("File not found", full);

            return Task.FromResult((Stream)File.OpenRead(full));
        }

        public Task DeleteAsync(string path, CancellationToken ct)
        {
            var full = FullPath(path);

            if (File.Exists(full))
                File.Delete(full);

            return Task.CompletedTask;
        }

        public Task<bool> ExistsAsync(string path, CancellationToken ct)
        {
            var full = FullPath(path);
            return Task.FromResult(File.Exists(full));
        }
    }
}
