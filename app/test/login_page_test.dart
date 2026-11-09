import 'package:douying/core/api_client.dart';
import 'package:douying/core/app_state.dart';
import 'package:douying/core/theme.dart';
import 'package:douying/pages/auth/login_page.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:provider/provider.dart';

void main() {
  testWidgets('登录页可以正常渲染并展示演示入口', (tester) async {
    final app = AppState(ApiClient(baseUrl: 'http://127.0.0.1:1'));
    await tester.pumpWidget(
      ChangeNotifierProvider<AppState>.value(
        value: app,
        child: MaterialApp(
          theme: buildAppTheme(),
          home: const LoginPage(),
        ),
      ),
    );

    expect(find.text('抖影'), findsOneWidget);
    expect(find.text('使用演示账号  aze / demo1234'), findsOneWidget);
    expect(find.text('登录'), findsWidgets);
  });
}
