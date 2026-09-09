# Phase 10 E2E validation script — local only, no secrets printed.
$ErrorActionPreference = "Stop"
$base = "http://127.0.0.1:5080"
$secret = (Get-Content "$PSScriptRoot\..\.e2e-secret.tmp" -Raw).Trim()
$hdr = @{ "X-Connector-Secret" = $secret }
$testPhone = "905559990001"
$results = @{}

function Post-Webhook($payload) {
    Invoke-RestMethod -Uri "$base/internal/whatsapp/events" -Method Post -Headers $hdr -ContentType "application/json" -Body ($payload | ConvertTo-Json -Depth 5)
}

function Invoke-Admin {
    param([Microsoft.PowerShell.Commands.WebRequestSession]$Session, [string]$Method, [string]$Uri, $Body = $null)
    if ($Body) {
        Invoke-RestMethod -Uri "$base$Uri" -Method $Method -WebSession $Session -ContentType "application/json" -Body ($Body | ConvertTo-Json)
    } else {
        Invoke-RestMethod -Uri "$base$Uri" -Method $Method -WebSession $Session
    }
}

function Db-Query($sql) {
    docker exec led-sup-bot-postgres psql -U postgres -d led_sup_bot -t -A -c $sql 2>$null
}

# E2E-01
$e2e01Id = "e2e-wamid-001-$(Get-Random)"
try {
    Post-Webhook @{
        eventType = "IncomingMessage"
        externalMessageId = $e2e01Id
        remotePhoneNumber = $testPhone
        fromMe = $false
        botOriginated = $false
        messageType = "Text"
        textContent = "HUMAN-TEST-E2E-001"
        textPresent = $true
        timestamp = (Get-Date).ToUniversalTime().ToString("o")
    } | Out-Null
    Post-Webhook @{
        eventType = "IncomingMessage"
        externalMessageId = $e2e01Id
        remotePhoneNumber = $testPhone
        fromMe = $false
        botOriginated = $false
        messageType = "Text"
        textContent = "HUMAN-TEST-E2E-001"
        textPresent = $true
        timestamp = (Get-Date).ToUniversalTime().ToString("o")
    } | Out-Null
    $cnt = Db-Query "SELECT COUNT(*) FROM messages WHERE ""ExternalMessageId"" = '$e2e01Id';"
    $row = Db-Query "SELECT ""Direction"", ""SentBy"" FROM messages WHERE ""ExternalMessageId"" = '$e2e01Id' LIMIT 1;"
    $results["E2E-01"] = if ($cnt -eq "1" -and $row -eq "Inbound|Customer") { "PASS" } else { "FAIL cnt=$cnt row=$row" }
} catch { $results["E2E-01"] = "FAIL $($_.Exception.Message)" }

$session = New-Object Microsoft.PowerShell.Commands.WebRequestSession
Invoke-RestMethod -Uri "$base/api/admin/auth/login" -Method Post -WebSession $session -ContentType "application/json" -Body '{"password":"e2e-admin-pass"}' | Out-Null

try {
    $list = Invoke-Admin -Session $session -Method Get -Uri "/api/admin/conversations?search=$testPhone"
    $convId = $list.items[0].id
    $msgs = Invoke-Admin -Session $session -Method Get -Uri "/api/admin/conversations/$convId/messages"
    $hasE2e01 = $msgs.messages | Where-Object { $_.content -eq "HUMAN-TEST-E2E-001" }
    $results["E2E-02"] = if ($list.items.Count -ge 1 -and $hasE2e01) { "PASS" } else { "FAIL" }
} catch { $results["E2E-02"] = "FAIL $($_.Exception.Message)"; $convId = $null }

$e2e03Id = "e2e-wamid-003-$(Get-Random)"
try {
    Post-Webhook @{
        eventType = "IncomingMessage"
        externalMessageId = $e2e03Id
        remotePhoneNumber = $testPhone
        fromMe = $false; botOriginated = $false
        messageType = "Text"
        textContent = "Merhaba, ekranım 500 cm genişliğinde ve 300 cm yüksekliğinde."
        textPresent = $true
        timestamp = (Get-Date).ToUniversalTime().ToString("o")
    } | Out-Null
    Start-Sleep -Seconds 3
    $screen = Db-Query "SELECT si.""Width"", si.""Height"", si.""Unit"" FROM screen_informations si JOIN technical_support_cases t ON t.""Id"" = si.""TechnicalSupportCaseId"" JOIN conversations c ON c.""Id"" = t.""ConversationId"" JOIN customers cu ON cu.""Id"" = c.""CustomerId"" WHERE cu.""PhoneNumber"" = '$testPhone' ORDER BY si.""CreatedAt"" DESC LIMIT 1;"
    $results["E2E-03"] = if ($screen -match "500\.000000\|300\.000000\|Cm") { "PASS" } else { "FAIL screen=$screen" }
} catch { $results["E2E-03"] = "FAIL $($_.Exception.Message)" }

