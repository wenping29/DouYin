import 'dart:async';

import 'package:audioplayers/audioplayers.dart';
import 'package:camera/camera.dart';
import 'package:flutter/material.dart';

import '../../core/theme.dart';
import '../../data/music_catalog.dart';
import '../upload/album_page.dart';
import '../upload/edit_page.dart';
import '../upload/publish_page.dart';

class CameraPage extends StatefulWidget {
  const CameraPage({super.key});

  @override
  State<CameraPage> createState() => _CameraPageState();
}

class _CameraPageState extends State<CameraPage>
    with WidgetsBindingObserver {
  List<CameraDescription> _cameras = [];
  CameraController? _controller;
  int _cameraIndex = 0; // 0=后置 1=前置
  bool _initError = false;
  String? _initErrorMsg;

  // 0 分段拍 / 1 照片 / 2 视频（按需求默认进入拍照页）
  int _mode = 1;
  bool _flashOn = false;
  bool _countdown = false;
  bool _recording = false;
  bool _pressing = false;
  bool _shutter = false;
  String? _musicName;
  final Stopwatch _recTimer = Stopwatch();
  Timer? _uiTimer;

  static const _modes = ['分段拍', '照片', '视频'];

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addObserver(this);
    _initCamera();
  }

  @override
  void dispose() {
    WidgetsBinding.instance.removeObserver(this);
    _uiTimer?.cancel();
    _controller?.dispose();
    super.dispose();
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    if (_controller == null) return;
    if (state == AppLifecycleState.inactive) {
      _controller?.dispose();
    } else if (state == AppLifecycleState.resumed) {
      _initCamera();
    }
  }

  Future<void> _initCamera() async {
    try {
      _cameras = await availableCameras();
      if (_cameras.isEmpty) {
        if (mounted) setState(() => _initError = true);
        return;
      }
      await _setupController(_cameraIndex);
    } catch (e) {
      if (mounted) {
        setState(() {
          _initError = true;
          _initErrorMsg = e.toString();
        });
      }
    }
  }

  Future<void> _setupController(int index) async {
    if (_cameras.isEmpty) return;
    _cameraIndex = index.clamp(0, _cameras.length - 1);
    final controller = CameraController(
      _cameras[_cameraIndex],
      ResolutionPreset.high,
      enableAudio: true,
      imageFormatGroup: ImageFormatGroup.jpeg,
    );
    _controller = controller;
    try {
      await controller.initialize();
      if (mounted) {
        setState(() {
          _initError = false;
          _initErrorMsg = null;
        });
      }
      await _applyFlash();
    } catch (e) {
      if (mounted) {
        setState(() {
          _initError = true;
          _initErrorMsg = e.toString();
        });
      }
    }
  }

  Future<void> _applyFlash() async {
    final c = _controller;
    if (c == null || !c.value.isInitialized) return;
    try {
      final mode = _flashOn
          ? (_mode == 1 ? FlashMode.always : FlashMode.torch)
          : FlashMode.off;
      await c.setFlashMode(mode);
    } catch (_) {
      // 前置摄像头可能不支持物理闪光，忽略
    }
  }

  Future<void> _flip() async {
    if (_cameras.length < 2) {
      _toast('设备仅有一个摄像头');
      return;
    }
    if (_recording) return;
    final next = (_cameraIndex + 1) % _cameras.length;
    await _controller?.dispose();
    _controller = null;
    if (mounted) setState(() {});
    await _setupController(next);
    _toast(next == 0 ? '已切换至后摄像头' : '已切换至前摄像头');
  }

  Future<void> _toggleFlash() async {
    _flashOn = !_flashOn;
    await _applyFlash();
    if (mounted) setState(() {});
    _toast(_flashOn ? '闪光灯开启' : '闪光灯关闭');
  }

  void _onModeChanged(int v) {
    if (_recording) return;
    setState(() => _mode = v);
    _applyFlash();
  }

  Future<void> _capturePhoto() async {
    final c = _controller;
    if (c == null || !c.value.isInitialized) {
      _toast('相机未就绪');
      return;
    }
    if (c.value.isTakingPicture) return;
    try {
      await c.setFlashMode(_flashOn ? FlashMode.always : FlashMode.off);
      // 快门闪烁
      if (mounted) setState(() => _shutter = true);
      final file = await c.takePicture();
      if (!mounted) return;
      Future.delayed(const Duration(milliseconds: 120), () {
        if (mounted) setState(() => _shutter = false);
      });
        final result = await Navigator.of(context).push<bool>(
          MaterialPageRoute(
            builder: (_) => PublishPage(
              image: XFile(file.path),
              musicName: _musicName ?? '',
            ),
          ),
        );
      if (result == true && mounted) Navigator.of(context).pop(true);
    } catch (e) {
      if (mounted) setState(() => _shutter = false);
      _toast('拍照失败：$e');
    }
  }

  Future<void> _startRecord() async {
    final c = _controller;
    if (c == null || !c.value.isInitialized) return;
    if (c.value.isRecordingVideo) return;
    setState(() => _pressing = true);
    try {
      await c.setFlashMode(_flashOn ? FlashMode.torch : FlashMode.off);
      await c.startVideoRecording();
      if (!mounted) return;
      _recTimer.reset();
      _recTimer.start();
      _uiTimer = Timer.periodic(const Duration(seconds: 1), (_) {
        if (mounted) setState(() {});
      });
      setState(() => _recording = true);
    } catch (e) {
      setState(() => _pressing = false);
      _toast('录制启动失败：$e');
    }
  }

  Future<void> _stopRecord() async {
    setState(() => _pressing = false);
    final c = _controller;
    if (c == null || !c.value.isRecordingVideo) return;
    try {
      _uiTimer?.cancel();
      _recTimer.stop();
      final file = await c.stopVideoRecording();
      if (!mounted) return;
      setState(() => _recording = false);
      final result = await Navigator.of(context).push<bool>(
        MaterialPageRoute(
          builder: (_) => EditPage(
            videos: [XFile(file.path)],
            musicName: _musicName ?? '',
          ),
        ),
      );
      if (result == true && mounted) Navigator.of(context).pop(true);
    } catch (e) {
      if (mounted) setState(() => _recording = false);
      _toast('停止录制失败：$e');
    }
  }

  void _toast(String msg) {
    ScaffoldMessenger.of(context).showSnackBar(
      SnackBar(content: Text(msg), duration: const Duration(seconds: 1)),
    );
  }

  void _close() => Navigator.of(context).pop();

  void _openAlbum() {
    Navigator.of(context).push(MaterialPageRoute(builder: (_) => const AlbumPage()));
  }

  void _showMusicSheet() {
    showModalBottomSheet(
      context: context,
      backgroundColor: AppColors.surfaceAlt,
      shape: const RoundedRectangleBorder(
        borderRadius: BorderRadius.vertical(top: Radius.circular(18)),
      ),
      builder: (sheetContext) => _MusicSheet(
        musicName: _musicName,
        onPick: (name) {
          setState(() => _musicName = name);
          Navigator.of(sheetContext).pop();
        },
      ),
    );
  }

  String get _recDuration {
    final s = _recTimer.elapsed.inSeconds;
    final m = s ~/ 60;
    final sec = s % 60;
    return '${m.toString().padLeft(2, '0')}:${sec.toString().padLeft(2, '0')}';
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      backgroundColor: Colors.black,
      extendBodyBehindAppBar: true,
      body: Stack(
        children: [
          // 实时相机预览
          _cameraPreview(),
          // 录制中红点 + 计时
          if (_recording)
            SafeArea(
              child: Align(
                alignment: Alignment.topCenter,
                child: Container(
                  margin: const EdgeInsets.only(top: 64),
                  padding:
                      const EdgeInsets.symmetric(horizontal: 10, vertical: 4),
                  decoration: BoxDecoration(
                    color: Colors.black54,
                    borderRadius: BorderRadius.circular(14),
                  ),
                  child: Row(
                    mainAxisSize: MainAxisSize.min,
                    children: [
                      Container(
                        width: 9,
                        height: 9,
                        decoration: const BoxDecoration(
                          color: Colors.red,
                          shape: BoxShape.circle,
                        ),
                      ),
                      const SizedBox(width: 6),
                      Text(_recDuration,
                          style: const TextStyle(color: Colors.white)),
                    ],
                  ),
                ),
              ),
            ),
          // 快门闪烁
          if (_shutter)
            Container(color: Colors.white.withValues(alpha: 0.85)),
          // 顶部状态栏 + 工具栏
          SafeArea(
            child: Column(
              children: [
                _TopBar(
                  musicName: _musicName,
                  onClose: _close,
                  onMusic: _showMusicSheet,
                ),
                const SizedBox(height: 8),
                Expanded(
                  child: Row(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      const Spacer(),
                      _SideTools(
                        flashOn: _flashOn,
                        flipped: _cameraIndex != 0,
                        countdown: _countdown,
                        recording: _recording,
                        onFlip: _flip,
                        onFlash: _toggleFlash,
                        onCountdown: () {
                          setState(() => _countdown = !_countdown);
                          _toast(_countdown ? '倒计时 3 秒已开启' : '倒计时已关闭');
                        },
                        onToast: _toast,
                      ),
                    ],
                  ),
                ),
              ],
            ),
          ),
          // 底部拍摄区 + 导航
          SafeArea(
            top: false,
            child: Column(
              mainAxisAlignment: MainAxisAlignment.end,
              children: [
                _ModeTabs(
                  mode: _mode,
                  onChanged: _onModeChanged,
                ),
                const SizedBox(height: 18),
                _CaptureBar(
                  mode: _mode,
                  pressing: _pressing,
                  recording: _recording,
                  onPhotoTap: _capturePhoto,
                  onVideoDown: _startRecord,
                  onVideoUp: _stopRecord,
                  onAlbum: _openAlbum,
                  onEffect: () => _toast('特效（敬请期待）'),
                ),
                const SizedBox(height: 14),
                _CameraNavBar(onTap: _toast),
              ],
            ),
          ),
        ],
      ),
    );
  }

  Widget _cameraPreview() {
    final c = _controller;
    if (c != null && c.value.isInitialized) {
      return Positioned.fill(child: CameraPreview(c));
    }
    if (_initError) {
      return Container(
        color: const Color(0xFF0E0D0C),
        child: Center(
          child: Padding(
            padding: const EdgeInsets.all(24),
            child: Column(
              mainAxisSize: MainAxisSize.min,
              children: [
                const Icon(Icons.camera_alt_outlined,
                    color: Colors.white54, size: 40),
                const SizedBox(height: 12),
                const Text('无法访问相机',
                    style: TextStyle(color: Colors.white70, fontSize: 14)),
                const SizedBox(height: 6),
                Text(
                  _initErrorMsg ?? '',
                  style:
                      const TextStyle(color: Colors.white38, fontSize: 11),
                  textAlign: TextAlign.center,
                ),
                const SizedBox(height: 12),
                FilledButton(
                  onPressed: () {
                    setState(() => _initError = false);
                    _initCamera();
                  },
                  child: const Text('重试'),
                ),
              ],
            ),
          ),
        ),
      );
    }
    return Container(
      color: const Color(0xFF0E0D0C),
      child: const Center(
        child: CircularProgressIndicator(color: Colors.white54),
      ),
    );
  }
}

