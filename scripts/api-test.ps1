<#
    End-to-end check of every HTTP endpoint against a freshly started instance.

        pwsh ./scripts/api-test.ps1          (or: powershell -File scripts\api-test.ps1)

    It starts the service on its own port with an empty connection string, so it needs
    neither PostgreSQL nor a bot token, exercises the endpoints, flips bot.config.json to
    prove the hot reload works, restores the file and prints a summary.
#>

$ErrorActionPreference = "Continue"
$env:NUGET_CERT_REVOCATION_MODE = "offline"
$env:ASPNETCORE_ENVIRONMENT = "Development"

$proj = Join-Path (Split-Path $PSScriptRoot -Parent) "AICommentModerator"
$cfgPath = Join-Path $proj "bot.config.json"
$cfgBackup = Get-Content $cfgPath -Raw
$base = "http://localhost:5212"
$script:pass = 0
$script:fail = 0

function Check($name, $condition, $detail = "") {
    if ($condition) { $script:pass++; "  OK   $name" }
    else { $script:fail++; "  FAIL $name $detail" }
}

function Call($method, $path, $body = $null, $headers = @{}) {
    $a = @{ Uri = "$base$path"; Method = $method; UseBasicParsing = $true; TimeoutSec = 20; Headers = $headers }
    if ($body) { $a.Body = $body; $a.ContentType = "application/json" }
    try { $r = Invoke-WebRequest @a; return @{ Status = [int]$r.StatusCode; Body = $r.Content } }
    catch {
        $code = 0
        if ($_.Exception.Response) { $code = [int]$_.Exception.Response.StatusCode }
        return @{ Status = $code; Body = "" }
    }
}

$secret = @{ "X-Telegram-Bot-Api-Secret-Token" = "test-secret" }

function Update($text, $chat = 42, $msgId = 1, $isBot = $false, $username = "someone", $kind = "message") {
    $bot = if ($isBot) { "true" } else { "false" }
    $inner = '{"message_id":' + $msgId + ',"chat":{"id":' + $chat + ',"type":"supergroup"},' +
             '"from":{"id":5,"username":"' + $username + '","is_bot":' + $bot + '},"text":"' + $text + '"}'
    return '{"update_id":' + $msgId + ',"' + $kind + '":' + $inner + '}'
}

