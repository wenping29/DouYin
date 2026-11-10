"""按渠道打包浏览器扩展：复制 edge/ 到 deploy/dist，改写默认服务器地址，输出 zip。

渠道：
    store     Edge 加载项商店包，默认指向线上服务器；不允许打包本地地址
    internal  自用/内测包，默认指向本地开发服务器，可用 --server 覆盖

用法：
    python tools/pack-extension.py --channel store
    python tools/pack-extension.py --channel internal
    python tools/pack-extension.py --channel internal --server http://192.168.1.10:5080

产物：
    deploy/dist/extension-<版本>-<渠道>/          解压即可加载
    deploy/dist/sp-extension-<版本>-<渠道>.zip    可直接分发或上传商店

store 包的 zip 根目录就是 manifest.json，可直接上传 Edge 加载项商店。
"""

import argparse
import json
import shutil
import sys
import zipfile
from pathlib import Path
from urllib.parse import urlparse

DEFAULT_SERVER_LINE = 'const SERVER_URL = "http://127.0.0.1:5080";'
DEFAULT_PROD_SERVER = "http://43.142.147.244:7788"
DEFAULT_LOCAL_SERVER = "http://127.0.0.1:5080"
LOCAL_HOSTS = {"localhost", "127.0.0.1", "0.0.0.0", "::1"}

CHANNELS = {
    "store": {
        "label": "商店",
        "default_server": DEFAULT_PROD_SERVER,
        "allow_local": False,
    },
    "internal": {
        "label": "内测",
        "default_server": DEFAULT_LOCAL_SERVER,
        "allow_local": True,
    },
}


def is_local_address(server: str) -> bool:
    host = (urlparse(server).hostname or "").lower()
    return host in LOCAL_HOSTS


def main() -> int:
    root = Path(__file__).resolve().parent.parent
    edge = root / "edge"
    manifest = json.loads((edge / "manifest.json").read_text(encoding="utf-8"))
    version = manifest["version"]

    parser = argparse.ArgumentParser()
    parser.add_argument(
        "--channel",
        choices=sorted(CHANNELS),
        default="store",
        help="打包渠道：store=Edge 加载项商店，internal=自用/内测（默认 store）",
    )
    parser.add_argument(
        "--server",
        default=None,
        help="打进包里的后端服务器地址；不填时用渠道默认值（写死在 config.js，用户不可改）",
    )
    parser.add_argument("--out-dir", default=str(root / "deploy" / "dist"))
    args = parser.parse_args()

    channel = CHANNELS[args.channel]
    server = (args.server or channel["default_server"]).rstrip("/")
    if not server.startswith(("http://", "https://")):
        print("--server 必须是 http:// 或 https:// 开头", file=sys.stderr)
        return 1
    if not channel["allow_local"] and is_local_address(server):
        print(
            f"{channel['label']}渠道不允许使用本地地址 {server}，"
            "请用 --server 指定线上地址",
            file=sys.stderr,
        )
        return 1
    if server.startswith("http://"):
        print(f"提示：{channel['label']}包使用明文 HTTP 服务器 {server}，上线建议改用 HTTPS")

    out_dir = Path(args.out_dir)
    stage = out_dir / f"extension-{version}-{args.channel}"
    if stage.exists():
        shutil.rmtree(stage)
    shutil.copytree(edge, stage)

    config = stage / "config.js"
    text = config.read_text(encoding="utf-8")
    if DEFAULT_SERVER_LINE not in text:
        print("config.js 里找不到服务器地址常量，请确认代码是否改过", file=sys.stderr)
        return 1
    config.write_text(
        text.replace(DEFAULT_SERVER_LINE, f'const SERVER_URL = "{server}";', 1),
        encoding="utf-8",
    )

    out_dir.mkdir(parents=True, exist_ok=True)
    zip_path = out_dir / f"sp-extension-{version}-{args.channel}.zip"
    if zip_path.exists():
        zip_path.unlink()
    with zipfile.ZipFile(zip_path, "w", zipfile.ZIP_DEFLATED) as archive:
        for path in sorted(stage.rglob("*")):
            if path.is_file():
                archive.write(path, path.relative_to(stage).as_posix())

    print(f"渠道：{channel['label']}（{args.channel}）")
    print(f"目录：{stage}")
    print(f"扩展包：{zip_path}")
    print(f"默认服务器：{server}")
    print(f"文件数：{sum(1 for p in stage.rglob('*') if p.is_file())}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
