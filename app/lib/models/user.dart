class User {
  final int id;
  final String username;
  final String nickname;
  final String? avatarUrl;
  final String bio;
  final int followerCount;
  final int followingCount;
  final int videoCount;
  final bool isFollowing;
  final String createdAt;

  const User({
    required this.id,
    required this.username,
    required this.nickname,
    required this.avatarUrl,
    required this.bio,
    required this.followerCount,
    required this.followingCount,
    required this.videoCount,
    required this.isFollowing,
    required this.createdAt,
  });

  factory User.fromJson(Map<String, dynamic> json) {
    return User(
      id: _int(json['id']),
      username: '${json['username'] ?? ''}',
      nickname: '${json['nickname'] ?? json['username'] ?? ''}',
      avatarUrl: json['avatarUrl']?.toString(),
      bio: '${json['bio'] ?? ''}',
      followerCount: _int(json['followerCount']),
      followingCount: _int(json['followingCount']),
      videoCount: _int(json['videoCount']),
      isFollowing: json['isFollowing'] == true,
      createdAt: '${json['createdAt'] ?? ''}',
    );
  }

  User copyWith({
    String? nickname,
    String? avatarUrl,
    String? bio,
    int? followerCount,
    int? followingCount,
    int? videoCount,
    bool? isFollowing,
  }) {
    return User(
      id: id,
      username: username,
      nickname: nickname ?? this.nickname,
      avatarUrl: avatarUrl ?? this.avatarUrl,
      bio: bio ?? this.bio,
      followerCount: followerCount ?? this.followerCount,
      followingCount: followingCount ?? this.followingCount,
      videoCount: videoCount ?? this.videoCount,
      isFollowing: isFollowing ?? this.isFollowing,
      createdAt: createdAt,
    );
  }
}

int _int(dynamic value) {
  if (value is num) return value.toInt();
  return int.tryParse('$value') ?? 0;
}
