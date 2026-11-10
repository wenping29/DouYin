import 'dart:typed_data';
import 'dart:ui' as ui;

import 'package:flutter/material.dart';
import 'package:flutter/rendering.dart';
import 'package:image_picker/image_picker.dart';
import 'package:provider/provider.dart';
import 'package:video_player/video_player.dart';

import '../../core/api_client.dart';
import '../../core/app_state.dart';
import '../../core/theme.dart';
import 'preview_controller.dart';
import 'video_frame_capture.dart';

class UploadPage extends StatefulWidget {
  const UploadPage({super.key});

  @override
  State<UploadPage> createState() => _UploadPageState();
}

class _UploadItem {
  _UploadItem(this.video);

  final String id = UniqueKey().toString();
  final XFile video;
  XFile? customCover;
  Uint8List? customCoverBytes;
  Uint8List? previewFrame;
  double duration = 0;
  bool previewCaptureFailed = false;

  XFile? get cover =>
      customCover ??
      (previewFrame == null
          ? null
          : XFile.fromData(
              previewFrame!,
              name: 'preview-cover.png',
              mimeType: 'image/png',
            ));

  Uint8List? get coverBytes => customCoverBytes ?? previewFrame;
}

class _UploadPageState extends State<UploadPage> {
  final _title = TextEditingController();
  final _description = TextEditingController();
  final _music = TextEditingController();
  final _picker = ImagePicker();
  final _previewKey = GlobalKey();
  final Set<_UploadItem> _published = {};

  final List<_UploadItem> _videos = [];
  _UploadItem? _selected;
  VideoPlayerController? _controller;
  bool _previewLoading = false;
  bool _uploading = false;
  bool _playing = false;
  String? _previewError;
  double _progress = 0;
  int _previewGeneration = 0;

  @override
  void dispose() {
    _previewGeneration++;
    _controller?.dispose();
    _title.dispose();
    _description.dispose();
    _music.dispose();
    super.dispose();
  }

  Future<void> _pickVideos() async {
    try {
      final files = await _picker.pickMultipleMedia();
      if (files.isEmpty) return;
      final selected = files.where(_isVideo).toList();
      if (selected.isEmpty) {
        _showMessage('请选择视频文件');
        return;
      }
      final existing = _videos.map((item) => item.video.path).toSet();
      final additions = selected
          .where((file) => !existing.contains(file.path))
          .map(_UploadItem.new)
          .toList();
      if (additions.isEmpty) {
        _showMessage('所选视频已添加');
        return;
      }
      setState(() => _videos.addAll(additions));
      if (_selected == null) await _selectVideo(additions.first);
    } catch (error) {
      if (mounted) _showMessage('选择视频失败：$error');
    }
  }

  bool _isVideo(XFile file) {
    final mimeType = file.mimeType?.toLowerCase() ?? '';
    final name = file.name.toLowerCase();
    return mimeType.startsWith('video/') ||
        const ['.mp4', '.mov', '.m4v', '.webm', '.mkv'].any(name.endsWith);
  }

  Future<void> _selectVideo(_UploadItem item) async {
    final generation = ++_previewGeneration;
    final previous = _controller;
    _controller = null;
    _selected = item;
    _playing = false;
    _previewLoading = true;
    _previewError = null;
    if (mounted) setState(() {});
    await previous?.dispose();

    final controller = createPreviewController(item.video);
    try {
      await controller.initialize();
      await controller.setLooping(true);
      if (!mounted || generation != _previewGeneration) {
        await controller.dispose();
        return;
      }
      _controller = controller;
      item.duration = controller.value.duration.inMilliseconds / 1000;
      _previewLoading = false;
      setState(() {});
      WidgetsBinding.instance.addPostFrameCallback((_) {
        if (mounted && generation == _previewGeneration) {
          _capturePreviewFrame(item);
        }
      });
    } catch (error) {
      await controller.dispose();
      if (!mounted || generation != _previewGeneration) return;
      _previewLoading = false;
      _previewError = '无法预览该视频：$error';
      setState(() {});
    }
  }

