int _int(dynamic value) {
  if (value is num) return value.toInt();
  return int.tryParse('$value') ?? 0;
}

double _double(dynamic value) {
  if (value is num) return value.toDouble();
  return double.tryParse('$value') ?? 0;
}

class VideoAuthor {
  final int id;
  final String username;
  final String nickname;
  final String? avatarUrl;
  final bool isFollowing;

  const VideoAuthor({
    required this.id,
    required this.username,
    required this.nickname,
    required this.avatarUrl,
    required this.isFollowing,
  });

  factory VideoAuthor.fromJson(Map<String, dynamic> json) {
    return VideoAuthor(
      id: _int(json['id']),
      username: '${json['username'] ?? ''}',
      nickname: '${json['nickname'] ?? json['username'] ?? ''}',
      avatarUrl: json['avatarUrl']?.toString(),
      isFollowing: json['isFollowing'] == true,
    );
  }

  VideoAuthor copyWith({bool? isFollowing}) {
    return VideoAuthor(
      id: id,
      username: username,
      nickname: nickname,
      avatarUrl: avatarUrl,
      isFollowing: isFollowing ?? this.isFollowing,
    );
  }
}

class Video {
  final int id;
  final String title;
  final String description;
  final String videoUrl;
  final String? coverUrl;
  final double duration;
  final int viewCount;
  final int likeCount;
  final int commentCount;
  final int shareCount;
  final String createdAt;
  final bool liked;
  final VideoAuthor author;

  const Video({
    required this.id,
    required this.title,
    required this.description,
    required this.videoUrl,
    required this.coverUrl,
    required this.duration,
    required this.viewCount,
    required this.likeCount,
    required this.commentCount,
    required this.shareCount,
    required this.createdAt,
    required this.liked,
    required this.author,
  });

  factory Video.fromJson(Map<String, dynamic> json) {
    return Video(
      id: _int(json['id']),
      title: '${json['title'] ?? ''}',
      description: '${json['description'] ?? ''}',
      videoUrl: '${json['videoUrl'] ?? ''}',
      coverUrl: json['coverUrl']?.toString(),
      duration: _double(json['duration']),
      viewCount: _int(json['viewCount']),
      likeCount: _int(json['likeCount']),
      commentCount: _int(json['commentCount']),
      shareCount: _int(json['shareCount']),
      createdAt: '${json['createdAt'] ?? ''}',
      liked: json['liked'] == true,
      author: VideoAuthor.fromJson(
        (json['author'] as Map?)?.cast<String, dynamic>() ?? const {},
      ),
    );
  }

  DateTime? get createdAtDate => DateTime.tryParse(createdAt)?.toLocal();

  Video copyWith({
    bool? liked,
    int? likeCount,
    int? commentCount,
    int? shareCount,
    VideoAuthor? author,
  }) {
    return Video(
      id: id,
      title: title,
      description: description,
      videoUrl: videoUrl,
      coverUrl: coverUrl,
      duration: duration,
      viewCount: viewCount,
      likeCount: likeCount ?? this.likeCount,
      commentCount: commentCount ?? this.commentCount,
      shareCount: shareCount ?? this.shareCount,
      createdAt: createdAt,
      liked: liked ?? this.liked,
      author: author ?? this.author,
    );
  }
}
