import 'package:flutter/material.dart';

import '../models/video.dart';
import 'home/video_card.dart';

class VideoDetailPage extends StatefulWidget {
  final Video video;

  const VideoDetailPage({super.key, required this.video});

  @override
  State<VideoDetailPage> createState() => _VideoDetailPageState();
}

class _VideoDetailPageState extends State<VideoDetailPage> {
  late Video _video = widget.video;

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      backgroundColor: Colors.black,
      body: Stack(
        children: [
          VideoCard(
            video: _video,
            active: true,
            onChanged: (video) => setState(() => _video = video),
          ),
          Positioned(
            top: 0,
            left: 0,
            child: SafeArea(
              child: IconButton(
                onPressed: () => Navigator.pop(context),
                icon: const Icon(
                  Icons.arrow_back_ios_new_rounded,
                  color: Colors.white,
                  size: 22,
                  shadows: [Shadow(blurRadius: 8, color: Colors.black54)],
                ),
                tooltip: '返回',
              ),
            ),
          ),
        ],
      ),
    );
  }
}
