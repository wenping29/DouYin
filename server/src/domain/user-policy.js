const USERNAME_PATTERN = /^[a-zA-Z0-9_]{3,20}$/;

export function normalizeUsername(value) {
  return String(value || '').trim().toLowerCase();
}

export function isValidUsername(username) {
  return USERNAME_PATTERN.test(username);
}

export function isValidPassword(password) {
  return password.length >= 6 && password.length <= 64;
}
