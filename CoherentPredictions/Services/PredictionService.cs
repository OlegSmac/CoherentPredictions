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
}