import 'package:flutter/material.dart';

import '../core/theme.dart';
import 'camera/camera_page.dart';
import 'home/home_feed_page.dart';
import 'notifications_page.dart';
import 'profile/profile_page.dart';
import 'search/search_page.dart';

class RootShell extends StatefulWidget {
  const RootShell({super.key});

  @override
  State<RootShell> createState() => _RootShellState();
}

class _RootShellState extends State<RootShell> {
  int _index = 0;

  int get _stackIndex => _index <= 1 ? _index : _index - 1;

  Future<void> _openUpload() async {
    final published = await Navigator.of(
      context,
    ).push<bool>(MaterialPageRoute(builder: (_) => const CameraPage()));
    if (published == true && mounted) {
      setState(() => _index = 4);
    }
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      backgroundColor: Colors.black,
      body: IndexedStack(
        index: _stackIndex,
        children: [
          HomeFeedPage(active: _index == 0),
          const SearchPage(),
          const NotificationsPage(),
          const ProfilePage(),
        ],
      ),
      bottomNavigationBar: Container(
        decoration: const BoxDecoration(
          color: Colors.black,
          border: Border(top: BorderSide(color: Colors.white10)),
        ),
        child: SafeArea(
          top: false,
          child: SizedBox(
            height: 54,
            child: Row(
              mainAxisAlignment: MainAxisAlignment.spaceAround,
              children: [
                _NavItem(
                  icon: Icons.home_filled,
                  label: '首页',
                  active: _index == 0,
                  onTap: () => setState(() => _index = 0),
                ),
                _NavItem(
                  icon: Icons.search_rounded,
                  label: '发现',
                  active: _index == 1,
                  onTap: () => setState(() => _index = 1),
                ),
                _AddButton(onTap: _openUpload),
                _NavItem(
                  icon: Icons.chat_bubble_outline_rounded,
                  label: '消息',
                  active: _index == 3,
                  onTap: () => setState(() => _index = 3),
                ),
                _NavItem(
                  icon: Icons.person_outline_rounded,
                  label: '我',
                  active: _index == 4,
                  onTap: () => setState(() => _index = 4),
                ),
              ],
            ),
          ),
        ),
      ),
    );
  }
}

class _NavItem extends StatelessWidget {
  final IconData icon;
  final String label;
  final bool active;
  final VoidCallback onTap;

  const _NavItem({
    required this.icon,
    required this.label,
    required this.active,
    required this.onTap,
  });

  @override
  Widget build(BuildContext context) {
    final color = active ? Colors.white : AppColors.muted;
    return GestureDetector(
      onTap: onTap,
      behavior: HitTestBehavior.opaque,
      child: SizedBox(
        width: 58,
        height: 52,
        child: Column(
          mainAxisAlignment: MainAxisAlignment.center,
          children: [
            Icon(icon, color: color, size: 25),
            const SizedBox(height: 3),
            Text(
              label,
              style: TextStyle(
                color: color,
                fontSize: 10,
                fontWeight: active ? FontWeight.w600 : FontWeight.w400,
              ),
            ),
          ],
        ),
      ),
    );
  }
}

class _AddButton extends StatelessWidget {
  final VoidCallback onTap;

  const _AddButton({required this.onTap});

  @override
  Widget build(BuildContext context) {
    return GestureDetector(
      onTap: onTap,
      behavior: HitTestBehavior.opaque,
      child: SizedBox(
        width: 58,
        height: 52,
        child: Center(
          child: SizedBox(
            width: 46,
            height: 31,
            child: Stack(
              children: [
                Positioned(
                  left: 4,
                  top: 0,
                  child: Container(
                    width: 42,
                    height: 29,
                    decoration: BoxDecoration(
                      color: AppColors.cyan,
                      borderRadius: BorderRadius.circular(9),
                    ),
                  ),
                ),
                Positioned(
                  right: 0,
                  bottom: 0,
                  child: Container(
                    width: 42,
                    height: 29,
                    decoration: BoxDecoration(
                      color: AppColors.accent,
                      borderRadius: BorderRadius.circular(9),
                    ),
                  ),
                ),
                Positioned(
                  left: 2,
                  top: 1,
                  child: Container(
                    width: 42,
                    height: 29,
                    decoration: BoxDecoration(
                      color: Colors.white,
                      borderRadius: BorderRadius.circular(9),
                    ),
                    child: const Icon(
                      Icons.add_rounded,
                      color: Colors.black,
                      size: 23,
                    ),
                  ),
                ),
              ],
            ),
          ),
        ),
      ),
    );
  }
}
