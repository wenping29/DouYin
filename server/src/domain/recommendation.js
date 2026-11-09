const LIKE_WEIGHT = 3;
const COMMENT_WEIGHT = 5;
const SHARE_WEIGHT = 4;
const VIEW_WEIGHT = 0.1;
const BASE_ENGAGEMENT = 2;
const AGE_OFFSET_HOURS = 2;
const AGE_DECAY = 0.55;

export function recommendationScore(row, now = Date.now()) {
  const ageHours = Math.max(0, (now - Date.parse(row.created_at)) / 3600000);
  const engagement =
    Number(row.like_count) * LIKE_WEIGHT +
    Number(row.comment_count) * COMMENT_WEIGHT +
    Number(row.share_count) * SHARE_WEIGHT +
    Number(row.view_count) * VIEW_WEIGHT +
    BASE_ENGAGEMENT;
  return engagement / Math.pow(ageHours + AGE_OFFSET_HOURS, AGE_DECAY);
}

export function rankRecommendations(rows, now = Date.now()) {
  return [...rows].sort((a, b) => {
    const diff = recommendationScore(b, now) - recommendationScore(a, now);
    return diff !== 0 ? diff : b.id - a.id;
  });
}
