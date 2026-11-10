#!/usr/bin/env python3
# 临时打包脚本：把 extension/ 内的运行 + 构建文件打成可分发的 zip。
# zip 顶层直接是 manifest.json，解压后即可"加载已解压的扩展程序"。
import os, zipfile

EXT = r"C:/Users/fuwen/WorkBuddy/noad/extension"
OUT = r"C:/Users/fuwen/WorkBuddy/noad/dist/cleanview-adblock-0.1.0.zip"

# 顶层文件白名单
top_files = [
    "manifest.json",
    "package.json",
    "rules-sources.json",
    "NOTICE",
    "README.md",
]

# 顶层目录白名单：None=全含；list=仅含这些文件名；callable=按返回过滤
dirs = {
    "src": None,        # 全部 JS/CSS/HTML
    "rules": None,      # 含 ruleset_core.json / ruleset_custom.json（运行必需）
    "server": None,     # 匿名统计服务器（可选启用）
    "tools": lambda n: not n.startswith("build-report"),  # 排除构建报告
    "filters": ["custom.txt"],  # 仅含用户自定义规则入口
    "icons": None,      # 扩展图标（16/32/48/128），运行必需
}

def add_from_dir(zf, d):
    base = os.path.join(EXT, d)
    if not os.path.isdir(base):
        print("跳过缺失目录:", d)
        return
    allow = dirs[d]
    for root, subdirs, files in os.walk(base):
        # 过滤掉中间/缓存目录与隐藏文件
        subdirs[:] = [s for s in subdirs if not s.startswith("_") and s != "node_modules"]
        for f in files:
            if f.startswith(".") or f == ".env":
                continue
            full = os.path.join(root, f)
            rel = os.path.relpath(full, EXT)
            if isinstance(allow, list):
                if os.path.basename(full) not in allow:
                    continue
            elif callable(allow):
                if not allow(f):
                    continue
            zf.write(full, rel)

os.makedirs(os.path.dirname(OUT), exist_ok=True)
with zipfile.ZipFile(OUT, "w", zipfile.ZIP_DEFLATED) as zf:
    for f in top_files:
        full = os.path.join(EXT, f)
        if os.path.isfile(full):
            zf.write(full, f)
        else:
            print("缺失顶层文件:", f)
    for d in dirs:
        add_from_dir(zf, d)

with zipfile.ZipFile(OUT) as zf:
    infos = zf.infolist()
    total_raw = sum(i.file_size for i in infos)
    total_cmp = sum(i.compress_size for i in infos)
    tops = sorted(set(i.filename.split("/")[0] for i in infos))
    print("打包完成:", OUT)
    print("文件数:", len(infos))
    print("未压缩: %.2f MB | 压缩后: %.2f MB" % (total_raw/1024/1024, total_cmp/1024/1024))
    print("顶层条目:", tops)