  Future<void> _capturePreviewFrame(_UploadItem item) async {
    try {
      final controller = _controller;
      final browserFrame = await captureVideoFrame(
        item.video,
        controller?.value.position ?? Duration.zero,
      );
      if (browserFrame != null) {
        if (!mounted || !identical(item, _selected)) return;
        setState(() {
          item.previewFrame = browserFrame;
          item.previewCaptureFailed = false;
        });
        return;
      }
      final renderObject = _previewKey.currentContext?.findRenderObject();
      if (renderObject is! RenderRepaintBoundary || renderObject.size.isEmpty) {
        item.previewCaptureFailed = true;
        if (mounted) setState(() {});
        return;
      }
      final image = await renderObject.toImage(pixelRatio: 1);
      try {
        final data = await image.toByteData(format: ui.ImageByteFormat.png);
        if (data == null) throw StateError('无法读取视频预览画面');
        if (!mounted || !identical(item, _selected)) return;
        setState(() {
          item.previewFrame = data.buffer.asUint8List();
          item.previewCaptureFailed = false;
        });
      } finally {
        image.dispose();
      }
    } catch (_) {
      if (!mounted || !identical(item, _selected)) return;
      setState(() => item.previewCaptureFailed = true);
    }
  }

  Future<void> _pickCover() async {
    final item = _selected;
    if (item == null) return;
    try {
      final file = await _picker.pickImage(
        source: ImageSource.gallery,
        maxWidth: 1080,
        imageQuality: 86,
      );
      if (file == null) return;
      final bytes = await file.readAsBytes();
      if (!mounted) return;
      setState(() {
        item.customCover = file;
        item.customCoverBytes = bytes;
      });
    } catch (error) {
      if (mounted) _showMessage('选择封面失败：$error');
    }
  }

  Future<void> _usePreviewAsCover() async {
    final item = _selected;
    if (item == null) return;
    await _capturePreviewFrame(item);
    if (!mounted) return;
    if (item.previewFrame == null) {
      _showMessage('无法截取预览画面，请上传封面图片');
      return;
    }
    setState(() {
      item.customCover = null;
      item.customCoverBytes = null;
    });
  }

  Future<void> _togglePlayback() async {
    final controller = _controller;
    if (controller == null) return;
    if (controller.value.isPlaying) {
      await controller.pause();
    } else {
      await controller.play();
    }
    if (mounted) setState(() => _playing = controller.value.isPlaying);
  }

