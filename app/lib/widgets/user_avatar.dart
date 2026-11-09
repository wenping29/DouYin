import 'package:cached_network_image/cached_network_image.dart';
import 'package:flutter/material.dart';

import '../core/app_config.dart';
import '../core/theme.dart';

class UserAvatar extends StatelessWidget {
  final String? url;
  final String name;
  final double size;
  final VoidCallback? onTap;

  const UserAvatar({
    super.key,
    required this.url,
    required this.name,
    this.size = 40,
    this.onTap,
  });

  @override
  Widget build(BuildContext context) {
    final initial = name.trim().isEmpty ? '?' : name.trim().substring(0, 1);
    final resolved = AppConfig.absolute(url);
    final avatar = Container(
      width: size,
      height: size,
      decoration: const BoxDecoration(
        shape: BoxShape.circle,
        gradient: LinearGradient(
          begin: Alignment.topLeft,
          end: Alignment.bottomRight,
          colors: [AppColors.accent, Color(0xFF7B2FF7)],
        ),
      ),
      clipBehavior: Clip.antiAlias,
      alignment: Alignment.center,
      child: resolved == null
          ? Text(
              initial,
              style: TextStyle(
                color: Colors.white,
                fontSize: size * 0.38,
                fontWeight: FontWeight.w600,
              ),
            )
          : CachedNetworkImage(
              imageUrl: resolved,
              width: size,
              height: size,
              fit: BoxFit.cover,
              fadeInDuration: const Duration(milliseconds: 120),
              errorWidget: (_, _, _) => Center(
                child: Text(
                  initial,
                  style: TextStyle(
                    color: Colors.white,
                    fontSize: size * 0.38,
                    fontWeight: FontWeight.w600,
                  ),
                ),
              ),
            ),
    );

    final wrapped = Container(
      padding: const EdgeInsets.all(1.5),
      decoration: BoxDecoration(
        shape: BoxShape.circle,
        border: Border.all(color: Colors.white24, width: 1),
      ),
      child: avatar,
    );

    if (onTap == null) return wrapped;
    return GestureDetector(onTap: onTap, child: wrapped);
  }
}
