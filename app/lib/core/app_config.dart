import 'package:flutter/foundation.dart';

/// 全局环境配置。
///
/// 真机调试时用 --dart-define 覆盖，例如：
/// flutter run --dart-define=API_BASE_URL=http://192.168.1.10:8080
class AppConfig {
  static const _envUrl = String.fromEnvironment('API_BASE_URL');

  static String get baseUrl {
    if (_envUrl.isNotEmpty) return _envUrl;
    if (kIsWeb) return 'http://127.0.0.1:8080';
    if (defaultTargetPlatform == TargetPlatform.android) {
      // Android 模拟器访问宿主机
      return 'http://10.0.2.2:8080';
    }
    return 'http://127.0.0.1:8080';
  }

  static String? absolute(String? path) {
    if (path == null || path.isEmpty) return null;
    if (path.startsWith('http://') || path.startsWith('https://')) return path;
    if (path.startsWith('/')) return '$baseUrl$path';
    return '$baseUrl/$path';
  }
}
