import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../../core/api_client.dart';
import '../../core/app_state.dart';
import '../../core/theme.dart';
import '../../models/user.dart';
import '../../models/video.dart';
import '../../widgets/cover_thumb.dart';
import '../../widgets/empty_state.dart';
import '../../widgets/user_avatar.dart';
import '../profile/profile_page.dart';
import '../video_detail_page.dart';

class SearchPage extends StatefulWidget {
  const SearchPage({super.key});

  @override
  State<SearchPage> createState() => _SearchPageState();
}

class _SearchPageState extends State<SearchPage> {
  late final AppState _app;
  final _query = TextEditingController();
  final List<Video> _videos = [];
  final List<User> _users = [];
  final List<User> _suggested = [];
  bool _loading = false;
  bool _loadingSuggested = false;
  bool _searched = false;
  String? _error;

  @override
  void initState() {
    super.initState();
    _app = context.read<AppState>();
    _loadSuggested();
  }

  @override
  void dispose() {
    _query.dispose();
    super.dispose();
  }

  Future<void> _loadSuggested() async {
    setState(() => _loadingSuggested = true);
    try {
      final data = await _app.api.suggestedUsers(limit: 10);
      final items = (data['items'] as List? ?? [])
          .whereType<Map>()
          .map((item) => User.fromJson(item.cast<String, dynamic>()))
          .toList();
      if (!mounted) return;
      setState(() {
        _suggested
          ..clear()
          ..addAll(items);
      });
    } catch (_) {
      // 推荐关注失败不阻塞搜索
    } finally {
      if (mounted) setState(() => _loadingSuggested = false);
    }
  }

  Future<void> _search() async {
    final q = _query.text.trim();
    if (q.isEmpty) {
      setState(() {
        _searched = false;
        _videos.clear();
        _users.clear();
        _error = null;
      });
      return;
    }
    FocusScope.of(context).unfocus();
    setState(() {
      _loading = true;
      _searched = true;
      _error = null;
    });
    try {
      final data = await _app.api.search(q);
      final videos = (data['videos'] as List? ?? [])
          .whereType<Map>()
          .map((item) => Video.fromJson(item.cast<String, dynamic>()))
          .toList();
      final users = (data['users'] as List? ?? [])
          .whereType<Map>()
          .map((item) => User.fromJson(item.cast<String, dynamic>()))
          .toList();
      if (!mounted) return;
      setState(() {
        _videos
          ..clear()
          ..addAll(videos);
        _users
          ..clear()
          ..addAll(users);
      });
    } catch (error) {
      if (mounted) {
        setState(() => _error = error is ApiException ? error.message : '搜索失败');
      }
    } finally {
      if (mounted) setState(() => _loading = false);
    }
  }

