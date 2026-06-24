namespace CoherentPredictions.Services.Notifications;

using CoherentPredictions.Data;
using Telegram.Bot;

public class NotificationService
{
    private const string NotificationMessage = "New matches are available, you can share your scores.\n\nTap \"Give me next matches for 24 hours\"";

    private readonly SqlConnectionFactory _connectionFactory;
    private readonly ITelegramBotClient _botClient;
    private readonly ILogger<NotificationService> _logger;

    public NotificationService(
        SqlConnectionFactory connectionFactory,
        ITelegramBotClient botClient,
        ILogger<NotificationService> logger)
    {
        _connectionFactory = connectionFactory;
        _botClient = botClient;
        _logger = logger;
    }

    public async Task SendDailyNotificationsAsync(CancellationToken cancellationToken)
    {
        var telegramUserIds = await GetUsersWithAvailableMatchesAsync(cancellationToken);

        foreach (var telegramUserId in telegramUserIds)
        {
            try
            {
                await _botClient.SendMessage(
                    chatId: telegramUserId,
                    text: NotificationMessage,
                    cancellationToken: cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to send notification to Telegram user {telegramUserId}");
            }
        }

        _logger.LogInformation($"Daily notifications sent to {telegramUserIds.Count} users");
    }

    private async Task<List<long>> GetUsersWithAvailableMatchesAsync(
        CancellationToken cancellationToken)
    {
        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT DISTINCT u.telegram_user_id
            FROM Users u
            WHERE EXISTS (
                SELECT 1
                FROM Matches m
                WHERE m.score IS NULL
                  AND m.[datetime] > DATEADD(HOUR, 3, SYSUTCDATETIME())
                  AND m.[datetime] <= DATEADD(HOUR, 27, SYSUTCDATETIME())
                  AND NOT EXISTS (
                      SELECT 1
                      FROM Predictions p
                      WHERE p.user_id = u.user_id
                        AND p.match_id = m.match_id
                  )
            );
            """;

        var result = new List<long>();

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(reader.GetInt64(0));
        }

        return result;
    }
}