"""生成「选择音乐」列表所用的占位音频（原创合成，非任何歌曲原声）。

这些 .wav 仅用于本地试听占位，明确标注为非原曲。
如需替换为真实/自有版权音乐：把同名文件（如 placeholder_01.wav）
替换为你的合法音频（可改扩展名并在 music_catalog.dart 同步路径）即可，无需改其它代码。

运行：python3 tools/gen_placeholder_audio.py
依赖：Python 标准库（wave / math / struct / os），无需第三方包。
"""
import os
import math
import wave
import struct

OUT = os.path.join(os.path.dirname(__file__), "..", "assets", "music")
os.makedirs(OUT, exist_ok=True)

SR = 16000          # 采样率（占位音质足够，体积小）
DUR = 3.0           # 时长（秒）
COUNT = 30          # 与 music_catalog.dart 中歌曲数量一致

# C 大调五声音阶，听感更悦耳，避免刺耳单音
SCALE = [261.63, 293.66, 329.63, 392.00, 440.00, 523.25, 587.33, 659.25]


def make_tone(path: str, freq: float) -> None:
    n = int(SR * DUR)
    fade = int(SR * 0.05)
    frames = bytearray()
    for i in range(n):
        env = 1.0
        if i < fade:
            env = i / fade
        elif i > n - fade:
            env = (n - i) / fade
        t = i / SR
        # 基频 + 少量二次泛音，听感更饱满
        s = 0.6 * math.sin(2 * math.pi * freq * t) + 0.2 * math.sin(2 * math.pi * 2 * freq * t)
        val = int(max(-1.0, min(1.0, s * env)) * 32767)
        frames += struct.pack("<h", val)
    with wave.open(path, "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes(bytes(frames))


def main() -> None:
    for i in range(COUNT):
        base = SCALE[i % len(SCALE)]
        freq = base * (1.5 if (i // len(SCALE)) % 2 == 1 else 1.0)
        path = os.path.join(OUT, f"placeholder_{i + 1:02d}.wav")
        make_tone(path, freq)
    print(f"generated {COUNT} placeholder wav files in {os.path.abspath(OUT)}")


if __name__ == "__main__":
    main()
