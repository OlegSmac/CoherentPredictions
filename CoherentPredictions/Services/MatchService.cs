using CoherentPredictions.Data;
using CoherentPredictions.Models;

namespace CoherentPredictions.Services;

public class MatchService
{
    private readonly SqlConnectionFactory _connectionFactory;

    public MatchService(SqlConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<List<Match>> GetAvailableMatchesAsync(
        int userId,
        CancellationToken cancellationToken)
    {
        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = """
                              SELECT m.match_id, m.team1, m.team2, m.score, m.[datetime]
                              FROM Matches m
                              WHERE m.score IS NULL
                                AND m.[datetime] > SYSDATETIME()
                                AND m.[datetime] <= DATEADD(HOUR, 24, SYSDATETIME())
                                AND NOT EXISTS (
                                    SELECT 1
                                    FROM Predictions p
                                    WHERE p.user_id = @userId
                                      AND p.match_id = m.match_id
                                )
                              ORDER BY m.[datetime];
                              """;

        command.Parameters.AddWithValue("@userId", userId);

        var matches = new List<Match>();

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            matches.Add(new Match
            {
                MatchId = reader.GetInt32(0),
                Team1 = reader.GetString(1),
                Team2 = reader.GetString(2),
                Score = reader.IsDBNull(3) ? null : reader.GetString(3),
                DateTime = reader.GetDateTime(4)
            });
        }

        return matches;
    }
}