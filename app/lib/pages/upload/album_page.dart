import 'dart:async';
import 'dart:typed_data';

import 'package:flutter/material.dart';
import 'package:image_picker/image_picker.dart';
import 'package:photo_manager/photo_manager.dart';

import '../../core/theme.dart';
import 'edit_page.dart';
import 'publish_page.dart';

class AlbumPage extends StatefulWidget {
  const AlbumPage({super.key});

  @override
  State<AlbumPage> createState() => _AlbumPageState();
}

class _AlbumPageState extends State<AlbumPage> {
  static const int _maxSelect = 12;

  bool _loading = true;
  bool _denied = false;
  List<AssetEntity> _assets = [];
  final List<AssetEntity> _selected = [];
  int _category = 0;
  final _query = TextEditingController();

  static const _categories = ['全部', '视频', '图片', '动图', '人像'];
  static const _requestTypes = [
    RequestType.common,
    RequestType.video,
    RequestType.image,
    RequestType.common, // 动图：演示中归并到全部
    RequestType.common, // 人像：演示中归并到全部
  ];

  @override
  void initState() {
    super.initState();
    _loadAssets();
  }

  @override
  void dispose() {
    _query.dispose();
    super.dispose();
  }

  Future<void> _loadAssets() async {
    setState(() => _loading = true);
    try {
      final state = await PhotoManager.requestPermissionExtend();
      final granted =
          state == PermissionState.authorized || state == PermissionState.limited;
      if (!granted) {
        if (mounted) setState(() => _denied = true);
        return;
      }
      final paths = await PhotoManager.getAssetPathList(
        type: _requestTypes[_category],
      );
      if (paths.isEmpty) {
        if (mounted) setState(() => _assets = []);
        return;
      }
      // 优先取“最近/全部”合集，否则取首个相册
      final path =
          paths.where((p) => p.isAll).firstOrNull ?? paths.first;
      final list = await path.getAssetListPaged(page: 0, size: 90);
      if (mounted) setState(() => _assets = list);
    } catch (e) {
      if (mounted) _toast('加载相册失败：$e');
    } finally {
      if (mounted) setState(() => _loading = false);
    }
  }

  void _toast(String msg) {
    ScaffoldMessenger.of(context).showSnackBar(
      SnackBar(content: Text(msg), duration: const Duration(seconds: 1)),
    );
  }

  void _toggle(AssetEntity entity) {
    setState(() {
      final idx = _selected.indexOf(entity);
      if (idx >= 0) {
        _selected.removeAt(idx);
      } else {
        if (_selected.length >= _maxSelect) {
          _toast('最多选择 $_maxSelect 个媒体');
          return;
        }
        _selected.add(entity);
      }
    });
  }

  void _toggleSelectAll() {
    setState(() {
      if (_selected.length == _assets.length) {
        _selected.clear();
      } else {
        _selected
          ..clear()
          ..addAll(_assets.take(_maxSelect));
        if (_assets.length > _maxSelect) _toast('最多选择 $_maxSelect 个媒体');
      }
    });
  }

