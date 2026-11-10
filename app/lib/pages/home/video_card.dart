import 'dart:async';
import 'dart:math' as math;

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:provider/provider.dart';
import 'package:video_player/video_player.dart';

import '../../core/app_config.dart';
import '../../core/app_state.dart';
import '../../core/format.dart';
import '../../core/route_observer.dart';
import '../../core/theme.dart';
import '../../models/video.dart';
import '../../widgets/cover_thumb.dart';
import '../../widgets/user_avatar.dart';
import '../comments/comments_sheet.dart';
import '../profile/profile_page.dart';

class VideoCard extends StatefulWidget {
  final Video video;
  final bool active;
  final ValueChanged<Video> onChanged;

  const VideoCard({
    super.key,
    required this.video,
    required this.active,
    required this.onChanged,
  });

  @override
  State<VideoCard> createState() => _VideoCardState();
}

class _VideoCardState extends State<VideoCard> with RouteAware {
  VideoPlayerController? _controller;
  ModalRoute<dynamic>? _route;
  bool _initializing = true;
  bool _failed = false;
  bool _muted = false;
  bool _showHeart = false;
  bool _liking = false;
  bool _following = false;
  bool _viewCounted = false;
  Timer? _heartTimer;

  Video get video => widget.video;

  @override
  void initState() {
    super.initState();
    if (widget.active) {
      _start();
    }
  }

  @override
  void didChangeDependencies() {
    super.didChangeDependencies();
    final route = ModalRoute.of(context);
    if (route != _route) {
      if (_route != null) routeObserver.unsubscribe(this);
      _route = route;
      if (route is ModalRoute) {
        routeObserver.subscribe(this, route);
      }
    }
  }

  @override
  void didUpdateWidget(covariant VideoCard oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (widget.active && !oldWidget.active) {
      _start();
    } else if (!widget.active && oldWidget.active) {
      _controller?.pause();
    }
  }

  @override
  void didPushNext() {
    _controller?.pause();
  }

  @override
  void didPopNext() {
    if (widget.active) _start();
  }

  void _start() {
    final controller = _controller;
    if (controller != null && controller.value.isInitialized) {
      controller.play();
      _countView();
      return;
    }
    _initPlayer();
    _countView();
  }

  Future<void> _initPlayer() async {
    final url = AppConfig.absolute(video.videoUrl);
    if (url == null) {
      setState(() => _failed = true);
      return;
    }
    await _controller?.dispose();
    _controller = null;
    setState(() {
      _initializing = true;
      _failed = false;
    });

    final controller = VideoPlayerController.networkUrl(Uri.parse(url));
    _controller = controller;
    try {
      await controller.initialize();
      if (!mounted) {
        await controller.dispose();
        return;
      }
      await controller.setLooping(true);
      await controller.setVolume(_muted ? 0 : 1);
      if (widget.active) await controller.play();
      if (mounted) setState(() => _initializing = false);
    } catch (_) {
      if (!mounted) return;
      setState(() {
        _initializing = false;
        _failed = true;
      });
    }
  }

  void _countView() {
    if (_viewCounted) return;
    _viewCounted = true;
    context
        .read<AppState>()
        .api
        .viewVideo(video.id)
        .catchError((_) => <String, dynamic>{});
  }

  @override
  void dispose() {
    routeObserver.unsubscribe(this);
    _heartTimer?.cancel();
    _controller?.dispose();
    super.dispose();
  }

  Future<void> _togglePlay() async {
    final controller = _controller;
    if (controller == null || !controller.value.isInitialized) return;
    if (controller.value.isPlaying) {
      await controller.pause();
    } else {
      await controller.play();
    }
    if (mounted) setState(() {});
  }

  void _showLikeBurst() {
    _heartTimer?.cancel();
    setState(() => _showHeart = true);
    _heartTimer = Timer(const Duration(milliseconds: 650), () {
      if (mounted) setState(() => _showHeart = false);
    });
  }