class _TopBar extends StatelessWidget {
  final String? musicName;
  final VoidCallback onClose;
  final VoidCallback onMusic;

  const _TopBar({
    required this.musicName,
    required this.onClose,
    required this.onMusic,
  });

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 6),
      child: Row(
        children: [
          IconButton(
            onPressed: onClose,
            icon: const Icon(Icons.close, color: Colors.white, size: 26),
            tooltip: '关闭',
          ),
          const SizedBox(width: 6),
          // 选择音乐胶囊
          GestureDetector(
            onTap: onMusic,
            child: Container(
              padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 7),
              decoration: BoxDecoration(
                color: Colors.black.withValues(alpha: 0.35),
                borderRadius: BorderRadius.circular(18),
                border: Border.all(color: Colors.white24),
              ),
              child: Row(
                mainAxisSize: MainAxisSize.min,
                children: [
                  const Icon(Icons.music_note, color: Colors.white, size: 16),
                  const SizedBox(width: 5),
                  Text(
                    musicName ?? '选择音乐',
                    style: const TextStyle(color: Colors.white, fontSize: 13),
                  ),
                ],
              ),
            ),
          ),
          const Spacer(),
          // 顶部小绿点
          Container(
            width: 8,
            height: 8,
            decoration: const BoxDecoration(
              color: Colors.greenAccent,
              shape: BoxShape.circle,
            ),
          ),
        ],
      ),
    );
  }
}

