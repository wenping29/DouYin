import 'dart:io';

import 'package:flutter/material.dart';
import 'package:image_picker/image_picker.dart';
import 'package:video_player/video_player.dart';

import '../../core/theme.dart';
import 'publish_page.dart';

class EditPage extends StatefulWidget {
  final List<XFile> videos;

  const EditPage({super.key, required this.videos});

  @override
  State<EditPage> createState() => _EditPageState();
}

class _EditPageState extends State<EditPage> {
  VideoPlayerController? _controller;

  static const _tools = [
    ('文字', Icons.text_fields),
    ('话题', Icons.tag),
    ('一键成片', Icons.movie_edit),
    ('贴纸', Icons.emoji_emotions_outlined),
    ('拼图', Icons.grid_4x4),
    ('标记', Icons.label_outline),
  ];

  @override
  void initState() {
    super.initState();
    _initVideo();
  }

  Future<void> _initVideo() async {
    final video = widget.videos.firstOrNull;
    if (video == null) return;
    final controller = VideoPlayerController.file(File(video.path));
    try {
      await controller.initialize();
      await controller.setLooping(true);
      if (!mounted) {
        await controller.dispose();
        return;
      }
      await controller.play();
      setState(() => _controller = controller);
    } catch (_) {
      await controller.dispose();
    }
  }

  @override
  void dispose() {
    _controller?.dispose();
    super.dispose();
  }

  void _toast(String msg) {
    ScaffoldMessenger.of(context).showSnackBar(
      SnackBar(content: Text(msg), duration: const Duration(seconds: 1)),
    );
  }

