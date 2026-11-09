int _int(dynamic value) {
  if (value is num) return value.toInt();
  return int.tryParse('$value') ?? 0;
}

class CommentAuthor {
  final int id;
  final String username;
  final String nickname;
  final String? avatarUrl;

  const CommentAuthor({
    required this.id,
    required this.username,
    required this.nickname,
    required this.avatarUrl,
  });

  factory CommentAuthor.fromJson(Map<String, dynamic> json) {
    return CommentAuthor(
      id: _int(json['id']),
      username: '${json['username'] ?? ''}',
      nickname: '${json['nickname'] ?? json['username'] ?? ''}',
      avatarUrl: json['avatarUrl']?.toString(),
    );
  }
}

class Comment {
  final int id;
  final String content;
  final int likeCount;
  final String createdAt;
  final bool isMine;
  final bool canDelete;
  final CommentAuthor author;

  const Comment({
    required this.id,
    required this.content,
    required this.likeCount,
    required this.createdAt,
    required this.isMine,
    required this.canDelete,
    required this.author,
  });

  factory Comment.fromJson(Map<String, dynamic> json) {
    return Comment(
      id: _int(json['id']),
      content: '${json['content'] ?? ''}',
      likeCount: _int(json['likeCount']),
      createdAt: '${json['createdAt'] ?? ''}',
      isMine: json['isMine'] == true,
      canDelete: json['canDelete'] == true,
      author: CommentAuthor.fromJson(
        (json['author'] as Map?)?.cast<String, dynamic>() ?? const {},
      ),
    );
  }
}
