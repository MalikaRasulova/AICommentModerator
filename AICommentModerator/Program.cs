using System.Text.Json;
using System.Text.Json.Serialization;
using AICommentModerator.Application.Abstractions;
using AICommentModerator.Application.Bot;
using AICommentModerator.Application.Moderation;
using AICommentModerator.Application.Options;
using AICommentModerator.Infrastructure.Ai;
using AICommentModerator.Infrastructure.Persistence;
using AICommentModerator.Infrastructure.Telegram;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

// Behaviour of the bot itself lives in its own file, watched for changes.
builder.Configuration.AddJsonFile("bot.config.json", optional: true, reloadOnChange: true);

// ---- configuration -------------------------------------------------------
builder.Services.AddOptions<TelegramOptions>()
    .Bind(builder.Configuration.GetSection(TelegramOptions.Section))
    .ValidateDataAnnotations();

builder.Services.AddOptions<OpenAiOptions>()
    .Bind(builder.Configuration.GetSection(OpenAiOptions.Section))
    .ValidateDataAnnotations();

builder.Services.AddOptions<ModerationOptions>()
    .Bind(builder.Configuration.GetSection(ModerationOptions.Section))
    .ValidateDataAnnotations();

builder.Services.AddOptions<BotOptions>()
    .Bind(builder.Configuration.GetSection(BotOptions.Section))
    .ValidateDataAnnotations();

var openAi = builder.Configuration.GetSection(OpenAiOptions.Section).Get<OpenAiOptions>() ?? new OpenAiOptions();
var telegram = builder.Configuration.GetSection(TelegramOptions.Section).Get<TelegramOptions>() ?? new TelegramOptions();
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");

// ---- web -----------------------------------------------------------------
builder.Services.AddControllers().AddJsonOptions(json =>
{
    // Telegram speaks snake_case; the C# models stay PascalCase.
    json.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
    json.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    json.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// ---- moderation ----------------------------------------------------------
builder.Services.AddSingleton<RuleBasedModerationService>();

if (openAi.IsConfigured)
{
    builder.Services.AddHttpClient<IModerationService, OpenAiModerationService>(http =>
    {
        http.BaseAddress = new Uri(openAi.BaseUrl.EndsWith('/') ? openAi.BaseUrl : openAi.BaseUrl + "/");
        http.Timeout = TimeSpan.FromSeconds(openAi.TimeoutSeconds);
    });

    builder.Services.AddHttpClient<IReplyGenerator, OpenAiReplyGenerator>(http =>
    {
        http.BaseAddress = new Uri(openAi.BaseUrl.EndsWith('/') ? openAi.BaseUrl : openAi.BaseUrl + "/");
        http.Timeout = TimeSpan.FromSeconds(openAi.TimeoutSeconds);
    });
}
else
{
    // No API key: the rule engine alone keeps the service fully functional.
    builder.Services.AddSingleton<IModerationService>(sp => sp.GetRequiredService<RuleBasedModerationService>());
}

// ---- bot behaviour -------------------------------------------------------
builder.Services.AddSingleton<BotPolicy>();
builder.Services.AddSingleton<WorkingHoursCalendar>();
builder.Services.AddSingleton<IReplyService>(sp => new ReplyService(
    sp.GetRequiredService<BotPolicy>(),
    sp.GetRequiredService<WorkingHoursCalendar>(),
    sp.GetRequiredService<ILogger<ReplyService>>(),
    sp.GetService<IReplyGenerator>()));

// ---- telegram ------------------------------------------------------------
builder.Services.AddHttpClient<ITelegramClient, TelegramClient>(http =>
{
    http.BaseAddress = new Uri("https://api.telegram.org/");
    http.Timeout = TimeSpan.FromSeconds(telegram.TimeoutSeconds);
});

// ---- storage -------------------------------------------------------------
var hasDatabase = !string.IsNullOrWhiteSpace(connectionString);

builder.Services.AddSingleton<InMemoryAuditLog>();

if (hasDatabase)
{
    builder.Services.AddDbContext<ApplicationDbContext>(options => options.UseNpgsql(connectionString));
    builder.Services.AddScoped<EfAuditLog>();

    // Writes to PostgreSQL, falls back to memory if it is unreachable.
    builder.Services.AddScoped<IAuditLog, ResilientAuditLog>();
}
else
{
    // Still runs without PostgreSQL - the history just lives in memory.
    builder.Services.AddSingleton<IAuditLog>(sp => sp.GetRequiredService<InMemoryAuditLog>());
}

var app = builder.Build();

// ---- database migration --------------------------------------------------
if (hasDatabase && app.Configuration.GetValue("Database:MigrateOnStartup", true))
{
    using var scope = app.Services.CreateScope();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    try
    {
        scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.Migrate();
        logger.LogInformation("Database schema is up to date");
    }
    catch (Exception ex)
    {
        // A missing database must not stop the webhook from answering Telegram.
        logger.LogError(ex, "Database migration failed; the service starts without it");
    }
}

// ---- who am I ------------------------------------------------------------
// Mention rules need the bot username; ask Telegram once unless the file states it.
var botOptions = app.Services.GetRequiredService<IOptionsMonitor<BotOptions>>().CurrentValue;
if (string.IsNullOrWhiteSpace(botOptions.Username) && telegram.IsConfigured)
{
    var identity = await app.Services.GetRequiredService<ITelegramClient>().GetMeAsync();
    if (!string.IsNullOrWhiteSpace(identity))
    {
        botOptions.Username = identity;
        app.Services.GetRequiredService<ILogger<Program>>().LogInformation("Bot username resolved as @{Username}", identity);
    }
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapControllers();

// ---- health --------------------------------------------------------------
app.MapGet("/health", (IServiceProvider services) =>
{
    var openAiOptions = services.GetRequiredService<IOptionsMonitor<OpenAiOptions>>().CurrentValue;
    var telegramOptions = services.GetRequiredService<IOptionsMonitor<TelegramOptions>>().CurrentValue;
    var bot = services.GetRequiredService<IOptionsMonitor<BotOptions>>().CurrentValue;

    return Results.Ok(new
    {
        status = "ok",
        moderation = openAiOptions.IsConfigured ? "openai + rules" : "rules only",
        telegram = telegramOptions.IsConfigured ? "configured" : "token missing",
        storage = hasDatabase ? "postgres (falls back to memory)" : "in-memory",
        webhookSecret = string.IsNullOrWhiteSpace(telegramOptions.WebhookSecret) ? "not set" : "set",
        replies = bot.Replies.Enabled ? bot.Replies.RespondTo : "off",
        workingHours = bot.WorkingHours.Enabled
            ? $"{bot.WorkingHours.From}-{bot.WorkingHours.To} {bot.WorkingHours.TimeZone} (open now: {services.GetRequiredService<WorkingHoursCalendar>().IsOpen()})"
            : "around the clock",
        scope = bot.Scope.IsAllowlist ? $"allowlist ({bot.Scope.AllowedChatIds.Length} chats)" : "all chats"
    });
});

app.Run();

/// <summary>Exposed so the test project can spin the app up with WebApplicationFactory.</summary>
public partial class Program;