class _SideToolItem {
  final IconData icon;
  final String label;
  final bool dimmed;
  final bool active;
  final Widget? badge;
  final VoidCallback onTap;

  _SideToolItem({
    required this.icon,
    required this.label,
    required this.onTap,
    this.dimmed = false,
    this.active = false,
    this.badge,
  });
}

class _SideTools extends StatelessWidget {
  final bool flashOn;
  final bool flipped;
  final bool countdown;
  final bool recording;
  final VoidCallback onFlip;
  final VoidCallback onFlash;
  final VoidCallback onCountdown;
  final void Function(String) onToast;

  const _SideTools({
    required this.flashOn,
    required this.flipped,
    required this.countdown,
    required this.recording,
    required this.onFlip,
    required this.onFlash,
    required this.onCountdown,
    required this.onToast,
  });

  @override
  Widget build(BuildContext context) {
    final items = [
      _SideToolItem(
        icon: Icons.flip_camera_android,
        label: '翻转',
        onTap: recording ? () {} : onFlip,
        active: flipped,
      ),
      _SideToolItem(
        icon: flashOn ? Icons.flash_on : Icons.flash_off,
        label: '闪光灯',
        onTap: onFlash,
        active: flashOn,
      ),
      _SideToolItem(
        icon: Icons.settings,
        label: '设置',
        onTap: () => onToast('设置（敬请期待）'),
      ),
      _SideToolItem(
        icon: Icons.lightbulb_outline,
        label: '灵感跟拍',
        onTap: () => onToast('灵感跟拍（敬请期待）'),
        badge: Container(
          padding: const EdgeInsets.symmetric(horizontal: 4, vertical: 1),
          decoration: BoxDecoration(
            color: AppColors.accent,
            borderRadius: BorderRadius.circular(4),
          ),
          child: const Text(
            '新',
            style: TextStyle(color: Colors.white, fontSize: 9),
          ),
        ),
      ),
      _SideToolItem(
        icon: Icons.timer,
        label: '倒计时',
        onTap: onCountdown,
        active: countdown,
        badge: countdown
            ? const Text(
                '3',
                style: TextStyle(color: Colors.white, fontSize: 10),
              )
            : null,
      ),
      _SideToolItem(
        icon: Icons.face_retouching_natural,
        label: '美颜',
        onTap: () => onToast('美颜（敬请期待）'),
      ),
      _SideToolItem(
        icon: Icons.notes,
        label: '提词器',
        onTap: () => onToast('提词器（敬请期待）'),
        dimmed: true,
      ),
      _SideToolItem(
        icon: Icons.keyboard_arrow_down,
        label: '更多',
        onTap: () => onToast('更多（敬请期待）'),
      ),
    ];

    return Padding(
      padding: const EdgeInsets.only(right: 12, top: 8),
      child: Column(
        children: items
            .map(
              (item) => Padding(
                padding: const EdgeInsets.symmetric(vertical: 9),
                child: _SideToolButton(item: item),
              ),
            )
            .toList(),
      ),
    );
  }
}