  Future<void> _goNext(bool edit) async {
    if (_selected.isEmpty) return;
    _toast('正在准备 ${_selected.length} 个媒体…');
    final files = <XFile>[];
    for (final e in _selected) {
      final f = await e.file;
      if (f != null) files.add(XFile(f.path));
    }
    if (!mounted) return;
    if (files.isEmpty) {
      _toast('所选媒体读取失败');
      return;
    }
    if (edit) {
      Navigator.of(
        context,
      ).push(MaterialPageRoute(builder: (_) => EditPage(videos: files)));
    } else {
      Navigator.of(
        context,
      ).push(MaterialPageRoute(builder: (_) => PublishPage(videos: files)));
    }
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      backgroundColor: Colors.white,
      appBar: AppBar(
        backgroundColor: Colors.white,
        foregroundColor: Colors.black,
        elevation: 0,
        leading: IconButton(
          icon: const Icon(Icons.close),
          onPressed: () => Navigator.of(context).pop(),
        ),
        title: Row(
          mainAxisSize: MainAxisSize.min,
          children: [
            GestureDetector(
              onTap: () => _toast('切换相册（敬请期待）'),
              child: const Row(
                children: [
                  Text(
                    '最近项目',
                    style: TextStyle(fontWeight: FontWeight.w700, fontSize: 16),
                  ),
                  Icon(Icons.arrow_drop_down, size: 20),
                ],
              ),
            ),
            const SizedBox(width: 10),
            const Text(
              '往日回忆',
              style: TextStyle(color: Colors.grey, fontSize: 13),
            ),
          ],
        ),
        centerTitle: true,
        actions: [
          GestureDetector(
            onTap: () => _toast('AI 形象（敬请期待）'),
            child: Container(
              margin: const EdgeInsets.only(right: 12),
              padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 6),
              decoration: BoxDecoration(
                color: Colors.grey.shade200,
                borderRadius: BorderRadius.circular(16),
              ),
              child: const Row(
                children: [
                  Icon(Icons.person_outline, size: 16),
                  SizedBox(width: 4),
                  Text('AI 形象', style: TextStyle(fontSize: 13)),
                ],
              ),
            ),
          ),
        ],
      ),
      body: Column(
        children: [
          // 搜索栏
          Padding(
            padding: const EdgeInsets.fromLTRB(12, 8, 12, 8),
            child: TextField(
              controller: _query,
              onChanged: (_) => setState(() {}),
              decoration: InputDecoration(
                hintText: '搜索人像、美食、风景',
                hintStyle: const TextStyle(color: Colors.grey),
                prefixIcon: const Icon(Icons.search, color: Colors.grey),
                filled: true,
                fillColor: Colors.grey.shade100,
                contentPadding: const EdgeInsets.symmetric(vertical: 0),
                border: OutlineInputBorder(
                  borderRadius: BorderRadius.circular(20),
                  borderSide: BorderSide.none,
                ),
              ),
            ),
          ),
          // 分类标签
          _CategoryBar(
            category: _category,
            onChanged: (i) {
              setState(() => _category = i);
              _selected.clear();
              _loadAssets();
            },
            onSelectAll: _toggleSelectAll,
            allSelected:
                _assets.isNotEmpty && _selected.length == _assets.length,
          ),
          const Divider(height: 1, color: Color(0xFFEEEEEE)),
          // 网格
          Expanded(
            child: _denied
                ? _DeniedView(onOpen: () => PhotoManager.openSetting())
                : _loading
                ? const Center(child: CircularProgressIndicator())
                : _buildGrid(),
          ),
        ],
      ),
      bottomNavigationBar: _ActionBar(
        count: _selected.length,
        onEdit: () => _goNext(true),
        onNext: () => _goNext(false),
      ),
    );
  }

  Widget _buildGrid() {
    final q = _query.text.trim().toLowerCase();
    final assets = q.isEmpty
        ? _assets
        : _assets
              .where((e) => (e.title ?? '').toLowerCase().contains(q))
              .toList();
    if (assets.isEmpty) {
      return const Center(
        child: Text('暂无媒体', style: TextStyle(color: Colors.grey)),
      );
    }
    return GridView.builder(
      padding: const EdgeInsets.all(2),
      gridDelegate: const SliverGridDelegateWithFixedCrossAxisCount(
        crossAxisCount: 3,
        crossAxisSpacing: 2,
        mainAxisSpacing: 2,
      ),
      itemCount: assets.length,
      itemBuilder: (context, index) {
        final entity = assets[index];
        final selectedIndex = _selected.indexOf(entity);
        final selected = selectedIndex >= 0;
        return _AssetTile(
          entity: entity,
          selected: selected,
          order: selected ? selectedIndex + 1 : null,
          onTap: () => _toggle(entity),
        );
      },
    );
  }
}

class _AssetTile extends StatelessWidget {
  final AssetEntity entity;
  final bool selected;
  final int? order;
  final VoidCallback onTap;

  const _AssetTile({
    required this.entity,
    required this.selected,
    this.order,
    required this.onTap,
  });

  @override
  Widget build(BuildContext context) {
    final isVideo = entity.type == AssetType.video;
    return GestureDetector(
      onTap: onTap,
      child: Stack(
        fit: StackFit.expand,
        children: [
          FutureBuilder<Uint8List?>(
            future: entity.thumbnailDataWithSize(
              const ThumbnailSize(300, 300),
              quality: 90,
            ),
            builder: (context, snap) {
              if (snap.hasData && snap.data != null) {
                return Image.memory(snap.data!, fit: BoxFit.cover);
              }
              return Container(color: Colors.grey.shade300);
            },
          ),
          if (isVideo)
            Positioned(
              right: 5,
              bottom: 5,
              child: Row(
                children: [
                  const Icon(
                    Icons.play_circle_fill,
                    color: Colors.white,
                    size: 14,
                  ),
                  const SizedBox(width: 2),
                  Text(
                    _duration(entity.duration),
                    style: const TextStyle(color: Colors.white, fontSize: 10),
                  ),
                ],
              ),
            ),
          Positioned(
            top: 5,
            right: 5,
            child: Container(
              width: 22,
              height: 22,
              decoration: BoxDecoration(
                shape: BoxShape.circle,
                color: selected ? AppColors.accent : Colors.black38,
                border: Border.all(color: Colors.white, width: 1.5),
              ),
              child: selected
                  ? Center(
                      child: Text(
                        '$order',
                        style: const TextStyle(
                          color: Colors.white,
                          fontSize: 12,
                          fontWeight: FontWeight.w600,
                        ),
                      ),
                    )
                  : null,
            ),
          ),
        ],
      ),
    );
  }

