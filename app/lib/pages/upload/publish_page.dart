import 'dart:typed_data';

import 'package:flutter/material.dart';
import 'package:image_picker/image_picker.dart';
import 'package:provider/provider.dart';
import 'package:video_player/video_player.dart';

import '../../core/api_client.dart';
import '../../core/app_state.dart';
import '../../core/theme.dart';
import 'preview_controller.dart';
import 'video_frame_capture.dart';

class PublishPage extends StatefulWidget {
  final List<XFile> videos;
  final XFile? image;

  const PublishPage({super.key, this.videos = const [], this.image});

  @override
  State<PublishPage> createState() => _PublishPageState();
}

class _PublishPageState extends State<PublishPage> {
  final _title = TextEditingController();
  final _description = TextEditingController();
  final _location = TextEditingController();

  VideoPlayerController? _controller;
  Uint8List? _coverBytes;
  bool _loadingCover = true;
  bool _uploading = false;
  double _progress = 0;

  static const _topics = ['# 航空母舰', '# 海军舰艇开放日', '# 海军舰艇开放'];
  static const _locations = ['南京南站', '南京雨花台风景区', '1701 LIVE HOUSE'];

  @override
  void initState() {
    super.initState();
    _generateCover();
  }

  @override
  void dispose() {
    _title.dispose();
    _description.dispose();
    _location.dispose();
    _controller?.dispose();
    super.dispose();
  }

  Future<void> _generateCover() async {
    // 图片帖：图片本身即封面
    if (widget.image != null) {
      try {
        _coverBytes = await widget.image!.readAsBytes();
      } catch (_) {}
      if (mounted) setState(() => _loadingCover = false);
      return;
    }
    final video = widget.videos.firstOrNull;
    if (video == null) {
      if (mounted) setState(() => _loadingCover = false);
      return;
    }
    try {
      final controller = createPreviewController(video);
      await controller.initialize();
      await controller.setLooping(true);
      if (!mounted) {
        await controller.dispose();
        return;
      }
      _controller = controller;
      final bytes = await captureVideoFrame(video, Duration.zero);
      if (mounted) {
        setState(() {
          _coverBytes = bytes;
          _loadingCover = false;
        });
      }
    } catch (_) {
      if (mounted) setState(() => _loadingCover = false);
    }
  }

  void _toast(String msg) {
    ScaffoldMessenger.of(context).showSnackBar(
      SnackBar(content: Text(msg), duration: const Duration(seconds: 1)),
    );
  }