class _SideToolButton extends StatelessWidget {
  final _SideToolItem item;

  const _SideToolButton({required this.item});

  @override
  Widget build(BuildContext context) {
    final color = item.dimmed
        ? Colors.white38
        : (item.active ? AppColors.accent : Colors.white);
    final child = Stack(
      clipBehavior: Clip.none,
      children: [
        Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            Icon(item.icon, color: color, size: 24),
            const SizedBox(height: 3),
            Text(
              item.label,
              style: TextStyle(color: color, fontSize: 11),
            ),
          ],
        ),
        if (item.badge != null)
          Positioned(
            top: -6,
            right: -10,
            child: Container(
              padding: const EdgeInsets.all(2),
              decoration: BoxDecoration(
                color: Colors.black54,
                borderRadius: BorderRadius.circular(8),
              ),
              child: item.badge,
            ),
          ),
      ],
    );

    if (item.label == '更多') {
      return Container(
        width: 34,
        height: 34,
        decoration: BoxDecoration(
          color: Colors.black.withValues(alpha: 0.4),
          shape: BoxShape.circle,
        ),
        child: IconButton(
          onPressed: item.onTap,
          icon: Icon(item.icon, color: Colors.white, size: 20),
          padding: EdgeInsets.zero,
        ),
      );
    }
    return GestureDetector(onTap: item.onTap, child: child);
  }
}

