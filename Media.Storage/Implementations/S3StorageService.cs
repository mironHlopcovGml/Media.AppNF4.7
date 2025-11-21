using Amazon.S3;
using Amazon.S3.Transfer;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;
using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace Media.Storage.Implementations
{
    public class S3StorageService : IStorageService
    {
        private readonly IAmazonS3 _s3;
        private readonly StorageOptions _options;
        private readonly TransferUtility _transferUtility;
        private readonly ILogger<S3StorageService> _logger;
        
        public S3StorageService(IAmazonS3 s3, IOptions<StorageOptions> options, ILogger<S3StorageService> logger)
        {
            _s3 = s3;
            _options = options.Value;
            _transferUtility = new TransferUtility(_s3);
            _logger = logger;
        }

        public Task UploadAsync(string path, Stream data, CancellationToken ct = default) => UploadStreamAsync(path, data, ct);

        public async Task UploadStreamAsync(string path, Stream data, CancellationToken ct = default)
        {
            var stopwatch = Stopwatch.StartNew();
            var uploadRequest = new TransferUtilityUploadRequest
            {
                BucketName = _options.BucketName,
                Key = path,
                InputStream = data,
                PartSize = 10 * 1024 * 1024 // Увеличил до 10MB для скорости
            };

            try
            {
                await _transferUtility.UploadAsync(uploadRequest, ct).ConfigureAwait(false);
                _logger.LogInformation("Uploaded to S3: {Path} ({Elapsed}ms)", path, stopwatch.ElapsedMilliseconds);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                _logger.LogWarning("Upload of {Path} canceled", path);
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to upload to S3: {Path}", path);
                throw;
            }
        }

        public async Task<Stream> DownloadAsync(string path, CancellationToken ct = default)
        {
            var resp = await _s3.GetObjectAsync(_options.BucketName, path, ct).ConfigureAwait(false);
            return resp.ResponseStream;
        }

        public async Task DeleteAsync(string path, CancellationToken ct = default)
        {
            await _s3.DeleteObjectAsync(_options.BucketName, path, ct).ConfigureAwait(false);
        }

        public async Task<bool> ExistsAsync(string path, CancellationToken ct = default)
        {
            try
            {
                await _s3.GetObjectMetadataAsync(_options.BucketName, path, ct).ConfigureAwait(false);
                return true;
            }
            catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                return false;
            }
        }
    }
}