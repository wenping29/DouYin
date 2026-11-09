import test from 'node:test';
import assert from 'node:assert/strict';
import {
  isValidPassword,
  isValidUsername,
  normalizeUsername,
} from './user-policy.js';

test('username normalization trims whitespace and ignores case', () => {
  assert.equal(normalizeUsername('  Aze_01 '), 'aze_01');
});

test('username policy accepts 3-20 ASCII letters, digits, and underscores', () => {
  assert.equal(isValidUsername('abc'), true);
  assert.equal(isValidUsername('a'.repeat(20)), true);
  assert.equal(isValidUsername('ab'), false);
  assert.equal(isValidUsername('a'.repeat(21)), false);
  assert.equal(isValidUsername('user-name'), false);
  assert.equal(isValidUsername('阿泽'), false);
});

test('password policy accepts lengths from 6 through 64', () => {
  assert.equal(isValidPassword('123456'), true);
  assert.equal(isValidPassword('p'.repeat(64)), true);
  assert.equal(isValidPassword('12345'), false);
  assert.equal(isValidPassword('p'.repeat(65)), false);
});
