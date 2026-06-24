using System.Text;
using CoherentPredictions.Data;

namespace CoherentPredictions.Services;

public class PredictionService
{
    private readonly SqlConnectionFactory _connectionFactory;

    public PredictionService(SqlConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task SavePredictionAsync(
        int userId,
        int matchId,
        string score,
        CancellationToken cancellationToken)
    {
        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = """
                              INSERT INTO Predictions (user_id, match_id, score)
                              VALUES (@userId, @matchId, @score);
                              """;

        command.Parameters.AddWithValue("@userId", userId);
        command.Parameters.AddWithValue("@matchId", matchId);
        command.Parameters.AddWithValue("@score", score);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }
    
    public async Task<string> GetUserPredictionsTextAsync(
        int userId,
        CancellationToken cancellationToken)
    {
        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = """
        SELECT
            m.team1,
            m.team2,
            p.score AS prediction,
            m.score AS final_score,
            CASE
                WHEN m.score IS NULL THEN NULL
                WHEN p.score = m.score THEN 3

                WHEN ps.p1 > ps.p2 AND ms.m1 > ms.m2 THEN 1
                WHEN ps.p1 < ps.p2 AND ms.m1 < ms.m2 THEN 1
                WHEN ps.p1 = ps.p2 AND ms.m1 = ms.m2 THEN 1

                ELSE 0
            END AS points
        FROM Predictions p
        INNER JOIN Matches m ON m.match_id = p.match_id

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

        WHERE p.user_id = @userId
        ORDER BY m.[datetime] ASC;
        """;

        command.Parameters.AddWithValue("@userId", userId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var sb = new StringBuilder();
        sb.AppendLine("📋 My predictions");
        sb.AppendLine();

        var total = 0;
        var hasRows = false;

        while (await reader.ReadAsync(cancellationToken))
        {
            hasRows = true;

            var team1 = reader.GetString(0);
            var team2 = reader.GetString(1);
            var prediction = reader.GetString(2);
            var finalScore = reader.IsDBNull(3) ? "-" : reader.GetString(3);
            var points = reader.IsDBNull(4) ? 0 : reader.GetInt32(4);

            total += points;

            sb.AppendLine($"{team1} - {team2}");
            sb.AppendLine($"Your prediction: {prediction}");
            sb.AppendLine($"Final score: {finalScore}");
            sb.AppendLine($"Points: {points}");
            sb.AppendLine();
        }

        if (!hasRows)
            return "You don't have predictions yet.";

        sb.AppendLine($"Total points: {total}");

        return sb.ToString();
    }
}