  Future<void> _publish() async {
    if (_uploading) return;
    final api = context.read<AppState>().api;
    final pending = _videos
        .where((item) => !_published.contains(item))
        .toList();
    if (pending.isEmpty) {
      _showMessage('请先选择要发布的视频');
      return;
    }
    setState(() {
      _uploading = true;
      _progress = 0;
    });

    var completedBytes = 0;
    try {
      final weights = <_UploadItem, int>{};
      for (final item in pending) {
        weights[item] =
            await item.video.length() + (await item.cover?.length() ?? 0);
      }
      final totalBytes = weights.values.fold<int>(0, (sum, size) => sum + size);
      final title = _title.text.trim();
      for (final item in pending) {
        final weight = weights[item]!;
        await api.uploadVideo(
          video: item.video,
          cover: item.cover,
          title: title.isEmpty ? '未命名作品' : title,
          description: _description.text.trim(),
          musicName: _music.text.trim(),
          duration: item.duration,
          onProgress: (progress) {
            if (!mounted) return;
            setState(() {
              _progress = totalBytes == 0
                  ? 0
                  : ((completedBytes + weight * progress) / totalBytes)
                        .clamp(0, 1)
                        .toDouble();
            });
          },
        );
        _published.add(item);
        completedBytes += weight;
        if (mounted) {
          setState(() {
            _progress = totalBytes == 0 ? 1 : completedBytes / totalBytes;
          });
        }
      }
      if (!mounted) return;
      context.read<AppState>().bumpData();
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(content: Text('发布成功，共发布 ${_published.length} 个作品')),
      );
      Navigator.of(context).pop(true);
    } catch (error) {
      if (mounted) {
        if (_published.isNotEmpty) context.read<AppState>().bumpData();
        final detail = error is ApiException ? error.message : '请稍后重试';
        _showMessage(
          _published.isEmpty
              ? '发布失败：$detail'
              : '已发布 ${_published.length} 个作品，其余发布失败：$detail',
        );
      }
    } finally {
      if (mounted) setState(() => _uploading = false);
    }
  }

  void _reorderVideos(int oldIndex, int newIndex) {
    setState(() {
      if (newIndex > oldIndex) newIndex--;
      final item = _videos.removeAt(oldIndex);
      _videos.insert(newIndex, item);
    });
  }

  Future<void> _removeVideo(_UploadItem item) async {
    if (_published.contains(item)) return;
    final wasSelected = identical(item, _selected);
    setState(() => _videos.remove(item));
    if (wasSelected) {
      final previous = _controller;
      _controller = null;
      _selected = null;
      _previewGeneration++;
      await previous?.dispose();
      if (mounted && _videos.isNotEmpty) await _selectVideo(_videos.first);
    }
  }

  void _showMessage(String message) {
    ScaffoldMessenger.of(
      context,
    ).showSnackBar(SnackBar(content: Text(message)));
  }

  Widget _buildVideoPreview() {
    final item = _selected;
    final controller = _controller;
    if (item == null) {
      return AspectRatio(
        aspectRatio: 4 / 3,
        child: Container(
          decoration: BoxDecoration(
            color: AppColors.surface,
            borderRadius: BorderRadius.circular(16),
            border: Border.all(color: Colors.white12),
          ),
          child: Center(
            child: Column(
              mainAxisSize: MainAxisSize.min,
              children: [
                const Icon(Icons.video_library_outlined, size: 40),
                const SizedBox(height: 10),
                const Text('选择一个或多个视频'),
                const SizedBox(height: 14),
                OutlinedButton.icon(
                  onPressed: _pickVideos,
                  icon: const Icon(Icons.add),
                  label: const Text('选择视频'),
                ),
              ],
            ),
          ),
        ),
      );
    }
    return ClipRRect(
      borderRadius: BorderRadius.circular(16),
      child: Container(
        height: 340,
        width: double.infinity,
        color: Colors.black,
        child: Stack(
          alignment: Alignment.center,
          children: [
            if (controller != null && controller.value.isInitialized)
              RepaintBoundary(
                key: _previewKey,
                child: Center(
                  child: AspectRatio(
                    aspectRatio: controller.value.aspectRatio,
                    child: VideoPlayer(controller),
                  ),
                ),
              )
            else
              Center(
                child: _previewLoading
                    ? const CircularProgressIndicator()
                    : Text(
                        _previewError ?? '正在加载视频预览…',
                        style: const TextStyle(color: Colors.white70),
                        textAlign: TextAlign.center,
                      ),
              ),
            if (controller != null && controller.value.isInitialized)
              Positioned(
                left: 0,
                right: 0,
                bottom: 0,
                child: ColoredBox(
                  color: Colors.black45,
                  child: Padding(
                    padding: const EdgeInsets.fromLTRB(12, 8, 12, 10),
                    child: VideoProgressIndicator(
                      controller,
                      allowScrubbing: true,
                      colors: const VideoProgressColors(
                        playedColor: AppColors.accent,
                        bufferedColor: Colors.white38,
                        backgroundColor: Colors.white24,
                      ),
                    ),
                  ),
                ),
              ),
            if (controller != null && controller.value.isInitialized)
              Positioned(
                top: 10,
                right: 10,
                child: IconButton.filledTonal(
                  onPressed: _togglePlayback,
                  icon: Icon(
                    _playing ? Icons.pause_rounded : Icons.play_arrow_rounded,
                  ),
                  tooltip: _playing ? '暂停预览' : '播放预览',
                ),
              ),
          ],
        ),
      ),
    );
  }

  Widget _buildCoverSettings() {
    final item = _selected!;
    final coverBytes = item.coverBytes;
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        const Text(
          '作品封面',
          style: TextStyle(fontSize: 15, fontWeight: FontWeight.w600),
        ),
        const SizedBox(height: 10),
        Row(
          children: [
            ClipRRect(
              borderRadius: BorderRadius.circular(8),
              child: SizedBox(
                width: 68,
                height: 88,
                child: coverBytes == null
                    ? const ColoredBox(
                        color: AppColors.surface,
                        child: Icon(
                          Icons.image_outlined,
                          color: AppColors.muted,
                        ),
                      )
                    : Image.memory(coverBytes, fit: BoxFit.cover),
              ),
            ),
            const SizedBox(width: 12),
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(
                    item.customCover == null ? '默认使用当前预览画面' : '已使用自定义封面',
                    style: const TextStyle(fontSize: 13),
                  ),
                  const SizedBox(height: 4),
                  const Text(
                    '可拖动视频进度条选帧，设为封面',
                    style: TextStyle(color: AppColors.muted, fontSize: 11),
                  ),
                  const SizedBox(height: 8),
                  Wrap(
                    spacing: 8,
                    runSpacing: 4,
                    children: [
                      TextButton(
                        onPressed: _pickCover,
                        child: const Text('上传封面'),
                      ),
                      if (item.customCover != null)
                        TextButton(
                          onPressed: _usePreviewAsCover,
                          child: const Text('恢复预览画面'),
                        )
                      else
                        TextButton(
                          onPressed: _usePreviewAsCover,
                          child: const Text('使用当前画面'),
                        ),
                    ],
                  ),
                  if (item.previewCaptureFailed && item.customCover == null)
                    const Text(
                      '无法截取预览画面，请上传封面',
                      style: TextStyle(color: AppColors.accent, fontSize: 11),
                    ),
                ],
              ),
            ),
          ],
        ),
      ],
    );
  }

  Widget _buildVideoQueue() {
    if (_videos.isEmpty) return const SizedBox.shrink();
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Row(
          children: [
            Text(
              '已选 ${_videos.length} 个视频',
              style: const TextStyle(fontSize: 15, fontWeight: FontWeight.w600),
            ),
            const Spacer(),
            const Text(
              '拖动排序',
              style: TextStyle(color: AppColors.muted, fontSize: 12),
            ),
          ],
        ),
        const SizedBox(height: 8),
        ReorderableListView.builder(
          shrinkWrap: true,
          physics: const NeverScrollableScrollPhysics(),
          itemCount: _videos.length,
          onReorder: _reorderVideos,
          itemBuilder: (context, index) {
            final item = _videos[index];
            final coverBytes = item.coverBytes;
            return ListTile(
              key: ValueKey(item.id),
              onTap: () => _selectVideo(item),
              contentPadding: const EdgeInsets.symmetric(horizontal: 8),
              leading: ClipRRect(
                borderRadius: BorderRadius.circular(6),
                child: SizedBox(
                  width: 42,
                  height: 56,
                  child: coverBytes == null
                      ? const ColoredBox(
                          color: AppColors.surface,
                          child: Icon(Icons.movie_outlined, size: 18),
                        )
                      : Image.memory(coverBytes, fit: BoxFit.cover),
                ),
              ),
              title: Text(
                item.video.name,
                maxLines: 1,
                overflow: TextOverflow.ellipsis,
                style: TextStyle(
                  color: identical(item, _selected)
                      ? AppColors.cyan
                      : Colors.white,
                ),
              ),
              subtitle: Text(
                _published.contains(item) ? '已发布' : '点击预览 · 拖动排序',
                style: const TextStyle(color: AppColors.muted, fontSize: 11),
              ),
              trailing: Row(
                mainAxisSize: MainAxisSize.min,
                children: [
                  if (_published.contains(item))
                    const Icon(
                      Icons.check_circle,
                      color: AppColors.cyan,
                      size: 18,
                    )
                  else
                    IconButton(
                      onPressed: () => _removeVideo(item),
                      icon: const Icon(Icons.close, size: 18),
                      tooltip: '移除视频',
                    ),
                  const Icon(Icons.drag_handle, color: AppColors.muted),
                ],
              ),
            );
          },
        ),
      ],
    );
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(title: const Text('发布作品')),
      body: AbsorbPointer(
        absorbing: _uploading,
        child: ListView(
          padding: const EdgeInsets.fromLTRB(18, 8, 18, 24),
          children: [
            _buildVideoPreview(),
            const SizedBox(height: 8),
            if (_selected != null) ...[
              _buildCoverSettings(),
              const SizedBox(height: 12),
            ],
            OutlinedButton.icon(
              onPressed: _pickVideos,
              icon: const Icon(Icons.video_library_outlined, size: 18),
              label: Text(_videos.isEmpty ? '选择视频' : '继续添加视频'),
              style: OutlinedButton.styleFrom(
                foregroundColor: Colors.white,
                side: const BorderSide(color: Colors.white24),
                padding: const EdgeInsets.symmetric(vertical: 12),
              ),
            ),
            _buildVideoQueue(),
            const SizedBox(height: 14),
            TextField(
              controller: _title,
              maxLength: 100,
              decoration: const InputDecoration(
                hintText: '给作品起个标题',
                counterText: '',
              ),
            ),
            const SizedBox(height: 12),
            TextField(
              controller: _description,
              maxLines: 4,
              maxLength: 500,
              decoration: const InputDecoration(
                hintText: '添加描述、话题，让更多人看到（选填）',
                counterText: '',
                alignLabelWithHint: true,
              ),
            ),
            const SizedBox(height: 12),
            TextField(
              controller: _music,
              maxLength: 100,
              decoration: const InputDecoration(
                hintText: '音乐名称 / 音乐人（选填）',
                prefixIcon: Icon(Icons.music_note_rounded),
                counterText: '',
              ),
            ),
            const Padding(
              padding: EdgeInsets.only(top: 2),
              child: Text(
                '音乐名称会展示在作品中；当前不会对视频音轨进行混音。',
                style: TextStyle(color: AppColors.muted, fontSize: 11),
              ),
            ),
            const SizedBox(height: 16),
            Container(
              padding: const EdgeInsets.all(14),
              decoration: BoxDecoration(
                color: const Color(0xFF161616),
                borderRadius: BorderRadius.circular(12),
              ),
              child: const Row(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Icon(
                    Icons.info_outline_rounded,
                    color: AppColors.muted,
                    size: 17,
                  ),
                  SizedBox(width: 9),
                  Expanded(
                    child: Text(
                      '支持 mp4 / mov / webm，每个视频不超过 200MB。封面默认截取当前预览画面，也可以上传图片。当前为学习演示版本，请勿上传违规或侵权内容。',
                      style: TextStyle(
                        color: AppColors.muted,
                        fontSize: 12,
                        height: 1.6,
                      ),
                    ),
                  ),
                ],
              ),
            ),
          ],
        ),
      ),
      bottomNavigationBar: SafeArea(
        child: Padding(
          padding: const EdgeInsets.fromLTRB(18, 8, 18, 12),
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              if (_uploading) ...[
                Row(
                  children: [
                    Text(
                      '正在发布 ${_published.length + 1}/${_videos.length}',
                      style: const TextStyle(
                        color: AppColors.muted,
                        fontSize: 12,
                      ),
                    ),
                    const Spacer(),
                    Text(
                      '${(_progress * 100).clamp(0, 100).toStringAsFixed(0)}%',
                      style: const TextStyle(
                        color: AppColors.muted,
                        fontSize: 12,
                      ),
                    ),
                  ],
                ),
                const SizedBox(height: 6),
                ClipRRect(
                  borderRadius: BorderRadius.circular(2),
                  child: LinearProgressIndicator(
                    value: _progress <= 0 ? null : _progress,
                    minHeight: 4,
                    backgroundColor: Colors.white12,
                    valueColor: const AlwaysStoppedAnimation(AppColors.accent),
                  ),
                ),
                const SizedBox(height: 12),
              ],
              SizedBox(
                height: 48,
                width: double.infinity,
                child: FilledButton(
                  onPressed: _uploading ? null : _publish,
                  style: FilledButton.styleFrom(
                    backgroundColor: AppColors.accent,
                    disabledBackgroundColor: AppColors.accent.withValues(
                      alpha: 0.4,
                    ),
                    shape: RoundedRectangleBorder(
                      borderRadius: BorderRadius.circular(24),
                    ),
                  ),
                  child: Text(
                    _uploading
                        ? '发布中…'
                        : _published.isEmpty
                        ? '发布${_videos.isEmpty ? '' : ' ${_videos.length} 个作品'}'
                        : '继续发布剩余作品',
                    style: const TextStyle(
                      fontSize: 16,
                      fontWeight: FontWeight.w600,
                    ),
                  ),
                ),
              ),
            ],
          ),
        ),
      ),
    );
  }
}