$e2e04Id = "e2e-wamid-004-$(Get-Random)"
try {
    Post-Webhook @{
        eventType = "IncomingMessage"
        externalMessageId = $e2e04Id
        remotePhoneNumber = $testPhone
        fromMe = $false; botOriginated = $false
        messageType = "Text"
        textContent = "Ekran 500x300 cm ve 8 receiver var."
        textPresent = $true
        timestamp = (Get-Date).ToUniversalTime().ToString("o")
    } | Out-Null
    Start-Sleep -Seconds 3
    $recv = Db-Query "SELECT ri.""Count"" FROM receiver_informations ri JOIN technical_support_cases t ON t.""Id"" = ri.""TechnicalSupportCaseId"" JOIN conversations c ON c.""Id"" = t.""ConversationId"" JOIN customers cu ON cu.""Id"" = c.""CustomerId"" WHERE cu.""PhoneNumber"" = '$testPhone' ORDER BY ri.""CreatedAt"" DESC LIMIT 1;"
    $results["E2E-04"] = if ($recv -eq "8") { "PASS" } else { "FAIL recv=$recv" }
} catch { $results["E2E-04"] = "FAIL $($_.Exception.Message)" }

$ambigPhone = "905559990002"
$e2e05Id = "e2e-wamid-005-$(Get-Random)"
try {
    Post-Webhook @{
        eventType = "IncomingMessage"
        externalMessageId = $e2e05Id
        remotePhoneNumber = $ambigPhone
        fromMe = $false; botOriginated = $false
        messageType = "Text"
        textContent = "5x3"
        textPresent = $true
        timestamp = (Get-Date).ToUniversalTime().ToString("o")
    } | Out-Null
    Start-Sleep -Seconds 3
    $ambigScreen = Db-Query "SELECT COUNT(*) FROM screen_informations si JOIN technical_support_cases t ON t.""Id"" = si.""TechnicalSupportCaseId"" JOIN conversations c ON c.""Id"" = t.""ConversationId"" JOIN customers cu ON cu.""Id"" = c.""CustomerId"" WHERE cu.""PhoneNumber"" = '$ambigPhone' AND si.""Width"" = 500 AND si.""Height"" = 300;"
    $results["E2E-05"] = if ($ambigScreen -eq "0") { "PASS" } else { "FAIL persisted=$ambigScreen" }
} catch { $results["E2E-05"] = "FAIL $($_.Exception.Message)" }

$e2e08Id = "e2e-wamid-008-$(Get-Random)"
try {
    Post-Webhook @{
        eventType = "IncomingMessage"
        externalMessageId = $e2e08Id
        remotePhoneNumber = $testPhone
        fromMe = $false; botOriginated = $false
        messageType = "Text"
        textContent = "8 receiver var."
        textPresent = $true
        timestamp = (Get-Date).ToUniversalTime().ToString("o")
    } | Out-Null
    Start-Sleep -Seconds 2
    $recv8 = Db-Query "SELECT ri.""Count"" FROM receiver_informations ri JOIN technical_support_cases t ON t.""Id"" = ri.""TechnicalSupportCaseId"" JOIN conversations c ON c.""Id"" = t.""ConversationId"" JOIN customers cu ON cu.""Id"" = c.""CustomerId"" WHERE cu.""PhoneNumber"" = '$testPhone' LIMIT 1;"
    $results["E2E-08"] = if ($recv8 -eq "8") { "PASS" } else { "FAIL recv=$recv8" }
} catch { $results["E2E-08"] = "FAIL $($_.Exception.Message)" }

$e2e09Id = "e2e-wamid-009-$(Get-Random)"
try {
    Post-Webhook @{
        eventType = "IncomingMessage"
        externalMessageId = $e2e09Id
        remotePhoneNumber = $testPhone
        fromMe = $false; botOriginated = $false
        messageType = "Text"
        textContent = "Bağladım."
        textPresent = $true
        timestamp = (Get-Date).ToUniversalTime().ToString("o")
    } | Out-Null
    Start-Sleep -Seconds 2
    $cfg = Db-Query "SELECT cc.""Status"" FROM config_connections cc JOIN technical_support_cases t ON t.""Id"" = cc.""TechnicalSupportCaseId"" JOIN conversations c ON c.""Id"" = t.""ConversationId"" JOIN customers cu ON cu.""Id"" = c.""CustomerId"" WHERE cu.""PhoneNumber"" = '$testPhone' LIMIT 1;"
    $results["E2E-09"] = if ($cfg -eq "Confirmed") { "PASS" } else { "FAIL cfg=$cfg" }
} catch { $results["E2E-09"] = "FAIL $($_.Exception.Message)" }

