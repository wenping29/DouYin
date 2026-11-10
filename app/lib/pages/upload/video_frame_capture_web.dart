// ignore_for_file: avoid_web_libraries_in_flutter, deprecated_member_use

import 'dart:convert';
import 'dart:html' as html;
import 'dart:math' as math;
import 'dart:typed_data';

import 'package:image_picker/image_picker.dart';

Future<Uint8List?> captureVideoFrame(XFile file, Duration position) async {
  final video = html.VideoElement()
    ..src = file.path
    ..muted = true
    ..preload = 'auto';
  video.load();
  await video.onLoadedMetadata.first.timeout(const Duration(seconds: 5));
  final duration = video.duration.isFinite ? video.duration : 0;
  final time = math.min(position.inMilliseconds / 1000, duration);
  if (time > 0) {
    final seeked = video.onSeeked.first;
    video.currentTime = time;
    await seeked.timeout(const Duration(seconds: 5));
  } else {
    await video.onLoadedData.first.timeout(const Duration(seconds: 5));
  }

  final sourceWidth = video.videoWidth;
  final sourceHeight = video.videoHeight;
  if (sourceWidth == 0 || sourceHeight == 0) return null;
  final scale = math.min(1, 640 / math.max(sourceWidth, sourceHeight));
  final width = (sourceWidth * scale).round();
  final height = (sourceHeight * scale).round();
  final canvas = html.CanvasElement(width: width, height: height);
  final context = canvas.context2D;
  context.drawImageScaled(video, 0, 0, width, height);
  final encoded = canvas.toDataUrl('image/png').split(',').last;
  video.pause();
  video.src = '';
  return base64Decode(encoded);
}
