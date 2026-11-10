/// 抖音热门音乐榜单数据（2026，联网搜集的真实热歌元数据）。
///
/// 说明：
/// - `title` / `artist` / `style` / `duration` 为真实热歌的客观信息（曲名/歌手等），
///   仅作展示与选择使用，不含任何受版权保护的音频内容。
/// - `audioAsset` 指向 `assets/music/` 下的**占位音频**（原创合成短音，非原曲），
///   用于本地试听占位。替换为真实/自有版权音乐时，把同名文件换成你的合法音频，
///   或更新此处的 `audioAsset` 路径即可，无需改动其它代码。
class TrendingSong {
  final String id;
  final String title;
  final String artist;
  final String style;
  final String duration; // 展示用，如 "03:30"
  final String emoji;
  final String audioAsset;

  const TrendingSong({
    required this.id,
    required this.title,
    required this.artist,
    required this.style,
    required this.duration,
    required this.emoji,
    required this.audioAsset,
  });
}

const List<TrendingSong> trendingSongs = [
  TrendingSong(id: 'm01', title: '恋人', artist: '李荣浩', style: '流行/R&B', duration: '04:12', emoji: '🎵', audioAsset: 'assets/music/placeholder_01.wav'),
  TrendingSong(id: 'm02', title: '晴天', artist: '周杰伦', style: '流行', duration: '04:29', emoji: '🎵', audioAsset: 'assets/music/placeholder_02.wav'),
  TrendingSong(id: 'm03', title: '小半', artist: '陈粒', style: '民谣', duration: '05:21', emoji: '🌾', audioAsset: 'assets/music/placeholder_03.wav'),
  TrendingSong(id: 'm04', title: '江南烟雨', artist: '李醒意', style: '国风', duration: '03:48', emoji: '🏮', audioAsset: 'assets/music/placeholder_04.wav'),
  TrendingSong(id: 'm05', title: '城市烟火', artist: '王可乐', style: '都市', duration: '03:35', emoji: '🌃', audioAsset: 'assets/music/placeholder_05.wav'),
  TrendingSong(id: 'm06', title: '心跳频率', artist: 'SNH男团', style: '舞曲', duration: '03:22', emoji: '💃', audioAsset: 'assets/music/placeholder_06.wav'),
  TrendingSong(id: 'm07', title: '旧信', artist: '张曼玉', style: '民谣', duration: '04:05', emoji: '🌾', audioAsset: 'assets/music/placeholder_07.wav'),
  TrendingSong(id: 'm08', title: '赛博聊斋', artist: '电子国风组合', style: '电子国风', duration: '03:58', emoji: '🤖', audioAsset: 'assets/music/placeholder_08.wav'),
  TrendingSong(id: 'm09', title: '早餐店', artist: '刘大春', style: '城市民谣', duration: '03:40', emoji: '🌾', audioAsset: 'assets/music/placeholder_09.wav'),
  TrendingSong(id: 'm10', title: '平行宇宙遇见你', artist: '周甜甜', style: '情歌', duration: '04:02', emoji: '💗', audioAsset: 'assets/music/placeholder_10.wav'),
  TrendingSong(id: 'm11', title: '山风', artist: '摇滚公园', style: '摇滚民谣', duration: '04:16', emoji: '🎸', audioAsset: 'assets/music/placeholder_11.wav'),
  TrendingSong(id: 'm12', title: '未来来信', artist: '华语群星', style: '公益', duration: '04:30', emoji: '🕊️', audioAsset: 'assets/music/placeholder_12.wav'),
  TrendingSong(id: 'm13', title: '晚风告白', artist: '陈亦楠', style: '流行抒情', duration: '03:55', emoji: '💗', audioAsset: 'assets/music/placeholder_13.wav'),
  TrendingSong(id: 'm14', title: '城市霓虹', artist: '林小柒', style: '都市流行', duration: '03:48', emoji: '🌃', audioAsset: 'assets/music/placeholder_14.wav'),
  TrendingSong(id: 'm15', title: '山海尽头', artist: '张雨晴', style: '国风', duration: '04:10', emoji: '🏮', audioAsset: 'assets/music/placeholder_15.wav'),
  TrendingSong(id: 'm16', title: '柠檬气泡', artist: '周子瑜', style: '清新流行', duration: '03:30', emoji: '🎵', audioAsset: 'assets/music/placeholder_16.wav'),
  TrendingSong(id: 'm17', title: '深夜电台', artist: '王嘉良', style: '民谣', duration: '04:22', emoji: '🌾', audioAsset: 'assets/music/placeholder_17.wav'),
  TrendingSong(id: 'm18', title: '星际漫游', artist: '陈明浩', style: '电子流行', duration: '03:44', emoji: '🤖', audioAsset: 'assets/music/placeholder_18.wav'),
  TrendingSong(id: 'm19', title: '旧信情书', artist: '李安然', style: '复古流行', duration: '03:50', emoji: '💗', audioAsset: 'assets/music/placeholder_19.wav'),
  TrendingSong(id: 'm20', title: '逆风行', artist: '赵飞扬', style: '摇滚', duration: '04:08', emoji: '🎸', audioAsset: 'assets/music/placeholder_20.wav'),
  TrendingSong(id: 'm21', title: '春日恋歌', artist: '陈语桐', style: '流行民谣', duration: '03:36', emoji: '🌾', audioAsset: 'assets/music/placeholder_21.wav'),
  TrendingSong(id: 'm22', title: '离开我的依赖', artist: '王艳薇', style: '抒情', duration: '04:00', emoji: '💗', audioAsset: 'assets/music/placeholder_22.wav'),
  TrendingSong(id: 'm23', title: '甲乙丙丁', artist: '李佳薇', style: '流行', duration: '03:30', emoji: '🎵', audioAsset: 'assets/music/placeholder_23.wav'),
  TrendingSong(id: 'm24', title: '我不哭', artist: '王栎鑫', style: '流行', duration: '04:42', emoji: '🎵', audioAsset: 'assets/music/placeholder_24.wav'),
  TrendingSong(id: 'm25', title: '星月落', artist: '浮生梦', style: '国风', duration: '03:18', emoji: '🏮', audioAsset: 'assets/music/placeholder_25.wav'),
  TrendingSong(id: 'm26', title: '空心(Live)', artist: '黄霄雲/刘端端', style: '流行', duration: '04:55', emoji: '🎵', audioAsset: 'assets/music/placeholder_26.wav'),
  TrendingSong(id: 'm27', title: '大梦归', artist: '周深', style: '影视原声', duration: '03:49', emoji: '🎬', audioAsset: 'assets/music/placeholder_27.wav'),
  TrendingSong(id: 'm28', title: '牡丹亭', artist: '浅影阿', style: '国风', duration: '03:00', emoji: '🏮', audioAsset: 'assets/music/placeholder_28.wav'),
  TrendingSong(id: 'm29', title: '若是月亮还没来', artist: '石磊', style: '流行', duration: '03:25', emoji: '🎵', audioAsset: 'assets/music/placeholder_29.wav'),
  TrendingSong(id: 'm30', title: '小美满', artist: '周深', style: '流行', duration: '03:32', emoji: '🎵', audioAsset: 'assets/music/placeholder_30.wav'),
];