  Future<void> _toggleLike({bool forceLike = false}) async {
    if (_liking) return;
    if (forceLike && video.liked) {
      _showLikeBurst();
      return;
    }
    final nextLiked = !video.liked;
    final previous = video;
    widget.onChanged(
      video.copyWith(
        liked: nextLiked,
        likeCount: math.max(0, video.likeCount + (nextLiked ? 1 : -1)),
      ),
    );
    setState(() => _liking = true);
    try {
      final api = context.read<AppState>().api;
      final data = nextLiked
          ? await api.likeVideo(video.id)
          : await api.unlikeVideo(video.id);
      final count = (data['likeCount'] as num?)?.toInt();
      if (count != null) {
        widget.onChanged(widget.video.copyWith(likeCount: count));
      }
      if (nextLiked) _showLikeBurst();
    } catch (_) {
      widget.onChanged(previous);
      if (mounted) {
        ScaffoldMessenger.of(
          context,
        ).showSnackBar(const SnackBar(content: Text('操作失败，请稍后重试')));
      }
    } finally {
      if (mounted) setState(() => _liking = false);
    }
  }

  Future<void> _follow() async {
    if (_following || video.author.isFollowing) return;
    setState(() => _following = true);
    try {
      final api = context.read<AppState>().api;
      await api.follow(video.author.id);
      widget.onChanged(
        video.copyWith(author: video.author.copyWith(isFollowing: true)),
      );
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(
            content: Text('已关注 ${video.author.nickname}'),
            duration: const Duration(seconds: 1),
          ),
        );
      }
    } catch (_) {
      if (mounted) {
        ScaffoldMessenger.of(
          context,
        ).showSnackBar(const SnackBar(content: Text('关注失败，请稍后重试')));
      }
    } finally {
      if (mounted) setState(() => _following = false);
    }
  }

  Future<void> _share() async {
    final link = AppConfig.absolute(video.videoUrl) ?? '';
    final api = context.read<AppState>().api;
    await Clipboard.setData(ClipboardData(text: '${video.title}\n$link'));
    try {
      final data = await api.shareVideo(video.id);
      final count = (data['shareCount'] as num?)?.toInt();
      if (count != null) widget.onChanged(video.copyWith(shareCount: count));
    } catch (_) {
      // 分享计数失败不影响复制链接
    }
    if (mounted) {
      ScaffoldMessenger.of(
        context,
      ).showSnackBar(const SnackBar(content: Text('视频链接已复制，去分享给朋友吧')));
    }
  }

  Future<void> _openComments() async {
    await showModalBottomSheet<void>(
      context: context,
      isScrollControlled: true,
      backgroundColor: Colors.transparent,
      builder: (_) => CommentsSheet(
        videoId: video.id,
        initialCount: video.commentCount,
        onCountChanged: (count) {
          widget.onChanged(video.copyWith(commentCount: count));
        },
      ),
    );
  }

  void _openProfile() {
    Navigator.of(context).push(
      MaterialPageRoute(builder: (_) => ProfilePage(userId: video.author.id)),
    );
  }

  @override
  Widget build(BuildContext context) {
    final controller = _controller;
    final ready = controller?.value.isInitialized ?? false;
    final playing = controller?.value.isPlaying ?? false;
    final me = context.watch<AppState>().me;
    final videoSize = ready ? controller!.value.size : null;

    return GestureDetector(
      behavior: HitTestBehavior.opaque,
      onTap: _togglePlay,
      onDoubleTap: () => _toggleLike(forceLike: true),
      child: Stack(
        fit: StackFit.expand,
        children: [
          CoverThumb(coverUrl: video.coverUrl, seed: video.id),
          if (ready &&
              controller != null &&
              videoSize != null &&
              videoSize.width > 0)
            ClipRect(
              child: FittedBox(
                fit: BoxFit.cover,
                child: SizedBox(
                  width: videoSize.width,
                  height: videoSize.height,
                  child: VideoPlayer(controller),
                ),
              ),
            ),
          if (_initializing && !_failed)
            const Center(
              child: SizedBox(
                width: 28,
                height: 28,
                child: CircularProgressIndicator(
                  strokeWidth: 2,
                  color: Colors.white70,
                ),
              ),
            ),
          if (_failed)
            Center(
              child: Column(
                mainAxisSize: MainAxisSize.min,
                children: [
                  const Icon(
                    Icons.error_outline,
                    color: Colors.white70,
                    size: 42,
                  ),
                  const SizedBox(height: 10),
                  const Text('视频加载失败', style: TextStyle(color: Colors.white70)),
                  const SizedBox(height: 12),
                  OutlinedButton(
                    onPressed: _initPlayer,
                    style: OutlinedButton.styleFrom(
                      foregroundColor: Colors.white,
                      side: const BorderSide(color: Colors.white38),
                    ),
                    child: const Text('重试'),
                  ),
                ],
              ),
            ),
          const Positioned(
            left: 0,
            right: 0,
            bottom: 0,
            height: 300,
            child: IgnorePointer(
              child: DecoratedBox(
                decoration: BoxDecoration(
                  gradient: LinearGradient(
                    begin: Alignment.topCenter,
                    end: Alignment.bottomCenter,
                    colors: [Colors.transparent, Colors.black87],
                    stops: [0, 1],
                  ),
                ),
              ),
            ),
          ),
          Positioned(
            top: 0,
            right: 0,
            child: SafeArea(
              child: IconButton(
                onPressed: () async {
                  final c = _controller;
                  if (c == null) return;
                  setState(() => _muted = !_muted);
                  await c.setVolume(_muted ? 0 : 1);
                },
                icon: Icon(
                  _muted ? Icons.volume_off_rounded : Icons.volume_up_rounded,
                  color: Colors.white70,
                  size: 22,
                ),
              ),
            ),
          ),
          if (ready && !playing)
            const IgnorePointer(
              child: Center(
                child: Icon(
                  Icons.play_arrow_rounded,
                  color: Colors.white54,
                  size: 82,
                ),
              ),
            ),
          Positioned(
            right: 8,
            bottom: 96,
            child: _ActionRail(
              video: video,
              canFollow: me == null || me.id != video.author.id,
              busyFollowing: _following,
              onLike: _toggleLike,
              onComment: _openComments,
              onShare: _share,
              onFollow: _follow,
              onProfile: _openProfile,
            ),
          ),
          Positioned(
            left: 14,
            right: 78,
            bottom: 26,
            child: _VideoInfo(video: video, onProfile: _openProfile),
          ),
          if (controller != null)
            Positioned(
              left: 0,
              right: 0,
              bottom: 0,
              child: ValueListenableBuilder<VideoPlayerValue>(
                valueListenable: controller,
                builder: (context, value, _) {
                  if (!ready) return const SizedBox(height: 2);
                  final total = value.duration.inMilliseconds;
                  final progress = total <= 0
                      ? 0.0
                      : (value.position.inMilliseconds / total).clamp(0.0, 1.0);
                  return LinearProgressIndicator(
                    value: progress,
                    minHeight: 2,
                    backgroundColor: Colors.white24,
                    valueColor: const AlwaysStoppedAnimation(Colors.white70),
                  );
                },
              ),
            ),
          IgnorePointer(
            child: Center(
              child: AnimatedOpacity(
                opacity: _showHeart ? 1 : 0,
                duration: const Duration(milliseconds: 180),
                child: AnimatedScale(
                  scale: _showHeart ? 1 : 0.5,
                  duration: const Duration(milliseconds: 220),
                  curve: Curves.easeOutBack,
                  child: const Icon(
                    Icons.favorite,
                    color: AppColors.accent,
                    size: 110,
                  ),
                ),
              ),
            ),
          ),
        ],
      ),
    );
  }
}

