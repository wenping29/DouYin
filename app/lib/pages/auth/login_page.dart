import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../../core/api_client.dart';
import '../../core/app_state.dart';
import '../../core/theme.dart';

class LoginPage extends StatefulWidget {
  const LoginPage({super.key});

  @override
  State<LoginPage> createState() => _LoginPageState();
}

class _LoginPageState extends State<LoginPage> {
  final _formKey = GlobalKey<FormState>();
  final _username = TextEditingController();
  final _password = TextEditingController();
  final _nickname = TextEditingController();
  bool _register = false;
  bool _loading = false;
  bool _obscure = true;

  @override
  void dispose() {
    _username.dispose();
    _password.dispose();
    _nickname.dispose();
    super.dispose();
  }

  Future<void> _submit() async {
    if (!_formKey.currentState!.validate()) return;
    FocusScope.of(context).unfocus();
    setState(() => _loading = true);
    final app = context.read<AppState>();
    try {
      if (_register) {
        await app.register(
          _username.text.trim(),
          _password.text,
          _nickname.text.trim().isEmpty
              ? _username.text.trim()
              : _nickname.text.trim(),
        );
      } else {
        await app.login(_username.text.trim(), _password.text);
      }
    } on ApiException catch (error) {
      if (!mounted) return;
      ScaffoldMessenger.of(
        context,
      ).showSnackBar(SnackBar(content: Text(error.message)));
    } catch (_) {
      if (!mounted) return;
      ScaffoldMessenger.of(
        context,
      ).showSnackBar(const SnackBar(content: Text('登录失败，请稍后重试')));
    } finally {
      if (mounted) {
        setState(() => _loading = false);
      }
    }
  }

  void _fillDemo() {
    _username.text = 'aze';
    _password.text = 'demo1234';
    setState(() => _register = false);
  }

