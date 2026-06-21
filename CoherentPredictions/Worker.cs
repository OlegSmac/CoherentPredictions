using CoherentPredictions.Services;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types.Enums;

namespace CoherentPredictions;

public class Worker : BackgroundService
{
    private readonly ITelegramBotClient _botClient;
    private readonly BotUpdateHandler _handler;

    public Worker(
        ITelegramBotClient botClient,
        BotUpdateHandler handler)
    {
        _botClient = botClient;
        _handler = handler;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var options = new ReceiverOptions
        {
            AllowedUpdates = [UpdateType.Message]
        };

        _botClient.StartReceiving(
            _handler.HandleUpdateAsync,
            _handler.HandleErrorAsync,
            options,
            stoppingToken);

        await Task.Delay(Timeout.Infinite, stoppingToken);
    }
}