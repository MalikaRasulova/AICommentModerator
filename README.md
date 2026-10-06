# AICommentModerator

A Telegram comment moderator built on ASP.NET Core 8. Telegram posts an update to the
webhook, the comment is judged, offending messages are deleted from the chat, and every
decision is written to an audit log you can read back over HTTP.

Two engines decide together:

| Engine | Needs | What it catches |
|---|---|---|
| **Rules** | nothing | banned words, link spam, channel promotion, shouting, character noise, overlong text |
| **OpenAI** | an API key | insults, threats, scams and everything a word list cannot express |

The rule engine always runs first — a banned word never costs an API call — and it is also
the fallback for every failure path: no API key, HTTP error, timeout, or an answer the model
did not format correctly. **The service never stops moderating because OpenAI is unavailable.**

The stricter of the two verdicts wins, so the model can never soften a rule hit.

```
Telegram ──POST /api/telegram/webhook──▶ secret header check
                                           │
                                           ▼
                                      rule engine ──Block──▶ delete message
                                           │                      │
                                      (Allow/Flag)                │
                                           ▼                      │
                                      OpenAI verdict ─────────────┤
                                           │                      ▼
                                           └──────────────▶ audit log (Postgres or memory)
```

## Quick start

Runs with no API key and no database — the rule engine and an in-memory log keep it fully
functional, which makes it easy to try:

```sh
cd AICommentModerator
dotnet run
```

Open <http://localhost:5165/swagger> and try a comment:

```sh
curl -X POST http://localhost:5165/api/telegram/check \
  -H "Content-Type: application/json" \
  -d '{"text":"obuna bo'"'"'ling t.me/somechannel"}'
```

```json
{
  "decision": "Flag",
  "reason": "Needs a human look: channel-promotion",
  "categories": ["channel-promotion"],
  "confidence": 0.6,
  "source": "rules"
}
```

`GET /health` reports which engines are actually live:

```json
{ "status": "ok", "moderation": "rules only", "telegram": "token missing", "storage": "in-memory" }
```

### With PostgreSQL

```sh
docker compose up -d          # postgres on :5433
cd AICommentModerator
dotnet run                    # the schema is migrated on startup
```

The container publishes **5433**, so a PostgreSQL already installed on the machine keeps
5432 to itself. The matching connection string is already in `appsettings.Development.json`. Set
`ConnectionStrings:DefaultConnection` for other environments; leave it empty and the
service falls back to the in-memory log.

### With OpenAI and a real bot

Keep secrets out of the repository — use user-secrets locally:

```sh
cd AICommentModerator
dotnet user-secrets init
dotnet user-secrets set "OpenAI:ApiKey" "sk-..."
dotnet user-secrets set "Telegram:BotToken" "123456:ABC-DEF..."
dotnet user-secrets set "Telegram:WebhookSecret" "$(openssl rand -hex 16)"
```

In production the same values come from environment variables:
`OpenAI__ApiKey`, `Telegram__BotToken`, `Telegram__WebhookSecret`.

## Pointing Telegram at the service