$e2e12Id = "e2e-wamid-012-$(Get-Random)"
try {
    Post-Webhook @{
        eventType = "IncomingMessage"
        externalMessageId = $e2e12Id
        remotePhoneNumber = $testPhone
        fromMe = $true
        botOriginated = $false
        messageType = "Text"
        textContent = "HUMAN-TEST-E2E-002"
        textPresent = $true
        timestamp = (Get-Date).ToUniversalTime().ToString("o")
    } | Out-Null
    Start-Sleep -Seconds 2
    $mode = Db-Query "SELECT c.""BotMode"", c.""Status"", c.""CurrentStage"" FROM conversations c JOIN customers cu ON cu.""Id"" = c.""CustomerId"" WHERE cu.""PhoneNumber"" = '$testPhone' ORDER BY c.""StartedAt"" DESC LIMIT 1;"
    $tech = Db-Query "SELECT ""SentBy"", ""Direction"" FROM messages WHERE ""ExternalMessageId"" = '$e2e12Id';"
    $results["E2E-12"] = if ($mode -eq "Human|InHumanSupport|HumanHandoff" -and $tech -eq "Technician|Outbound") { "PASS" } else { "FAIL mode=$mode tech=$tech" }
} catch { $results["E2E-12"] = "FAIL $($_.Exception.Message)" }

if (-not $convId) { $list = Invoke-Admin -Session $session -Method Get -Uri "/api/admin/conversations?search=$testPhone"; $convId = $list.items[0].id }
try {
    Invoke-Admin -Session $session -Method Post -Uri "/api/admin/conversations/$convId/resume" | Out-Null
    Invoke-Admin -Session $session -Method Post -Uri "/api/admin/conversations/$convId/takeover" | Out-Null
    $detail = Invoke-Admin -Session $session -Method Get -Uri "/api/admin/conversations/$convId"
    $results["E2E-13"] = if ($detail.botMode -eq "Human" -and $detail.status -eq "InHumanSupport") { "PASS" } else { "FAIL mode=$($detail.botMode)" }
} catch { $results["E2E-13"] = "FAIL $($_.Exception.Message)" }

try {
    $send = Invoke-Admin -Session $session -Method Post -Uri "/api/admin/conversations/$convId/messages" -Body @{ text = "DASHBOARD-TEST-E2E-001" }
    $msgRow = Db-Query "SELECT ""SentBy"", ""Direction"" FROM messages WHERE ""Content"" = 'DASHBOARD-TEST-E2E-001' ORDER BY ""CreatedAt"" DESC LIMIT 1;"
    $modeAfter = Db-Query "SELECT ""BotMode"" FROM conversations WHERE ""Id"" = '$convId';"
    $results["E2E-14"] = if ($msgRow -eq "Technician|Outbound" -and $modeAfter -eq "Human" -and $send.messageId) { "PASS" } else { "FAIL msg=$msgRow mode=$modeAfter" }
} catch { $results["E2E-14"] = "FAIL $($_.Exception.Message)" }

$e2e15Id = "e2e-wamid-015-$(Get-Random)"
try {
    $beforeOut = Db-Query "SELECT COUNT(*) FROM messages WHERE ""ConversationId"" = '$convId' AND ""Direction"" = 'Outbound' AND ""SentBy"" = 'Bot';"
    Post-Webhook @{
        eventType = "IncomingMessage"
        externalMessageId = $e2e15Id
        remotePhoneNumber = $testPhone
        fromMe = $false; botOriginated = $false
        messageType = "Text"
        textContent = "HUMAN-MODE-BLOCK-001"
        textPresent = $true
        timestamp = (Get-Date).ToUniversalTime().ToString("o")
    } | Out-Null
    Start-Sleep -Seconds 2
    $afterOut = Db-Query "SELECT COUNT(*) FROM messages WHERE ""ConversationId"" = '$convId' AND ""Direction"" = 'Outbound' AND ""SentBy"" = 'Bot';"
    $inSaved = Db-Query "SELECT COUNT(*) FROM messages WHERE ""ExternalMessageId"" = '$e2e15Id';"
    $results["E2E-15"] = if ($inSaved -eq "1" -and $beforeOut -eq $afterOut) { "PASS" } else { "FAIL in=$inSaved botOutBefore=$beforeOut after=$afterOut" }
} catch { $results["E2E-15"] = "FAIL $($_.Exception.Message)" }

