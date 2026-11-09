import 'package:douying/core/format.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  group('formatCount', () {
    test('小于一万直接展示', () {
      expect(formatCount(0), '0');
      expect(formatCount(2361), '2361');
      expect(formatCount(9999), '9999');
    });

    test('万与亿', () {
      expect(formatCount(10000), '1万');
      expect(formatCount(12345), '1.2万');
      expect(formatCount(123456789), '1.2亿');
    });
  });

  group('timeAgo', () {
    test('相对时间', () {
      final now = DateTime.now();
      expect(timeAgo(now.subtract(const Duration(seconds: 20))), '刚刚');
      expect(timeAgo(now.subtract(const Duration(minutes: 5))), '5 分钟前');
      expect(timeAgo(now.subtract(const Duration(hours: 3))), '3 小时前');
      expect(timeAgo(now.subtract(const Duration(days: 2))), '2 天前');
    });

    test('空值', () {
      expect(timeAgo(null), '');
    });
  });
}