The webhook needs a public HTTPS URL; [ngrok](https://ngrok.com) is enough for testing:

```sh
ngrok http 5165
```

Register the webhook with the same secret the service expects:

```sh
curl "https://api.telegram.org/bot<TOKEN>/setWebhook" \
  -d "url=https://<your-domain>/api/telegram/webhook" \
  -d "secret_token=<WebhookSecret>"
```

Telegram then sends that value in the `X-Telegram-Bot-Api-Secret-Token` header, and requests
without it are answered with 401. Add the bot to the group, give it the **Delete messages**
admin right, and turn group privacy off in @BotFather so it can see every comment.

## Endpoints

| Method | Path | Purpose |
|---|---|---|
| `POST` | `/api/telegram/webhook` | Telegram updates. Validates the secret header, moderates, deletes, logs. |
| `POST` | `/api/telegram/check` | Moderate a piece of text without touching Telegram. `{"text":"..."}` |
| `GET` | `/api/moderation/recent?take=50` | Latest decisions, newest first. |
| `GET` | `/api/bot/config` | The bot configuration currently in effect. |
| `GET` | `/health` | Which engines and storage are active. |
| `GET` | `/swagger` | API explorer (Development only). |

## Bot behaviour — `bot.config.json`

Who the bot watches, who it leaves alone, what it does after each verdict and which comments
it answers all live in `bot.config.json`, separate from the technical settings. The file is
watched, so **edits take effect without a restart** — `GET /api/bot/config` shows what is in
effect right now.

### Where it works

```json
"Scope": {
  "Mode": "AllChats",          // or "Allowlist"
  "AllowedChatIds": [ -1001234567890 ],
  "IgnoreBots": true,          // never moderate another bot
  "IgnoreChannelPosts": true,  // skip the channel owner's own posts
  "IgnoreEditedMessages": false
}
```

### Who it never touches

```json
"Exempt": {
  "Usernames": [ "malika53" ], // without the @, case-insensitive
  "UserIds": [ 123456789 ],
  "SkipModeration": true       // false = still judged, just never deleted
}
```

### What happens after a verdict

```json
"Actions": {
  "OnBlock": { "Delete": true,  "WarnAuthor": true,  "NotifyModerators": true },
  "OnFlag":  { "Delete": false, "WarnAuthor": false, "NotifyModerators": true },
  "OnAllow": { "Delete": false, "WarnAuthor": false, "NotifyModerators": false }
}
```

Notifications go to `Moderators.ChatId` (0 turns them off). Both templates take placeholders:
`{decision}`, `{reason}`, `{author}`, `{chat}`, `{text}` for the moderator note, and `{reason}`
for the warning posted in the chat.

### Which comments get an answer

```json
"Replies": {
  "Enabled": true,
  "RespondTo": "MentionsAndReplies",   // Nobody | MentionsAndReplies | Everyone
  "CooldownSeconds": 30,               // per person, per chat
  "MaxRepliesPerChatPerHour": 20,      // ceiling for the whole chat
  "UseAiWhenNoRuleMatches": false,     // needs an OpenAI key

  "Rules": [
    {
      "Name": "price",
      "Match": "Regex",                 // or "Keyword" (whole word, case-insensitive)
      "Patterns": [ "narx|qancha turadi|сколько стоит|price" ],
      "Reply": "Narxlar haqida to'liq ma'lumotni shaxsiy xabarda yuboramiz.",
      "OnlyForUsernames": []            // empty = anyone
    }
  ]
}
```

Rules are checked in order and the first match wins. `OnlyForUsernames` makes a rule personal —
useful for answering a client or a colleague differently from everyone else. A rule with a
broken regex is skipped rather than taking the bot down. Nothing is ever answered twice inside
the cooldown, blocked comments are never answered at all, and `Everyone` only ever applies to
comments that passed moderation.

`UseAiWhenNoRuleMatches` hands anything unmatched to the model, which answers briefly in the
commenter's own language (`OpenAI:ReplyPrompt` sets the tone).

### Working hours

```json
"WorkingHours": {
  "Enabled": false,
  "TimeZone": "Asia/Tashkent",
  "Days": [ "Monday", "Tuesday", "Wednesday", "Thursday", "Friday" ],
  "From": "09:00",
  "To": "18:00",                      // earlier than From = a shift through midnight
  "Holidays": [ "2026-01-01" ],

  "Outside": {
    "Moderate": true,                 // keep deleting rule-breakers at night
    "NotifyModerators": true,
    "Replies": "AutoReply",           // Silent | AutoReply
    "Message": "Hozir ish vaqtimiz emas. Dushanba-juma, 9:00-18:00 oralig'ida javob beramiz."
  }
}
```

Moderation and replies are separate questions here. Outside the schedule the bot normally
keeps deleting rule-breaking comments (`Moderate: true`) but stops answering in its usual
voice: `AutoReply` sends the away message once per person, `Silent` says nothing at all.
Set `Moderate: false` and the bot ignores the chat entirely until the next working day.

`Enabled: false` — the default — means the bot behaves the same at any hour. An unknown time
zone falls back to UTC with a warning rather than crashing, and an unreadable `From`/`To` is
treated as always open.

### Different rules in one chat

```json
"PerChat": [
  {
    "ChatId": -1001234567890,
    "Enabled": true,            // false switches the bot off in this chat only
    "RepliesEnabled": false,    // moderate, but stay silent here
    "ExtraBannedWords": [ "spoiler" ]
  }
]
```

## Technical settings — `appsettings.json`

| Key | Default | Meaning |
|---|---|---|
| `Telegram:BotToken` | empty | Bot token. Empty means decisions are logged but nothing is deleted. |
| `Telegram:WebhookSecret` | empty | Expected secret header. Empty disables the check (fine for local runs). |
| `Telegram:DeleteBlockedMessages` | `true` | Delete a message when the verdict is Block. |
| `Telegram:ReplyOnBlock` | `false` | Post a short note in the chat after deleting. |
| `OpenAI:ApiKey` | empty | Empty means rules only. |
| `OpenAI:Model` | `gpt-4o-mini` | Any chat model that supports JSON output. |
| `OpenAI:Policy` | see `OpenAiOptions` | House rules sent as the system prompt. |
| `Moderation:BannedWords` | `[]` | Always Block. Whole-word, case-insensitive. |
| `Moderation:SuspiciousWords` | promo, casino, … | Raise a Flag. |
| `Moderation:MaxLinks` | `2` | More links than this is spam. |
| `Moderation:MaxUppercaseRatio` | `0.7` | Share of capitals that counts as shouting. |
| `Moderation:MaxRepeatedChars` | `8` | Longest run of one character before it reads as noise. |
| `Database:MigrateOnStartup` | `true` | Apply EF migrations at startup. |

Word lists are deliberately empty in `appsettings.json`: fill `Moderation:BannedWords` with
the words your own community does not tolerate, in any language.

## Tests

```sh
dotnet test                      # 79 unit tests
pwsh ./scripts/api-test.ps1      # 46 checks against a running instance
```

`scripts/api-test.ps1` starts the service on its own port with an empty connection string,
so it needs neither PostgreSQL nor a bot token. It walks every endpoint, checks the verdicts
and the HTTP codes (401 on a wrong webhook secret, 400 on bad input, 404, 405), edits
`bot.config.json` while the service is running to prove the hot reload, then restores it.

79 unit tests covering the rule engine (banned words, word boundaries, per-chat words, link spam,
shouting, noise, length), the parsing of model answers including malformed ones, the webhook
flow (delete on Block, keep on Allow, exempt authors, allowlisted chats, moderator notices,
wrong secret header), the reply rules (keyword and regex matching, mention-only mode, cooldown,
hourly ceiling, per-person rules, AI fallback), the working-hours calendar (weekends,
holidays, night shifts across midnight, unknown time zones) and the database fallback.

## Project layout

```
AICommentModerator/
  Application/        abstractions, options, the rule engine   (no framework dependencies)
  Domain/             entities and the Telegram payload models
  Infrastructure/     OpenAI client, Telegram client, EF and in-memory audit logs
  Controllers/        webhook and moderation log endpoints
tests/                xUnit test project
docker-compose.yml    PostgreSQL for local runs
```

## Notes

- `global.json` pins the .NET 8 SDK; the project targets `net8.0`.
- `NuGet.config` restricts restore to nuget.org so a private company feed cannot break it.
- API responses use snake_case, because the same serializer reads Telegram's snake_case updates.
- The webhook always answers `200 OK` quickly — Telegram retries anything else — except for a
  failed secret check, which is `401`.

---

### Qisqacha (UZ)

Telegram izohlarini moderatsiya qiladigan ASP.NET Core 8 xizmati. Webhook izohni qabul qiladi,
qoidalar va OpenAI modeli uni baholaydi, qoidabuzar xabar chatdan o'chiriladi, har bir qaror
audit jurnaliga yoziladi.

API kalitisiz va bazasiz ham to'liq ishlaydi: qoidalar dvigateli va xotiradagi jurnal yetarli —
`dotnet run`, so'ng <http://localhost:5165/swagger>.

Botning xulq-atvori `bot.config.json` faylida: qaysi chatlarda ishlasin, kimga tegmasin,
qoidabuzarlikda nima qilsin, qaysi izohlarga qanday javob bersin va ish vaqti jadvali. Fayl kuzatiladi —
o'zgartirish qayta ishga tushirmasdan kuchga kiradi, joriy holatni `GET /api/bot/config`
ko'rsatadi.
