using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Media.VideoProcessing.Implementations
{
    public class FFmpegWaveformGenerator : IWaveformGenerator
    {
        private readonly FFmpegOptions _opts;
        private readonly ILogger<FFmpegWaveformGenerator> _logger;
        private readonly TimeSpan _timeout = TimeSpan.FromMinutes(5);
        private static readonly SemaphoreSlim _processSemaphore = new SemaphoreSlim(4, 4);

        public FFmpegWaveformGenerator(IOptions<FFmpegOptions> options, ILogger<FFmpegWaveformGenerator> logger)
        {
            _opts = options.Value ?? throw new ArgumentNullException(nameof(options));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            if (string.IsNullOrEmpty(_opts.Path) || !File.Exists(_opts.Path))
                throw new InvalidOperationException("Invalid FFmpeg path.");
        }

        public Task<string> GenerateWaveformAsync(string inputFile, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(inputFile) || !File.Exists(inputFile))
                throw new ArgumentException("Invalid input file.", nameof(inputFile));

            var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(_timeout);

            var tcs = new TaskCompletionSource<string>();

            string output = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".png");

            var psi = new ProcessStartInfo
            {
                FileName = _opts.Path,
                Arguments = $"-i \"{inputFile}\" -filter_complex \"[0:a]showwavespic=s=1200x300:colors=blue:scale=sqrt[wave]\" -map \"[wave]\" -f image2 -frames:v 1 -y \"{output}\"",
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            return Task.Run(async () =>
            {
                await _processSemaphore.WaitAsync(cts.Token).ConfigureAwait(false);

                Process process = null;
                try
                {
                    process = new Process { StartInfo = psi, EnableRaisingEvents = true };

                    process.Exited += delegate
                    {
                        try
                        {
                            // В .NET Standard 2.1 ReadToEndAsync(CancellationToken) может отсутствовать — делаем через Task.Run
                            string errors = process.StandardError.ReadToEnd();

                            if (!string.IsNullOrWhiteSpace(errors))
                                _logger.LogError("FFmpeg waveform stderr: {Errors}", errors);

                            if (process.ExitCode != 0)
                                tcs.TrySetException(new InvalidOperationException($"FFmpeg waveform failed (code {process.ExitCode}): {errors}"));
                            else
                                tcs.TrySetResult(output);
                        }
                        catch (Exception ex)
                        {
                            tcs.TrySetException(ex);
                        }
                        finally
                        {
                            process.Dispose();
                        }
                    };

                    cts.Token.Register(() =>
                    {
                        try
                        {
                            if (process != null && !process.HasExited)
                                process.Kill(); // В netstandard2.1 нет Kill(true) — убиваем только основной процесс
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to kill FFmpeg waveform process");
                        }

                        tcs.TrySetCanceled();
                    });

                    if (!process.Start())
                        throw new InvalidOperationException("Failed to start FFmpeg waveform process");

                    _logger.LogInformation("FFmpeg waveform started, PID {Pid}, Input: {InputFile}, Output: {Output}", process.Id, inputFile, output);
                }
                catch
                {
                    _processSemaphore.Release();
                    throw;
                }

                // Освобождаем семафор только после завершения процесса (в Exited)
                // Если процесс завершится синхронно — всё равно сработает Exited
                process.Exited += delegate { _processSemaphore.Release(); };

                return await tcs.Task.ConfigureAwait(false);

            }, cts.Token);
        }
    }

}