class _ModeTabs extends StatelessWidget {
  final int mode;
  final ValueChanged<int> onChanged;

  const _ModeTabs({required this.mode, required this.onChanged});

  @override
  Widget build(BuildContext context) {
    return Row(
      mainAxisAlignment: MainAxisAlignment.center,
      children: List.generate(_CameraPageState._modes.length, (i) {
        final active = i == mode;
        return GestureDetector(
          onTap: () => onChanged(i),
          child: Container(
            margin: const EdgeInsets.symmetric(horizontal: 8),
            padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 5),
            decoration: BoxDecoration(
              color: active ? Colors.white : Colors.transparent,
              borderRadius: BorderRadius.circular(16),
            ),
            child: Text(
              _CameraPageState._modes[i],
              style: TextStyle(
                color: active ? Colors.black : Colors.white70,
                fontSize: 14,
                fontWeight: active ? FontWeight.w600 : FontWeight.w400,
              ),
            ),
          ),
        );
      }),
    );
  }
}

class _CaptureBar extends StatelessWidget {
  final int mode;
  final bool pressing;
  final bool recording;
  final VoidCallback onPhotoTap;
  final VoidCallback onVideoDown;
  final VoidCallback onVideoUp;
  final VoidCallback onAlbum;
  final VoidCallback onEffect;

  const _CaptureBar({
    required this.mode,
    required this.pressing,
    required this.recording,
    required this.onPhotoTap,
    required this.onVideoDown,
    required this.onVideoUp,
    required this.onAlbum,
    required this.onEffect,
  });

  bool get _isPhoto => mode == 1;

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: const EdgeInsets.symmetric(horizontal: 22),
      child: Row(
        mainAxisAlignment: MainAxisAlignment.spaceBetween,
        crossAxisAlignment: CrossAxisAlignment.center,
        children: [
          // 特效
          _RoundTile(
            icon: Icons.auto_awesome,
            label: '特效',
            onTap: onEffect,
          ),
          // 录制/拍照按钮
          GestureDetector(
            onTap: _isPhoto ? onPhotoTap : null,
            onTapDown: _isPhoto ? null : (_) => onVideoDown(),
            onTapUp: _isPhoto ? null : (_) => onVideoUp(),
            onTapCancel: _isPhoto ? null : onVideoUp,
            child: AnimatedScale(
              scale: pressing ? 0.88 : 1.0,
              duration: const Duration(milliseconds: 120),
              child: Container(
                width: 76,
                height: 76,
                decoration: BoxDecoration(
                  shape: BoxShape.circle,
                  border: Border.all(color: Colors.white, width: 5),
                ),
                child: Center(
                  child: AnimatedContainer(
                    duration: const Duration(milliseconds: 120),
                    width: recording ? 30 : 60,
                    height: recording ? 30 : 60,
                    decoration: BoxDecoration(
                      color: AppColors.accent,
                      shape: recording ? BoxShape.rectangle : BoxShape.circle,
                      borderRadius:
                          recording ? BorderRadius.circular(8) : null,
                    ),
                  ),
                ),
              ),
            ),
          ),
          // 相册
          _RoundTile(
            icon: Icons.photo_library,
            label: '相册',
            onTap: onAlbum,
            filled: true,
          ),
        ],
      ),
    );
  }
}