# empty connection string => in-memory log, so the run is deterministic without Docker
$arguments = "run --no-build --project `"$proj`" --urls $base --ConnectionStrings:DefaultConnection= --Telegram:WebhookSecret=test-secret"
$p = Start-Process -FilePath "dotnet" -ArgumentList $arguments -PassThru -WindowStyle Hidden -RedirectStandardOutput "$env:TEMP\apitest_out.txt" -RedirectStandardError "$env:TEMP\apitest_err.txt"
Start-Sleep -Seconds 16

try {
    "=== 1. GET /health ==="
    $r = Call GET "/health"
    $health = $r.Body | ConvertFrom-Json
    Check "200 qaytardi" ($r.Status -eq 200) "status=$($r.Status)"
    Check "status=ok" ($health.status -eq "ok")
    Check "xotira jurnali" ($health.storage -eq "in-memory") "storage=$($health.storage)"
    Check "secret o'rnatilgan" ($health.webhookSecret -eq "set")
    Check "qoidalar rejimi" ($health.moderation -eq "rules only")
    Check "ish vaqti: sutka bo'yi" ($health.workingHours -eq "around the clock") "wh=$($health.workingHours)"

    "=== 2. GET /api/bot/config ==="
    $r = Call GET "/api/bot/config"
    $cfg = $r.Body | ConvertFrom-Json
    Check "200 qaytardi" ($r.Status -eq 200)
    Check "scope ko'rinadi" ($cfg.scope.mode -eq "AllChats")
    Check "exempt ro'yxati" ($cfg.exempt.usernames -contains "malika53")
    Check "javob qoidalari (4 ta)" (@($cfg.replies.rules).Count -ge 4) "count=$(@($cfg.replies.rules).Count)"
    Check "ish vaqti bo'limi" ($null -ne $cfg.working_hours)
    Check "hozir ochiq" ($cfg.working_hours.open_now -eq $true)
    Check "per-chat override" (@($cfg.per_chat_overrides).Count -ge 1)

    "=== 3. POST /api/telegram/check ==="
    $cases = @(
        @{ Text = "Rahmat, foydali post"; Expect = "Allow" },
        @{ Text = "https://a.uz https://b.uz https://c.uz"; Expect = "Block" },
        @{ Text = "casino tonight"; Expect = "Flag" },
        @{ Text = "BUGUN HAMMASI JUDA YOMON BOLDI"; Expect = "Flag" },
        @{ Text = "whaaaaaaaaat bu nima"; Expect = "Flag" },
        @{ Text = "obuna boling t.me/kanal"; Expect = "Flag" }
    )
    foreach ($c in $cases) {
        $r = Call POST "/api/telegram/check" (@{ text = $c.Text } | ConvertTo-Json -Compress)
        $v = $r.Body | ConvertFrom-Json
        Check "`"$($c.Text.Substring(0, [Math]::Min(28, $c.Text.Length)))`" -> $($c.Expect)" ($v.decision -eq $c.Expect) "oldim=$($v.decision)"
    }

    $r = Call POST "/api/telegram/check" '{"text":""}'
    Check "bo'sh matn -> 400" ($r.Status -eq 400) "status=$($r.Status)"

    "=== 4. POST /api/telegram/webhook ==="
    $r = Call POST "/api/telegram/webhook" (Update "salom hammaga" 42 101)
    Check "secretsiz -> 401" ($r.Status -eq 401) "status=$($r.Status)"

    $r = Call POST "/api/telegram/webhook" (Update "salom hammaga" 42 101) @{ "X-Telegram-Bot-Api-Secret-Token" = "wrong" }
    Check "noto'g'ri secret -> 401" ($r.Status -eq 401) "status=$($r.Status)"

    $r = Call POST "/api/telegram/webhook" (Update "salom hammaga" 42 101) $secret
    $w = $r.Body | ConvertFrom-Json
    Check "to'g'ri secret -> 200" ($r.Status -eq 200)
    Check "oddiy izoh -> allow" ($w.decision -eq "allow") "decision=$($w.decision)"

    $r = Call POST "/api/telegram/webhook" (Update "https://a.uz https://b.uz https://c.uz" 42 102) $secret
    $w = $r.Body | ConvertFrom-Json
    Check "spam -> block" ($w.decision -eq "block") "decision=$($w.decision)"
    Check "tokensiz o'chirilmadi" ($w.deleted -eq $false)

    $r = Call POST "/api/telegram/webhook" (Update "salom" 42 103 $true) $secret
    Check "bot xabari e'tiborsiz" ((($r.Body | ConvertFrom-Json).reason) -eq "bot message")

    $r = Call POST "/api/telegram/webhook" (Update "kanal posti" 42 104 $false "someone" "channel_post") $secret
    Check "kanal posti e'tiborsiz" ((($r.Body | ConvertFrom-Json).reason) -eq "channel post")

    $r = Call POST "/api/telegram/webhook" '{"update_id":9,"message":{"message_id":9,"chat":{"id":42,"type":"supergroup"}}}' $secret
    Check "matnsiz xabar e'tiborsiz" ((($r.Body | ConvertFrom-Json).reason) -eq "no text")

    $r = Call POST "/api/telegram/webhook" (Update "https://a.uz https://b.uz https://c.uz" 42 105 $false "malika53") $secret
    Check "exempt foydalanuvchi himoyalangan" ((($r.Body | ConvertFrom-Json).source) -eq "exempt")

    "=== 5. GET /api/moderation/recent ==="
    $r = Call GET "/api/moderation/recent?take=50"
    $log = $r.Body | ConvertFrom-Json
    $logCount = ($log | Measure-Object).Count
    Check "200 qaytardi" ($r.Status -eq 200)
    Check "yozuvlar bor" ($logCount -ge 3) "count=$logCount"
    Check "block yozuvi bor" (@($log | Where-Object { $_.decision -eq "Block" }).Count -ge 1)
    Check "allow yozuvi bor" (@($log | Where-Object { $_.decision -eq "Allow" }).Count -ge 1)
    Check "exempt manbasi yozilgan" (@($log | Where-Object { $_.source -eq "exempt" }).Count -eq 1)
    Check "muallif saqlangan" ($log[0].author -like "@*")

    $r = Call GET "/api/moderation/recent?take=1"
    Check "take=1 -> bitta" ((($r.Body | ConvertFrom-Json) | Measure-Object).Count -eq 1)

    $r = Call GET "/api/moderation/recent?take=99999"
    Check "take=99999 -> 200 (clamp)" ($r.Status -eq 200)

    "=== 6. Kanal + muhokama guruhi oqimi ==="
    $post = '{"update_id":300,"message":{"message_id":500,"chat":{"id":42,"type":"supergroup"},' +
            '"sender_chat":{"id":-1009,"type":"channel","title":"Kanal"},"is_automatic_forward":true,' +
            '"text":"Yangi mahsulot chiqdi, narxi 500 000 som"}}'
    $r = Call POST "/api/telegram/webhook" $post $secret
    $w = $r.Body | ConvertFrom-Json
    Check "kanal posti moderatsiyadan o'tmaydi" ($w.reason -eq "channel post in the discussion group") "reason=$($w.reason)"

    $cfgAfter = (Call GET "/api/bot/config").Body | ConvertFrom-Json
    Check "post eslab qolindi" ($cfgAfter.replies.posts_remembered -ge 1) "count=$($cfgAfter.replies.posts_remembered)"
    Check "faqat post ostidagi izohlar" ($cfgAfter.replies.only_under_channel_posts -eq $true)
    Check "post kontekst sifatida" ($cfgAfter.replies.use_post_as_context -eq $true)

    $comment = '{"update_id":301,"message":{"message_id":501,"chat":{"id":42,"type":"supergroup"},' +
               '"from":{"id":7,"username":"xaridor","is_bot":false},"message_thread_id":500,' +
               '"text":"Bu qachon sotuvga chiqadi?"}}'
    $r = Call POST "/api/telegram/webhook" $comment $secret
    $w = $r.Body | ConvertFrom-Json
    Check "post ostidagi izoh qayta ishlandi" ($w.status -eq "processed" -and $w.decision -eq "allow") "$($w.status)/$($w.decision)"

    $channelSpam = '{"update_id":302,"message":{"message_id":502,"chat":{"id":42,"type":"supergroup"},' +
                   '"sender_chat":{"id":-1009,"type":"channel"},"text":"https://a.uz https://b.uz https://c.uz"}}'
    $r = Call POST "/api/telegram/webhook" $channelSpam $secret
    Check "kanal nomidan yozilgani tegilmaydi" ((($r.Body | ConvertFrom-Json).reason) -eq "posted by a channel")

    "=== 7. Ish vaqti (config hot reload bilan) ==="
    $closed = $cfgBackup -replace '"Enabled": false,\s*\r?\n(\s*)"TimeZone"', '"Enabled": true,
