import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../../core/api_client.dart';
import '../../core/app_state.dart';
import '../../core/format.dart';
import '../../core/theme.dart';
import '../../models/user.dart';
import '../../models/video.dart';
import '../../widgets/cover_thumb.dart';
import '../../widgets/empty_state.dart';
import '../../widgets/user_avatar.dart';
import '../upload/upload_page.dart';
import '../video_detail_page.dart';
import 'edit_profile_page.dart';

class ProfilePage extends StatefulWidget {
  /// 为空表示当前登录用户
  final int? userId;

  const ProfilePage({super.key, this.userId});

  @override
  State<ProfilePage> createState() => _ProfilePageState();
}

class _ProfilePageState extends State<ProfilePage> {
  late final AppState _app;
  final List<Video> _videos = [];
  User? _user;
  String? _cursor;
  String? _error;
  bool _loading = false;
  bool _loadingMore = false;
  bool _followingBusy = false;
  int _tab = 0; // 0 作品，1 喜欢
  int _lastDataVersion = 0;

  bool get _isOwn => widget.userId == null || widget.userId == _app.me?.id;

  int? get _targetId => widget.userId ?? _app.me?.id;

  @override
  void initState() {
    super.initState();
    _app = context.read<AppState>();
    _lastDataVersion = _app.dataVersion;
    _app.addListener(_onAppStateChanged);
    _loadAll();
  }

  @override
  void dispose() {
    _app.removeListener(_onAppStateChanged);
    super.dispose();
  }

  void _onAppStateChanged() {
    if (_app.dataVersion == _lastDataVersion) return;
    _lastDataVersion = _app.dataVersion;
    if (_isOwn && mounted) _loadAll();
  }

  Future<void> _loadAll() async {
    final id = _targetId;
    if (id == null) return;
    setState(() {
      _loading = true;
      _error = null;
    });
    try {
      final results = await Future.wait([
        _app.api.user(id),
        _app.api.userVideos(id, type: _tab == 0 ? 'posts' : 'liked'),
      ]);
      final user = User.fromJson(
        (results[0]['user'] as Map).cast<String, dynamic>(),
      );
      final items = (results[1]['items'] as List? ?? [])
          .whereType<Map>()
          .map((item) => Video.fromJson(item.cast<String, dynamic>()))
          .toList();
      if (!mounted) return;
      setState(() {
        _user = user;
        _videos
          ..clear()
          ..addAll(items);
        _cursor = results[1]['nextCursor']?.toString();
      });
      if (_isOwn) _app.setUser(user);
    } on ApiException catch (error) {
      if (mounted) setState(() => _error = error.message);
    } catch (_) {
      if (mounted) setState(() => _error = '主页加载失败，请稍后重试');
    } finally {
      if (mounted) setState(() => _loading = false);
    }
  }

  Future<void> _loadMore() async {
    final id = _targetId;
    if (id == null || _cursor == null || _loadingMore) return;
    setState(() => _loadingMore = true);
    try {
      final data = await _app.api.userVideos(
        id,
        type: _tab == 0 ? 'posts' : 'liked',
        cursor: _cursor,
      );
      final items = (data['items'] as List? ?? [])
          .whereType<Map>()
          .map((item) => Video.fromJson(item.cast<String, dynamic>()))
          .toList();
      if (!mounted) return;
      setState(() {
        _videos.addAll(items);
        _cursor = data['nextCursor']?.toString();
      });
    } catch (_) {
      if (mounted) {
        ScaffoldMessenger.of(
          context,
        ).showSnackBar(const SnackBar(content: Text('加载失败，请稍后重试')));
      }
    } finally {
      if (mounted) setState(() => _loadingMore = false);
    }
  }

  Future<void> _switchTab(int tab) async {
    if (tab == _tab) return;
    setState(() {
      _tab = tab;
      _videos.clear();
      _cursor = null;
    });
    await _loadAll();
  }