  @override
  Widget build(BuildContext context) {
    final size = MediaQuery.sizeOf(context);
    return Scaffold(
      body: Container(
        decoration: const BoxDecoration(
          gradient: LinearGradient(
            begin: Alignment.topCenter,
            end: Alignment.bottomCenter,
            colors: [Color(0xFF1A0710), Colors.black, Colors.black],
          ),
        ),
        child: Stack(
          children: [
            Positioned(
              top: -size.width * 0.35,
              right: -size.width * 0.3,
              child: _Glow(
                color: AppColors.accent.withValues(alpha: 0.22),
                size: size.width * 0.9,
              ),
            ),
            Positioned(
              top: size.height * 0.22,
              left: -size.width * 0.45,
              child: _Glow(
                color: AppColors.cyan.withValues(alpha: 0.10),
                size: size.width * 0.9,
              ),
            ),
            SafeArea(
              child: Center(
                child: SingleChildScrollView(
                  padding: const EdgeInsets.symmetric(
                    horizontal: 28,
                    vertical: 24,
                  ),
                  child: ConstrainedBox(
                    constraints: const BoxConstraints(maxWidth: 420),
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.stretch,
                      children: [
                        const SizedBox(height: 12),
                        Center(
                          child: Container(
                            width: 74,
                            height: 74,
                            decoration: BoxDecoration(
                              borderRadius: BorderRadius.circular(22),
                              gradient: const LinearGradient(
                                begin: Alignment.topLeft,
                                end: Alignment.bottomRight,
                                colors: [
                                  AppColors.cyan,
                                  AppColors.accent,
                                  Color(0xFF7B2FF7),
                                ],
                              ),
                              boxShadow: [
                                BoxShadow(
                                  color: AppColors.accent.withValues(
                                    alpha: 0.35,
                                  ),
                                  blurRadius: 30,
                                  spreadRadius: 4,
                                ),
                              ],
                            ),
                            child: const Icon(
                              Icons.play_arrow_rounded,
                              color: Colors.white,
                              size: 48,
                            ),
                          ),
                        ),
                        const SizedBox(height: 20),
                        const Text(
                          '抖影',
                          textAlign: TextAlign.center,
                          style: TextStyle(
                            fontSize: 36,
                            fontWeight: FontWeight.w800,
                            letterSpacing: 3,
                          ),
                        ),
                        const SizedBox(height: 8),
                        const Text(
                          '记录每一个值得被看见的瞬间',
                          textAlign: TextAlign.center,
                          style: TextStyle(
                            color: AppColors.muted,
                            fontSize: 13,
                            letterSpacing: 0.5,
                          ),
                        ),
                        const SizedBox(height: 40),
                        Row(
                          mainAxisAlignment: MainAxisAlignment.center,
                          children: [
                            _TabLabel(
                              text: '登录',
                              active: !_register,
                              onTap: () => setState(() => _register = false),
                            ),
                            const SizedBox(width: 34),
                            _TabLabel(
                              text: '注册',
                              active: _register,
                              onTap: () => setState(() => _register = true),
                            ),
                          ],
                        ),
                        const SizedBox(height: 24),
                        Form(
                          key: _formKey,
                          child: Column(
                            children: [
                              TextFormField(
                                controller: _username,
                                textInputAction: TextInputAction.next,
                                autocorrect: false,
                                decoration: const InputDecoration(
                                  hintText: '用户名',
                                  prefixIcon: Icon(
                                    Icons.person_outline,
                                    color: AppColors.muted,
                                  ),
                                ),
                                validator: (value) {
                                  final text = value?.trim() ?? '';
                                  if (text.length < 3) return '用户名至少 3 位';
                                  return null;
                                },
                              ),
                              const SizedBox(height: 14),
                              TextFormField(
                                controller: _password,
                                obscureText: _obscure,
                                textInputAction: TextInputAction.done,
                                onFieldSubmitted: (_) => _submit(),
                                decoration: InputDecoration(
                                  hintText: '密码',
                                  prefixIcon: const Icon(
                                    Icons.lock_outline,
                                    color: AppColors.muted,
                                  ),
                                  suffixIcon: IconButton(
                                    onPressed: () =>
                                        setState(() => _obscure = !_obscure),
                                    icon: Icon(
                                      _obscure
                                          ? Icons.visibility_off_outlined
                                          : Icons.visibility_outlined,
                                      color: AppColors.muted,
                                      size: 20,
                                    ),
                                  ),
                                ),
                                validator: (value) {
                                  if ((value ?? '').length < 6) {
                                    return '密码至少 6 位';
                                  }
                                  return null;
                                },
                              ),
                              AnimatedSize(
                                duration: const Duration(milliseconds: 180),
                                alignment: Alignment.topCenter,
                                child: _register
                                    ? Padding(
                                        padding: const EdgeInsets.only(top: 14),
                                        child: TextFormField(
                                          controller: _nickname,
                                          maxLength: 20,
                                          decoration: const InputDecoration(
                                            hintText: '昵称（选填）',
                                            counterText: '',
                                            prefixIcon: Icon(
                                              Icons.badge_outlined,
                                              color: AppColors.muted,
                                            ),
                                          ),
                                        ),
                                      )
                                    : const SizedBox.shrink(),
                              ),
                            ],
                          ),
                        ),
                        const SizedBox(height: 22),
                        SizedBox(
                          height: 50,
                          child: FilledButton(
                            onPressed: _loading ? null : _submit,
                            style: FilledButton.styleFrom(
                              backgroundColor: AppColors.accent,
                              disabledBackgroundColor: AppColors.accent
                                  .withValues(alpha: 0.5),
                              foregroundColor: Colors.white,
                              shape: RoundedRectangleBorder(
                                borderRadius: BorderRadius.circular(25),
                              ),
                              textStyle: const TextStyle(
                                fontSize: 16,
                                fontWeight: FontWeight.w600,
                              ),
                            ),
                            child: _loading
                                ? const SizedBox(
                                    width: 20,
                                    height: 20,
                                    child: CircularProgressIndicator(
                                      strokeWidth: 2,
                                      color: Colors.white,
                                    ),
                                  )
                                : Text(_register ? '创建账号' : '登录'),
                          ),
                        ),
                        const SizedBox(height: 14),
                        TextButton(
                          onPressed: _loading ? null : _fillDemo,
                          child: const Text(
                            '使用演示账号  aze / demo1234',
                            style: TextStyle(
                              color: AppColors.muted,
                              fontSize: 13,
                            ),
                          ),
                        ),
                        const SizedBox(height: 28),
                        const Text(
                          '继续即代表同意《用户协议》与《隐私政策》\n当前为本地学习演示版本，请勿上传违规内容',
                          textAlign: TextAlign.center,
                          style: TextStyle(
                            color: Color(0xFF5A5A5E),
                            fontSize: 11,
                            height: 1.7,
                          ),
                        ),
                      ],
                    ),
                  ),
                ),
              ),
            ),
          ],
        ),
      ),
    );
  }
}

class _TabLabel extends StatelessWidget {
  final String text;
  final bool active;
  final VoidCallback onTap;

  const _TabLabel({
    required this.text,
    required this.active,
    required this.onTap,
  });

  @override
  Widget build(BuildContext context) {
    return GestureDetector(
      onTap: onTap,
      behavior: HitTestBehavior.opaque,
      child: Column(
        children: [
          Text(
            text,
            style: TextStyle(
              fontSize: 17,
              fontWeight: active ? FontWeight.w700 : FontWeight.w500,
              color: active ? Colors.white : AppColors.muted,
            ),
          ),
          const SizedBox(height: 6),
          AnimatedContainer(
            duration: const Duration(milliseconds: 160),
            width: active ? 22 : 0,
            height: 3,
            decoration: BoxDecoration(
              color: AppColors.accent,
              borderRadius: BorderRadius.circular(2),
            ),
          ),
        ],
      ),
    );
  }
}

class _Glow extends StatelessWidget {
  final Color color;
  final double size;

  const _Glow({required this.color, required this.size});

  @override
  Widget build(BuildContext context) {
    return IgnorePointer(
      child: Container(
        width: size,
        height: size,
        decoration: BoxDecoration(
          shape: BoxShape.circle,
          gradient: RadialGradient(colors: [color, color.withValues(alpha: 0)]),
        ),
      ),
    );
  }
}
