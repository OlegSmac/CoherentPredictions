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
                                  SUM(
                                      CASE
                                          WHEN p.score = m.score THEN 3
                              
                                          WHEN
                                              (
                                                  LEFT(p.score, CHARINDEX(':', p.score) - 1) >
                                                  SUBSTRING(p.score, CHARINDEX(':', p.score) + 1, 10)
                              
                                                  AND
                              
                                                  LEFT(m.score, CHARINDEX(':', m.score) - 1) >
                                                  SUBSTRING(m.score, CHARINDEX(':', m.score) + 1, 10)
                                              )
                                          THEN 1
                              
                                          WHEN
                                              (
                                                  LEFT(p.score, CHARINDEX(':', p.score) - 1) <
                                                  SUBSTRING(p.score, CHARINDEX(':', p.score) + 1, 10)
                              
                                                  AND
                              
                                                  LEFT(m.score, CHARINDEX(':', m.score) - 1) <
                                                  SUBSTRING(m.score, CHARINDEX(':', m.score) + 1, 10)
                                              )
                                          THEN 1
                              
                                          WHEN
                                              (
                                                  LEFT(p.score, CHARINDEX(':', p.score) - 1) =
                                                  SUBSTRING(p.score, CHARINDEX(':', p.score) + 1, 10)
                              
                                                  AND
                              
                                                  LEFT(m.score, CHARINDEX(':', m.score) - 1) =
                                                  SUBSTRING(m.score, CHARINDEX(':', m.score) + 1, 10)
                                              )
                                          THEN 1
                              
                                          ELSE 0
                                      END
                                  ) AS points
                              FROM Users u
                              LEFT JOIN Predictions p ON p.user_id = u.user_id
                              LEFT JOIN Matches m ON m.match_id = p.match_id
                              WHERE m.score IS NOT NULL
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