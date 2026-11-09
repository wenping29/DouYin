import 'package:cached_network_image/cached_network_image.dart';
import 'package:flutter/material.dart';

import '../core/app_config.dart';
import '../core/theme.dart';

class CoverThumb extends StatelessWidget {
  final String? coverUrl;
  final int seed;
  final String? label;
  final IconData? icon;

  const CoverThumb({
    super.key,
    required this.coverUrl,
    required this.seed,
    this.label,
    this.icon,
  });

  @override
  Widget build(BuildContext context) {
    final resolved = AppConfig.absolute(coverUrl);
    return Stack(
      fit: StackFit.expand,
      children: [
        if (resolved == null)
          _gradient()
        else
          CachedNetworkImage(
            imageUrl: resolved,
            fit: BoxFit.cover,
            fadeInDuration: const Duration(milliseconds: 120),
            placeholder: (_, _) => _gradient(),
            errorWidget: (_, _, _) => _gradient(),
          ),
        const DecoratedBox(
          decoration: BoxDecoration(
            gradient: LinearGradient(
              begin: Alignment.topCenter,
              end: Alignment.bottomCenter,
              colors: [Colors.transparent, Colors.black54],
              stops: [0.55, 1],
            ),
          ),
        ),
        if (icon != null)
          Center(child: Icon(icon, color: Colors.white70, size: 34)),
        if (label != null)
          Positioned(
            left: 6,
            bottom: 6,
            child: Row(
              children: [
                const Icon(
                  Icons.play_arrow_rounded,
                  color: Colors.white,
                  size: 15,
                ),
                const SizedBox(width: 1),
                Text(
                  label!,
                  style: const TextStyle(
                    color: Colors.white,
                    fontSize: 11,
                    fontWeight: FontWeight.w500,
                    shadows: [Shadow(blurRadius: 4, color: Colors.black54)],
                  ),
                ),
              ],
            ),
          ),
      ],
    );
  }

  Widget _gradient() {
    final hue = (seed * 47) % 360;
    return DecoratedBox(
      decoration: BoxDecoration(
        gradient: LinearGradient(
          begin: Alignment.topLeft,
          end: Alignment.bottomRight,
          colors: [
            HSLColor.fromAHSL(1, hue.toDouble(), 0.55, 0.32).toColor(),
            HSLColor.fromAHSL(1, (hue + 55) % 360, 0.6, 0.16).toColor(),
          ],
        ),
      ),
      child: const Center(
        child: Icon(
          Icons.play_circle_outline_rounded,
          color: AppColors.muted,
          size: 30,
        ),
      ),
    );
  }
}
