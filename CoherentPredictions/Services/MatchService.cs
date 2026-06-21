using System.Text;
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
                                AND m.[datetime] > DATEADD(HOUR, 3, SYSDATETIME())
                                AND m.[datetime] <= DATEADD(HOUR, 27, SYSDATETIME())
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
    
    public async Task AddMatchAsync(
        string team1,
        string team2,
        DateTime dateTime,
        string? score,
        CancellationToken cancellationToken)
    {
        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = """
                              INSERT INTO Matches (team1, team2, score, [datetime])
                              VALUES (@team1, @team2, @score, @datetime);
                              """;

        command.Parameters.AddWithValue("@team1", team1);
        command.Parameters.AddWithValue("@team2", team2);
        command.Parameters.AddWithValue("@score", score is null ? DBNull.Value : score);
        command.Parameters.AddWithValue("@datetime", dateTime);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }
    
    public async Task SetScoreAsync(
        int matchId,
        string score,
        CancellationToken cancellationToken)
    {
        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = """
                              UPDATE Matches
                              SET score = @score
                              WHERE match_id = @matchId;
                              """;

        command.Parameters.AddWithValue("@matchId", matchId);
        command.Parameters.AddWithValue("@score", score);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }
    
    public async Task<string> GetMatchesForScoreUpdateAsync(CancellationToken cancellationToken)
    {
        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = """
                              SELECT TOP 20 match_id, team1, team2, score, [datetime]
                              FROM Matches
                              ORDER BY [datetime] DESC;
                              """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var sb = new StringBuilder();
        sb.AppendLine("Matches:");
        sb.AppendLine();

        while (await reader.ReadAsync(cancellationToken))
        {
            var id = reader.GetInt32(0);
            var team1 = reader.GetString(1);
            var team2 = reader.GetString(2);
            var score = reader.IsDBNull(3) ? "-" : reader.GetString(3);
            var dateTime = reader.GetDateTime(4);

            sb.AppendLine($"{id}. {team1} - {team2} | {score} | {dateTime:yyyy-MM-dd HH:mm}");
        }

        return sb.ToString();
    }
}