class _VideoInfo extends StatelessWidget {
  final Video video;
  final VoidCallback onProfile;

  const _VideoInfo({required this.video, required this.onProfile});

  @override
  Widget build(BuildContext context) {
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      mainAxisSize: MainAxisSize.min,
      children: [
        GestureDetector(
          onTap: onProfile,
          child: Text(
            '@${video.author.nickname}',
            style: const TextStyle(
              fontSize: 16,
              fontWeight: FontWeight.w700,
              color: Colors.white,
              shadows: [Shadow(blurRadius: 6, color: Colors.black54)],
            ),
          ),
        ),
        const SizedBox(height: 8),
        Text(
          video.title,
          maxLines: 2,
          overflow: TextOverflow.ellipsis,
          style: const TextStyle(
            fontSize: 14.5,
            height: 1.4,
            color: Colors.white,
            fontWeight: FontWeight.w500,
            shadows: [Shadow(blurRadius: 6, color: Colors.black54)],
          ),
        ),
        if (video.description.isNotEmpty) ...[
          const SizedBox(height: 5),
          Text(
            video.description,
            maxLines: 1,
            overflow: TextOverflow.ellipsis,
            style: const TextStyle(
              fontSize: 12.5,
              color: Colors.white70,
              shadows: [Shadow(blurRadius: 6, color: Colors.black54)],
            ),
          ),
        ],
        const SizedBox(height: 9),
        Row(
          children: [
            const Icon(Icons.music_note_rounded, color: Colors.white, size: 15),
            const SizedBox(width: 4),
            Expanded(
              child: Text(
                video.musicName.isEmpty
                    ? '原声 · ${video.author.nickname}'
                    : video.musicName,
                maxLines: 1,
                overflow: TextOverflow.ellipsis,
                style: const TextStyle(
                  fontSize: 12,
                  color: Colors.white,
                  shadows: [Shadow(blurRadius: 5, color: Colors.black54)],
                ),
              ),
            ),
          ],
        ),
      ],
    );
  }
}