  Future<void> _toggleFollow() async {
    final user = _user;
    if (user == null || _isOwn || _followingBusy) return;
    setState(() => _followingBusy = true);
    try {
      final data = user.isFollowing
          ? await _app.api.unfollow(user.id)
          : await _app.api.follow(user.id);
      final following = data['following'] == true;
      final followerCount =
          (data['followerCount'] as num?)?.toInt() ?? user.followerCount;
      if (!mounted) return;
      setState(() {
        _user = user.copyWith(
          isFollowing: following,
          followerCount: followerCount,
        );
      });
    } catch (error) {
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(
            content: Text(error is ApiException ? error.message : '操作失败'),
          ),
        );
      }
    } finally {
      if (mounted) setState(() => _followingBusy = false);
    }
  }

  Future<void> _editProfile() async {
    final changed = await Navigator.of(
      context,
    ).push<bool>(MaterialPageRoute(builder: (_) => const EditProfilePage()));
    if (changed == true) {
      await _loadAll();
    }
  }

  Future<void> _openUpload() async {
    await Navigator.of(
      context,
    ).push<bool>(MaterialPageRoute(builder: (_) => const UploadPage()));
  }

  Future<void> _logout() async {
    final confirmed = await showDialog<bool>(
      context: context,
      builder: (context) => AlertDialog(
        backgroundColor: AppColors.surfaceAlt,
        title: const Text('退出登录'),
        content: const Text('确定要退出当前账号吗？'),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(context, false),
            child: const Text('取消', style: TextStyle(color: AppColors.muted)),
          ),
          TextButton(
            onPressed: () => Navigator.pop(context, true),
            child: const Text('退出', style: TextStyle(color: AppColors.accent)),
          ),
        ],
      ),
    );
    if (confirmed == true) await _app.logout();
  }

  void _openVideo(Video video) {
    Navigator.of(
      context,
    ).push(MaterialPageRoute(builder: (_) => VideoDetailPage(video: video)));
  }

  @override
  Widget build(BuildContext context) {
    final canPop = Navigator.of(context).canPop();
    return Scaffold(
      appBar: AppBar(
        backgroundColor: Colors.transparent,
        leading: canPop
            ? IconButton(
                onPressed: () => Navigator.pop(context),
                icon: const Icon(Icons.arrow_back_ios_new_rounded, size: 20),
              )
            : null,
        title: Text(_user == null ? '个人主页' : '@${_user!.username}'),
        actions: [
          if (_isOwn)
            PopupMenuButton<String>(
              icon: const Icon(Icons.menu_rounded),
              color: AppColors.surfaceAlt,
              onSelected: (value) {
                if (value == 'edit') _editProfile();
                if (value == 'upload') _openUpload();
                if (value == 'logout') _logout();
              },
              itemBuilder: (context) => const [
                PopupMenuItem(value: 'edit', child: Text('编辑资料')),
                PopupMenuItem(value: 'upload', child: Text('发布作品')),
                PopupMenuItem(value: 'logout', child: Text('退出登录')),
              ],
            ),
        ],
      ),
      body: _buildBody(),
    );
  }

  Widget _buildBody() {
    if (_loading && _user == null) {
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
    if (_error != null && _user == null) {
      return EmptyState(
        icon: Icons.person_off_outlined,
        title: '无法打开主页',
        subtitle: _error,
        actionLabel: '重试',
        onAction: _loadAll,
      );
    }
    final user = _user;
    if (user == null) return const SizedBox.shrink();

    return RefreshIndicator(
      onRefresh: _loadAll,
      color: AppColors.accent,
      backgroundColor: AppColors.surfaceAlt,
      child: ListView(
        padding: const EdgeInsets.only(bottom: 28),
        children: [_buildHeader(user), _buildTabs(), _buildGrid()],
      ),
    );
  }

  Widget _buildHeader(User user) {
    return Padding(
      padding: const EdgeInsets.fromLTRB(20, 4, 20, 0),
      child: Column(
        children: [
          UserAvatar(url: user.avatarUrl, name: user.nickname, size: 88),
          const SizedBox(height: 14),
          Text(
            user.nickname,
            style: const TextStyle(fontSize: 20, fontWeight: FontWeight.w700),
          ),
          const SizedBox(height: 4),
          Text(
            '@${user.username}',
            style: const TextStyle(color: AppColors.muted, fontSize: 12),
          ),
          if (user.bio.isNotEmpty) ...[
            const SizedBox(height: 10),
            Text(
              user.bio,
              textAlign: TextAlign.center,
              style: const TextStyle(
                color: Colors.white70,
                fontSize: 13,
                height: 1.5,
              ),
            ),
          ],
          const SizedBox(height: 18),
          Row(
            mainAxisAlignment: MainAxisAlignment.center,
            children: [
              _Stat(value: formatCount(user.videoCount), label: '作品'),
              _Stat(value: formatCount(user.followingCount), label: '关注'),
              _Stat(value: formatCount(user.followerCount), label: '粉丝'),
            ],
          ),
          const SizedBox(height: 18),
          Row(
            children: [
              if (_isOwn) ...[
                Expanded(
                  child: OutlinedButton(
                    onPressed: _editProfile,
                    style: OutlinedButton.styleFrom(
                      foregroundColor: Colors.white,
                      side: const BorderSide(color: Colors.white24),
                      padding: const EdgeInsets.symmetric(vertical: 12),
                      shape: RoundedRectangleBorder(
                        borderRadius: BorderRadius.circular(10),
                      ),
                    ),
                    child: const Text('编辑资料'),
                  ),
                ),
                const SizedBox(width: 10),
                Expanded(
                  child: FilledButton(
                    onPressed: _openUpload,
                    style: FilledButton.styleFrom(
                      backgroundColor: AppColors.accent,
                      padding: const EdgeInsets.symmetric(vertical: 12),
                      shape: RoundedRectangleBorder(
                        borderRadius: BorderRadius.circular(10),
                      ),
                    ),
                    child: const Text('发布作品'),
                  ),
                ),
              ] else ...[
                Expanded(
                  child: user.isFollowing
                      ? OutlinedButton(
                          onPressed: _followingBusy ? null : _toggleFollow,
                          style: OutlinedButton.styleFrom(
                            foregroundColor: Colors.white70,
                            side: const BorderSide(color: Colors.white24),
                            padding: const EdgeInsets.symmetric(vertical: 12),
                            shape: RoundedRectangleBorder(
                              borderRadius: BorderRadius.circular(10),
                            ),
                          ),
                          child: const Text('已关注'),
                        )
                      : FilledButton(
                          onPressed: _followingBusy ? null : _toggleFollow,
                          style: FilledButton.styleFrom(
                            backgroundColor: AppColors.accent,
                            padding: const EdgeInsets.symmetric(vertical: 12),
                            shape: RoundedRectangleBorder(
                              borderRadius: BorderRadius.circular(10),
                            ),
                          ),
                          child: Text(_followingBusy ? '处理中…' : '关注'),
                        ),
                ),
                const SizedBox(width: 10),
                Expanded(
                  child: OutlinedButton(
                    onPressed: () {
                      ScaffoldMessenger.of(context).showSnackBar(
                        const SnackBar(content: Text('私信功能将在 P1 版本开放')),
                      );
                    },
                    style: OutlinedButton.styleFrom(
                      foregroundColor: Colors.white,
                      side: const BorderSide(color: Colors.white24),
                      padding: const EdgeInsets.symmetric(vertical: 12),
                      shape: RoundedRectangleBorder(
                        borderRadius: BorderRadius.circular(10),
                      ),
                    ),
                    child: const Text('私信'),
                  ),
                ),
              ],
            ],
          ),
          const SizedBox(height: 18),
        ],
      ),
    );
  }

  Widget _buildTabs() {
    return Container(
      decoration: const BoxDecoration(
        border: Border(bottom: BorderSide(color: Colors.white12)),
      ),
      child: Row(
        children: [
          _ProfileTab(
            text: '作品',
            active: _tab == 0,
            onTap: () => _switchTab(0),
          ),
          _ProfileTab(
            text: '喜欢',
            active: _tab == 1,
            onTap: () => _switchTab(1),
          ),
        ],
      ),
    );
  }

  Widget _buildGrid() {
    if (_loading && _videos.isEmpty) {
      return const Padding(
        padding: EdgeInsets.symmetric(vertical: 60),
        child: Center(
          child: SizedBox(
            width: 24,
            height: 24,
            child: CircularProgressIndicator(
              strokeWidth: 2,
              color: Colors.white70,
            ),
          ),
        ),
      );
    }
    if (_videos.isEmpty) {
      return Padding(
        padding: const EdgeInsets.symmetric(vertical: 32),
        child: EmptyState(
          icon: _tab == 0
              ? Icons.video_library_outlined
              : Icons.favorite_border_rounded,
          title: _tab == 0 ? '还没有作品' : '还没有喜欢的视频',
          subtitle: _isOwn && _tab == 0 ? '点击上方「发布作品」记录生活' : null,
        ),
      );
    }

    return Column(
      children: [
        GridView.builder(
          shrinkWrap: true,
          physics: const NeverScrollableScrollPhysics(),
          padding: const EdgeInsets.all(2),
          gridDelegate: const SliverGridDelegateWithFixedCrossAxisCount(
            crossAxisCount: 3,
            mainAxisSpacing: 2,
            crossAxisSpacing: 2,
            childAspectRatio: 3 / 4,
          ),
          itemCount: _videos.length,
          itemBuilder: (context, index) {
            final video = _videos[index];
            return GestureDetector(
              onTap: () => _openVideo(video),
              child: ClipRRect(
                borderRadius: BorderRadius.circular(2),
                child: CoverThumb(
                  coverUrl: video.coverUrl,
                  seed: video.id,
                  label: formatCount(video.viewCount),
                ),
              ),
            );
          },
        ),
        if (_cursor != null)
          Padding(
            padding: const EdgeInsets.only(top: 12),
            child: TextButton(
              onPressed: _loadingMore ? null : _loadMore,
              child: Text(
                _loadingMore ? '加载中…' : '加载更多',
                style: const TextStyle(color: AppColors.muted),
              ),
            ),
          ),
      ],
    );
  }
}

