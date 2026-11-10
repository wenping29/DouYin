import 'dart:typed_data';

import 'package:image_picker/image_picker.dart';

import 'video_frame_capture_stub.dart'
    if (dart.library.html) 'video_frame_capture_web.dart'
    as platform;

Future<Uint8List?> captureVideoFrame(XFile file, Duration position) =>
    platform.captureVideoFrame(file, position);
