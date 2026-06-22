using System.Text;
using CoherentPredictions.Data;

namespace CoherentPredictions.Services;

public class LeaderboardService
{
    private readonly SqlConnectionFactory _connectionFactory;

    public LeaderboardService(SqlConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<string> GetLeaderboardTextAsync(CancellationToken cancellationToken)
    {
        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = """
                              SELECT
                                  u.name,
                                  COALESCE(SUM(
                                      CASE
                                          WHEN p.score = m.score THEN 3

                                          WHEN ps.p1 IS NULL OR ps.p2 IS NULL OR ms.m1 IS NULL OR ms.m2 IS NULL THEN 0

                                          WHEN ps.p1 > ps.p2 AND ms.m1 > ms.m2 THEN 1
                                          WHEN ps.p1 < ps.p2 AND ms.m1 < ms.m2 THEN 1
                                          WHEN ps.p1 = ps.p2 AND ms.m1 = ms.m2 THEN 1

                                          ELSE 0
                                      END
                                  ), 0) AS points
                              FROM Users u
                              LEFT JOIN Predictions p ON p.user_id = u.user_id
                              LEFT JOIN Matches m ON m.match_id = p.match_id AND m.score IS NOT NULL

                              OUTER APPLY (
                                  SELECT
                                      TRY_CONVERT(INT, LEFT(LTRIM(RTRIM(p.score)), CHARINDEX(':', LTRIM(RTRIM(p.score))) - 1)) AS p1,
                                      TRY_CONVERT(INT, SUBSTRING(LTRIM(RTRIM(p.score)), CHARINDEX(':', LTRIM(RTRIM(p.score))) + 1, 10)) AS p2
                                  WHERE p.score IS NOT NULL AND CHARINDEX(':', p.score) > 0
                              ) ps

                              OUTER APPLY (
                                  SELECT
                                      TRY_CONVERT(INT, LEFT(LTRIM(RTRIM(m.score)), CHARINDEX(':', LTRIM(RTRIM(m.score))) - 1)) AS m1,
                                      TRY_CONVERT(INT, SUBSTRING(LTRIM(RTRIM(m.score)), CHARINDEX(':', LTRIM(RTRIM(m.score))) + 1, 10)) AS m2
                                  WHERE m.score IS NOT NULL AND CHARINDEX(':', m.score) > 0
                              ) ms

                              GROUP BY u.user_id, u.name
                              ORDER BY points DESC;
                              """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var sb = new StringBuilder();
        sb.AppendLine("🏆 Leaderboard");
        sb.AppendLine();

        var place = 1;

        while (await reader.ReadAsync(cancellationToken))
        {
            var name = reader.GetString(0);
            var points = reader.GetInt32(1);

            sb.AppendLine($"{place}. {name} — {points} points");
            place++;
        }

        if (place == 1)
            return "Leaderboard is empty.";

        return sb.ToString();
    }
}