  Future<void> _publish() async {
    if (_uploading) return;
    if (widget.image == null && widget.videos.isEmpty) {
      _toast('请先选择要发布的内容');
      return;
    }
    setState(() {
      _uploading = true;
      _progress = 0;
    });
    final api = context.read<AppState>().api;
    try {
      if (widget.image != null) {
        // 图片帖：封面即图片本身
        final coverFile = _coverBytes == null
            ? null
            : XFile.fromData(
                _coverBytes!,
                name: 'cover.jpg',
                mimeType: 'image/jpeg',
              );
        await api.uploadImagePost(
          image: widget.image!,
          cover: coverFile,
          title: _title.text.trim().isEmpty ? '未命名作品' : _title.text.trim(),
          description: _description.text.trim(),
          musicName: '',
          onProgress: (p) {
            if (mounted) setState(() => _progress = p);
          },
        );
      } else {
        var completed = 0;
        for (final video in widget.videos) {
          await api.uploadVideo(
            video: video,
            cover: _coverBytes == null
                ? null
                : XFile.fromData(
                    _coverBytes!,
                    name: 'cover.jpg',
                    mimeType: 'image/jpeg',
                  ),
            title: _title.text.trim().isEmpty ? '未命名作品' : _title.text.trim(),
            description: _description.text.trim(),
            musicName: '',
            duration: _controller?.value.duration.inMilliseconds != null
                ? _controller!.value.duration.inSeconds.toDouble()
                : null,
            onProgress: (p) {
              if (!mounted) return;
              setState(
                () => _progress = (completed + p) / widget.videos.length,
              );
            },
          );
          completed += 1;
          if (mounted) {
            setState(() => _progress = completed / widget.videos.length);
          }
        }
      }
      if (!mounted) return;
      context.read<AppState>().bumpData();
      _toast(widget.image != null
          ? '图片发布成功'
          : '发布成功，共发布 ${widget.videos.length} 个作品');
      Navigator.of(context).pop(true);
    } on ApiException catch (e) {
      if (mounted) _toast('发布失败：${e.message}');
    } catch (e) {
      if (mounted) _toast('发布失败：$e');
    } finally {
      if (mounted) setState(() => _uploading = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      backgroundColor: Colors.white,
      appBar: AppBar(
        backgroundColor: Colors.white,
        foregroundColor: Colors.black,
        elevation: 0,
        leading: IconButton(
          icon: const Icon(Icons.arrow_back_ios_new, size: 20),
          onPressed: () => Navigator.of(context).pop(),
        ),
        actions: [
          TextButton(
            onPressed: () => _toast('预览（敬请期待）'),
            child: const Text('预览', style: TextStyle(color: Colors.black)),
          ),
        ],
      ),
      body: ListView(
        padding: const EdgeInsets.fromLTRB(16, 4, 16, 12),
        children: [
          _CoverPreview(coverBytes: _coverBytes, loading: _loadingCover),
          const SizedBox(height: 14),
          TextField(
            controller: _title,
            maxLength: 100,
            decoration: const InputDecoration(
              hintText: '添加标题',
              border: InputBorder.none,
              counterText: '',
              contentPadding: EdgeInsets.zero,
            ),
            style: const TextStyle(fontSize: 16, fontWeight: FontWeight.w600),
          ),
          const SizedBox(height: 8),
          TextField(
            controller: _description,
            maxLines: 4,
            maxLength: 500,
            decoration: const InputDecoration(
              hintText: '添加作品描述...',
              border: InputBorder.none,
              counterText: '',
              contentPadding: EdgeInsets.zero,
            ),
            style: const TextStyle(fontSize: 14),
          ),
          const SizedBox(height: 10),
          _ChipRow(onTopic: () => _toast('话题（敬请期待）'), onFriend: () => _toast('@朋友（敬请期待）')),
          const SizedBox(height: 10),
          _TopicChips(
            topics: _topics,
            onTap: (t) {
              final cur = _description.text;
              _description.text = cur.isEmpty ? t : '$cur $t';
            },
          ),
          const SizedBox(height: 14),
          _LocationRow(
            controller: _location,
            locations: _locations,
            onSelected: (loc) {
              _location.text = loc;
              setState(() {});
            },
          ),
          const Divider(height: 18),
          _SimpleRow(
            icon: Icons.grid_view_outlined,
            label: '添加标签',
            onTap: () => _toast('添加标签（敬请期待）'),
          ),
          const SizedBox(height: 8),
          _SimpleRow(
            icon: Icons.verified_user_outlined,
            label: '添加自主声明',
            onTap: () => _toast('自主声明（敬请期待）'),
          ),
          const SizedBox(height: 8),
          _SimpleRow(
            icon: Icons.lock_outline,
            label: '公开 · 所有人可见',
            onTap: () => _toast('可见范围（敬请期待）'),
          ),
          const SizedBox(height: 8),
          _SimpleRow(
            icon: Icons.local_fire_department_outlined,
            label: 'DOU+ 上热门',
            onTap: () => _toast('DOU+（敬请期待）'),
          ),
          const SizedBox(height: 8),
          _SimpleRow(
            icon: Icons.settings_outlined,
            label: '高级设置',
            trailing: const Text('付费可看功能已开放', style: TextStyle(color: Colors.grey, fontSize: 12)),
            onTap: () => _toast('高级设置（敬请期待）'),
          ),
          const SizedBox(height: 14),
          const Text(
            '发布成功后将保存内容至本地',
            style: TextStyle(color: Colors.grey, fontSize: 12),
          ),
        ],
      ),
      bottomNavigationBar: _PublishBar(
        uploading: _uploading,
        progress: _progress,
        onShare: () => _toast('分享（敬请期待）'),
        onPublish: _publish,
      ),
    );
  }
}

class _CoverPreview extends StatelessWidget {
  final Uint8List? coverBytes;
  final bool loading;

  const _CoverPreview({this.coverBytes, required this.loading});

  @override
  Widget build(BuildContext context) {
    return Column(
      children: [
        Container(
          height: 220,
          width: double.infinity,
          decoration: BoxDecoration(
            color: Colors.black,
            borderRadius: BorderRadius.circular(12),
          ),
          child: Stack(
            alignment: Alignment.center,
            children: [
              if (coverBytes != null)
                ClipRRect(
                  borderRadius: BorderRadius.circular(12),
                  child: Image.memory(coverBytes!, fit: BoxFit.cover),
                )
              else if (loading)
                const CircularProgressIndicator(color: Colors.white)
              else
                const Icon(Icons.video_library_outlined,
                    color: Colors.white38, size: 40),
              Positioned(
                bottom: 12,
                child: Container(
                  padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 6),
                  decoration: BoxDecoration(
                    color: Colors.black54,
                    borderRadius: BorderRadius.circular(16),
                  ),
                  child: const Row(
                    children: [
                      Text(
                        'AI',
                        style: TextStyle(
                          color: Color(0xFF3B82F6),
                          fontWeight: FontWeight.w700,
                        ),
                      ),
                      SizedBox(width: 4),
                      Text('编辑封面', style: TextStyle(color: Colors.white)),
                    ],
                  ),
                ),
              ),
            ],
          ),
        ),
        const SizedBox(height: 8),
        Row(
          children: [
            _Thumb(coverBytes: coverBytes, selected: true),
            const SizedBox(width: 8),
            _Thumb(coverBytes: null, selected: false),
            const SizedBox(width: 8),
            Container(
              width: 56,
              height: 72,
              decoration: BoxDecoration(
                color: Colors.grey.shade200,
                borderRadius: BorderRadius.circular(8),
              ),
              child: const Icon(Icons.add, color: Colors.grey),
            ),
          ],
        ),
      ],
    );
  }
}