class _ActionRail extends StatelessWidget {
  final Video video;
  final bool canFollow;
  final bool busyFollowing;
  final VoidCallback onLike;
  final VoidCallback onComment;
  final VoidCallback onShare;
  final VoidCallback onFollow;
  final VoidCallback onProfile;

  const _ActionRail({
    required this.video,
    required this.canFollow,
    required this.busyFollowing,
    required this.onLike,
    required this.onComment,
    required this.onShare,
    required this.onFollow,
    required this.onProfile,
  });

  @override
  Widget build(BuildContext context) {
    final showFollow = canFollow && !video.author.isFollowing;
    return Column(
      mainAxisSize: MainAxisSize.min,
      children: [
        SizedBox(
          width: 56,
          height: 62,
          child: Stack(
            clipBehavior: Clip.none,
            alignment: Alignment.topCenter,
            children: [
              UserAvatar(
                url: video.author.avatarUrl,
                name: video.author.nickname,
                size: 48,
                onTap: onProfile,
              ),
              if (showFollow)
                Positioned(
                  bottom: -2,
                  child: GestureDetector(
                    onTap: busyFollowing ? null : onFollow,
                    child: Container(
                      width: 22,
                      height: 22,
                      decoration: const BoxDecoration(
                        color: AppColors.accent,
                        shape: BoxShape.circle,
                      ),
                      child: busyFollowing
                          ? const Padding(
                              padding: EdgeInsets.all(5),
                              child: CircularProgressIndicator(
                                strokeWidth: 1.6,
                                color: Colors.white,
                              ),
                            )
                          : const Icon(
                              Icons.add,
                              color: Colors.white,
                              size: 17,
                            ),
                    ),
                  ),
                ),
            ],
          ),
        ),
        const SizedBox(height: 16),
        _ActionButton(
          icon: video.liked ? Icons.favorite : Icons.favorite_border,
          color: video.liked ? AppColors.accent : Colors.white,
          label: formatCount(video.likeCount),
          onTap: onLike,
        ),
        const SizedBox(height: 16),
        _ActionButton(
          icon: Icons.mode_comment_outlined,
          label: formatCount(video.commentCount),
          onTap: onComment,
        ),
        const SizedBox(height: 16),
        _ActionButton(
          icon: Icons.reply_rounded,
          label: formatCount(video.shareCount),
          onTap: onShare,
        ),
      ],
    );
  }
}

class _ActionButton extends StatelessWidget {
  final IconData icon;
  final String label;
  final VoidCallback onTap;
  final Color color;

  const _ActionButton({
    required this.icon,
    required this.label,
    required this.onTap,
    this.color = Colors.white,
  });

  @override
  Widget build(BuildContext context) {
    return GestureDetector(
      behavior: HitTestBehavior.opaque,
      onTap: onTap,
      child: Column(
        children: [
          Icon(
            icon,
            color: color,
            size: 34,
            shadows: const [Shadow(blurRadius: 8, color: Colors.black54)],
          ),
          const SizedBox(height: 3),
          Text(
            label,
            style: const TextStyle(
              color: Colors.white,
              fontSize: 12,
              fontWeight: FontWeight.w600,
              shadows: [Shadow(blurRadius: 5, color: Colors.black54)],
            ),
          ),
        ],
      ),
    );
  }
}
