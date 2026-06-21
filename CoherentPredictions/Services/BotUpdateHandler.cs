using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using CoherentPredictions.Models;
using CoherentPredictions.Options;
using Microsoft.Extensions.Options;
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

    private const string AddMatchCommand = "/addmatch";
    private const string SetScoreCommand = "/setscore";
    private const string AdminCommand = "/admin";

    private readonly ITelegramBotClient _botClient;
    private readonly UserService _userService;
    private readonly MatchService _matchService;
    private readonly PredictionService _predictionService;
    private readonly LeaderboardService _leaderboardService;
    private readonly IOptions<AdminOptions> _adminOptions;

    private readonly ConcurrentDictionary<long, PredictionSession> _sessions = new();
    private readonly ConcurrentDictionary<long, AdminSession> _adminSessions = new();

    public BotUpdateHandler(
        ITelegramBotClient botClient,
        UserService userService,
        MatchService matchService,
        PredictionService predictionService,
        LeaderboardService leaderboardService,
        IOptions<AdminOptions> adminOptions)
    {
        _botClient = botClient;
        _userService = userService;
        _matchService = matchService;
        _predictionService = predictionService;
        _leaderboardService = leaderboardService;
        _adminOptions = adminOptions;
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

        var telegramUserId = message.From.Id;

        var userId = await _userService.GetOrCreateUserAsync(
            telegramUserId,
            GetDisplayName(message.From),
            cancellationToken);

        if (text == "/start")
        {
            _sessions.TryRemove(chatId, out _);
            _adminSessions.TryRemove(chatId, out _);

            await SendMenuAsync(chatId, cancellationToken);
            return;
        }

        if (IsAdmin(telegramUserId))
        {
            if (await TryProcessAdminCommandAsync(chatId, text, cancellationToken))
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

            await _botClient.SendMessage(chatId, leaderboard, cancellationToken: cancellationToken);
            await SendMenuAsync(chatId, cancellationToken);
            return;
        }

        if (_sessions.ContainsKey(chatId))
        {
            await ProcessPredictionAsync(chatId, userId, text, cancellationToken);
            return;
        }

        await _botClient.SendMessage(
            chatId,
            "Unknown command. Choose an option from menu.",
            cancellationToken: cancellationToken);

        await SendMenuAsync(chatId, cancellationToken);
    }

    private async Task<bool> TryProcessAdminCommandAsync(
        long chatId,
        string text,
        CancellationToken cancellationToken)
    {
        if (text == AdminCommand)
        {
            await _botClient.SendMessage(
                chatId,
                """
                Admin commands:
                /addmatch - add new match
                /setscore - set match score
                """,
                cancellationToken: cancellationToken);

            return true;
        }

        if (text == AddMatchCommand)
        {
            _sessions.TryRemove(chatId, out _);

            _adminSessions[chatId] = new AdminSession
            {
                State = AdminState.AddingTeam1
            };

            await _botClient.SendMessage(chatId, "Enter team 1:", cancellationToken: cancellationToken);
            return true;
        }

        if (text == SetScoreCommand)
        {
            _sessions.TryRemove(chatId, out _);

            _adminSessions[chatId] = new AdminSession
            {
                State = AdminState.SettingScore
            };

            var matches = await _matchService.GetMatchesForScoreUpdateAsync(cancellationToken);

            await _botClient.SendMessage(
                chatId,
                matches + "\nSend match id and score, example: 15 2:1",
                cancellationToken: cancellationToken);

            return true;
        }

        if (_adminSessions.ContainsKey(chatId))
        {
            await ProcessAdminInputAsync(chatId, text, cancellationToken);
            return true;
        }

        return false;
    }

    private async Task ProcessAdminInputAsync(
        long chatId,
        string text,
        CancellationToken cancellationToken)
    {
        if (!_adminSessions.TryGetValue(chatId, out var session))
            return;

        switch (session.State)
        {
            case AdminState.AddingTeam1:
                session.Team1 = text;
                session.State = AdminState.AddingTeam2;

                await _botClient.SendMessage(chatId, "Enter team 2:", cancellationToken: cancellationToken);
                return;

            case AdminState.AddingTeam2:
                session.Team2 = text;
                session.State = AdminState.AddingDateTime;

                await _botClient.SendMessage(
                    chatId,
                    "Enter match date and time, example: 2026-06-22 21:00",
                    cancellationToken: cancellationToken);

                return;

            case AdminState.AddingDateTime:
                if (!DateTime.TryParse(text, out var dateTime))
                {
                    await _botClient.SendMessage(
                        chatId,
                        "Invalid datetime. Use format: 2026-06-22 21:00",
                        cancellationToken: cancellationToken);

                    return;
                }

                session.DateTime = dateTime;
                session.State = AdminState.AddingScore;

                await _botClient.SendMessage(
                    chatId,
                    "Enter score or '-' if match is not finished:",
                    cancellationToken: cancellationToken);

                return;

            case AdminState.AddingScore:
                var score = text == "-" ? null : text;

                if (score is not null && !IsValidScore(score))
                {
                    await _botClient.SendMessage(
                        chatId,
                        "Invalid score. Use format 2:1 or '-'",
                        cancellationToken: cancellationToken);

                    return;
                }

                await _matchService.AddMatchAsync(
                    session.Team1!,
                    session.Team2!,
                    session.DateTime!.Value,
                    score,
                    cancellationToken);

                _adminSessions.TryRemove(chatId, out _);

                await _botClient.SendMessage(
                    chatId,
                    "Match added successfully.",
                    cancellationToken: cancellationToken);

                await SendMenuAsync(chatId, cancellationToken);
                return;

            case AdminState.SettingScore:
                await ProcessSetScoreAsync(chatId, text, cancellationToken);
                return;
        }
    }

    private async Task ProcessSetScoreAsync(
        long chatId,
        string text,
        CancellationToken cancellationToken)
    {
        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length != 2 ||
            !int.TryParse(parts[0], out var matchId) ||
            !IsValidScore(parts[1]))
        {
            await _botClient.SendMessage(
                chatId,
                "Invalid format. Send: matchId score. Example: 15 2:1",
                cancellationToken: cancellationToken);

            return;
        }

        await _matchService.SetScoreAsync(matchId, parts[1], cancellationToken);

        _adminSessions.TryRemove(chatId, out _);

        await _botClient.SendMessage(
            chatId,
            "Score updated successfully.",
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
                chatId,
                "There are no available matches for prediction in the next 24 hours.",
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
                chatId,
                "Invalid score format. Enter score like 2:1",
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
            userId,
            match.MatchId,
            score,
            cancellationToken);

        session.CurrentIndex++;

        if (session.CurrentIndex >= session.Matches.Count)
        {
            _sessions.TryRemove(chatId, out _);

            await _botClient.SendMessage(
                chatId,
                "All predictions saved.",
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

        await _botClient.SendMessage(chatId, text, cancellationToken: cancellationToken);
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
            chatId,
            "Choose option:",
            replyMarkup: keyboard,
            cancellationToken: cancellationToken);
    }

    private bool IsAdmin(long telegramUserId)
    {
        return telegramUserId == _adminOptions.Value.TelegramUserId;
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