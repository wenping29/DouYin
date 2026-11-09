import 'package:flutter/material.dart';

import '../../core/theme.dart';
import '../search/search_page.dart';
import 'video_feed.dart';

class HomeFeedPage extends StatefulWidget {
  /// 当前底部 Tab 是否停在首页；离开首页时暂停视频。
  final bool active;

  const HomeFeedPage({super.key, this.active = true});

  @override
  State<HomeFeedPage> createState() => _HomeFeedPageState();
}

class _HomeFeedPageState extends State<HomeFeedPage> {
  int _tab = 1; // 0 关注，1 推荐

  @override
  Widget build(BuildContext context) {
    return Stack(
      children: [
        IndexedStack(
          index: _tab,
          children: [
            VideoFeed(tab: 'following', active: widget.active && _tab == 0),
            VideoFeed(tab: 'recommend', active: widget.active && _tab == 1),
          ],
        ),
        Positioned(
          top: 0,
          left: 0,
          right: 0,
          child: IgnorePointer(
            child: Container(
              height: 120,
              decoration: const BoxDecoration(
                gradient: LinearGradient(
                  begin: Alignment.topCenter,
                  end: Alignment.bottomCenter,
                  colors: [Color(0x99000000), Colors.transparent],
                ),
              ),
            ),
          ),
        ),
        Positioned(
          top: 0,
          left: 0,
          right: 0,
          child: SafeArea(
            bottom: false,
            child: SizedBox(
              height: 46,
              child: Stack(
                alignment: Alignment.center,
                children: [
                  Row(
                    mainAxisAlignment: MainAxisAlignment.center,
                    children: [
                      _TopTab(
                        text: '关注',
                        active: _tab == 0,
                        onTap: () => setState(() => _tab = 0),
                      ),
                      const SizedBox(width: 28),
                      _TopTab(
                        text: '推荐',
                        active: _tab == 1,
                        onTap: () => setState(() => _tab = 1),
                      ),
                    ],
                  ),
                  Positioned(
                    right: 4,
                    child: IconButton(
                      onPressed: () {
                        Navigator.of(context).push(
                          MaterialPageRoute(builder: (_) => const SearchPage()),
                        );
                      },
                      icon: const Icon(
                        Icons.search_rounded,
                        color: Colors.white,
                        size: 26,
                      ),
                      tooltip: '搜索',
                    ),
                  ),
                ],
              ),
            ),
          ),
        ),
      ],
    );
  }
}

class _TopTab extends StatelessWidget {
  final String text;
  final bool active;
  final VoidCallback onTap;

  const _TopTab({
    required this.text,
    required this.active,
    required this.onTap,
  });

  @override
  Widget build(BuildContext context) {
    return GestureDetector(
      onTap: onTap,
      behavior: HitTestBehavior.opaque,
      child: Column(
        mainAxisSize: MainAxisSize.min,
        children: [
          Text(
            text,
            style: TextStyle(
              fontSize: active ? 17 : 15,
              fontWeight: active ? FontWeight.w700 : FontWeight.w500,
              color: active ? Colors.white : Colors.white60,
              shadows: const [Shadow(blurRadius: 6, color: Colors.black54)],
            ),
          ),
          const SizedBox(height: 4),
          AnimatedContainer(
            duration: const Duration(milliseconds: 160),
            width: active ? 18 : 0,
            height: 2.5,
            decoration: BoxDecoration(
              color: AppColors.accent,
              borderRadius: BorderRadius.circular(2),
            ),
          ),
        ],
      ),
    );
  }
}
