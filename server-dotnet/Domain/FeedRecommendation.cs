namespace DouYing.Server.Domain;

public static class FeedRecommendation
{
    private const double LikeWeight = 3;
    private const double CommentWeight = 5;
    private const double ShareWeight = 4;
    private const double ViewWeight = 0.1;
    private const double BaseEngagement = 2;
    private const double AgeOffsetHours = 2;
    private const double AgeDecay = 0.55;

    public static double Score(
        DateTimeOffset createdAt,
        double likes,
        double comments,
        double shares,
        double views,
        DateTimeOffset now)
    {
        var ageHours = Math.Max(0, (now - createdAt).TotalHours);
        var engagement = likes * LikeWeight + comments * CommentWeight +
                         shares * ShareWeight + views * ViewWeight + BaseEngagement;
        return engagement / Math.Pow(ageHours + AgeOffsetHours, AgeDecay);
    }
}
