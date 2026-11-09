import 'package:flutter/material.dart';

import '../widgets/empty_state.dart';

class NotificationsPage extends StatelessWidget {
  const NotificationsPage({super.key});

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(title: const Text('消息')),
      body: const EmptyState(
        icon: Icons.notifications_none_rounded,
        title: '消息中心即将上线',
        subtitle: '点赞、评论、关注通知会在 P1 版本开放。\n先把作品发出去，认识第一批观众吧。',
      ),
      backgroundColor: Colors.black,
    );
  }
}
