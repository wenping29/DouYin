import crypto from 'node:crypto';
import fs from 'node:fs';
import path from 'node:path';
import zlib from 'node:zlib';

/* ---------- 极简 PNG 编码器（无第三方依赖） ---------- */

const CRC_TABLE = (() => {
  const table = new Uint32Array(256);
  for (let n = 0; n < 256; n++) {
    let c = n;
    for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1;
    table[n] = c >>> 0;
  }
  return table;
})();

function crc32(buf) {
  let c = 0xffffffff;
  for (let i = 0; i < buf.length; i++) c = CRC_TABLE[(c ^ buf[i]) & 0xff] ^ (c >>> 8);
  return (c ^ 0xffffffff) >>> 0;
}

function chunk(type, data) {
  const typeBuf = Buffer.from(type, 'ascii');
  const len = Buffer.alloc(4);
  len.writeUInt32BE(data.length, 0);
  const crc = Buffer.alloc(4);
  crc.writeUInt32BE(crc32(Buffer.concat([typeBuf, data])), 0);
  return Buffer.concat([len, typeBuf, data, crc]);
}

function encodePng(width, height, rgb) {
  const ihdr = Buffer.alloc(13);
  ihdr.writeUInt32BE(width, 0);
  ihdr.writeUInt32BE(height, 4);
  ihdr[8] = 8; // bit depth
  ihdr[9] = 2; // RGB
  ihdr[10] = 0;
  ihdr[11] = 0;
  ihdr[12] = 0;
  const raw = Buffer.alloc((width * 3 + 1) * height);
  for (let y = 0; y < height; y++) {
    const rowStart = y * (width * 3 + 1);
    raw[rowStart] = 0; // filter: none
    rgb.copy(raw, rowStart + 1, y * width * 3, (y + 1) * width * 3);
  }
  return Buffer.concat([
    Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]),
    chunk('IHDR', ihdr),
    chunk('IDAT', zlib.deflateSync(raw, { level: 8 })),
    chunk('IEND', Buffer.alloc(0)),
  ]);
}

/* ---------- 品牌渐变封面 ---------- */

const PALETTES = [
  [[254, 44, 85], [123, 47, 247]],
  [[37, 244, 238], [29, 38, 113]],
  [[255, 154, 68], [252, 96, 118]],
  [[67, 233, 123], [56, 249, 215]],
  [[250, 112, 154], [254, 225, 64]],
  [[48, 207, 208], [51, 8, 103]],
  [[255, 94, 247], [28, 7, 64]],
];

function paletteFor(seed) {
  const digest = crypto.createHash('sha1').update(String(seed)).digest();
  const p = PALETTES[digest[0] % PALETTES.length];
  return [p[0], p[1], digest];
}

const lerp = (a, b, t) => a + (b - a) * t;

function drawCover(seed, width, height, { avatar = false } = {}) {
  const [c1, c2, digest] = paletteFor(seed);
  const buf = Buffer.alloc(width * height * 3);
  const cx = width * 0.5;
  const cy = height * (avatar ? 0.5 : 0.46);
  const radius = Math.min(width, height) * (avatar ? 0.3 : 0.14);
  const angle = 0.35 + (digest[1] / 255) * 0.5;

  for (let y = 0; y < height; y++) {
    for (let x = 0; x < width; x++) {
      const nx = x / width;
      const ny = y / height;
      const t = Math.min(1, Math.max(0, (nx * 0.35 + ny * 0.65) * 0.9 + 0.05));
      let r = lerp(c1[0], c2[0], t);
      let g = lerp(c1[1], c2[1], t);
      let b = lerp(c1[2], c2[2], t);

      // 斜向光带
      const band = Math.sin((nx * Math.cos(angle) + ny * Math.sin(angle)) * 9) * 0.5 + 0.5;
      const light = 18 * band;
      r += light;
      g += light;
      b += light;

      // 暗角
      const dx = nx - 0.5;
      const dy = ny - 0.5;
      const vignette = Math.max(0, 1 - (dx * dx + dy * dy) * 1.35);
      r *= 0.55 + vignette * 0.45;
      g *= 0.55 + vignette * 0.45;
      b *= 0.55 + vignette * 0.45;

      if (!avatar) {
        const dxp = x - cx;
        const dyp = y - cy;
        const dist = Math.sqrt(dxp * dxp + dyp * dyp);
        if (Math.abs(dist - radius) < 2.2) {
          r = lerp(r, 255, 0.85);
          g = lerp(g, 255, 0.85);
          b = lerp(b, 255, 0.85);
        } else if (dist < radius) {
          r = lerp(r, 255, 0.14);
          g = lerp(g, 255, 0.14);
          b = lerp(b, 255, 0.14);
          // 播放三角形
          const tx = x - (cx - radius * 0.16);
          const ty = y - cy;
          if (tx > 0 && Math.abs(ty) < radius * 0.36 - tx * 0.62) {
            r = lerp(r, 255, 0.82);
            g = lerp(g, 255, 0.82);
            b = lerp(b, 255, 0.82);
          }
        }
      } else {
        const dxp = x - width / 2;
        const dyp = y - height / 2;
        const dist = Math.sqrt(dxp * dxp + dyp * dyp);
        if (Math.abs(dist - width * 0.32) < 2) {
          r = lerp(r, 255, 0.35);
          g = lerp(g, 255, 0.35);
          b = lerp(b, 255, 0.35);
        }
      }

      const i = (y * width + x) * 3;
      buf[i] = Math.max(0, Math.min(255, r));
      buf[i + 1] = Math.max(0, Math.min(255, g));
      buf[i + 2] = Math.max(0, Math.min(255, b));
    }
  }
  return encodePng(width, height, buf);
}

export function generateCover(seed, outPath) {
  fs.mkdirSync(path.dirname(outPath), { recursive: true });
  fs.writeFileSync(outPath, drawCover(seed, 360, 640));
  return outPath;
}

export function generateAvatar(seed, outPath) {
  fs.mkdirSync(path.dirname(outPath), { recursive: true });
  fs.writeFileSync(outPath, drawCover(seed, 256, 256, { avatar: true }));
  return outPath;
}
