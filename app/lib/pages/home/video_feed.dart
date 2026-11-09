import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../../core/api_client.dart';
import '../../core/app_state.dart';
import '../../core/theme.dart';
import '../../models/video.dart';
import '../../widgets/empty_state.dart';
import 'video_card.dart';

class VideoFeed extends StatefulWidget {
  final String tab;
  final bool active;

  const VideoFeed({super.key, required this.tab, this.active = true});

  @override
  State<VideoFeed> createState() => _VideoFeedState();
}

class _VideoFeedState extends State<VideoFeed> {
  final PageController _pageController = PageController();
  final List<Video> _videos = [];
  String? _cursor;
  String? _error;
  bool _loading = false;
  bool _requesting = false;
  int _current = 0;
  int _lastDataVersion = 0;
  late final AppState _app;

  @override
  void initState() {
    super.initState();
    _app = context.read<AppState>();
    _lastDataVersion = _app.dataVersion;
    _app.addListener(_onAppStateChanged);
    _load(refresh: true);
  }

  @override
  void dispose() {
    _app.removeListener(_onAppStateChanged);
    _pageController.dispose();
    super.dispose();
  }

  void _onAppStateChanged() {
    final version = _app.dataVersion;
    if (version == _lastDataVersion) return;
    _lastDataVersion = version;
    _load(refresh: true);
  }

  Future<void> _load({bool refresh = false}) async {
    if (_requesting) return;
    if (!refresh && _cursor == null && _videos.isNotEmpty) return;
    _requesting = true;
    setState(() {
      _loading = true;
      _error = null;
    });

    try {
      final data = await _app.api.feed(
        tab: widget.tab,
        cursor: refresh ? null : _cursor,
        limit: 8,
      );
      final items = (data['items'] as List? ?? [])
          .whereType<Map>()
          .map((item) => Video.fromJson(item.cast<String, dynamic>()))
          .toList();

      if (!mounted) return;
      setState(() {
        if (refresh) {
          _videos
            ..clear()
            ..addAll(items);
          _current = 0;
        } else {
          final existing = _videos.map((video) => video.id).toSet();
          _videos.addAll(items.where((video) => !existing.contains(video.id)));
        }
        _cursor = data['nextCursor']?.toString();
      });
      if (refresh && _pageController.hasClients) {
        _pageController.jumpToPage(0);
      }
    } on ApiException catch (error) {
      if (mounted) setState(() => _error = error.message);
    } catch (_) {
      if (mounted) setState(() => _error = '加载失败，请检查网络');
    } finally {
      _requesting = false;
      if (mounted) setState(() => _loading = false);
    }
  }

  void _onPageChanged(int index) {
    setState(() => _current = index);
    if (index >= _videos.length - 3) {
      _load();
    }
  }

  void _replaceVideo(Video updated) {
    final index = _videos.indexWhere((video) => video.id == updated.id);
    if (index < 0) return;
    setState(() => _videos[index] = updated);
  }

  @override
  Widget build(BuildContext context) {
    if (_videos.isEmpty) {
      if (_loading) {
        return const Center(
          child: SizedBox(
            width: 26,
            height: 26,
            child: CircularProgressIndicator(
              strokeWidth: 2,
              color: Colors.white70,
            ),
          ),
        );
      }
      if (_error != null) {
        return EmptyState(
          icon: Icons.wifi_off_rounded,
          title: '加载失败',
          subtitle: _error,
          actionLabel: '重新加载',
          onAction: () => _load(refresh: true),
        );
      }
      return EmptyState(
        icon: widget.tab == 'following'
            ? Icons.group_outlined
            : Icons.video_library_outlined,
        title: widget.tab == 'following' ? '关注的人还没有新作品' : '还没有视频',
        subtitle: widget.tab == 'following'
            ? '去搜索页关注几个喜欢的创作者，作品会出现在这里'
            : '点击底部 + 发布你的第一条作品',
        actionLabel: '刷新',
        onAction: () => _load(refresh: true),
      );
    }

    return Stack(
      children: [
        PageView.builder(
          controller: _pageController,
          scrollDirection: Axis.vertical,
          itemCount: _videos.length,
          onPageChanged: _onPageChanged,
          itemBuilder: (context, index) {
            final video = _videos[index];
            return VideoCard(
              key: ValueKey(video.id),
              video: video,
              active: widget.active && index == _current,
              onChanged: _replaceVideo,
            );
          },
        ),
        if (_loading && _videos.isNotEmpty)
          const Positioned(
            left: 0,
            right: 0,
            bottom: 10,
            child: IgnorePointer(
              child: Center(
                child: SizedBox(
                  width: 18,
                  height: 18,
                  child: CircularProgressIndicator(
                    strokeWidth: 2,
                    color: AppColors.muted,
                  ),
                ),
              ),
            ),
          ),
      ],
    );
  }
}
