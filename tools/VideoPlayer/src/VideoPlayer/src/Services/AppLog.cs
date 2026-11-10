using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;

namespace VideoPlayer.Services;

/// <summary>
/// 应用级日志：全量日志按天写入 app-yyyyMMdd.log，ERROR / FATAL 额外汇总到固定的 error.log。
/// 日志目录：%APPDATA%\VideoPlayer\logs
/// 约束：线程安全；任何写入、滚动、清理失败都不能影响播放器主流程。
/// </summary>
public static class AppLog
{
    private const long ErrorLogMaxBytes = 2L * 1024 * 1024;
    private const int RetentionDays = 7;
    private const int MaxArgsLength = 500;

    private static readonly object Sync = new();
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);
    private static readonly string LogDirectory = Path.Combine(SettingsService.DataDirectory, "logs");
    private static readonly int ProcessId = Environment.ProcessId;

    private static bool _cleaned;

    /// <summary>日志目录，可在设置界面或状态栏中展示给用户。</summary>
    public static string LogDirectoryPath => LogDirectory;

    /// <summary>只含错误与致命错误的文件，便于快速定位问题。</summary>
    public static string ErrorLogPath => Path.Combine(LogDirectory, "error.log");

    /// <summary>当天的全量日志文件。</summary>
    public static string AppLogPath => Path.Combine(LogDirectory, $"app-{DateTime.Now:yyyyMMdd}.log");

    public static void Info(string message) => Write("INFO", message, null);

    public static void Warn(string message) => Write("WARN", message, null);

    public static void Error(string message, Exception? exception = null) => Write("ERROR", message, exception);

    public static void Fatal(string message, Exception? exception = null) => Write("FATAL", message, exception);

    /// <summary>进程启动打点，同时清理过期日志。</summary>
    public static void SessionStart(string[] args)
    {
        CleanupOldLogs();

        var version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "未知";
        var argText = args.Length == 0 ? "(无)" : string.Join(" | ", args);
        if (argText.Length > MaxArgsLength)
        {
            argText = argText[..MaxArgsLength] + "…";
        }

        Info($"==== 启动 ==== 版本={version} 参数数={args.Length} 参数=[{argText}] "
             + $"应用日志={AppLogPath} 错误日志={ErrorLogPath}");
    }

    public static void SessionEnd() => Info("==== 退出 ====");

    /// <summary>把一行日志写入文件；错误级别同时写入 error.log。</summary>
    private static void Write(string level, string message, Exception? exception)
    {
        string line;
        try
        {
            var sb = new StringBuilder();
            sb.Append(DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", null));
            sb.Append(" [pid:").Append(ProcessId)
              .Append(" tid:").Append(Environment.CurrentManagedThreadId).Append(']');
            sb.Append(" [").Append(level).Append("] ").Append(message);
            if (exception is not null)
            {
                sb.AppendLine();
                sb.Append(exception);
            }

            line = sb.ToString();
        }
        catch
        {
            return;
        }

        try
        {
            lock (Sync)
            {
                Directory.CreateDirectory(LogDirectory);
                var entry = line + Environment.NewLine;
                File.AppendAllText(AppLogPath, entry, Utf8NoBom);

                if (level is "ERROR" or "FATAL")
                {
                    RollErrorLogIfNeeded();
                    File.AppendAllText(ErrorLogPath, entry, Utf8NoBom);
                }
            }
        }
        catch (Exception ex)
        {
            // 日志不能拖垮播放器，退到调试输出
            Trace.WriteLine($"[AppLog] 写入失败：{ex.Message} | {line}");
        }
    }

    /// <summary>error.log 超过 2 MB 时转存一份，避免无限增长。</summary>
    private static void RollErrorLogIfNeeded()
    {
        try
        {
            var info = new FileInfo(ErrorLogPath);
            if (!info.Exists || info.Length < ErrorLogMaxBytes)
            {
                return;
            }

            var backup = Path.Combine(LogDirectory, "error.1.log");
            File.Copy(ErrorLogPath, backup, overwrite: true);
            File.WriteAllText(
                ErrorLogPath,
                $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff} [INFO] 错误日志已滚动，旧内容见 error.1.log{Environment.NewLine}",
                Utf8NoBom);
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[AppLog] 滚动 error.log 失败：{ex.Message}");
        }
    }

    /// <summary>只清理本应用自己生成的按天日志文件，保留最近 7 天。</summary>
    private static void CleanupOldLogs()
    {
        try
        {
            lock (Sync)
            {
                if (_cleaned)
                {
                    return;
                }

                _cleaned = true;
                Directory.CreateDirectory(LogDirectory);
                var cutoff = DateTime.Now.AddDays(-RetentionDays);
                foreach (var file in Directory.EnumerateFiles(LogDirectory, "app-????????.log"))
                {
                    if (File.GetLastWriteTime(file) < cutoff)
                    {
                        File.Delete(file);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[AppLog] 清理旧日志失败：{ex.Message}");
        }
    }
}