  String _duration(int seconds) {
    final m = seconds ~/ 60;
    final s = seconds % 60;
    return '$m:${s.toString().padLeft(2, '0')}';
  }
}

class _CategoryBar extends StatelessWidget {
  final int category;
  final ValueChanged<int> onChanged;
  final VoidCallback onSelectAll;
  final bool allSelected;

  const _CategoryBar({
    required this.category,
    required this.onChanged,
    required this.onSelectAll,
    required this.allSelected,
  });

  @override
  Widget build(BuildContext context) {
    return SizedBox(
      height: 44,
      child: Row(
        children: [
          Expanded(
            child: ListView.separated(
              scrollDirection: Axis.horizontal,
              padding: const EdgeInsets.symmetric(horizontal: 12),
              itemCount: _AlbumPageState._categories.length,
              separatorBuilder: (_, _) => const SizedBox(width: 18),
              itemBuilder: (context, i) {
                final active = i == category;
                return GestureDetector(
                  onTap: () => onChanged(i),
                  child: Column(
                    mainAxisAlignment: MainAxisAlignment.center,
                    children: [
                      Text(
                        _AlbumPageState._categories[i],
                        style: TextStyle(
                          color: active ? Colors.black : Colors.grey,
                          fontWeight: active
                              ? FontWeight.w600
                              : FontWeight.w400,
                          fontSize: 14,
                        ),
                      ),
                      const SizedBox(height: 4),
                      if (active)
                        Container(
                          width: 18,
                          height: 2,
                          color: Colors.black,
                        ),
                    ],
                  ),
                );
              },
            ),
          ),
          GestureDetector(
            onTap: onSelectAll,
            child: Padding(
              padding: const EdgeInsets.symmetric(horizontal: 14),
              child: Text(
                allSelected ? '取消' : '全选',
                style: const TextStyle(color: Colors.black54, fontSize: 13),
              ),
            ),
          ),
        ],
      ),
    );
  }
}

class _ActionBar extends StatelessWidget {
  final int count;
  final VoidCallback onEdit;
  final VoidCallback onNext;

  const _ActionBar({
    required this.count,
    required this.onEdit,
    required this.onNext,
  });

  @override
  Widget build(BuildContext context) {
    final enabled = count > 0;
    return SafeArea(
      child: Container(
        padding: const EdgeInsets.fromLTRB(16, 10, 16, 14),
        decoration: const BoxDecoration(
          color: Colors.white,
          border: Border(top: BorderSide(color: Color(0xFFEEEEEE))),
        ),
        child: Row(
          children: [
            Expanded(
              child: OutlinedButton(
                onPressed: enabled ? onEdit : null,
                style: OutlinedButton.styleFrom(
                  foregroundColor: Colors.black,
                  side: const BorderSide(color: Color(0xFFDDDDDD)),
                  padding: const EdgeInsets.symmetric(vertical: 12),
                  shape: RoundedRectangleBorder(
                    borderRadius: BorderRadius.circular(22),
                  ),
                ),
                child: const Text('一键成片'),
              ),
            ),
            const SizedBox(width: 12),
            Expanded(
              child: FilledButton(
                onPressed: enabled ? onNext : null,
                style: FilledButton.styleFrom(
                  backgroundColor: AppColors.accent,
                  disabledBackgroundColor: Colors.grey.shade300,
                  padding: const EdgeInsets.symmetric(vertical: 12),
                  shape: RoundedRectangleBorder(
                    borderRadius: BorderRadius.circular(22),
                  ),
                ),
                child: Text(count > 0 ? '下一步 ($count)' : '下一步'),
              ),
            ),
          ],
        ),
      ),
    );
  }
}

class _DeniedView extends StatelessWidget {
  final VoidCallback onOpen;

  const _DeniedView({required this.onOpen});

  @override
  Widget build(BuildContext context) {
    return Center(
      child: Column(
        mainAxisSize: MainAxisSize.min,
        children: [
          const Icon(Icons.photo_library_outlined, size: 48, color: Colors.grey),
          const SizedBox(height: 12),
          const Text('需要访问相册以选择视频', style: TextStyle(color: Colors.grey)),
          const SizedBox(height: 14),
          FilledButton(onPressed: onOpen, child: const Text('去设置开启')),
        ],
      ),
    );
  }
}