class _Thumb extends StatelessWidget {
  final Uint8List? coverBytes;
  final bool selected;

  const _Thumb({this.coverBytes, required this.selected});

  @override
  Widget build(BuildContext context) {
    return Container(
      width: 56,
      height: 72,
      decoration: BoxDecoration(
        borderRadius: BorderRadius.circular(8),
        border: Border.all(
          color: selected ? AppColors.accent : Colors.transparent,
          width: 2,
        ),
      ),
      child: ClipRRect(
        borderRadius: BorderRadius.circular(6),
        child: coverBytes != null
            ? Image.memory(coverBytes!, fit: BoxFit.cover)
            : Container(color: Colors.grey.shade300),
      ),
    );
  }
}

class _ChipRow extends StatelessWidget {
  final VoidCallback onTopic;
  final VoidCallback onFriend;

  const _ChipRow({required this.onTopic, required this.onFriend});

  @override
  Widget build(BuildContext context) {
    return Row(
      children: [
        _Chip(icon: Icons.tag, label: '话题', onTap: onTopic),
        const SizedBox(width: 10),
        _Chip(icon: Icons.alternate_email, label: '朋友', onTap: onFriend),
        const Spacer(),
        IconButton(
          onPressed: () {},
          icon: const Icon(Icons.open_in_full, color: Colors.grey, size: 20),
          padding: EdgeInsets.zero,
        ),
      ],
    );
  }
}

class _Chip extends StatelessWidget {
  final IconData icon;
  final String label;
  final VoidCallback onTap;

  const _Chip({
    required this.icon,
    required this.label,
    required this.onTap,
  });

  @override
  Widget build(BuildContext context) {
    return GestureDetector(
      onTap: onTap,
      child: Container(
        padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 6),
        decoration: BoxDecoration(
          color: Colors.grey.shade100,
          borderRadius: BorderRadius.circular(16),
        ),
        child: Row(
          children: [
            Icon(icon, size: 15, color: Colors.black54),
            const SizedBox(width: 4),
            Text(label, style: const TextStyle(fontSize: 13)),
          ],
        ),
      ),
    );
  }
}

class _TopicChips extends StatelessWidget {
  final List<String> topics;
  final ValueChanged<String> onTap;

  const _TopicChips({required this.topics, required this.onTap});

  @override
  Widget build(BuildContext context) {
    return SizedBox(
      height: 32,
      child: ListView.separated(
        scrollDirection: Axis.horizontal,
        itemCount: topics.length,
        separatorBuilder: (_, _) => const SizedBox(width: 10),
        itemBuilder: (context, i) => GestureDetector(
          onTap: () => onTap(topics[i]),
          child: Container(
            padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 6),
            decoration: BoxDecoration(
              color: const Color(0xFFF0F1FF),
              borderRadius: BorderRadius.circular(16),
            ),
            child: Text(
              topics[i],
              style: const TextStyle(color: Color(0xFF3B4FE0), fontSize: 13),
            ),
          ),
        ),
      ),
    );
  }
}

class _LocationRow extends StatelessWidget {
  final TextEditingController controller;
  final List<String> locations;
  final ValueChanged<String> onSelected;

  const _LocationRow({
    required this.controller,
    required this.locations,
    required this.onSelected,
  });

  @override
  Widget build(BuildContext context) {
    return Column(
      children: [
        GestureDetector(
          onTap: () => _LocationSheet.show(context, onSelected, locations),
          child: Row(
            children: [
              const Icon(Icons.location_on_outlined, size: 18, color: Colors.black54),
              const SizedBox(width: 8),
              const Text('选择地点', style: TextStyle(fontSize: 14)),
              const Spacer(),
              const Icon(Icons.chevron_right, color: Colors.grey),
            ],
          ),
        ),
        if (controller.text.isNotEmpty)
          Padding(
            padding: const EdgeInsets.only(top: 8),
            child: Align(
              alignment: Alignment.centerLeft,
              child: Container(
                padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 4),
                decoration: BoxDecoration(
                  color: Colors.grey.shade100,
                  borderRadius: BorderRadius.circular(12),
                ),
                child: Text(controller.text, style: const TextStyle(fontSize: 12)),
              ),
            ),
          ),
      ],
    );
  }
}

