using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using VideoPlayer.Models;
using VideoPlayer.Services;
using VideoPlayer.ViewModels;

namespace VideoPlayer;

public partial class MainWindow : Window
{
    private readonly SettingsService _settingsService;
    private readonly MainViewModel _viewModel;
    private readonly DispatcherTimer _hideControlsTimer;

    private bool _isFullscreen;
    private volatile bool _shutdownStarted;
    private bool _allowClose;
    private WindowState _previousState = WindowState.Normal;
    private Rect _restoreBounds;
    private string[]? _startupPaths;
    private readonly List<string> _pendingExternalPaths = new();
    private bool _isRendered;
    private bool _standaloneNotice;

    public MainWindow(SettingsService settingsService)
    {
        InitializeComponent();

        _settingsService = settingsService;
        _viewModel = new MainViewModel(settingsService);
        DataContext = _viewModel;

        _viewModel.SettingsRequested += OnSettingsRequested;
        _viewModel.ToggleFullscreenRequested += (_, _) => ToggleFullscreen();
        _viewModel.PlaybackFailed += OnPlaybackFailed;
        _viewModel.PropertyChanged += ViewModel_PropertyChanged;

        RestoreWindowPlacement(settingsService.Settings);

        _hideControlsTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
        _hideControlsTimer.Tick += (_, _) => HideControlsIfIdle();

        Loaded += MainWindow_Loaded;
        ContentRendered += MainWindow_ContentRendered;
        SourceInitialized += (_, _) => WindowEffects.Apply(this);
        StateChanged += (_, _) => UpdateMaximizeGlyph();
        Closing += MainWindow_Closing;
        PreviewKeyDown += MainWindow_PreviewKeyDown;

        DragOver += MainWindow_DragOver;
        Drop += MainWindow_Drop;

        SeekSlider.PreviewMouseLeftButtonDown += (_, _) => _viewModel.IsSeeking = true;
        SeekSlider.PreviewMouseLeftButtonUp += (_, _) => CommitSeek();
        SeekSlider.LostMouseCapture += (_, _) => CommitSeek();
    }

    /// <summary>由命令行或第二实例转发调用。</summary>
    public void OpenPaths(IEnumerable<string> paths) => _viewModel.OpenPaths(paths);

    /// <summary>窗口关闭流程是否已开始：此时 IPC 应拒绝新请求，让发送方另开窗口。</summary>
    public bool CanAcceptFiles => !_shutdownStarted && !_viewModel.IsDisposing;

    /// <summary>由 App 在 Show() 之前调用，等窗口完成首次渲染后再打开启动参数。</summary>
    public void SetStartupPaths(IEnumerable<string> paths) => _startupPaths = paths.ToArray();

    /// <summary>转发失败降级为独立窗口时给一次状态栏提示。</summary>
    public void SetStandaloneNotice() => _standaloneNotice = true;

    /// <summary>
    /// 由 App 冲刷 IPC 早到请求。窗口未完成首次渲染时先缓存，
    /// 避免 4K 大文件在 VideoView 的 HwndHost 就绪前被要求起播。
    /// </summary>
    public void EnqueueOpenPaths(IEnumerable<string> paths)
    {
        if (_shutdownStarted)
        {
            AppLog.Warn("窗口正在关闭，忽略外部打开请求");
            return;
        }

        _pendingExternalPaths.AddRange(paths.Where(p => !string.IsNullOrWhiteSpace(p)));
        if (_isRendered)
        {
            FlushExternalPaths();
        }
    }

    private void FlushExternalPaths()
    {
        if (_pendingExternalPaths.Count == 0)
        {
            return;
        }

        var paths = _pendingExternalPaths.ToArray();
        _pendingExternalPaths.Clear();
        Dispatcher.BeginInvoke(
            DispatcherPriority.Normal,
            new Action(() => OpenPaths(paths)));
    }