class _RoundTile extends StatelessWidget {
  final IconData icon;
  final String label;
  final VoidCallback onTap;
  final bool filled;

  const _RoundTile({
    required this.icon,
    required this.label,
    required this.onTap,
    this.filled = false,
  });

  @override
  Widget build(BuildContext context) {
    return GestureDetector(
      onTap: onTap,
      child: SizedBox(
        width: 64,
        child: Column(
          children: [
            Container(
              width: 50,
              height: 50,
              decoration: BoxDecoration(
                color: filled
                    ? Colors.black.withValues(alpha: 0.45)
                    : Colors.transparent,
                borderRadius: BorderRadius.circular(12),
                border: filled ? null : Border.all(color: Colors.white24),
              ),
              child: Icon(icon, color: Colors.white, size: 26),
            ),
            const SizedBox(height: 4),
            Text(label, style: const TextStyle(color: Colors.white, fontSize: 11)),
          ],
        ),
      ),
    );
  }
}

class _CameraNavBar extends StatelessWidget {
  final void Function(String) onTap;

  const _CameraNavBar({required this.onTap});

  @override
  Widget build(BuildContext context) {
    final items = [
      _NavBarItem(icon: Icons.title, label: '文字', redDot: true),
      _NavBarItem(icon: Icons.auto_awesome, label: 'AI创作'),
      _NavBarItem(icon: Icons.videocam, label: '相机', active: true),
      _NavBarItem(icon: Icons.lightbulb_outline, label: '创作灵感'),
      _NavBarItem(icon: Icons.rss_feed, label: '开直播'),
    ];
    return Container(
      decoration: const BoxDecoration(
        color: Colors.black,
        border: Border(top: BorderSide(color: Colors.white10)),
      ),
      child: SafeArea(
        top: false,
        child: SizedBox(
          height: 56,
          child: Row(
            mainAxisAlignment: MainAxisAlignment.spaceAround,
            children: items
                .map(
                  (item) => _NavBarButton(item: item, onTap: onTap),
                )
                .toList(),
          ),
        ),
      ),
    );
  }
}

class _NavBarItem {
  final IconData icon;
  final String label;
  final bool active;
  final bool redDot;

  _NavBarItem({
    required this.icon,
    required this.label,
    this.active = false,
    this.redDot = false,
  });
}

class _NavBarButton extends StatelessWidget {
  final _NavBarItem item;
  final void Function(String) onTap;

  const _NavBarButton({required this.item, required this.onTap});

  @override
  Widget build(BuildContext context) {
    final color = item.active ? Colors.white : AppColors.muted;
    return GestureDetector(
      onTap: () => item.active ? null : onTap('${item.label}（敬请期待）'),
      behavior: HitTestBehavior.opaque,
      child: SizedBox(
        width: 60,
        child: Column(
          mainAxisAlignment: MainAxisAlignment.center,
          children: [
            Stack(
              clipBehavior: Clip.none,
              children: [
                Icon(item.icon, color: color, size: 24),
                if (item.redDot)
                  Positioned(
                    top: -2,
                    right: -4,
                    child: Container(
                      width: 7,
                      height: 7,
                      decoration: const BoxDecoration(
                        color: AppColors.accent,
                        shape: BoxShape.circle,
                      ),
                    ),
                  ),
              ],
            ),
            const SizedBox(height: 3),
            Text(
              item.label,
              style: TextStyle(
                color: color,
                fontSize: 10,
                fontWeight: item.active ? FontWeight.w700 : FontWeight.w400,
              ),
            ),
          ],
        ),
      ),
    );
  }
}

