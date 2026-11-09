import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../../core/api_client.dart';
import '../../core/app_state.dart';
import '../../core/format.dart';
import '../../core/theme.dart';
import '../../models/comment.dart';
import '../../widgets/empty_state.dart';
import '../../widgets/user_avatar.dart';
import '../profile/profile_page.dart';

class CommentsSheet extends StatefulWidget {
  final int videoId;
  final int initialCount;
  final ValueChanged<int> onCountChanged;

  const CommentsSheet({
    super.key,
    required this.videoId,
    required this.initialCount,
    required this.onCountChanged,
  });

  @override
  State<CommentsSheet> createState() => _CommentsSheetState();
}

class _CommentsSheetState extends State<CommentsSheet> {
  final _input = TextEditingController();
  final _scroll = ScrollController();
  final List<Comment> _comments = [];
  String? _cursor;
  late int _total = widget.initialCount;
  bool _loading = false;
  bool _loadingMore = false;
  bool _sending = false;
  String? _error;

  @override
  void initState() {
    super.initState();
    _load(refresh: true);
    _scroll.addListener(() {
      if (_scroll.position.pixels >= _scroll.position.maxScrollExtent - 120) {
        _load();
      }
    });
  }

  @override
  void dispose() {
    _input.dispose();
    _scroll.dispose();
    super.dispose();
  }

  Future<void> _load({bool refresh = false}) async {
    if (refresh) {
      if (_loading) return;
      setState(() {
        _loading = true;
        _error = null;
      });
    } else {
      if (_loadingMore || _cursor == null) return;
      setState(() => _loadingMore = true);
    }

    try {
      final data = await context.read<AppState>().api.comments(
        widget.videoId,
        cursor: refresh ? null : _cursor,
      );
      final items = (data['items'] as List? ?? [])
          .whereType<Map>()
          .map((item) => Comment.fromJson(item.cast<String, dynamic>()))
          .toList();
      if (!mounted) return;
      setState(() {
        if (refresh) {
          _comments
            ..clear()
            ..addAll(items);
        } else {
          _comments.addAll(items);
        }
        _cursor = data['nextCursor']?.toString();
        final total = (data['total'] as num?)?.toInt();
        if (total != null) _total = total;
      });
    } catch (error) {
      if (mounted) {
        setState(
          () => _error = error is ApiException ? error.message : '评论加载失败',
        );
      }
    } finally {
      if (mounted) {
        setState(() {
          _loading = false;
          _loadingMore = false;
        });
      }
    }
  }

