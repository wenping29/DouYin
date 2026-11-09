import crypto from 'node:crypto';
import path from 'node:path';
import multer from 'multer';
import { config } from './config.js';

const VIDEO_EXT = new Set(['.mp4', '.mov', '.m4v', '.webm', '.mkv']);
const IMAGE_EXT = new Set(['.jpg', '.jpeg', '.png', '.webp', '.gif']);

function pickExt(originalName, mimetype) {
  const ext = path.extname(originalName || '').toLowerCase();
  if (ext) return ext;
  if (mimetype === 'video/mp4') return '.mp4';
  if (mimetype === 'image/png') return '.png';
  if (mimetype === 'image/webp') return '.webp';
  return '.bin';
}

function storageFor(kind) {
  return multer.diskStorage({
    destination(_req, _file, cb) {
      cb(null, path.join(config.mediaDir, kind));
    },
    filename(_req, file, cb) {
      const ext = pickExt(file.originalname, file.mimetype);
      cb(null, `${Date.now()}-${crypto.randomBytes(6).toString('hex')}${ext}`);
    },
  });
}

function videoFilter(_req, file, cb) {
  if (file.fieldname === 'video') {
    const ext = path.extname(file.originalname || '').toLowerCase();
    if (!file.mimetype.startsWith('video/') && !VIDEO_EXT.has(ext)) {
      return cb(new Error('INVALID_VIDEO_TYPE'));
    }
    return cb(null, true);
  }
  if (file.fieldname === 'cover') {
    const ext = path.extname(file.originalname || '').toLowerCase();
    if (!file.mimetype.startsWith('image/') && !IMAGE_EXT.has(ext)) {
      return cb(new Error('INVALID_IMAGE_TYPE'));
    }
    return cb(null, true);
  }
  return cb(new Error('UNEXPECTED_FIELD'));
}

function imageFilter(_req, file, cb) {
  const ext = path.extname(file.originalname || '').toLowerCase();
  if (!file.mimetype.startsWith('image/') && !IMAGE_EXT.has(ext)) {
    return cb(new Error('INVALID_IMAGE_TYPE'));
  }
  cb(null, true);
}

export const videoUpload = multer({
  storage: storageFor('videos'),
  limits: { fileSize: config.maxVideoBytes, files: 2 },
  fileFilter: videoFilter,
});

export const avatarUpload = multer({
  storage: storageFor('avatars'),
  limits: { fileSize: config.maxImageBytes, files: 1 },
  fileFilter: imageFilter,
});
