import test from 'node:test';
import assert from 'node:assert/strict';
import { rankRecommendations, recommendationScore } from './recommendation.js';

const now = Date.parse('2026-11-09T00:00:00.000Z');

function video(id, overrides = {}) {
  return {
    id,
    created_at: new Date(now).toISOString(),
    like_count: 0,
    comment_count: 0,
    share_count: 0,
    view_count: 0,
    ...overrides,
  };
}

test('recommendation score combines engagement and time decay', () => {
  const fresh = recommendationScore(video(1), now);
  const old = recommendationScore(video(2, {
    created_at: new Date(now - 24 * 3600000).toISOString(),
  }), now);
  const popular = recommendationScore(video(3, { like_count: 10 }), now);

  assert.ok(fresh > old);
  assert.ok(popular > fresh);
});

test('recommendation ordering breaks equal scores by descending id', () => {
  const ranked = rankRecommendations([video(2), video(5), video(1)], now);
  assert.deepEqual(ranked.map((item) => item.id), [5, 2, 1]);
});
