using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ModularMediaApp.Modules.VideoProcessing;

namespace Media.VideoProcessing.Implementations
{
    public class FFmpegVideoConverter : IVideoConverter
    {
        private readonly FFmpegOptions _opts;
        private readonly ILogger<FFmpegVideoConverter> _logger;
        private readonly TimeSpan _timeout = TimeSpan.FromMinutes(30);
        private static readonly SemaphoreSlim _processSemaphore = new SemaphoreSlim(4, 4); // Лимит 4 параллельных FFmpeg

        public bool UseFragmentedMp4 { get; set; } = false;

        public FFmpegVideoConverter(IOptions<FFmpegOptions> options, ILogger<FFmpegVideoConverter> logger)
        {
            _opts = options.Value ?? throw new ArgumentNullException(nameof(options));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            if (string.IsNullOrEmpty(_opts.Path) || !File.Exists(_opts.Path))
                throw new InvalidOperationException("Invalid FFmpeg path in configuration.");
        }

        public Task<(Stream OutputStream, Task CompletionTask)> ConvertToMp4WithFlacAsync(string inputFile, CancellationToken ct = default)
        {
            return ConvertToMp4WithFlacAsync(inputFile, null, ct);
        }

        public async Task<(Stream OutputStream, Task CompletionTask)> ConvertToMp4WithFlacAsync(
            string inputFile, string saveWaveformPath, CancellationToken ct = default)
        {
            return await StartFFmpegProcess(inputFile, saveWaveformPath, ct);
        }

        private async Task<(Stream OutputStream, Task CompletionTask)> StartFFmpegProcess(
            string inputFile, string saveWaveformPath, CancellationToken ct)
        {

            var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(_timeout);

            var tcs = new TaskCompletionSource<bool>();

            string ffmpegArgs = saveWaveformPath != null
      ? "-i \"" + inputFile + "\" " +
        "-map 0:v -map 0:a " +
        "-c:v copy -c:a flac " +
        "-movflags frag_keyframe+empty_moov+separate_moof+default_base_moof " +
        "-f mp4 pipe:1 " +
        "-filter_complex \"[0:a]showwavespic=s=1200x300:colors=blue:scale=sqrt[wave]\" " +
        "-map \"[wave]\" -frames:v 1 -update 1 -y \"" + saveWaveformPath + "\""
      : "-i \"" + inputFile + "\" " +
        "-map 0:v -map 0:a " +
        "-c:v copy -c:a flac " +
        "-movflags frag_keyframe+empty_moov+separate_moof+default_base_moof " +
        "-f mp4 pipe:1 ";


            var psi = new ProcessStartInfo
            {
                FileName = _opts.Path,
                Arguments = ffmpegArgs,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            await _processSemaphore.WaitAsync(cts.Token);

            Process process = null;
            try
            {
                process = new Process { StartInfo = psi, EnableRaisingEvents = true };
                var errorOutput = new System.Text.StringBuilder();

                process.ErrorDataReceived += (sender, e) =>
                {
                    if (e.Data != null) errorOutput.AppendLine(e.Data);
                };

                process.Exited += (sender, e) =>
                {
                    try
                    {
                        string errors = errorOutput.ToString();

                        // Ошибку логируем только если код != 0
                        if (process.ExitCode != 0)
                        {
                            if (!string.IsNullOrWhiteSpace(errors))
                                _logger.LogError("FFmpeg stderr: {Errors}", errors);

                            tcs.TrySetException(
                                new InvalidOperationException(
                                    $"FFmpeg failed (code {process.ExitCode}). See stderr above."));
                        }
                        else
                        {
                            // успешный случай — не выводим stderr!
                            tcs.TrySetResult(true);
                        }
                    }
                    catch (Exception ex)
                    {
                        tcs.TrySetException(ex);
                    }
                    finally
                    {
                        _processSemaphore.Release();
                        process.Dispose();
                    }
                };


                cts.Token.Register(() =>
                {
                    try { if (!process.HasExited) process.Kill(); } catch { /* ignore */ }
                    tcs.TrySetCanceled();
                });

                if (!process.Start())
                {
                    _processSemaphore.Release();
                    process.Dispose();
                    throw new InvalidOperationException("Failed to start FFmpeg");
                }

                _logger.LogInformation("FFmpeg started, PID {Pid}, Input: {InputFile}", process.Id, inputFile);

                process.BeginErrorReadLine();

                return (process.StandardOutput.BaseStream, tcs.Task);
            }
            catch (Exception)
            {
                // Если Start() не удался, событие Exited не сработает,
                // поэтому семафор нужно освободить здесь.
                if (process != null && !process.HasExited)
                {
                    _processSemaphore.Release();
                }
                process?.Dispose();
                throw;
            }
        }
    }

}