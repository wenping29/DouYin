# -*- coding: utf-8 -*-
"""
从 music_results.txt 中读取网易云歌曲链接，下载音频并保存为 mp3 文件。

用法：
  python download_music.py                       # 读取 music_results.txt，下载到 downloads/
  python download_music.py -f my.txt -d music    # 指定结果文件和输出目录

说明：
  - 通过网易云外链接口（song/media/outer/url）获取 128kbps mp3 直链
  - VIP/版权受限歌曲拿不到直链，会被跳过并在末尾汇总
  - 已下载过的歌曲自动跳过，可重复运行

依赖：requests （pip install requests）
"""

import argparse
import os
import re
import sys
import time

import requests

# 外链接口对移动端 UA 更友好
HEADERS = {
    "User-Agent": (
        "Mozilla/5.0 (iPhone; CPU iPhone OS 16_0 like Mac OS X) "
        "AppleWebKit/605.1.15 (KHTML, like Gecko) Version/16.0 Mobile/15E148 Safari/604.1"
    ),
    "Referer": "https://music.163.com/",
}

RESULT_FILE = "music_results.txt"
DOWNLOAD_DIR = "downloads"

# 匹配形如 "1. 歌名 - 歌手  [专辑: xxx]" 后跟 "链接: ...song?id=数字" 的记录
SONG_TITLE_RE = re.compile(r"^\s*\d+\.\s*(.+?)\s*-\s*(.+?)(?:\s+\[|$)")
SONG_LINK_RE = re.compile(r"music\.163\.com/song\?id=(\d+)")


def parse_results(path):
    """解析结果 txt，返回 [{id, name, artists}]，按 id 去重保持顺序。"""
    songs = {}
    pending = None
    with open(path, "r", encoding="utf-8") as f:
        for line in f:
            m = SONG_TITLE_RE.match(line)
            if m:
                pending = {"name": m.group(1).strip(), "artists": m.group(2).strip()}
                continue
            m = SONG_LINK_RE.search(line)
            if m and pending:
                sid = m.group(1)
                if sid not in songs:
                    pending["id"] = sid
                    songs[sid] = pending
                pending = None
    return list(songs.values())


def safe_filename(name, max_len=80):
    """去掉 Windows 文件名非法字符并限长。"""
    name = re.sub(r'[\\/:*?"<>|\r\n]', " ", name).strip()
    name = re.sub(r"\s+", " ", name)
    return name[:max_len] if name else "untitled"


def get_audio_url(song_id):
    """用外链接口换取 mp3 直链；拿不到（VIP/下架）返回 None。"""
    url = f"https://music.163.com/song/media/outer/url?id={song_id}.mp3"
    try:
        resp = requests.get(url, headers=HEADERS, timeout=15, allow_redirects=False)
    except requests.RequestException as e:
        print(f"    查询直链出错: {e}")
        return None
    loc = resp.headers.get("Location", "")
    # 302 到 m*.music.126.net 才是有效音频地址；跳回 music.163.com 或无跳转表示不可用
    if resp.status_code in (301, 302, 303, 307, 308) and "126.net" in loc:
        return loc
    return None


def download_song(song, out_dir):
    """下载单首歌，返回 (状态, 文件名)。状态: ok / skip / fail"""
    sid = song["id"]
    filename = safe_filename(f"{song['name']} - {song['artists']}") + ".mp3"
    out_path = os.path.join(out_dir, filename)

    if os.path.exists(out_path) and os.path.getsize(out_path) > 1024:
        print(f"  已存在，跳过: {filename}")
        return "skip", filename

    audio_url = get_audio_url(sid)
    if not audio_url:
        print(f"  无外链（VIP/版权受限），跳过: {song['name']} - {song['artists']}")
        return "fail", filename

    try:
        with requests.get(audio_url, headers=HEADERS, timeout=60, stream=True) as resp:
            resp.raise_for_status()
            ctype = resp.headers.get("Content-Type", "")
            tmp_path = out_path + ".part"
            size = 0
            with open(tmp_path, "wb") as f:
                for chunk in resp.iter_content(chunk_size=64 * 1024):
                    f.write(chunk)
                    size += len(chunk)
            # 校验：正常 mp3 至少几百 KB；返回 HTML 说明链接失效
            if size < 100 * 1024 or "html" in ctype.lower():
                os.remove(tmp_path)
                print(f"  内容异常（{size} 字节），跳过: {song['name']}")
                return "fail", filename
            os.replace(tmp_path, out_path)
            print(f"  完成: {filename} ({size / 1024 / 1024:.1f} MB)")
            return "ok", filename
    except requests.RequestException as e:
        print(f"  下载失败: {song['name']} -> {e}")
        return "fail", filename


def main():
    parser = argparse.ArgumentParser(description="按 music_results.txt 中的链接下载 mp3")
    parser.add_argument("-f", "--file", default=RESULT_FILE, help="结果 txt 文件路径")
    parser.add_argument("-d", "--dir", default=DOWNLOAD_DIR, help="mp3 保存目录")
    args = parser.parse_args()

    if not os.path.exists(args.file):
        print(f"文件不存在: {args.file}", file=sys.stderr)
        sys.exit(1)

    songs = parse_results(args.file)
    if not songs:
        print("没有解析到任何歌曲链接")
        return

    os.makedirs(args.dir, exist_ok=True)
    print(f"共解析到 {len(songs)} 首不重复歌曲，保存到 {args.dir}/\n")

    stats = {"ok": 0, "skip": 0, "fail": 0}
    failed = []
    for i, s in enumerate(songs, 1):
        print(f"[{i}/{len(songs)}] {s['name']} - {s['artists']} (id={s['id']})")
        status, _ = download_song(s, args.dir)
        stats[status] += 1
        if status == "fail":
            failed.append(s)
        time.sleep(0.5)  # 轻微限速，避免请求过快

    print(f"\n结果: 成功 {stats['ok']}，已存在跳过 {stats['skip']}，失败 {stats['fail']}")
    if failed:
        print("以下歌曲无法下载（通常为 VIP 或版权受限）：")
        for s in failed:
            print(f"  - {s['name']} - {s['artists']}  https://music.163.com/song?id={s['id']}")


if __name__ == "__main__":
    main()
