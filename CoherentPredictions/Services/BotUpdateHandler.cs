using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using CoherentPredictions.Models;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace CoherentPredictions.Services;

public class BotUpdateHandler
{
    private const string PredictButtonText = "Give me next matches for 24 hours";
    private const string LeaderboardButtonText = "Leaderboard";

    private readonly ITelegramBotClient _botClient;
    private readonly UserService _userService;
    private readonly MatchService _matchService;
    private readonly PredictionService _predictionService;
    private readonly LeaderboardService _leaderboardService;

    private readonly ConcurrentDictionary<long, PredictionSession> _sessions = new();

    public BotUpdateHandler(
        ITelegramBotClient botClient,
        UserService userService,
        MatchService matchService,
        PredictionService predictionService,
        LeaderboardService leaderboardService)
    {
        _botClient = botClient;
        _userService = userService;
        _matchService = matchService;
        _predictionService = predictionService;
        _leaderboardService = leaderboardService;
    }

    public async Task HandleUpdateAsync(
        ITelegramBotClient botClient,
        Update update,
        CancellationToken cancellationToken)
    {
        if (update.Type != UpdateType.Message || update.Message?.Text is null)
            return;

        var message = update.Message;
        var chatId = message.Chat.Id;
        var text = message.Text.Trim();

        if (message.From is null)
            return;

        var userId = await _userService.GetOrCreateUserAsync(
            telegramUserId: message.From.Id,
            name: GetDisplayName(message.From),
            cancellationToken);

        if (text == "/start")
        {
            _sessions.TryRemove(chatId, out _);
            await SendMenuAsync(chatId, cancellationToken);
            return;
        }

        if (text == PredictButtonText)
        {
            await StartPredictionFlowAsync(chatId, userId, cancellationToken);
            return;
        }

        if (text == LeaderboardButtonText)
        {
            _sessions.TryRemove(chatId, out _);

            var leaderboard = await _leaderboardService.GetLeaderboardTextAsync(cancellationToken);

            await _botClient.SendMessage(
                chatId: chatId,
                text: leaderboard,
                cancellationToken: cancellationToken);

            await SendMenuAsync(chatId, cancellationToken);
            return;
        }

        if (_sessions.ContainsKey(chatId))
        {
            await ProcessPredictionAsync(chatId, userId, text, cancellationToken);
            return;
        }

        await _botClient.SendMessage(
            chatId: chatId,
            text: "Unknown command. Choose an option from menu.",
            cancellationToken: cancellationToken);

        await SendMenuAsync(chatId, cancellationToken);
    }

    private async Task StartPredictionFlowAsync(
        long chatId,
        int userId,
        CancellationToken cancellationToken)
    {
        var matches = await _matchService.GetAvailableMatchesAsync(userId, cancellationToken);

        if (matches.Count == 0)
        {
            _sessions.TryRemove(chatId, out _);

            await _botClient.SendMessage(
                chatId: chatId,
                text: "There are no available matches for prediction in the next 24 hours.",
                cancellationToken: cancellationToken);

            await SendMenuAsync(chatId, cancellationToken);
            return;
        }

        _sessions[chatId] = new PredictionSession
        {
            Matches = matches,
            CurrentIndex = 0
        };

        await SendCurrentMatchAsync(chatId, cancellationToken);
    }

    private async Task ProcessPredictionAsync(
        long chatId,
        int userId,
        string score,
        CancellationToken cancellationToken)
    {
        if (!IsValidScore(score))
        {
            await _botClient.SendMessage(
                chatId: chatId,
                text: "Invalid score format. Enter score like 2:1",
                cancellationToken: cancellationToken);

            return;
        }

        if (!_sessions.TryGetValue(chatId, out var session))
        {
            await SendMenuAsync(chatId, cancellationToken);
            return;
        }

        var match = session.Matches[session.CurrentIndex];

        await _predictionService.SavePredictionAsync(
            userId: userId,
            matchId: match.MatchId,
            score: score,
            cancellationToken: cancellationToken);

        session.CurrentIndex++;

        if (session.CurrentIndex >= session.Matches.Count)
        {
            _sessions.TryRemove(chatId, out _);

            await _botClient.SendMessage(
                chatId: chatId,
                text: "All predictions saved.",
                cancellationToken: cancellationToken);

            await SendMenuAsync(chatId, cancellationToken);
            return;
        }

        await SendCurrentMatchAsync(chatId, cancellationToken);
    }

    private async Task SendCurrentMatchAsync(
        long chatId,
        CancellationToken cancellationToken)
    {
        if (!_sessions.TryGetValue(chatId, out var session))
            return;

        var match = session.Matches[session.CurrentIndex];

        var text = $"""
        Match {session.CurrentIndex + 1}/{session.Matches.Count}

        {match.Team1} - {match.Team2}
        Time: {match.DateTime:yyyy-MM-dd HH:mm}

        Enter your prediction, for example: 2:1
        """;

        await _botClient.SendMessage(
            chatId: chatId,
            text: text,
            cancellationToken: cancellationToken);
    }

    private async Task SendMenuAsync(
        long chatId,
        CancellationToken cancellationToken)
    {
        var keyboard = new ReplyKeyboardMarkup(new[]
        {
            new KeyboardButton[] { PredictButtonText },
            new KeyboardButton[] { LeaderboardButtonText }
        })
        {
            ResizeKeyboard = true
        };

        await _botClient.SendMessage(
            chatId: chatId,
            text: "Choose option:",
            replyMarkup: keyboard,
            cancellationToken: cancellationToken);
    }

    private static bool IsValidScore(string text)
    {
        return Regex.IsMatch(text, @"^\d{1,2}:\d{1,2}$");
    }

    private static string GetDisplayName(Telegram.Bot.Types.User user)
    {
        if (!string.IsNullOrWhiteSpace(user.Username))
            return user.Username;

        var fullName = $"{user.FirstName} {user.LastName}".Trim();

        return string.IsNullOrWhiteSpace(fullName)
            ? $"user_{user.Id}"
            : fullName;
    }

    public Task HandleErrorAsync(
        ITelegramBotClient botClient,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var message = exception switch
        {
            ApiRequestException apiException =>
                $"Telegram API Error: [{apiException.ErrorCode}] {apiException.Message}",

            _ => exception.ToString()
        };

        Console.WriteLine(message);

        return Task.CompletedTask;
    }
}