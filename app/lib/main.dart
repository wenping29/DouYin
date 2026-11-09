import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import 'core/api_client.dart';
import 'core/app_state.dart';
import 'core/route_observer.dart';
import 'core/theme.dart';
import 'pages/auth/login_page.dart';
import 'pages/root_shell.dart';

void main() {
  WidgetsFlutterBinding.ensureInitialized();
  final appState = AppState(ApiClient());
  runApp(
    ChangeNotifierProvider<AppState>.value(
      value: appState,
      child: const DouYingApp(),
    ),
  );
  appState.boot();
}

class DouYingApp extends StatelessWidget {
  const DouYingApp({super.key});

  @override
  Widget build(BuildContext context) {
    return MaterialApp(
      title: '抖影',
      debugShowCheckedModeBanner: false,
      theme: buildAppTheme(),
      
      navigatorObservers: [routeObserver],
      home: Consumer<AppState>(
        builder: (context, app, _) {
          if (app.booting) return const _SplashPage();
          if (!app.isLoggedIn) return const LoginPage();
          return const RootShell();
        },
      ),
    );
  }
}

class _SplashPage extends StatelessWidget {
  const _SplashPage();

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      backgroundColor: Colors.black,
      body: Center(
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            Container(
              width: 70,
              height: 70,
              decoration: BoxDecoration(
                borderRadius: BorderRadius.circular(20),
                gradient: const LinearGradient(
                  begin: Alignment.topLeft,
                  end: Alignment.bottomRight,
                  colors: [AppColors.cyan, AppColors.accent, Color(0xFF7B2FF7)],
                ),
              ),
              child: const Icon(
                Icons.play_arrow_rounded,
                color: Colors.white,
                size: 46,
              ),
            ),
            const SizedBox(height: 22),
            const Text(
              '抖影',
              style: TextStyle(
                fontSize: 30,
                fontWeight: FontWeight.w800,
                letterSpacing: 3,
              ),
            ),
            const SizedBox(height: 26),
            const SizedBox(
              width: 22,
              height: 22,
              child: CircularProgressIndicator(
                strokeWidth: 2,
                color: AppColors.muted,
              ),
            ),
          ],
        ),
      ),
    );
  }
}
