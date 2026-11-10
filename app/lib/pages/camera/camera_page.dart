import 'package:flutter/material.dart';

import '../../core/theme.dart';
import '../upload/album_page.dart';

class CameraPage extends StatefulWidget {
  const CameraPage({super.key});

  @override
  State<CameraPage> createState() => _CameraPageState();
}

class _CameraPageState extends State<CameraPage> {
  // 0 分段拍 / 1 照片 / 2 视频
  int _mode = 2;
  bool _flashOn = false;
  bool _flipped = false;
  bool _countdown = false;
  bool _pressing = false;
  String? _musicName;

  static const _modes = ['分段拍', '照片', '视频'];

  void _toast(String msg) {
    ScaffoldMessenger.of(context).showSnackBar(
      SnackBar(content: Text(msg), duration: const Duration(seconds: 1)),
    );
  }

  void _close() => Navigator.of(context).pop();

  void _openAlbum() {
    Navigator.of(
      context,
    ).push(MaterialPageRoute(builder: (_) => const AlbumPage()));
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

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      backgroundColor: Colors.black,
      extendBodyBehindAppBar: true,
      body: Stack(
        children: [
          // 取景背景（演示用深色渐变占位，未接入实时相机）
          Container(
            decoration: const BoxDecoration(
              gradient: LinearGradient(
                begin: Alignment.topCenter,
                end: Alignment.bottomCenter,
                colors: [Color(0xFF2A2724), Color(0xFF0E0D0C)],
              ),
            ),
            child: Center(
              child: Column(
                mainAxisSize: MainAxisSize.min,
                children: [
                  Icon(
                    Icons.videocam_outlined,
                    size: 46,
                    color: Colors.white.withValues(alpha: 0.25),
                  ),
                  const SizedBox(height: 10),
                  Text(
                    '相机预览（演示，未接入实时取景）',
                    style: TextStyle(
                      color: Colors.white.withValues(alpha: 0.3),
                      fontSize: 12,
                    ),
                  ),
                ],
              ),
            ),
          ),
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
                        flipped: _flipped,
                        countdown: _countdown,
                        onFlip: () {
                          setState(() => _flipped = !_flipped);
                          _toast(_flipped ? '已切换至前摄像头' : '已切换至后摄像头');
                        },
                        onFlash: () {
                          setState(() => _flashOn = !_flashOn);
                          _toast(_flashOn ? '闪光灯开启' : '闪光灯关闭');
                        },
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
                  onChanged: (v) => setState(() => _mode = v),
                ),
                const SizedBox(height: 18),
                _CaptureBar(
                  pressing: _pressing,
                  onRecordDown: () => setState(() => _pressing = true),
                  onRecordUp: () {
                    setState(() => _pressing = false);
                    _toast('开始录制（演示，未接入相机）');
                  },
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
  final VoidCallback onFlip;
  final VoidCallback onFlash;
  final VoidCallback onCountdown;
  final void Function(String) onToast;

  const _SideTools({
    required this.flashOn,
    required this.flipped,
    required this.countdown,
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
        onTap: onFlip,
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
  final bool pressing;
  final VoidCallback onRecordDown;
  final VoidCallback onRecordUp;
  final VoidCallback onAlbum;
  final VoidCallback onEffect;

  const _CaptureBar({
    required this.pressing,
    required this.onRecordDown,
    required this.onRecordUp,
    required this.onAlbum,
    required this.onEffect,
  });

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
          // 录制按钮
          GestureDetector(
            onTapDown: (_) => onRecordDown(),
            onTapUp: (_) => onRecordUp(),
            onTapCancel: onRecordUp,
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
                  child: Container(
                    width: 60,
                    height: 60,
                    decoration: const BoxDecoration(
                      color: AppColors.accent,
                      shape: BoxShape.circle,
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

class _MusicSheet extends StatelessWidget {
  final String? musicName;
  final ValueChanged<String> onPick;

  const _MusicSheet({required this.musicName, required this.onPick});

  static const _songs = [
    '胜利 - 瑞鹅',
    '星辰大海 - 黄霄雲',
    '错位时空 - 艾辰',
  ];

  @override
  Widget build(BuildContext context) {
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
          ..._songs.map(
            (song) => ListTile(
              leading: const Icon(Icons.music_note, color: Colors.white70),
              title: Text(song, style: const TextStyle(color: Colors.white)),
              onTap: () => onPick(song),
            ),
          ),
        ],
      ),
    );
  }
}