$1"TimeZone"'
    $closed = $closed -replace '"From": "09:00"', '"From": "23:30"' -replace '"To": "18:00"', '"To": "23:45"'
    $closed = $closed -replace '"Moderate": true', '"Moderate": false'
    $closed | Set-Content $cfgPath -Encoding utf8 -NoNewline
    Start-Sleep -Seconds 5

    $cfg2 = (Call GET "/api/bot/config").Body | ConvertFrom-Json
    Check "jadval yoqildi (restartsiz)" ($cfg2.working_hours.enabled -eq $true) "enabled=$($cfg2.working_hours.enabled)"
    Check "hozir yopiq" ($cfg2.working_hours.open_now -eq $false) "open=$($cfg2.working_hours.open_now)"

    $r = Call POST "/api/telegram/webhook" (Update "https://a.uz https://b.uz https://c.uz" 42 201) $secret
    Check "ish vaqtidan tashqari moderatsiya o'chiq" ((($r.Body | ConvertFrom-Json).reason) -eq "outside working hours") "reason=$(($r.Body | ConvertFrom-Json).reason)"

    $cfgBackup | Set-Content $cfgPath -Encoding utf8 -NoNewline
    Start-Sleep -Seconds 5
    $cfg3 = (Call GET "/api/bot/config").Body | ConvertFrom-Json
    Check "config qaytarildi" ($cfg3.working_hours.enabled -eq $false)

    "=== 8. Boshqalar ==="
    Check "swagger ochiladi" ((Call GET "/swagger/index.html").Status -eq 200)
    Check "noma'lum yo'l -> 404" ((Call GET "/api/yoq-bunday-narsa").Status -eq 404)
    Check "buzuq JSON -> 400" ((Call POST "/api/telegram/check" "{buzuq json").Status -eq 400)
    Check "GET o'rniga POST -> 405" ((Call GET "/api/telegram/check").Status -eq 405)
}
finally {
    $cfgBackup | Set-Content $cfgPath -Encoding utf8 -NoNewline
    Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
}

""
"===================================="
"  O'TDI: $script:pass   YIQILDI: $script:fail"
"===================================="
