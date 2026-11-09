import 'package:dio/dio.dart';
import 'package:image_picker/image_picker.dart';

import 'app_config.dart';

class ApiException implements Exception {
  final String message;
  final int? statusCode;

  ApiException(this.message, [this.statusCode]);

  @override
  String toString() => message;
}

class ApiClient {
  final Dio _dio;
  String? _token;

  ApiClient({String? baseUrl})
    : _dio = Dio(
        BaseOptions(
          baseUrl: baseUrl ?? AppConfig.baseUrl,
          connectTimeout: const Duration(seconds: 12),
          receiveTimeout: const Duration(seconds: 45),
          sendTimeout: const Duration(seconds: 120),
          headers: const {'Accept': 'application/json'},
        ),
      );

  String? get token => _token;

  set token(String? value) {
    _token = value;
    if (value == null || value.isEmpty) {
      _dio.options.headers.remove('Authorization');
    } else {
      _dio.options.headers['Authorization'] = 'Bearer $value';
    }
  }

  Map<String, dynamic> _asMap(dynamic data) {
    if (data is Map) return Map<String, dynamic>.from(data);
    return <String, dynamic>{};
  }

  Future<Map<String, dynamic>> _run(
    Future<Response<dynamic>> Function() request,
  ) async {
    try {
      final response = await request();
      return _asMap(response.data);
    } on DioException catch (error) {
      final data = error.response?.data;
      String message = '网络请求失败，请稍后重试';
      if (data is Map && data['error'] is Map) {
        message = '${data['error']['message'] ?? message}';
      } else if (error.type == DioExceptionType.connectionTimeout ||
          error.type == DioExceptionType.connectionError ||
          error.type == DioExceptionType.receiveTimeout ||
          error.type == DioExceptionType.sendTimeout) {
        message = '连接服务器失败，请确认后端已启动（${AppConfig.baseUrl}）';
      }
      throw ApiException(message, error.response?.statusCode);
    }
  }

  /* ---------------- 认证 ---------------- */

  Future<Map<String, dynamic>> login(String username, String password) {
    return _run(
      () => _dio.post(
        '/api/auth/login',
        data: {'username': username, 'password': password},
      ),
    );
  }

  Future<Map<String, dynamic>> register(
    String username,
    String password,
    String nickname,
  ) {
    return _run(
      () => _dio.post(
        '/api/auth/register',
        data: {
          'username': username,
          'password': password,
          'nickname': nickname,
        },
      ),
    );
  }

  Future<Map<String, dynamic>> me() => _run(() => _dio.get('/api/me'));

  Future<Map<String, dynamic>> updateProfile({String? nickname, String? bio}) {
    return _run(
      () => _dio.patch('/api/me', data: {'nickname': ?nickname, 'bio': ?bio}),
    );
  }

  Future<Map<String, dynamic>> uploadAvatar(XFile file) async {
    final form = FormData();
    form.files.add(
      MapEntry(
        'avatar',
        MultipartFile.fromBytes(
          await file.readAsBytes(),
          filename: _safeName(file, 'avatar.jpg'),
        ),
      ),
    );
    return _run(() => _dio.post('/api/me/avatar', data: form));
  }

  /* ---------------- 用户 ---------------- */

  Future<Map<String, dynamic>> user(int id) =>
      _run(() => _dio.get('/api/users/$id'));

  Future<Map<String, dynamic>> userVideos(
    int id, {
    String type = 'posts',
    String? cursor,
    int limit = 12,
  }) {
    return _run(
      () => _dio.get(
        '/api/users/$id/videos',
        queryParameters: {'type': type, 'limit': limit, 'cursor': ?cursor},
      ),
    );
  }

  Future<Map<String, dynamic>> follow(int id) =>
      _run(() => _dio.post('/api/users/$id/follow'));

  Future<Map<String, dynamic>> unfollow(int id) =>
      _run(() => _dio.delete('/api/users/$id/follow'));

