import 'package:flutter/foundation.dart';
import 'package:shared_preferences/shared_preferences.dart';

import '../models/user.dart';
import 'api_client.dart';

class AppState extends ChangeNotifier {
  final ApiClient api;
  static const _tokenKey = 'douying_token';

  SharedPreferences? _prefs;
  User? me;
  bool booting = true;
  int dataVersion = 0;

  AppState(this.api);

  bool get isLoggedIn => me != null;

  Future<void> boot() async {
    _prefs = await SharedPreferences.getInstance();
    final token = _prefs?.getString(_tokenKey);
    if (token != null && token.isNotEmpty) {
      api.token = token;
      try {
        final data = await api.me();
        me = User.fromJson((data['user'] as Map).cast<String, dynamic>());
      } catch (_) {
        await _clearToken();
      }
    }
    booting = false;
    notifyListeners();
  }

  Future<void> login(String username, String password) async {
    final data = await api.login(username, password);
    await _applyAuth(data);
  }

  Future<void> register(
    String username,
    String password,
    String nickname,
  ) async {
    final data = await api.register(username, password, nickname);
    await _applyAuth(data);
  }

  Future<void> _applyAuth(Map<String, dynamic> data) async {
    final token = data['token']?.toString();
    final user = data['user'];
    if (token == null || token.isEmpty || user is! Map) {
      throw ApiException('登录响应异常，请稍后重试');
    }
    _prefs ??= await SharedPreferences.getInstance();
    await _prefs?.setString(_tokenKey, token);
    api.token = token;
    me = User.fromJson(user.cast<String, dynamic>());
    notifyListeners();
  }

  Future<void> refreshMe() async {
    if (api.token == null) return;
    final data = await api.me();
    me = User.fromJson((data['user'] as Map).cast<String, dynamic>());
    notifyListeners();
  }

  void setUser(User user) {
    me = user;
    notifyListeners();
  }

  Future<void> logout() async {
    await _clearToken();
    me = null;
    notifyListeners();
  }

  Future<void> _clearToken() async {
    api.token = null;
    _prefs ??= await SharedPreferences.getInstance();
    await _prefs?.remove(_tokenKey);
  }

  /// 发布/上传后通知主页等页面刷新
  void bumpData() {
    dataVersion += 1;
    notifyListeners();
  }
}
