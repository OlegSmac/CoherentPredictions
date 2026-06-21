using CoherentPredictions.Data;

namespace CoherentPredictions.Services;

public class UserService
{
    private readonly SqlConnectionFactory _connectionFactory;

    public UserService(SqlConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<int> GetOrCreateUserAsync(
        long telegramUserId,
        string name,
        CancellationToken cancellationToken)
    {
        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);

        var selectCommand = connection.CreateCommand();
        selectCommand.CommandText = """
                                    SELECT user_id
                                    FROM Users
                                    WHERE telegram_user_id = @telegramUserId;
                                    """;

        selectCommand.Parameters.AddWithValue("@telegramUserId", telegramUserId);

        var existingUserId = await selectCommand.ExecuteScalarAsync(cancellationToken);

        if (existingUserId is not null)
            return Convert.ToInt32(existingUserId);

        var insertCommand = connection.CreateCommand();
        insertCommand.CommandText = """
                                    INSERT INTO Users (telegram_user_id, name)
                                    OUTPUT INSERTED.user_id
                                    VALUES (@telegramUserId, @name);
                                    """;

        insertCommand.Parameters.AddWithValue("@telegramUserId", telegramUserId);
        insertCommand.Parameters.AddWithValue("@name", name);

        var newUserId = await insertCommand.ExecuteScalarAsync(cancellationToken);

        return Convert.ToInt32(newUserId);
    }
}