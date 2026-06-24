namespace CoherentPredictions.Services.Notifications;

public class NotificationWorker : BackgroundService
{
    private readonly NotificationService _notificationService;
    private readonly ILogger<NotificationWorker> _logger;

    public NotificationWorker(
        NotificationService notificationService,
        ILogger<NotificationWorker> logger)
    {
        _notificationService = notificationService;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Chisinau");

        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = GetDelayUntilNextRun(timeZone);

            _logger.LogInformation($"Next notification check in {delay}");

            await Task.Delay(delay, stoppingToken);

            _logger.LogInformation("Daily notification check started");

            await _notificationService.SendDailyNotificationsAsync(stoppingToken);
        }
    }

    private static TimeSpan GetDelayUntilNextRun(TimeZoneInfo timeZone)
    {
        var nowUtc = DateTimeOffset.UtcNow;
        var nowLocal = TimeZoneInfo.ConvertTime(nowUtc, timeZone);

        var nextRunLocal = new DateTimeOffset(
            nowLocal.Year,
            nowLocal.Month,
            nowLocal.Day,
            18,
            0,
            0,
            nowLocal.Offset);

        if (nowLocal >= nextRunLocal)
            nextRunLocal = nextRunLocal.AddDays(1);

        var nextRunUtc = TimeZoneInfo.ConvertTime(nextRunLocal, TimeZoneInfo.Utc);

        return nextRunUtc - nowUtc;
    }
}