class _LocationSheet {
  static void show(
    BuildContext context,
    ValueChanged<String> onSelected,
    List<String> locations,
  ) {
    showModalBottomSheet(
      context: context,
      backgroundColor: Colors.white,
      shape: const RoundedRectangleBorder(
        borderRadius: BorderRadius.vertical(top: Radius.circular(18)),
      ),
      builder: (_) => Padding(
        padding: const EdgeInsets.all(16),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            const Text('选择地点',
                style: TextStyle(fontWeight: FontWeight.w600, fontSize: 16)),
            const SizedBox(height: 12),
            ...locations.map(
              (loc) => ListTile(
                leading: const Icon(Icons.location_on_outlined, color: Colors.grey),
                title: Text(loc),
                onTap: () {
                  onSelected(loc);
                  Navigator.of(context).pop();
                },
              ),
            ),
          ],
        ),
      ),
    );
  }
}

class _SimpleRow extends StatelessWidget {
  final IconData icon;
  final String label;
  final Widget? trailing;
  final VoidCallback onTap;

  const _SimpleRow({
    required this.icon,
    required this.label,
    required this.onTap,
    this.trailing,
  });

  @override
  Widget build(BuildContext context) {
    return GestureDetector(
      onTap: onTap,
      child: Container(
        padding: const EdgeInsets.symmetric(vertical: 10),
        child: Row(
          children: [
            Icon(icon, size: 18, color: Colors.black54),
            const SizedBox(width: 10),
            Text(label, style: const TextStyle(fontSize: 14)),
            const Spacer(),
            trailing ?? const Icon(Icons.chevron_right, color: Colors.grey),
          ],
        ),
      ),
    );
  }
}

class _PublishBar extends StatelessWidget {
  final bool uploading;
  final double progress;
  final VoidCallback onShare;
  final VoidCallback onPublish;

  const _PublishBar({
    required this.uploading,
    required this.progress,
    required this.onShare,
    required this.onPublish,
  });

  @override
  Widget build(BuildContext context) {
    return SafeArea(
      child: Container(
        padding: const EdgeInsets.fromLTRB(16, 8, 16, 12),
        decoration: const BoxDecoration(
          color: Colors.white,
          border: Border(top: BorderSide(color: Color(0xFFEEEEEE))),
        ),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            if (uploading) ...[
              LinearProgressIndicator(
                value: progress,
                backgroundColor: Colors.grey.shade200,
                valueColor: const AlwaysStoppedAnimation(AppColors.accent),
                minHeight: 3,
              ),
              const SizedBox(height: 8),
            ],
            Row(
              children: [
                GestureDetector(
                  onTap: uploading ? null : onShare,
                  child: const Column(
                    children: [
                      Icon(Icons.share_outlined, size: 20),
                      SizedBox(height: 2),
                      Text('分享', style: TextStyle(fontSize: 12)),
                    ],
                  ),
                ),
                const SizedBox(width: 12),
                Expanded(
                  child: Container(
                    padding: const EdgeInsets.symmetric(vertical: 10),
                    decoration: BoxDecoration(
                      color: Colors.grey.shade100,
                      borderRadius: BorderRadius.circular(22),
                    ),
                    child: const Row(
                      mainAxisAlignment: MainAxisAlignment.center,
                      children: [
                        CircleAvatar(radius: 9, backgroundColor: Colors.orange),
                        SizedBox(width: 6),
                        Text('限时日常', style: TextStyle(fontSize: 14)),
                      ],
                    ),
                  ),
                ),
                const SizedBox(width: 12),
                Expanded(
                  child: FilledButton(
                    onPressed: uploading ? null : onPublish,
                    style: FilledButton.styleFrom(
                      backgroundColor: AppColors.accent,
                      padding: const EdgeInsets.symmetric(vertical: 12),
                      shape: RoundedRectangleBorder(
                        borderRadius: BorderRadius.circular(22),
                      ),
                    ),
                    child: uploading
                        ? Text(
                            '${(progress * 100).toInt()}%',
                            style: const TextStyle(
                              color: Colors.white,
                              fontSize: 15,
                              fontWeight: FontWeight.w600,
                            ),
                          )
                        : const Row(
                            mainAxisAlignment: MainAxisAlignment.center,
                            children: [
                              Icon(Icons.upload, size: 18, color: Colors.white),
                              SizedBox(width: 6),
                              Text(
                                '发作品',
                                style: TextStyle(
                                  color: Colors.white,
                                  fontSize: 15,
                                  fontWeight: FontWeight.w600,
                                ),
                              ),
                            ],
                          ),
                  ),
                ),
              ],
            ),
          ],
        ),
      ),
    );
  }
}