    private void MainWindow_ContentRendered(object? sender, EventArgs e)
    {
        ContentRendered -= MainWindow_ContentRendered;
        _isRendered = true;

        if (_startupPaths is { Length: > 0 } paths)
        {
            _startupPaths = null;
            Dispatcher.BeginInvoke(
                DispatcherPriority.Normal,
                new Action(() => OpenPaths(paths)));
        }

        FlushExternalPaths();

        if (_standaloneNotice)
        {
            // 低优先级：排在打开操作之后，避免被"已添加 N 个文件"覆盖
            Dispatcher.BeginInvoke(
                DispatcherPriority.Background,
                new Action(() => _viewModel.ShowStatus(
                    $"转发失败，已在新窗口打开。错误日志：{AppLog.ErrorLogPath}")));
        }
    }

    // ---------------- 初始化 ----------------

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        SetupVideoOverlay();
        ApplyPlaylistVisibility();
        UpdateMaximizeGlyph();
    }

    /// <summary>
    /// 视频由 HwndHost 承载，WPF 元素无法叠加在其上。
    /// 这里利用 VideoView 自带的前台透明窗口接管鼠标事件。
    /// </summary>
    private void SetupVideoOverlay()
    {
        try
        {
            var overlay = new Grid { Background = Brushes.Transparent };

            overlay.MouseMove += (_, _) => ShowControls();

            overlay.MouseLeftButtonDown += (_, args) =>
            {
                if (args.ClickCount == 2)
                {
                    ToggleFullscreen();
                }
            };

            overlay.MouseWheel += (_, args) =>
            {
                _viewModel.AdjustVolume(args.Delta > 0 ? 5 : -5);
                args.Handled = true;
            };

            VideoView.Content = overlay;
        }
        catch
        {
            // 个别驱动下前台窗口不可用，忽略即可
        }
    }

    private void RestoreWindowPlacement(AppSettings settings)
    {
        if (settings.WindowWidth >= MinWidth && settings.WindowHeight >= MinHeight)
        {
            Width = settings.WindowWidth;
            Height = settings.WindowHeight;
        }

        if (settings.WindowLeft is { } left && settings.WindowTop is { } top && IsOnScreen(left, top))
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = left;
            Top = top;
        }

        if (settings.WindowMaximized)
        {
            WindowState = WindowState.Maximized;
        }
    }

    private static bool IsOnScreen(double left, double top)
    {
        var virtualLeft = SystemParameters.VirtualScreenLeft;
        var virtualTop = SystemParameters.VirtualScreenTop;
        var virtualRight = virtualLeft + SystemParameters.VirtualScreenWidth;
        var virtualBottom = virtualTop + SystemParameters.VirtualScreenHeight;

        return left >= virtualLeft - 100
               && top >= virtualTop - 100
               && left + 240 <= virtualRight
               && top + 120 <= virtualBottom;
    }

    // ---------------- 窗口按钮 ----------------

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ToggleMaximize();
            return;
        }

        if (e.ButtonState == MouseButtonState.Pressed && WindowState != WindowState.Maximized)
        {
            try
            {
                DragMove();
            }
            catch
            {
                // ignore
            }
        }
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void MaximizeButton_Click(object sender, RoutedEventArgs e) => ToggleMaximize();

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void ToggleMaximize()
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void UpdateMaximizeGlyph()
    {
        var maximized = WindowState == WindowState.Maximized;
        MaximizeButton.Content = maximized ? "\uE923" : "\uE922";
        MaximizeButton.ToolTip = maximized ? "还原" : "最大化";
    }

    // ---------------- 全屏与控件自动隐藏 ----------------

    private void ToggleFullscreen()
    {
        if (_isFullscreen)
        {
            ExitFullscreen();
        }
        else
        {
            EnterFullscreen();
        }
    }

    private void EnterFullscreen()
    {
        if (_isFullscreen)
        {
            return;
        }

        _isFullscreen = true;
        _viewModel.IsFullscreen = true;

        _previousState = WindowState;
        _restoreBounds = WindowState == WindowState.Normal
            ? new Rect(Left, Top, Width, Height)
            : RestoreBounds;

        WindowState = WindowState.Normal;
        ResizeMode = ResizeMode.NoResize;
        Topmost = true;

        var bounds = ScreenHelper.GetMonitorBounds(this);
        Left = bounds.Left;
        Top = bounds.Top;
        Width = bounds.Width;
        Height = bounds.Height;

        TitleBar.Visibility = Visibility.Collapsed;
        StatusBar.Visibility = Visibility.Collapsed;
        ApplyPlaylistVisibility();

        ShowControls(force: true);
    }

    private void ExitFullscreen()
    {
        if (!_isFullscreen)
        {
            return;
        }

        _isFullscreen = false;
        _viewModel.IsFullscreen = false;

        Topmost = false;
        ResizeMode = ResizeMode.CanResize;
        TitleBar.Visibility = Visibility.Visible;
        StatusBar.Visibility = Visibility.Visible;

        WindowState = WindowState.Normal;

        if (_restoreBounds.Width > 0)
        {
            Left = _restoreBounds.Left;
            Top = _restoreBounds.Top;
            Width = _restoreBounds.Width;
            Height = _restoreBounds.Height;
        }

        if (_previousState == WindowState.Maximized)
        {
            WindowState = WindowState.Maximized;
        }

        ApplyPlaylistVisibility();
        ControlBar.Visibility = Visibility.Visible;
        Mouse.OverrideCursor = null;
        _hideControlsTimer.Stop();
    }

    private void ShowControls(bool force = false)
    {
        if (!_isFullscreen && !force)
        {
            return;
        }

        ControlBar.Visibility = Visibility.Visible;
        Mouse.OverrideCursor = null;
        RestartHideTimer();
    }

    private void RestartHideTimer()
    {
        _hideControlsTimer.Stop();
        if (_isFullscreen)
        {
            _hideControlsTimer.Start();
        }
    }

    private void HideControlsIfIdle()
    {
        _hideControlsTimer.Stop();

        if (!_isFullscreen || !_viewModel.IsPlaying)
        {
            return;
        }

        if (ControlBar.IsMouseOver || AdjustToggle.IsChecked == true)
        {
            return;
        }

        ControlBar.Visibility = Visibility.Collapsed;
        Mouse.OverrideCursor = Cursors.None;
    }

    private void ApplyPlaylistVisibility()
    {
        var visible = _viewModel.IsPlaylistVisible && !_isFullscreen;
        PlaylistPanel.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        PlaylistColumn.Width = visible ? new GridLength(300) : new GridLength(0);
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(MainViewModel.IsPlaylistVisible):
                ApplyPlaylistVisibility();
                break;
            case nameof(MainViewModel.IsPlaying) when _isFullscreen:
                RestartHideTimer();
                break;
        }
    }

    // ---------------- 进度拖动 ----------------

    private void CommitSeek()
    {
        if (!_viewModel.IsSeeking)
        {
            return;
        }

        _viewModel.IsSeeking = false;
        _viewModel.SeekToPosition(SeekSlider.Value);
    }

    // ---------------- 设置与播放列表 ----------------

    private void OnSettingsRequested(object? sender, EventArgs e)
    {
        var window = new SettingsWindow(
            _settingsService.Settings,
            typeof(App).Assembly.GetName().Version?.ToString(3) ?? "1.0.0",
            _viewModel.LibVlcVersion)
        {
            Owner = this
        };

        if (window.ShowDialog() == true)
        {
            _viewModel.ApplySettings();
            ApplyPlaylistVisibility();
        }
    }

    private void OnPlaybackFailed(string message)
    {
        MessageBox.Show(
            this,
            message,
            "VideoPlayer 播放失败",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    private void PlaylistList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (PlaylistList.SelectedItem is MediaItem item)
        {
            _viewModel.PlayItemCommand.Execute(item);
        }
    }

    // ---------------- 拖拽 ----------------

    private void MainWindow_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void MainWindow_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files)
        {
            _viewModel.OpenPaths(files);
        }
    }

    // ---------------- 快捷键 ----------------

    private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.FocusedElement is ComboBox or ComboBoxItem)
        {
            return;
        }

        var shift = Keyboard.Modifiers == ModifierKeys.Shift;
        var ctrl = Keyboard.Modifiers == ModifierKeys.Control;

        switch (e.Key)
        {
            case Key.Space:
                _viewModel.TogglePlayPause();
                e.Handled = true;
                break;
            case Key.Left:
                _viewModel.SeekRelative(TimeSpan.FromSeconds(shift ? -30 : -5));
                e.Handled = true;
                break;
            case Key.Right:
                _viewModel.SeekRelative(TimeSpan.FromSeconds(shift ? 30 : 5));
                e.Handled = true;
                break;
            case Key.Up:
                _viewModel.AdjustVolume(5);
                e.Handled = true;
                break;
            case Key.Down:
                _viewModel.AdjustVolume(-5);
                e.Handled = true;
                break;
            case Key.M:
                _viewModel.IsMuted = !_viewModel.IsMuted;
                e.Handled = true;
                break;
            case Key.F:
            case Key.F11:
                ToggleFullscreen();
                e.Handled = true;
                break;
            case Key.Escape:
                if (_isFullscreen)
                {
                    ExitFullscreen();
                    e.Handled = true;
                }

                break;
            case Key.S:
                _viewModel.TakeSnapshot();
                e.Handled = true;
                break;
            case Key.L:
                _viewModel.IsPlaylistVisible = !_viewModel.IsPlaylistVisible;
                e.Handled = true;
                break;
            case Key.Home:
                _viewModel.SeekToPosition(0);
                e.Handled = true;
                break;
            case Key.End:
                _viewModel.SeekToPosition(1);
                e.Handled = true;
                break;
            case Key.OemOpenBrackets:
                _viewModel.Rate = Math.Max(0.5, Math.Round(_viewModel.Rate - 0.25, 2));
                e.Handled = true;
                break;
            case Key.OemCloseBrackets:
                _viewModel.Rate = Math.Min(2.0, Math.Round(_viewModel.Rate + 0.25, 2));
                e.Handled = true;
                break;
            case Key.O when ctrl:
                _viewModel.OpenFilesCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.N when ctrl:
                _viewModel.NextCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.P when ctrl:
                _viewModel.PreviousCommand.Execute(null);
                e.Handled = true;
                break;
        }

        if (!e.Handled && e.Key >= Key.D0 && e.Key <= Key.D9)
        {
            var digit = e.Key == Key.D0 ? 0 : e.Key - Key.D0;
            _viewModel.SeekToPosition(digit / 10.0);
            e.Handled = true;
        }
    }

    // ---------------- 关闭 ----------------

    private async void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (_allowClose)
        {
            return;
        }

        e.Cancel = true;
        if (_shutdownStarted)
        {
            return;
        }

        _shutdownStarted = true;
        AppLog.Info("主窗口开始关闭，此后不再接收来自其他实例的文件");
        _hideControlsTimer.Stop();

        var settings = _settingsService.Settings;

        if (_isFullscreen)
        {
            ExitFullscreen();
        }

        if (WindowState == WindowState.Maximized)
        {
            settings.WindowMaximized = true;
        }
        else
        {
            settings.WindowMaximized = false;
            settings.WindowWidth = Width;
            settings.WindowHeight = Height;
            settings.WindowLeft = Left;
            settings.WindowTop = Top;
        }

        VideoView.ClearValue(LibVLCSharp.WPF.VideoView.MediaPlayerProperty);
        Hide();

        try
        {
            await _viewModel.DisposeAsync();
        }
        catch (Exception ex)
        {
            AppLog.Error("关闭播放器时释放资源失败", ex);
            MessageBox.Show(
                $"关闭播放器时释放资源失败：{ex.Message}\n\n错误日志：{AppLog.ErrorLogPath}",
                "VideoPlayer 关闭提示",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        finally
        {
            _allowClose = true;
            Close();
        }
    }
}
