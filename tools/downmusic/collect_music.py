# -*- coding: utf-8 -*-
"""
音乐收集脚本（不下载音频，只收集歌曲名称和链接地址，结果保存到 txt 文件）

功能：
  1. search  按关键词搜索网易云音乐，收集歌曲名称 + 播放页链接
  2. douyin  收集抖音热门歌曲（数据源：网易云音乐上的抖音热歌榜歌单）

用法示例：
  python collect_music.py search 周杰伦
  python collect_music.py search 晴天 -n 5
  python collect_music.py douyin              # 默认取抖音热歌榜前 50 首
  python collect_music.py douyin -n 30        # 取前 30 首
  python collect_music.py douyin -k 抖音热门背景音乐   # 自定义歌单搜索词
  python collect_music.py search 林俊杰 -o my.txt     # 指定输出文件

依赖：requests （pip install requests）
"""

import argparse
import json
import sys
import time
from datetime import datetime

import requests

# 网易云歌单/搜索接口需要 Referer，否则可能被拒绝
HEADERS = {
    "User-Agent": (
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 "
        "(KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36"
    ),
    "Referer": "https://music.163.com/",
    "Cookie": "NMTID=0oOx1",
}

DEFAULT_OUTPUT = "music_results.txt"
NETEASE_SONG_URL = "https://music.163.com/song?id={sid}"


def http_get(url, params=None, retries=3):
    """带简单重试的 GET 请求，返回解析后的 JSON。"""
    last_err = None
    for i in range(retries):
        try:
            resp = requests.get(url, params=params, headers=HEADERS, timeout=15)
            resp.raise_for_status()
            return resp.json()
        except Exception as e:  # noqa: BLE001
            last_err = e
            time.sleep(1 + i)
    raise RuntimeError(f"请求失败: {url} -> {last_err}")


def http_post(url, data=None, retries=3):
    """带简单重试的 POST 请求（表单），返回解析后的 JSON。"""
    last_err = None
    for i in range(retries):
        try:
            resp = requests.post(url, data=data, headers=HEADERS, timeout=15)
            resp.raise_for_status()
            return resp.json()
        except Exception as e:  # noqa: BLE001
            last_err = e
            time.sleep(1 + i)
    raise RuntimeError(f"请求失败: {url} -> {last_err}")


# ---------------------------------------------------------------- 数据源 ----

def search_netease_songs(keyword, limit=20):
    """搜索网易云音乐单曲，返回歌曲信息列表。"""
    collected = []
    offset = 0
    while len(collected) < limit:
        page = min(30, limit - len(collected))
        data = http_get(
            "https://music.163.com/api/search/get/web",
            params={"s": keyword, "type": 1, "limit": page, "offset": offset},
        )
        songs = (data.get("result") or {}).get("songs") or []
        if not songs:
            break
        for s in songs:
            collected.append(
                {
                    "name": s.get("name", ""),
                    "artists": "/".join(a.get("name", "") for a in s.get("artists", [])),
                    "album": s.get("album", {}).get("name", ""),
                    "id": s.get("id"),
                    "url": NETEASE_SONG_URL.format(sid=s.get("id")),
                }
            )
        offset += len(songs)
    return collected


def search_netease_playlists(keyword, limit=5):
    """搜索网易云歌单，返回歌单信息列表。"""
    data = http_get(
        "https://music.163.com/api/search/get/web",
        params={"s": keyword, "type": 1000, "limit": limit, "offset": 0},
    )
    playlists = (data.get("result") or {}).get("playlists") or []
    return [{"id": p.get("id"), "name": p.get("name", "")} for p in playlists]


def fetch_playlist_songs(playlist_id, limit=50):
    """获取歌单内歌曲（先取全部 trackId，再批量取歌曲详情）。"""
    data = http_get(
        "https://music.163.com/api/v3/playlist/detail", params={"id": playlist_id}
    )
    playlist = data.get("playlist") or (data.get("result") or {}).get("playlist") or {}
    track_ids = [t.get("id") for t in playlist.get("trackIds", []) if t.get("id")]

    songs = []
    batch = 50  # song/detail 接口单次查询数量上限
    for i in range(0, len(track_ids), batch):
        ids = track_ids[i : i + batch]
        detail = http_post(
            "https://music.163.com/api/song/detail",
            data={"ids": json.dumps(ids), "c": json.dumps([{"id": x} for x in ids])},
        )
        for s in detail.get("songs") or []:
            songs.append(
                {
                    "name": s.get("name", ""),
                    "artists": "/".join(a.get("name", "") for a in s.get("artists", [])),
                    "album": s.get("album", {}).get("name", ""),
                    "id": s.get("id"),
                    "url": NETEASE_SONG_URL.format(sid=s.get("id")),
                }
            )
        if len(songs) >= limit:
            break
    return songs[:limit]