  Future<void> _send() async {
    final text = _input.text.trim();
    if (text.isEmpty || _sending) return;
    FocusScope.of(context).unfocus();
    setState(() => _sending = true);
    try {
      final data = await context.read<AppState>().api.postComment(
        widget.videoId,
        text,
      );
      final comment = Comment.fromJson(
        (data['comment'] as Map).cast<String, dynamic>(),
      );
      final count = (data['commentCount'] as num?)?.toInt();
      if (!mounted) return;
      setState(() {
        _comments.insert(0, comment);
        _total = count ?? _total + 1;
        _input.clear();
      });
      widget.onCountChanged(_total);
      if (_scroll.hasClients) {
        _scroll.animateTo(
          0,
          duration: const Duration(milliseconds: 220),
          curve: Curves.easeOut,
        );
      }
    } catch (error) {
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(
            content: Text(error is ApiException ? error.message : '评论失败'),
          ),
        );
      }
    } finally {
      if (mounted) setState(() => _sending = false);
    }
  }

  Future<void> _delete(Comment comment) async {
    final confirmed = await showDialog<bool>(
      context: context,
      builder: (context) => AlertDialog(
        backgroundColor: AppColors.surfaceAlt,
        title: const Text('删除评论'),
        content: const Text('确定删除这条评论吗？'),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(context, false),
            child: const Text('取消', style: TextStyle(color: AppColors.muted)),
          ),
          TextButton(
            onPressed: () => Navigator.pop(context, true),
            child: const Text('删除', style: TextStyle(color: AppColors.accent)),
          ),
        ],
      ),
    );
    if (confirmed != true || !mounted) return;
    final api = context.read<AppState>().api;

    try {
      final data = await api.deleteComment(comment.id);
      final count = (data['commentCount'] as num?)?.toInt();
      if (!mounted) return;
      setState(() {
        _comments.removeWhere((item) => item.id == comment.id);
        _total = count ?? (_total > 0 ? _total - 1 : 0);
      });
      widget.onCountChanged(_total);
    } catch (error) {
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(
            content: Text(error is ApiException ? error.message : '删除失败'),
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

  @override
  Widget build(BuildContext context) {
    final insets = MediaQuery.viewInsetsOf(context);
    final height = MediaQuery.sizeOf(context).height * 0.72;
    return Container(
      height: height + insets.bottom,
      decoration: const BoxDecoration(
        color: AppColors.surfaceAlt,
        borderRadius: BorderRadius.vertical(top: Radius.circular(18)),
      ),
      child: Padding(
        padding: EdgeInsets.only(bottom: insets.bottom),
        child: Column(
          children: [
            const SizedBox(height: 8),
            Container(
              width: 36,
              height: 4,
              decoration: BoxDecoration(
                color: Colors.white24,
                borderRadius: BorderRadius.circular(2),
              ),
            ),
            SizedBox(
              height: 48,
              child: Row(
                children: [
                  const SizedBox(width: 18),
                  Text(
                    '${formatCount(_total)} 条评论',
                    style: const TextStyle(
                      fontSize: 14,
                      fontWeight: FontWeight.w600,
                    ),
                  ),
                  const Spacer(),
                  IconButton(
                    onPressed: () => Navigator.pop(context),
                    icon: const Icon(
                      Icons.close_rounded,
                      color: AppColors.muted,
                      size: 22,
                    ),
                  ),
                ],
              ),
            ),
            const Divider(height: 1),
            Expanded(child: _buildList()),
            _buildInput(),
          ],
        ),
      ),
    );
  }

  Widget _buildList() {
    if (_loading && _comments.isEmpty) {
      return const Center(
        child: SizedBox(
          width: 24,
          height: 24,
          child: CircularProgressIndicator(
            strokeWidth: 2,
            color: Colors.white70,
          ),
        ),
      );
    }
    if (_error != null && _comments.isEmpty) {
      return EmptyState(
        icon: Icons.chat_bubble_outline_rounded,
        title: '评论加载失败',
        subtitle: _error,
        actionLabel: '重试',
        onAction: () => _load(refresh: true),
      );
    }
    if (_comments.isEmpty) {
      return const EmptyState(
        icon: Icons.chat_bubble_outline_rounded,
        title: '还没有评论',
        subtitle: '来抢沙发，说点什么吧',
      );
    }

    return ListView.builder(
      controller: _scroll,
      padding: const EdgeInsets.fromLTRB(16, 14, 16, 14),
      itemCount: _comments.length + (_loadingMore ? 1 : 0),
      itemBuilder: (context, index) {
        if (index >= _comments.length) {
          return const Padding(
            padding: EdgeInsets.all(12),
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
          );
        }
        final comment = _comments[index];
        return Padding(
          padding: const EdgeInsets.only(bottom: 18),
          child: Row(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              UserAvatar(
                url: comment.author.avatarUrl,
                name: comment.author.nickname,
                size: 34,
                onTap: () => _openProfile(comment.author.id),
              ),
              const SizedBox(width: 10),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(
                      comment.author.nickname,
                      style: const TextStyle(
                        color: Colors.white60,
                        fontSize: 12,
                      ),
                    ),
                    const SizedBox(height: 3),
                    Text(
                      comment.content,
                      style: const TextStyle(fontSize: 14, height: 1.45),
                    ),
                    const SizedBox(height: 4),
                    Text(
                      timeAgoFromIso(comment.createdAt),
                      style: const TextStyle(
                        color: AppColors.muted,
                        fontSize: 11,
                      ),
                    ),
                  ],
                ),
              ),
              const SizedBox(width: 8),
              Column(
                children: [
                  const Icon(
                    Icons.favorite_border,
                    color: AppColors.muted,
                    size: 17,
                  ),
                  const SizedBox(height: 3),
                  Text(
                    comment.likeCount > 0 ? formatCount(comment.likeCount) : '',
                    style: const TextStyle(
                      color: AppColors.muted,
                      fontSize: 10,
                    ),
                  ),
                ],
              ),
              if (comment.canDelete)
                GestureDetector(
                  onTap: () => _delete(comment),
                  behavior: HitTestBehavior.opaque,
                  child: const Padding(
                    padding: EdgeInsets.only(left: 8, top: 2),
                    child: Icon(
                      Icons.delete_outline_rounded,
                      color: AppColors.muted,
                      size: 18,
                    ),
                  ),
                ),
            ],
          ),
        );
      },
    );
  }

  Widget _buildInput() {
    return Container(
      padding: const EdgeInsets.fromLTRB(14, 10, 14, 10),
      decoration: const BoxDecoration(
        border: Border(top: BorderSide(color: Colors.white12)),
      ),
      child: Row(
        children: [
          Expanded(
            child: TextField(
              controller: _input,
              minLines: 1,
              maxLines: 3,
              maxLength: 500,
              textInputAction: TextInputAction.send,
              onSubmitted: (_) => _send(),
              decoration: const InputDecoration(
                hintText: '说点什么…',
                counterText: '',
                contentPadding: EdgeInsets.symmetric(
                  horizontal: 14,
                  vertical: 10,
                ),
                isDense: true,
              ),
            ),
          ),
          const SizedBox(width: 10),
          SizedBox(
            width: 44,
            height: 40,
            child: FilledButton(
              onPressed: _sending ? null : _send,
              style: FilledButton.styleFrom(
                padding: EdgeInsets.zero,
                backgroundColor: AppColors.accent,
                disabledBackgroundColor: AppColors.accent.withValues(
                  alpha: 0.4,
                ),
                shape: RoundedRectangleBorder(
                  borderRadius: BorderRadius.circular(10),
                ),
              ),
              child: _sending
                  ? const SizedBox(
                      width: 16,
                      height: 16,
                      child: CircularProgressIndicator(
                        strokeWidth: 2,
                        color: Colors.white,
                      ),
                    )
                  : const Icon(
                      Icons.send_rounded,
                      color: Colors.white,
                      size: 18,
                    ),
            ),
          ),
        ],
      ),
    );
  }
}
