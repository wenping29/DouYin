import 'dart:typed_data';

import 'package:flutter/material.dart';
import 'package:image_picker/image_picker.dart';
import 'package:provider/provider.dart';

import '../../core/api_client.dart';
import '../../core/app_state.dart';
import '../../core/theme.dart';
import '../../models/user.dart';
import '../../widgets/user_avatar.dart';

class EditProfilePage extends StatefulWidget {
  const EditProfilePage({super.key});

  @override
  State<EditProfilePage> createState() => _EditProfilePageState();
}

class _EditProfilePageState extends State<EditProfilePage> {
  late final AppState _app;
  late final TextEditingController _nickname;
  late final TextEditingController _bio;
  XFile? _avatar;
  Uint8List? _avatarBytes;
  bool _saving = false;

  @override
  void initState() {
    super.initState();
    _app = context.read<AppState>();
    final me = _app.me;
    _nickname = TextEditingController(text: me?.nickname ?? '');
    _bio = TextEditingController(text: me?.bio ?? '');
  }

  @override
  void dispose() {
    _nickname.dispose();
    _bio.dispose();
    super.dispose();
  }

  Future<void> _pickAvatar() async {
    try {
      final file = await ImagePicker().pickImage(
        source: ImageSource.gallery,
        maxWidth: 800,
        imageQuality: 86,
      );
      if (file == null) return;
      final bytes = await file.readAsBytes();
      if (!mounted) return;
      setState(() {
        _avatar = file;
        _avatarBytes = bytes;
      });
    } catch (_) {
      if (mounted) {
        ScaffoldMessenger.of(
          context,
        ).showSnackBar(const SnackBar(content: Text('无法打开相册，请检查权限')));
      }
    }
  }

  Future<void> _save() async {
    if (_saving) return;
    final nickname = _nickname.text.trim();
    if (nickname.isEmpty) {
      ScaffoldMessenger.of(
        context,
      ).showSnackBar(const SnackBar(content: Text('昵称不能为空')));
      return;
    }
    setState(() => _saving = true);
    try {
      var data = await _app.api.updateProfile(
        nickname: nickname,
        bio: _bio.text.trim(),
      );
      if (_avatar != null) {
        data = await _app.api.uploadAvatar(_avatar!);
      }
      final user = User.fromJson((data['user'] as Map).cast<String, dynamic>());
      _app.setUser(user);
      if (!mounted) return;
      ScaffoldMessenger.of(
        context,
      ).showSnackBar(const SnackBar(content: Text('资料已更新')));
      Navigator.of(context).pop(true);
    } catch (error) {
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(
            content: Text(error is ApiException ? error.message : '保存失败'),
          ),
        );
      }
    } finally {
      if (mounted) setState(() => _saving = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final me = _app.me;
    return Scaffold(
      appBar: AppBar(
        title: const Text('编辑资料'),
        actions: [
          TextButton(
            onPressed: _saving ? null : _save,
            child: Text(
              _saving ? '保存中…' : '保存',
              style: const TextStyle(color: AppColors.accent, fontSize: 15),
            ),
          ),
        ],
      ),
      body: ListView(
        padding: const EdgeInsets.fromLTRB(20, 20, 20, 30),
        children: [
          Center(
            child: GestureDetector(
              onTap: _saving ? null : _pickAvatar,
              child: Stack(
                children: [
                  if (_avatarBytes != null)
                    ClipOval(
                      child: Image.memory(
                        _avatarBytes!,
                        width: 88,
                        height: 88,
                        fit: BoxFit.cover,
                      ),
                    )
                  else
                    UserAvatar(
                      url: me?.avatarUrl,
                      name: me?.nickname ?? '',
                      size: 88,
                    ),
                  Positioned(
                    right: 0,
                    bottom: 0,
                    child: Container(
                      width: 28,
                      height: 28,
                      decoration: BoxDecoration(
                        color: AppColors.accent,
                        shape: BoxShape.circle,
                        border: Border.all(color: Colors.black, width: 2),
                      ),
                      child: const Icon(
                        Icons.camera_alt_outlined,
                        color: Colors.white,
                        size: 15,
                      ),
                    ),
                  ),
                ],
              ),
            ),
          ),
          const SizedBox(height: 10),
          const Center(
            child: Text(
              '点击更换头像',
              style: TextStyle(color: AppColors.muted, fontSize: 12),
            ),
          ),
          const SizedBox(height: 30),
          const Text(
            '昵称',
            style: TextStyle(fontSize: 13, color: AppColors.muted),
          ),
          const SizedBox(height: 8),
          TextField(
            controller: _nickname,
            maxLength: 20,
            decoration: const InputDecoration(
              counterText: '',
              hintText: '你的昵称',
            ),
          ),
          const SizedBox(height: 18),
          const Text(
            '简介',
            style: TextStyle(fontSize: 13, color: AppColors.muted),
          ),
          const SizedBox(height: 8),
          TextField(
            controller: _bio,
            maxLines: 4,
            maxLength: 80,
            decoration: const InputDecoration(
              counterText: '',
              hintText: '介绍一下自己（选填）',
            ),
          ),
          const SizedBox(height: 26),
          const Text(
            '头像与昵称会展示在你的作品和评论中。\n请勿使用违规、侵权或冒充他人的信息。',
            style: TextStyle(
              color: Color(0xFF5A5A5E),
              fontSize: 12,
              height: 1.7,
            ),
          ),
        ],
      ),
    );
  }
}