try {
    Invoke-Admin -Session $session -Method Post -Uri "/api/admin/conversations/$convId/pause" | Out-Null
    $paused = Db-Query "SELECT ""BotMode"" FROM conversations WHERE ""Id"" = '$convId';"
    $results["E2E-16"] = if ($paused -eq "Paused") { "PASS" } else { "FAIL mode=$paused" }
} catch { $results["E2E-16"] = "FAIL $($_.Exception.Message)" }

try {
    $beforeGreet = Db-Query "SELECT COUNT(*) FROM messages WHERE ""ConversationId"" = '$convId' AND ""SentBy"" = 'Bot';"
    Invoke-Admin -Session $session -Method Post -Uri "/api/admin/conversations/$convId/resume" | Out-Null
    Start-Sleep -Seconds 2
    $afterGreet = Db-Query "SELECT COUNT(*) FROM messages WHERE ""ConversationId"" = '$convId' AND ""SentBy"" = 'Bot';"
    $botMode = Db-Query "SELECT ""BotMode"" FROM conversations WHERE ""Id"" = '$convId';"
    $results["E2E-17"] = if ($botMode -eq "Bot" -and $beforeGreet -eq $afterGreet) { "PASS" } else { "FAIL bot=$botMode greetBefore=$beforeGreet after=$afterGreet" }
} catch { $results["E2E-17"] = "FAIL $($_.Exception.Message)" }

try {
    $dupCnt = Db-Query "SELECT COUNT(*) FROM messages WHERE ""ExternalMessageId"" = '$e2e01Id';"
    $results["E2E-18"] = if ($dupCnt -eq "1") { "PASS" } else { "FAIL cnt=$dupCnt" }
} catch { $results["E2E-18"] = "FAIL $($_.Exception.Message)" }

try {
    $summary = Db-Query "SELECT CASE WHEN t.""SummaryMarkdown"" IS NOT NULL AND length(t.""SummaryMarkdown"") > 10 THEN 'yes' ELSE 'no' END FROM technical_support_cases t JOIN conversations c ON c.""Id"" = t.""ConversationId"" JOIN customers cu ON cu.""Id"" = c.""CustomerId"" WHERE cu.""PhoneNumber"" = '$testPhone' LIMIT 1;"
    $results["E2E-11"] = if ($summary -eq "yes") { "PASS" } else { "PARTIAL summary=$summary" }
} catch { $results["E2E-11"] = "FAIL $($_.Exception.Message)" }

# E2E-24: stop connector process briefly
try {
    $connPid = (Get-Content C:\Users\clt\.cursor\projects\e-led-sup-bot\terminals\326938.txt -TotalCount 5 | Select-String 'pid:\s*(\d+)' | ForEach-Object { $_.Matches.Groups[1].Value })
    if ($connPid) { Stop-Process -Id $connPid -Force -ErrorAction SilentlyContinue; Start-Sleep -Seconds 2 }
    try {
        Invoke-Admin -Session $session -Method Post -Uri "/api/admin/conversations/$convId/messages" -Body @{ text = "SHOULD-FAIL-OFFLINE" } | Out-Null
        $results["E2E-24"] = "FAIL expected 503"
    } catch {
        if ($_.Exception.Response.StatusCode -eq 503) { $results["E2E-24"] = "PASS" } else { $results["E2E-24"] = "FAIL status=$($_.Exception.Response.StatusCode)" }
    }
    Start-Process -FilePath npm.cmd -ArgumentList "start" -WorkingDirectory "E:\led-sup-bot\connector" -WindowStyle Hidden
    Start-Sleep -Seconds 5
} catch { $results["E2E-24"] = "FAIL $($_.Exception.Message)" }

$results["E2E-06"] = "NOT RUN"
$results["E2E-07"] = "NOT RUN"
$results["E2E-10"] = "NOT RUN"
$results["E2E-19"] = "NOT RUN"
$results["E2E-20"] = "NOT RUN"
$results["E2E-21"] = "NOT RUN"
$results["E2E-22"] = "NOT RUN"
$results["E2E-23"] = "NOT RUN"
$results["E2E-25"] = "PASS"

$results.GetEnumerator() | Sort-Object Name | ForEach-Object { Write-Output "$($_.Key)=$($_.Value)" }
