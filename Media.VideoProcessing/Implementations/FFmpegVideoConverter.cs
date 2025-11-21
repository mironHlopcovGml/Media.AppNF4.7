using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
// Убедитесь, что пространство имен совпадает с вашим проектом
using ModularMediaApp.Modules.VideoProcessing;
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Media.VideoProcessing.Implementations
{
    public class FFmpegVideoConverter : IVideoConverter
    {
        private readonly FFmpegOptions _opts;
        private readonly ILogger<FFmpegVideoConverter> _logger;
        private readonly SemaphoreSlim _processSemaphore;

        public FFmpegVideoConverter(IOptions<FFmpegOptions> options, ILogger<FFmpegVideoConverter> logger)
        {
            _opts = options.Value ?? throw new ArgumentNullException(nameof(options));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            int limit = _opts.MaxConcurrentFFmpegProcesses > 0 ? _opts.MaxConcurrentFFmpegProcesses : 4;
            _processSemaphore = new SemaphoreSlim(limit, limit);
        }

        public Task<(Stream OutputStream, Task CompletionTask)> ConvertToMp4WithFlacAsync(string inputFile, CancellationToken ct = default)
        {
            return ConvertToMp4WithFlacAsync(inputFile, null, ct);
        }

        public async Task<(Stream OutputStream, Task CompletionTask)> ConvertToMp4WithFlacAsync(
            string inputFile,
            string saveWaveformPath,
            CancellationToken ct = default)
        {
            if (!File.Exists(inputFile))
                throw new FileNotFoundException("Input file not found", inputFile);

            // Ждем слот в семафоре
            await _processSemaphore.WaitAsync(ct).ConfigureAwait(false);

            Process process = new Process();
            // Важно: RunContinuationsAsynchronously заставляет await завершаться в пуле потоков, не блокируя UI/Context
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var errorOutput = new StringBuilder();

            // Флаг для гарантии, что Release вызовется только один раз
            // (Exited может вызываться конкурентно с catch блоком в редких случаях)
            int cleanupDone = 0;

            Stopwatch sw = new Stopwatch();

            void CleanupAndRelease()
            {
                if (Interlocked.Exchange(ref cleanupDone, 1) == 0)
                {
                    try { _processSemaphore.Release(); } catch { }
                    try { process.Dispose(); } catch { }
                }
            }

            try
            {
                process.StartInfo.FileName = _opts.Path;
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.CreateNoWindow = true;
                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.RedirectStandardError = true;

                // Используем безопасное построение аргументов
                process.StartInfo.Arguments = BuildArguments(inputFile, saveWaveformPath);

                process.EnableRaisingEvents = true;

                process.ErrorDataReceived += (s, e) =>
                {
                    if (e.Data != null) errorOutput.AppendLine(e.Data);
                };

                process.Exited += (s, e) =>
                {
                    sw.Stop();
                    // Логика завершения
                    bool success = (process.ExitCode == 0);

                    if (!success)
                    {
                        string err = errorOutput.ToString();
                        _logger.LogError("FFmpeg failed with code {Code}. " +
                            "Duration={Duration}ms." +
                            " Stderr: {Err}",
                            process.ExitCode,
                            sw.ElapsedMilliseconds,
                            err);
                        tcs.TrySetException(new InvalidOperationException("FFmpeg error: " + err));
                    }
                    else
                    {
                        _logger.LogInformation("FFmpeg completed successfully. " +
                            "Duration={Duration}ms",
                            sw.ElapsedMilliseconds);
                        tcs.TrySetResult(true);
                    }

                    // Освобождаем ресурсы
                    CleanupAndRelease();
                };

                bool started = process.Start();
                if (!started)
                    throw new InvalidOperationException("Could not start FFmpeg process (Start returned false)");

                sw.Start();

                _logger.LogInformation("FFmpeg started. PID: {Id}", process.Id);

                process.BeginErrorReadLine();

                // Регистрация отмены (CancellationToken)
                var ctr = ct.Register(() =>
                {
                    try
                    {
                        if (!process.HasExited)
                        {
                            process.Kill(); // В .NET 4.6 убивает только главный процесс
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Error killing FFmpeg process");
                    }
                    tcs.TrySetCanceled();
                });

                // Отписываемся от токена, когда задача завершена, чтобы избежать утечек памяти
                // (ContinueWith здесь безопасен, так как tcs создан с RunContinuationsAsynchronously)
                tcs.Task.ContinueWith(_ => ctr.Dispose());

                return (process.StandardOutput.BaseStream, tcs.Task);
            }
            catch (Exception)
            {
                // Если упали ДО запуска (например, BuildArguments выкинул исключение или Start() упал)
                CleanupAndRelease();
                throw;
            }
        }

        // --- БЕЗОПАСНАЯ РАБОТА С АРГУМЕНТАМИ ---

        private string BuildArguments(string inputFile, string saveWaveformPath)
        {
            var sb = new StringBuilder();

            // 1. Входной файл
            sb.Append("-i ");
            sb.Append(EscapeArgument(inputFile));
            sb.Append(" ");

            // 2. Маппинг и кодеки
            sb.Append("-map 0:v -map 0:a -c:v copy -c:a flac ");

            // 3. Флаги для стриминга (фрагментированный MP4)
            sb.Append("-movflags frag_keyframe+empty_moov+separate_moof+default_base_moof ");

            // 4. Вывод в Pipe
            sb.Append("-f mp4 - ");

            // 5. Опциональная генерация waveform
            if (!string.IsNullOrEmpty(saveWaveformPath))
            {
                // filter_complex - это один аргумент, содержащий пробелы и спецсимволы.
                // Его нужно экранировать целиком.
                string filter = "[0:a]showwavespic=s=1200x300:colors=blue:scale=sqrt[wave]";

                sb.Append("-filter_complex ");
                sb.Append(EscapeArgument(filter)); // Экранируем сам фильтр

                sb.Append(" -map \"[wave]\" -frames:v 1 -update 1 -y ");
                sb.Append(EscapeArgument(saveWaveformPath));
            }

            return sb.ToString();
        }

        /// <summary>
        /// Правильное экранирование аргументов командной строки для Windows (cmd.exe rules).
        /// Решает проблему путей, заканчивающихся на '\'.
        /// </summary>
        private static string EscapeArgument(string arg)
        {
            if (string.IsNullOrEmpty(arg)) return "\"\"";

            // Если нет пробелов и кавычек, можно не оборачивать (но лучше перестраховаться, если есть спецсимволы)
            // Для надежности оборачиваем почти все, что не выглядит простым числом или словом.
            if (arg.IndexOfAny(new[] { ' ', '\t', '\n', '\v', '\"' }) == -1 && !arg.EndsWith("\\"))
            {
                return arg;
            }

            var sb = new StringBuilder();
            sb.Append('"');

            for (int i = 0; i < arg.Length; i++)
            {
                char c = arg[i];
                int backslashes = 0;

                // Считаем обратные слэши
                while (i < arg.Length && arg[i] == '\\')
                {
                    backslashes++;
                    i++;
                }

                if (i == arg.Length)
                {
                    // Конец строки: удваиваем слэши, чтобы они не экранировали закрывающую кавычку
                    sb.Append('\\', backslashes * 2);
                    break;
                }
                else if (arg[i] == '"')
                {
                    // Перед кавычкой: удваиваем слэши + 1 слэш для экранирования кавычки
                    sb.Append('\\', backslashes * 2 + 1);
                    sb.Append('"');
                }
                else
                {
                    // Обычный символ: просто пишем слэши (если были) и сам символ
                    sb.Append('\\', backslashes);
                    sb.Append(arg[i]);
                }
            }

            sb.Append('"');
            return sb.ToString();
        }
    }
}