  Future<void> _toggleFollow(User user, {bool fromSuggested = false}) async {
    try {
      final data = user.isFollowing
          ? await _app.api.unfollow(user.id)
          : await _app.api.follow(user.id);
      final following = data['following'] == true;
      final followerCount =
          (data['followerCount'] as num?)?.toInt() ?? user.followerCount;
      if (!mounted) return;
      setState(() {
        final updated = user.copyWith(
          isFollowing: following,
          followerCount: followerCount,
        );
        final index = _users.indexWhere((item) => item.id == user.id);
        if (index >= 0) _users[index] = updated;
        final suggestedIndex = _suggested.indexWhere(
          (item) => item.id == user.id,
        );
        if (suggestedIndex >= 0) _suggested[suggestedIndex] = updated;
      });
      if (following && fromSuggested) await _loadSuggested();
    } catch (error) {
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(
            content: Text(error is ApiException ? error.message : '操作失败'),
          ),
        );
      }
    }
  }

  void _openProfile(int userId) {
    Navigator.of(
      context,
    ).push(MaterialPageRoute(builder: (_) => ProfilePage(userId: userId)));
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
        title: const Text('发现'),
        leading: canPop
            ? IconButton(
                onPressed: () => Navigator.pop(context),
                icon: const Icon(Icons.arrow_back_ios_new_rounded, size: 20),
              )
            : null,
      ),
      body: Column(
        children: [
          Padding(
            padding: const EdgeInsets.fromLTRB(16, 4, 16, 12),
            child: TextField(
              controller: _query,
              textInputAction: TextInputAction.search,
              onSubmitted: (_) => _search(),
              decoration: InputDecoration(
                hintText: '搜索视频、用户',
                prefixIcon: const Icon(
                  Icons.search_rounded,
                  color: AppColors.muted,
                  size: 21,
                ),
                suffixIcon: _query.text.isEmpty
                    ? null
                    : IconButton(
                        onPressed: () {
                          _query.clear();
                          setState(() {});
                          _search();
                        },
                        icon: const Icon(
                          Icons.close_rounded,
                          color: AppColors.muted,
                          size: 18,
                        ),
                      ),
                border: OutlineInputBorder(
                  borderRadius: BorderRadius.circular(22),
                  borderSide: BorderSide.none,
                ),
                contentPadding: const EdgeInsets.symmetric(vertical: 12),
              ),
              onChanged: (_) => setState(() {}),
            ),
          ),
          Expanded(child: _buildContent()),
        ],
      ),
    );
  }

  Widget _buildContent() {
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
        icon: Icons.search_off_rounded,
        title: '搜索失败',
        subtitle: _error,
        actionLabel: '重试',
        onAction: _search,
      );
    }
    if (!_searched) return _buildSuggested();
    if (_videos.isEmpty && _users.isEmpty) {
      return const EmptyState(
        icon: Icons.search_off_rounded,
        title: '没有找到相关内容',
        subtitle: '换个关键词试试，或者去推荐关注里逛逛',
      );
    }

    return ListView(
      padding: const EdgeInsets.fromLTRB(16, 4, 16, 24),
      children: [
        if (_videos.isNotEmpty) ...[
          const _SectionTitle(title: '视频'),
          GridView.builder(
            shrinkWrap: true,
            physics: const NeverScrollableScrollPhysics(),
            gridDelegate: const SliverGridDelegateWithFixedCrossAxisCount(
              crossAxisCount: 3,
              mainAxisSpacing: 3,
              crossAxisSpacing: 3,
              childAspectRatio: 3 / 4,
            ),
            itemCount: _videos.length,
            itemBuilder: (context, index) {
              final video = _videos[index];
              return GestureDetector(
                onTap: () => _openVideo(video),
                child: ClipRRect(
                  borderRadius: BorderRadius.circular(4),
                  child: CoverThumb(coverUrl: video.coverUrl, seed: video.id),
                ),
              );
            },
          ),
          const SizedBox(height: 20),
        ],
        if (_users.isNotEmpty) ...[
          const _SectionTitle(title: '用户'),
          ..._users.map(
            (user) => _UserTile(
              user: user,
              isMe: _app.me?.id == user.id,
              onTap: () => _openProfile(user.id),
              onFollow: () => _toggleFollow(user),
            ),
          ),
        ],
      ],
    );
  }

  Widget _buildSuggested() {
    return ListView(
      padding: const EdgeInsets.fromLTRB(16, 4, 16, 24),
      children: [
        const _SectionTitle(title: '推荐关注'),
        if (_loadingSuggested)
          const Padding(
            padding: EdgeInsets.symmetric(vertical: 40),
            child: Center(
              child: SizedBox(
                width: 22,
                height: 22,
                child: CircularProgressIndicator(
                  strokeWidth: 2,
                  color: Colors.white70,
                ),
              ),
            ),
          )
        else if (_suggested.isEmpty)
          const EmptyState(
            icon: Icons.group_outlined,
            title: '暂时没有推荐用户',
            subtitle: '试试搜索昵称找到朋友',
          )
        else
          ..._suggested.map(
            (user) => _UserTile(
              user: user,
              isMe: _app.me?.id == user.id,
              onTap: () => _openProfile(user.id),
              onFollow: () => _toggleFollow(user, fromSuggested: true),
            ),
          ),
        const SizedBox(height: 18),
        const Text(
          '搜索支持视频标题、描述、用户昵称与用户名。',
          style: TextStyle(color: Color(0xFF5A5A5E), fontSize: 12),
        ),
      ],
    );
  }
}

class _SectionTitle extends StatelessWidget {
  final String title;

  const _SectionTitle({required this.title});

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: const EdgeInsets.only(bottom: 10, top: 4),
      child: Text(
        title,
        style: const TextStyle(fontSize: 15, fontWeight: FontWeight.w700),
      ),
    );
  }
}

class _UserTile extends StatelessWidget {
  final User user;
  final bool isMe;
  final VoidCallback onTap;
  final VoidCallback onFollow;

  const _UserTile({
    required this.user,
    required this.isMe,
    required this.onTap,
    required this.onFollow,
  });

  @override
  Widget build(BuildContext context) {
    return InkWell(
      onTap: onTap,
      child: Padding(
        padding: const EdgeInsets.symmetric(vertical: 9),
        child: Row(
          children: [
            UserAvatar(url: user.avatarUrl, name: user.nickname, size: 48),
            const SizedBox(width: 12),
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(
                    user.nickname,
                    maxLines: 1,
                    overflow: TextOverflow.ellipsis,
                    style: const TextStyle(
                      fontSize: 14,
                      fontWeight: FontWeight.w600,
                    ),
                  ),
                  const SizedBox(height: 3),
                  Text(
                    user.bio.isEmpty ? '@${user.username}' : user.bio,
                    maxLines: 1,
                    overflow: TextOverflow.ellipsis,
                    style: const TextStyle(
                      color: AppColors.muted,
                      fontSize: 12,
                    ),
                  ),
                ],
              ),
            ),
            const SizedBox(width: 10),
            if (!isMe)
              SizedBox(
                height: 32,
                child: user.isFollowing
                    ? OutlinedButton(
                        onPressed: onFollow,
                        style: OutlinedButton.styleFrom(
                          foregroundColor: Colors.white70,
                          side: const BorderSide(color: Colors.white24),
                          padding: const EdgeInsets.symmetric(horizontal: 14),
                          shape: RoundedRectangleBorder(
                            borderRadius: BorderRadius.circular(16),
                          ),
                        ),
                        child: const Text(
                          '已关注',
                          style: TextStyle(fontSize: 12),
                        ),
                      )
                    : FilledButton(
                        onPressed: onFollow,
                        style: FilledButton.styleFrom(
                          backgroundColor: AppColors.accent,
                          padding: const EdgeInsets.symmetric(horizontal: 16),
                          shape: RoundedRectangleBorder(
                            borderRadius: BorderRadius.circular(16),
                          ),
                        ),
                        child: const Text('关注', style: TextStyle(fontSize: 12)),
                      ),
              ),
          ],
        ),
      ),
    );
  }
}