class _Stat extends StatelessWidget {
  final String value;
  final String label;

  const _Stat({required this.value, required this.label});

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: const EdgeInsets.symmetric(horizontal: 22),
      child: Column(
        children: [
          Text(
            value,
            style: const TextStyle(fontSize: 17, fontWeight: FontWeight.w700),
          ),
          const SizedBox(height: 3),
          Text(
            label,
            style: const TextStyle(color: AppColors.muted, fontSize: 12),
          ),
        ],
      ),
    );
  }
}

class _ProfileTab extends StatelessWidget {
  final String text;
  final bool active;
  final VoidCallback onTap;

  const _ProfileTab({
    required this.text,
    required this.active,
    required this.onTap,
  });

  @override
  Widget build(BuildContext context) {
    return Expanded(
      child: GestureDetector(
        onTap: onTap,
        behavior: HitTestBehavior.opaque,
        child: Column(
          children: [
            Padding(
              padding: const EdgeInsets.symmetric(vertical: 13),
              child: Text(
                text,
                style: TextStyle(
                  fontSize: 14,
                  fontWeight: active ? FontWeight.w700 : FontWeight.w500,
                  color: active ? Colors.white : AppColors.muted,
                ),
              ),
            ),
            AnimatedContainer(
              duration: const Duration(milliseconds: 160),
              height: 2,
              width: active ? 26 : 0,
              decoration: BoxDecoration(
                color: Colors.white,
                borderRadius: BorderRadius.circular(1),
              ),
            ),
          ],
        ),
      ),
    );
  }
}
