using System.IO;
using System.IO.Pipes;
using System.Text;

namespace VideoPlayer.Services;

/// <summary>
/// 单实例管理：第二次启动时把文件路径通过命名管道转发给已运行的实例。
///
/// 修复要点：
/// 1. 管道监听由调用方在拿到 Mutex 后立刻启动，不等主窗口 / LibVLC 初始化；
/// 2. 服务端并发受理连接，ReadLine 行协议 + 单字节 ack，避免"发出去就算成功"；
/// 3. 客户端失败时把原因写入日志，由调用方决定是否降级为独立窗口；
/// 4. Mutex 不再争抢所有权，避免第二实例在退出中的旧进程上阻塞等待。
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private const string MutexName = @"Local\VideoPlayer.SingleInstance";
    private const string PipeName = "VideoPlayer.Pipe";
    private const string ProtocolHeader = "VP1";
    private const string ProtocolEnd = "END";

    private const int ConnectTimeoutMs = 300;
    private const int MaxAttempts = 20;          // 总预算约 8 秒；监听已提前启动，无需长时间重试
    private const int RetryDelayMs = 100;
    private const int AckTimeoutMs = 5000;
    private const byte AckOk = 1;
    private const byte AckReject = 2;

    private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);

    private readonly Mutex _mutex;
    private readonly bool _isFirstInstance;
    private readonly bool _ownsMutex;
    private CancellationTokenSource? _cts;
    private Thread? _serverThread;

    public SingleInstance()
    {
        // initiallyOwned=false：第二实例只判断 createdNew，不会阻塞等待旧进程释放所有权。
        _mutex = new Mutex(initiallyOwned: false, MutexName, out _isFirstInstance);
        if (_isFirstInstance)
        {
            try
            {
                _ownsMutex = _mutex.WaitOne(0, false);
            }
            catch (AbandonedMutexException)
            {
                // 上一个进程异常退出未释放，此时本线程已获得所有权
                _ownsMutex = true;
            }
        }
    }

    public bool IsFirstInstance => _isFirstInstance;

    /// <summary>
    /// 收到来自其他实例的文件参数（在后台线程触发）。
    /// 返回 true 表示路径已被受理并排队，服务端据此回 ack；窗口正在退出时返回 false。
    /// </summary>
    public Func<string[], bool>? FilesReceived { get; set; }

    /// <summary>启动管道监听。应在确认自己是首个实例后立即调用，不要等主窗口构造。</summary>
    public void StartServer()
    {
        if (!_isFirstInstance || _cts is not null)
        {
            return;
        }

        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _serverThread = new Thread(() => ServerLoopAsync(token).GetAwaiter().GetResult())
        {
            IsBackground = true,
            Name = "VideoPlayer.IPC"
        };
        _serverThread.Start();
        AppLog.Info($"IPC 监听已启动（管道 {PipeName}）");
    }

    private async Task ServerLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            NamedPipeServerStream? server = null;
            try
            {
                server = new NamedPipeServerStream(
                    PipeName,
                    PipeDirection.InOut,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                await server.WaitForConnectionAsync(token).ConfigureAwait(false);

                // 连接交给独立任务处理，主循环立即创建下一个监听实例，
                // 消除"正在读一个请求时不接受新连接"的窗口。
                var connected = server;
                server = null;
                _ = Task.Run(() => HandleClient(connected), CancellationToken.None);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                AppLog.Warn($"IPC 监听异常：{ex.GetType().Name}: {ex.Message}");
                try
                {
                    await Task.Delay(200, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
            finally
            {
                server?.Dispose();
            }
        }

        AppLog.Info("IPC 监听已停止");
    }

    private void HandleClient(NamedPipeServerStream server)
    {
        using (server)
        {
            try
            {
                using var reader = new StreamReader(server, Encoding.UTF8, true, 4096, leaveOpen: true);
                var lines = new List<string>();
                string? line;
                while ((line = reader.ReadLine()) is not null)
                {
                    if (line == ProtocolEnd)
                    {
                        break;
                    }

                    lines.Add(line);
                }

                if (lines.Count == 0 || lines[0] != ProtocolHeader)
                {
                    AppLog.Warn("IPC 收到无法识别的请求，已拒绝");
                    server.WriteByte(AckReject);
                    return;
                }

                lines.RemoveAt(0);
                var accepted = lines.Count > 0
                               && (FilesReceived?.Invoke(lines.ToArray()) ?? false);
                AppLog.Info($"IPC 收到 {lines.Count} 个路径，受理={accepted}");
                server.WriteByte(accepted ? AckOk : AckReject);
                server.Flush();
            }
            catch (Exception ex)
            {
                AppLog.Warn($"IPC 处理连接失败：{ex.GetType().Name}: {ex.Message}");
            }
        }
    }

    /// <summary>把启动参数发给已运行实例，只有对方明确回 ack 才算成功。</summary>
    public static bool TrySendToExisting(string[] args)
    {
        var paths = args
            .Where(a => !string.IsNullOrWhiteSpace(a))
            .Select(a => a.Replace('\r', ' ').Replace('\n', ' '))
            .ToArray();

        if (paths.Length == 0)
        {
            return true;
        }

        AppLog.Info($"开始转发 {paths.Length} 个路径到已运行实例");

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            try
            {
                client.Connect(ConnectTimeoutMs);
            }
            catch (Exception ex) when (ex is IOException or TimeoutException
                                          or UnauthorizedAccessException or ObjectDisposedException)
            {
                if (attempt == MaxAttempts)
                {
                    AppLog.Error($"IPC 转发失败：{MaxAttempts} 次均无法连接管道。"
                                 + $" 最后一次={ex.GetType().Name}: {ex.Message}");
                    break;
                }

                if (attempt <= 3 || attempt == MaxAttempts)
                {
                    AppLog.Info($"IPC 连接尝试 {attempt}/{MaxAttempts} 失败：{ex.GetType().Name}: {ex.Message}");
                }

                Thread.Sleep(RetryDelayMs);
                continue;
            }

            try
            {
                using (var writer = new StreamWriter(client, Utf8NoBom, 4096, leaveOpen: true))
                {
                    writer.WriteLine(ProtocolHeader);
                    foreach (var path in paths)
                    {
                        writer.WriteLine(path);
                    }

                    writer.WriteLine(ProtocolEnd);
                    writer.Flush();
                }

                // 同步读放在线程池里做，避免在 UI 线程无限等一个不回 ack 的实例。
                var ackTask = Task.Run(() => client.ReadByte());
                if (!ackTask.Wait(AckTimeoutMs))
                {
                    ObserveFault(ackTask);
                    AppLog.Error("IPC 转发失败：已运行实例在 5 秒内未回确认（可能正在启动或退出中）");
                    return false;
                }

                var ack = ackTask.Result;
                if (ack == AckOk)
                {
                    AppLog.Info($"IPC 转发成功（第 {attempt} 次尝试）");
                    return true;
                }

                // 对方明确拒绝（例如窗口正在关闭）：重试没有意义，交给调用方降级处理。
                AppLog.Error($"IPC 转发被拒绝（ack={ack}），已运行实例当前无法接收文件");
                return false;
            }
            catch (Exception ex)
            {
                AppLog.Error("IPC 转发过程中出现异常", ex);
                return false;
            }
        }

        return false;
    }

    private static void ObserveFault(Task task)
        => task.ContinueWith(
            t => { _ = t.Exception; },
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

    public void Dispose()
    {
        try
        {
            _cts?.Cancel();
        }
        catch (Exception ex)
        {
            AppLog.Warn($"停止 IPC 监听时出错：{ex.Message}");
        }

        if (_ownsMutex)
        {
            try
            {
                _mutex.ReleaseMutex();
            }
            catch (Exception ex)
            {
                AppLog.Warn($"释放单实例 Mutex 时出错：{ex.Message}");
            }
        }

        _cts?.Dispose();
        _mutex.Dispose();
    }
}