  void _goPublish() {
    Navigator.of(context).push(
      MaterialPageRoute(builder: (_) => PublishPage(videos: widget.videos)),
    );
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      backgroundColor: Colors.black,
      body: SafeArea(
        child: Column(
          children: [
            // 顶部
            Padding(
              padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 8),
              child: Row(
                children: [
                  IconButton(
                    onPressed: () => Navigator.of(context).pop(),
                    icon: const Icon(Icons.arrow_back_ios_new,
                        color: Colors.white, size: 20),
                  ),
                  GestureDetector(
                    onTap: () => _toast('更换配乐（敬请期待）'),
                    child: Container(
                      padding: const EdgeInsets.symmetric(
                        horizontal: 10,
                        vertical: 6,
                      ),
                      decoration: BoxDecoration(
                        color: Colors.white12,
                        borderRadius: BorderRadius.circular(16),
                      ),
                      child: const Row(
                        children: [
                          Icon(Icons.music_note, color: Colors.white, size: 14),
                          SizedBox(width: 4),
                          Text('Ai',
                              style:
                                  TextStyle(color: Colors.white, fontSize: 12)),
                          SizedBox(width: 6),
                          Text('曲：胜利 - 瑞鹅',
                              style:
                                  TextStyle(color: Colors.white, fontSize: 12)),
                          SizedBox(width: 6),
                          Icon(Icons.close, color: Colors.white54, size: 14),
                        ],
                      ),
                    ),
                  ),
                  const Spacer(),
                  IconButton(
                    onPressed: () => _toast('设置（敬请期待）'),
                    icon: const Icon(Icons.settings, color: Colors.white),
                  ),
                ],
              ),
            ),
            // 左：动图胶囊
            Align(
              alignment: Alignment.centerLeft,
              child: Padding(
                padding: const EdgeInsets.only(left: 12),
                child: Container(
                  padding:
                      const EdgeInsets.symmetric(horizontal: 10, vertical: 5),
                  decoration: BoxDecoration(
                    color: Colors.white12,
                    borderRadius: BorderRadius.circular(14),
                  ),
                  child: const Row(
                    mainAxisSize: MainAxisSize.min,
                    children: [
                      Icon(Icons.auto_awesome, color: Colors.white, size: 14),
                      SizedBox(width: 4),
                      Text('动图', style: TextStyle(color: Colors.white, fontSize: 12)),
                      Icon(Icons.arrow_drop_down, color: Colors.white),
                    ],
                  ),
                ),
              ),
            ),
            const SizedBox(height: 8),
            // 预览区
            Expanded(
              child: Stack(
                children: [
                  Center(
                    child: _controller != null && _controller!.value.isInitialized
                        ? AspectRatio(
                            aspectRatio: _controller!.value.aspectRatio,
                            child: VideoPlayer(_controller!),
                          )
                        : Container(
                            color: Colors.grey.shade900,
                            child: const Center(
                              child: CircularProgressIndicator(color: Colors.white),
                            ),
                          ),
                  ),
                  // 右侧工具条
                  Positioned(
                    right: 8,
                    top: 0,
                    bottom: 0,
                    child: Column(
                      mainAxisAlignment: MainAxisAlignment.center,
                      children: <Widget>[
                        ..._tools.map(
                          (t) => Padding(
                            padding: const EdgeInsets.symmetric(vertical: 10),
                            child: GestureDetector(
                              onTap: () => _toast('${t.$1}（敬请期待）'),
                              child: Column(
                                children: [
                                  Icon(t.$2, color: Colors.white, size: 24),
                                  const SizedBox(height: 2),
                                  Text(t.$1,
                                      style: const TextStyle(
                                          color: Colors.white, fontSize: 11)),
                                ],
                              ),
                            ),
                          ),
                        ),
                        GestureDetector(
                          onTap: () => _toast('更多（敬请期待）'),
                          child: Container(
                            width: 32,
                            height: 32,
                            decoration: const BoxDecoration(
                              color: Colors.white12,
                              shape: BoxShape.circle,
                            ),
                            child: const Icon(Icons.keyboard_arrow_down,
                                color: Colors.white, size: 20),
                          ),
                        ),
                      ],
                    ),
                  ),
                  // 中部一键成片胶囊
                  Positioned(
                    left: 0,
                    right: 60,
                    bottom: 12,
                    child: Center(
                      child: GestureDetector(
                        onTap: () => _toast('一键成片（敬请期待）'),
                        child: Container(
                          padding: const EdgeInsets.symmetric(
                            horizontal: 16,
                            vertical: 8,
                          ),
                          decoration: BoxDecoration(
                            color: Colors.white,
                            borderRadius: BorderRadius.circular(20),
                          ),
                          child: const Row(
                            mainAxisSize: MainAxisSize.min,
                            children: [
                              Icon(Icons.auto_awesome, size: 16),
                              SizedBox(width: 6),
                              Text('一键成片',
                                  style: TextStyle(fontWeight: FontWeight.w600)),
                            ],
                          ),
                        ),
                      ),
                    ),
                  ),
                ],
              ),
            ),
            // 片段缩略图条
            _ClipStrip(count: widget.videos.length),
            const SizedBox(height: 10),
            // 底部
            SafeArea(
              top: false,
              child: Padding(
                padding: const EdgeInsets.fromLTRB(16, 6, 16, 14),
                child: Row(
                  children: [
                    Container(
                      padding: const EdgeInsets.symmetric(
                        horizontal: 14,
                        vertical: 10,
                      ),
                      decoration: BoxDecoration(
                        color: Colors.white12,
                        borderRadius: BorderRadius.circular(22),
                      ),
                      child: const Row(
                        children: [
                          Icon(Icons.timer_outlined,
                              color: Colors.white, size: 16),
                          SizedBox(width: 6),
                          Text('限时日常',
                              style:
                                  TextStyle(color: Colors.white, fontSize: 14)),
                        ],
                      ),
                    ),
                    const Spacer(),
                    FilledButton(
                      onPressed: _goPublish,
                      style: FilledButton.styleFrom(
                        backgroundColor: AppColors.accent,
                        padding: const EdgeInsets.symmetric(
                          horizontal: 28,
                          vertical: 12,
                        ),
                        shape: RoundedRectangleBorder(
                          borderRadius: BorderRadius.circular(22),
                        ),
                      ),
                      child: const Text('下一步',
                          style: TextStyle(
                              color: Colors.white,
                              fontSize: 15,
                              fontWeight: FontWeight.w600)),
                    ),
                  ],
                ),
              ),
            ),
          ],
        ),
      ),
    );
  }
}

class _ClipStrip extends StatelessWidget {
  final int count;

  const _ClipStrip({required this.count});

  @override
  Widget build(BuildContext context) {
    return SizedBox(
      height: 56,
      child: ListView.separated(
        scrollDirection: Axis.horizontal,
        padding: const EdgeInsets.symmetric(horizontal: 12),
        itemCount: count + 2,
        separatorBuilder: (_, _) => const SizedBox(width: 8),
        itemBuilder: (context, i) {
          if (i == 0) {
            return Container(
              width: 48,
              decoration: BoxDecoration(
                color: Colors.grey.shade800,
                borderRadius: BorderRadius.circular(8),
              ),
              child: const Center(
                child: Text('文字',
                    style: TextStyle(color: Colors.white, fontSize: 11)),
              ),
            );
          }
          if (i == 1) {
            return Container(
              width: 48,
              decoration: BoxDecoration(
                border: Border.all(color: Colors.white, width: 2),
                borderRadius: BorderRadius.circular(8),
              ),
              color: Colors.grey.shade700,
            );
          }
          return Container(
            width: 48,
            decoration: BoxDecoration(
              color: Colors.grey.shade700,
              borderRadius: BorderRadius.circular(8),
            ),
            child: const Center(
              child: Icon(Icons.add, color: Colors.white54),
            ),
          );
        },
      ),
    );
  }
}