  Future<Map<String, dynamic>> suggestedUsers({int limit = 8}) {
    return _run(
      () => _dio.get('/api/users/suggested', queryParameters: {'limit': limit}),
    );
  }

  /* ---------------- 视频流 ---------------- */

  Future<Map<String, dynamic>> feed({
    String tab = 'recommend',
    String? cursor,
    int limit = 8,
  }) {
    return _run(
      () => _dio.get(
        '/api/feed',
        queryParameters: {'tab': tab, 'limit': limit, 'cursor': ?cursor},
      ),
    );
  }

  Future<Map<String, dynamic>> video(int id) =>
      _run(() => _dio.get('/api/videos/$id'));

  Future<Map<String, dynamic>> uploadVideo({
    required XFile video,
    XFile? cover,
    required String title,
    String description = '',
    double? duration,
    void Function(double progress)? onProgress,
  }) async {
    final form = FormData();
    form.files.add(
      MapEntry(
        'video',
        MultipartFile.fromBytes(
          await video.readAsBytes(),
          filename: _safeName(video, 'video.mp4'),
        ),
      ),
    );
    if (cover != null) {
      form.files.add(
        MapEntry(
          'cover',
          MultipartFile.fromBytes(
            await cover.readAsBytes(),
            filename: _safeName(cover, 'cover.jpg'),
          ),
        ),
      );
    }
    form.fields.add(MapEntry('title', title));
    form.fields.add(MapEntry('description', description));
    if (duration != null && duration > 0) {
      form.fields.add(MapEntry('duration', duration.toStringAsFixed(2)));
    }

    return _run(
      () => _dio.post(
        '/api/videos',
        data: form,
        onSendProgress: (sent, total) {
          if (total > 0) onProgress?.call(sent / total);
        },
      ),
    );
  }

  Future<Map<String, dynamic>> deleteVideo(int id) =>
      _run(() => _dio.delete('/api/videos/$id'));

  Future<Map<String, dynamic>> likeVideo(int id) =>
      _run(() => _dio.post('/api/videos/$id/like'));

  Future<Map<String, dynamic>> unlikeVideo(int id) =>
      _run(() => _dio.delete('/api/videos/$id/like'));

  Future<Map<String, dynamic>> viewVideo(int id) =>
      _run(() => _dio.post('/api/videos/$id/view'));

  Future<Map<String, dynamic>> shareVideo(int id) =>
      _run(() => _dio.post('/api/videos/$id/share'));

  /* ---------------- 评论 ---------------- */

  Future<Map<String, dynamic>> comments(
    int videoId, {
    String? cursor,
    int limit = 20,
  }) {
    return _run(
      () => _dio.get(
        '/api/videos/$videoId/comments',
        queryParameters: {'limit': limit, 'cursor': ?cursor},
      ),
    );
  }

  Future<Map<String, dynamic>> postComment(int videoId, String content) {
    return _run(
      () => _dio.post(
        '/api/videos/$videoId/comments',
        data: {'content': content},
      ),
    );
  }

  Future<Map<String, dynamic>> deleteComment(int id) =>
      _run(() => _dio.delete('/api/comments/$id'));

  /* ---------------- 搜索 ---------------- */

  Future<Map<String, dynamic>> search(String q, {String type = 'all'}) {
    return _run(
      () => _dio.get('/api/search', queryParameters: {'q': q, 'type': type}),
    );
  }

  String _safeName(XFile file, String fallback) {
    final name = file.name.trim().isEmpty ? fallback : file.name.trim();
    if (name.contains('.')) return name;
    final mime = file.mimeType ?? '';
    if (fallback.endsWith('.mp4') || mime.contains('video')) return '$name.mp4';
    if (mime.contains('png')) return '$name.png';
    if (mime.contains('webp')) return '$name.webp';
    return '$name.jpg';
  }
}