def collect_douyin_hot(keyword, limit):
    """收集抖音热门歌曲：搜索热歌歌单，合并去重后按热度取前 limit 首。"""
    playlists = search_netease_playlists(keyword, limit=5)
    if not playlists:
        print(f"未找到与「{keyword}」相关的歌单")
        return []

    print("使用歌单：")
    for p in playlists[:3]:
        print(f"  - {p['name']} (id={p['id']})")

    seen = set()
    songs = []
    for p in playlists[:3]:  # 取前 3 个歌单，避免请求过多
        try:
            for s in fetch_playlist_songs(p["id"], limit):
                if s["id"] not in seen:
                    seen.add(s["id"])
                    songs.append(s)
        except Exception as e:  # noqa: BLE001
            print(f"  歌单 {p['id']} 获取失败: {e}")
        if len(songs) >= limit:
            break
    return songs[:limit]


# ---------------------------------------------------------------- 输出 ----

def save_results(title, songs, output_file):
    """把歌曲列表追加保存到 txt 文件，同时在控制台打印。"""
    header = (
        f"\n{'=' * 60}\n{title}\n"
        f"时间: {datetime.now():%Y-%m-%d %H:%M:%S}    共 {len(songs)} 首\n{'=' * 60}\n"
    )
    lines = [header]
    for i, s in enumerate(songs, 1):
        line = f"{i}. {s['name']} - {s['artists']}"
        if s.get("album"):
            line += f"  [专辑: {s['album']}]"
        link = f"   链接: {s['url']}"
        lines.append(line + "\n" + link + "\n")
        print(line)
        print(link)

    text = "".join(lines)
    try:
        with open(output_file, "a", encoding="utf-8") as f:
            f.write(text)
        print(f"\n已保存到: {output_file}")
    except OSError as e:
        print(f"写入文件失败: {e}", file=sys.stderr)


# ---------------------------------------------------------------- 入口 ----

def main():
    parser = argparse.ArgumentParser(
        description="收集网易云音乐 / 抖音热门歌曲的名称与链接，保存到 txt",
        formatter_class=argparse.RawDescriptionHelpFormatter,
        epilog=(
            "示例:\n"
            "  python collect_music.py search 周杰伦\n"
            "  python collect_music.py douyin -n 30\n"
        ),
    )
    sub = parser.add_subparsers(dest="command", required=True)

    p_search = sub.add_parser("search", help="搜索网易云音乐")
    p_search.add_argument("keyword", help="搜索关键词（歌名/歌手）")
    p_search.add_argument("-n", "--num", type=int, default=20, help="收集数量，默认 20")
    p_search.add_argument("-o", "--output", default=DEFAULT_OUTPUT, help="输出 txt 文件")

    p_dy = sub.add_parser("douyin", help="收集抖音热门歌曲")
    p_dy.add_argument("-n", "--num", type=int, default=50, help="收集数量，默认 50")
    p_dy.add_argument(
        "-k", "--keyword", default="抖音热歌", help="热歌歌单搜索词，默认「抖音热歌」"
    )
    p_dy.add_argument("-o", "--output", default=DEFAULT_OUTPUT, help="输出 txt 文件")

    args = parser.parse_args()

    if args.command == "search":
        print(f"搜索网易云音乐: {args.keyword} ...")
        songs = search_netease_songs(args.keyword, args.num)
        if not songs:
            print("没有找到歌曲")
            return
        save_results(f"网易云音乐搜索结果: {args.keyword}", songs, args.output)

    elif args.command == "douyin":
        print(f"收集抖音热门歌曲（关键词: {args.keyword}）...")
        songs = collect_douyin_hot(args.keyword, args.num)
        if not songs:
            print("没有获取到歌曲")
            return
        save_results(f"抖音热门歌曲（{args.keyword}）", songs, args.output)


if __name__ == "__main__":
    main()
