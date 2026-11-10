import 'package:image_picker/image_picker.dart';
import 'package:video_player/video_player.dart';

import 'preview_controller_stub.dart'
    if (dart.library.io) 'preview_controller_io.dart'
    if (dart.library.html) 'preview_controller_web.dart'
    as platform;

VideoPlayerController createPreviewController(XFile file) =>
    platform.createPreviewController(file);