class _MusicSheet extends StatefulWidget {
  final String? musicName;
  final ValueChanged<String> onPick;

  const _MusicSheet({required this.musicName, required this.onPick});

  @override
  State<_MusicSheet> createState() => _MusicSheetState();
}

class _MusicSheetState extends State<_MusicSheet> {
  final AudioPlayer _player = AudioPlayer();
  String? _playingId;
  String _query = '';

  @override
  void dispose() {
    _player.dispose();
    super.dispose();
  }

  Future<void> _toggle(TrendingSong song) async {
    if (_playingId == song.id) {
      await _player.stop();
      if (mounted) setState(() => _playingId = null);
      return;
    }
    try {
      await _player.stop();
      await _player.play(AssetSource(song.audioAsset));
      if (mounted) setState(() => _playingId = song.id);
    } catch (_) {
      // 占位音频缺失时静默失败
    }
  }

  List<TrendingSong> get _filtered {
    final q = _query.trim();
    if (q.isEmpty) return trendingSongs;
    return trendingSongs
        .where((s) => s.title.contains(q) || s.artist.contains(q))
        .toList();
  }

  @override
  Widget build(BuildContext context) {
    final list = _filtered;
    return Padding(
      padding: const EdgeInsets.fromLTRB(16, 10, 16, 24),
      child: Column(
        mainAxisSize: MainAxisSize.min,
        children: [
          Container(
            width: 38,
            height: 4,
            decoration: BoxDecoration(
              color: Colors.white24,
              borderRadius: BorderRadius.circular(2),
            ),
          ),
          const SizedBox(height: 12),
          const Text(
            '选择音乐',
            style: TextStyle(
              color: Colors.white,
              fontSize: 16,
              fontWeight: FontWeight.w600,
            ),
          ),
          const SizedBox(height: 12),
          TextField(
            onChanged: (v) => setState(() => _query = v),
            decoration: InputDecoration(
              hintText: '搜索音乐',
              prefixIcon: const Icon(Icons.search, size: 20),
              filled: true,
              fillColor: AppColors.surface,
              contentPadding: const EdgeInsets.symmetric(
                horizontal: 14,
                vertical: 10,
              ),
              border: OutlineInputBorder(
                borderRadius: BorderRadius.circular(12),
                borderSide: BorderSide.none,
              ),
            ),
          ),
          const SizedBox(height: 8),
          ConstrainedBox(
            constraints: const BoxConstraints(maxHeight: 380),
            child: ListView.separated(
              shrinkWrap: true,
              itemCount: list.length,
              separatorBuilder: (_, _) =>
                  const Divider(height: 1, color: Colors.white12),
              itemBuilder: (ctx, i) {
                final s = list[i];
                final selected = widget.musicName == '${s.title} - ${s.artist}';
                final playing = _playingId == s.id;
                return ListTile(
                  leading: Container(
                    width: 44,
                    height: 44,
                    decoration: BoxDecoration(
                      color: AppColors.surface,
                      borderRadius: BorderRadius.circular(8),
                    ),
                    child: Center(
                      child: Text(s.emoji, style: const TextStyle(fontSize: 22)),
                    ),
                  ),
                  title: Text(
                    s.title,
                    style: TextStyle(
                      color: Colors.white,
                      fontWeight:
                          selected ? FontWeight.w700 : FontWeight.w400,
                    ),
                  ),
                  subtitle: Text(
                    '${s.artist} · ${s.style} · ${s.duration}',
                    style: const TextStyle(color: Colors.white60, fontSize: 12),
                  ),
                  trailing: IconButton(
                    icon: Icon(
                      playing
                          ? Icons.pause_circle_filled
                          : Icons.play_circle_outline,
                      color: playing ? AppColors.accent : Colors.white70,
                    ),
                    onPressed: () => _toggle(s),
                  ),
                  onTap: () {
                    widget.onPick('${s.title} - ${s.artist}');
                    Navigator.of(context).pop();
                  },
                );
              },
            ),
          ),
        ],
      ),
    );
  }
}
