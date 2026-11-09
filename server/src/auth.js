import crypto from 'node:crypto';
import jwt from 'jsonwebtoken';
import { config } from './config.js';
import { getUserRow } from './database/index.js';

const SCRYPT_KEYLEN = 64;

export function hashPassword(password) {
  const salt = crypto.randomBytes(16).toString('hex');
  const hash = crypto.scryptSync(password, salt, SCRYPT_KEYLEN).toString('hex');
  return `scrypt$${salt}$${hash}`;
}

export function verifyPassword(password, stored) {
  const [scheme, salt, hash] = String(stored || '').split('$');
  if (scheme !== 'scrypt' || !salt || !hash) return false;
  const candidate = crypto.scryptSync(password, salt, SCRYPT_KEYLEN);
  const expected = Buffer.from(hash, 'hex');
  return candidate.length === expected.length && crypto.timingSafeEqual(candidate, expected);
}

export function signToken(userId) {
  return jwt.sign({ uid: userId }, config.jwtSecret, { expiresIn: '30d' });
}

async function readUser(req) {
  const header = req.headers.authorization || '';
  if (!header.startsWith('Bearer ')) return null;
  try {
    const payload = jwt.verify(header.slice(7), config.jwtSecret);
    if (!payload || typeof payload.uid !== 'number') return null;
    return (await getUserRow(payload.uid)) || null;
  } catch {
    return null;
  }
}

export async function optionalAuth(req, _res, next) {
  try {
    req.user = await readUser(req);
    next();
  } catch (error) {
    next(error);
  }
}

export async function requireAuth(req, res, next) {
  try {
    req.user = await readUser(req);
    if (!req.user) {
      return res.status(401).json({
        error: { code: 'UNAUTHORIZED', message: '请先登录' },
      });
    }
    return next();
  } catch (error) {
    return next(error);
  }
}
