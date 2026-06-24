using CoherentPredictions;

using Telegram.Bot;
using CoherentPredictions.Data;
using CoherentPredictions.Options;
using CoherentPredictions.Services;
using CoherentPredictions.Services.Notifications;
using Microsoft.Extensions.Options;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.Configure<TelegramOptions>(builder.Configuration.GetSection(TelegramOptions.SectionName));
builder.Services.Configure<SqlOptions>(builder.Configuration.GetSection(SqlOptions.SectionName));
builder.Services.Configure<AdminOptions>(builder.Configuration.GetSection(AdminOptions.SectionName));

builder.Services.AddSingleton<ITelegramBotClient>(sp =>
{
    var options = sp.GetRequiredService<IOptions<TelegramOptions>>().Value;
    return new TelegramBotClient(options.BotToken);
});

builder.Services.AddSingleton<SqlConnectionFactory>();

builder.Services.AddSingleton<UserService>();
builder.Services.AddSingleton<MatchService>();
builder.Services.AddSingleton<PredictionService>();
builder.Services.AddSingleton<LeaderboardService>();

builder.Services.AddSingleton<BotUpdateHandler>();
builder.Services.AddHostedService<Worker>();

builder.Services.AddSingleton<NotificationService>();
builder.Services.AddHostedService<NotificationWorker>();

var host = builder.Build();
host.Run();