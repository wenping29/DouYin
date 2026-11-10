using System.Windows;
using System.Windows.Threading;
using VideoPlayer.Services;

namespace VideoPlayer;

public partial class App : Application
{
    private readonly object _pendingLock = new();
    private readonly Queue<string[]> _pendingPaths = new();

    private SingleInstance? _singleInstance;
    private MainWindow? _mainWindow;
    private bool _forwardFallback;
    private bool _handlersInstalled;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 最先装全局异常钩子并打启动点，保证后续任何失败都有日志可查。
        InstallGlobalExceptionHandlers();
        AppLog.SessionStart(e.Args);

        if (e.Args.Length == 1 && e.Args[0] == "--configure-default-player")
        {
            if (FileAssociationService.Register())
            {
                MessageBox.Show(
                    "VideoPlayer 已登记为视频文件打开方式候选应用。请在接下来打开的 Windows“默认应用”设置中选择 VideoPlayer。",
                    "设置默认播放器",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                FileAssociationService.OpenDefaultAppsSettings();
            }
            else
            {
                AppLog.Error("登记文件关联失败（--configure-default-player）");
                MessageBox.Show(
                    "无法登记 VideoPlayer 的文件关联。请稍后在软件设置中重试。",
                    "设置默认播放器失败",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }

            Shutdown();
            return;
        }

        if (e.Args.Length == 1 && e.Args[0] == "--unregister-file-associations")
        {
            if (!FileAssociationService.Unregister())
            {
                AppLog.Error("卸载时清除文件关联失败（--unregister-file-associations）");
                MessageBox.Show(
                    "卸载时未能清除 VideoPlayer 的文件关联注册。",
                    "VideoPlayer 卸载提示",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }

            Shutdown();
            return;
        }

        _singleInstance = new SingleInstance();
        if (!_singleInstance.IsFirstInstance)
        {
            if (e.Args.Length == 0)
            {
                AppLog.Info("已有实例在运行且无启动参数，本进程退出");
                Shutdown();
                return;
            }

            if (SingleInstance.TrySendToExisting(e.Args))
            {
                AppLog.Info("启动参数已转发给已运行实例，本进程退出");
                Shutdown();
                return;
            }

            // 转发不通时不再只弹错误框并退出，而是由本进程直接打开文件，
            // 保证用户双击的文件一定能被打开（详见 error.log）。
            _forwardFallback = true;
            AppLog.Error("IPC 转发失败，降级为独立窗口直接打开启动参数中的文件。"
                         + " 常见原因：旧实例正在冷启动大文件或退出中、管道权限不一致。");
        }
        else
        {
            // 关键：管道监听必须先于主窗口构造（LibVLC 初始化 + 恢复播放列表可能耗时数秒），
            // 否则冷启动期间双击的文件会因为连不上管道而丢失。
            _singleInstance.FilesReceived = OnFilesReceived;
            _singleInstance.StartServer();
        }

        try
        {
            var settingsService = new SettingsService();
            _mainWindow = new MainWindow(settingsService);
            MainWindow = _mainWindow;

            if (_forwardFallback)
            {
                _mainWindow.SetStandaloneNotice();
            }

            if (e.Args.Length > 0)
            {
                // 交给窗口在首次渲染完成后再打开：此时 VideoView 的 HwndHost 已建立，
                // 避免 4K 大文件冷启动时视频输出尚未就绪就被要求起播。
                _mainWindow.SetStartupPaths(e.Args);
            }

            _mainWindow.Show();

            // 冲刷在窗口构造期间提前到达的 IPC 请求
            FlushPendingPaths();
        }
        catch (Exception ex)
        {
            AppLog.Fatal("主窗口初始化失败", ex);
            MessageBox.Show(
                $"播放器启动失败：{ex.Message}\n\n错误日志：{AppLog.ErrorLogPath}",
                "VideoPlayer 启动失败",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown();
        }
    }

    /// <summary>
    /// 后台 IPC 线程调用：窗口还没建好也照样受理，先入队回 ack，
    /// 等窗口就绪由 UI 线程冲刷。窗口正在退出时明确拒绝，让发送方另开窗口。
    /// </summary>
    private bool OnFilesReceived(string[] paths)
    {
        if (_mainWindow is { CanAcceptFiles: false })
        {
            AppLog.Warn("IPC 请求被拒绝：主窗口正在关闭，改由发送方独立打开");
            return false;
        }

        lock (_pendingLock)
        {
            _pendingPaths.Enqueue(paths);
        }

        AppLog.Info($"IPC 路径已入队（{paths.Length} 个），主窗口"
                    + (_mainWindow is null ? "尚未创建" : "已创建"));
        Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(FlushPendingPaths));
        return true;
    }

    private void FlushPendingPaths()
    {
        if (_mainWindow is null)
        {
            return;
        }

        string[][] batches;
        lock (_pendingLock)
        {
            if (_pendingPaths.Count == 0)
            {
                return;
            }

            batches = _pendingPaths.ToArray();
            _pendingPaths.Clear();
        }

        var paths = batches.SelectMany(batch => batch).ToArray();
        AppLog.Info($"冲刷 IPC 待打开路径：{paths.Length} 个");
        _mainWindow.EnqueueOpenPaths(paths);
    }

    private void InstallGlobalExceptionHandlers()
    {
        if (_handlersInstalled)
        {
            return;
        }

        _handlersInstalled = true;

        DispatcherUnhandledException += OnDispatcherUnhandledException;

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            AppLog.Fatal(
                $"AppDomain 未处理异常（IsTerminating={args.IsTerminating}）",
                args.ExceptionObject as Exception);

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            AppLog.Error("后台任务出现未观察异常", args.Exception);
            args.SetObserved();
        };
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        AppLog.Fatal("UI 线程未处理异常", e.Exception);

        MessageBox.Show(
            $"{e.Exception.Message}\n\n错误日志：{AppLog.ErrorLogPath}",
            "VideoPlayer 出现错误",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
        e.Handled = true;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        AppLog.SessionEnd();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
