import 'package:flutter/material.dart';

/// 用于让播放器感知路由被覆盖/恢复（打开评论、主页等时暂停）。
final RouteObserver<ModalRoute<dynamic>> routeObserver =
    RouteObserver<ModalRoute<dynamic>>();
