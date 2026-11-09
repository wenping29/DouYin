import crypto from 'node:crypto';
import path from 'node:path';
import express from 'express';
import { config } from '../config.js';
import { db, getUserRow, nowIso } from '../database/index.js';
import { hashPassword, signToken, verifyPassword } from '../auth.js';
import { generateAvatar } from '../cover.js';
import { publicUser } from '../serializers.js';
import { fail, text, wrap } from '../util.js';

export const authRouter = express.Router();

authRouter.post(
  '/register',
  wrap(async (req, res) => {
    const username = String(req.body?.username || '').trim().toLowerCase();
    const password = String(req.body?.password || '');
    const nickname = text(req.body?.nickname, 20) || username;

    if (!/^[a-zA-Z0-9_]{3,20}$/.test(username)) {
      return fail(res, 400, 'VALIDATION_ERROR', '用户名需为 3-20 位字母、数字或下划线');
    }
    if (password.length < 6 || password.length > 64) {
      return fail(res, 400, 'VALIDATION_ERROR', '密码长度需为 6-64 位');
    }
    const existing = await db
      .prepare('SELECT id FROM users WHERE username = ?')
      .get(username);
    if (existing) {
      return fail(res, 409, 'USERNAME_TAKEN', '用户名已被占用');
    }

    const avatarName = `u-${Date.now()}-${crypto.randomBytes(4).toString('hex')}.png`;
    const avatarUrl = `/media/avatars/${avatarName}`;
    generateAvatar(`${username}:${Date.now()}`, path.join(config.mediaDir, 'avatars', avatarName));

    const info = await db
      .prepare(
        `INSERT INTO users (username, password_hash, nickname, avatar_url, bio, created_at)
         VALUES (?, ?, ?, ?, ?, ?) RETURNING id`,
      )
      .run(username, hashPassword(password), nickname, avatarUrl, '', nowIso());

    const user = publicUser(await getUserRow(Number(info.lastInsertRowid)));
    return res.status(201).json({ token: signToken(user.id), user });
  }),
);

authRouter.post(
  '/login',
  wrap(async (req, res) => {
    const username = String(req.body?.username || '').trim().toLowerCase();
    const password = String(req.body?.password || '');
    const row = await db.prepare('SELECT * FROM users WHERE username = ?').get(username);
    if (!row || !verifyPassword(password, row.password_hash)) {
      return fail(res, 401, 'INVALID_CREDENTIALS', '用户名或密码错误');
    }
    return res.json({
      token: signToken(row.id),
      user: publicUser(await getUserRow(row.id)),
    });
  }),
);
