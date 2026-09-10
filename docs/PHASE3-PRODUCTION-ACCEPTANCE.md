# Phase 3 production acceptance

This runbook maps the final no-real-WhatsApp production-path acceptance to existing integration coverage and the user-owned physical test.

## Automated production path

- General greeting and general-knowledge routing: `WorkflowIntegrationTests`, `AiWorkflowIntegrationTests`.
- Package request clarification and current-message priority: `PanelIdentityTextWorkflowIntegrationTests` and package workflow tests.
- Exact natural-sentence package delivery through webhook to document transport: `WhatsAppWebhookTests.Exact_package_request_through_webhook_sends_every_asset_to_trusted_lid_route`.
- All-asset delivery and failed-asset-only retry: `PanelFileDeliverySafetyGateTests.Folder_package_sends_all_five_files_in_order_and_retries_only_failed_asset`.
- A35 exact-spec routing and provenance: `WorkflowIntegrationTests.CONV03_A35_question_uses_exact_rag_before_web_while_anydesk_stays_pending`.
- X20/X20m isolation: exact-model workflow and knowledge tests.

Run the targeted suite without real Gemini or WhatsApp:

```powershell
dotnet test tests/LedSupBot.IntegrationTests/LedSupBot.IntegrationTests.csproj -c Release --filter "FullyQualifiedName~Exact_package_request_through_webhook|FullyQualifiedName~Folder_package_sends_all_five_files|FullyQualifiedName~CONV03_A35|FullyQualifiedName~PanelIdentityTextWorkflowIntegrationTests"
```

## Safe runtime start

From the repository root:

```powershell
docker start led-sup-bot-postgres
$env:MEDIA_STORAGE_ROOT=(Resolve-Path '.\src\LedSupBot.Api\data\media').Path
dotnet run --project .\src\LedSupBot.Api\LedSupBot.Api.csproj -c Release --no-launch-profile
```

In a second terminal, preserving the existing session:

```powershell
Set-Location .\connector
npm ci
npm start
```

Then start the Phase 3 Release `LedSupport.exe`. Do not reset the WhatsApp session if the connector reports authentication or encryption degradation; record the status and relink only by explicit operator decision.

Desktop-managed startup also launches the API with `--configuration Release` and pins an existing API-project media store through `MEDIA_STORAGE_ROOT`, preventing Debug/Release drift and working-directory-dependent package loss.

## User-owned physical sequence

1. `Merhaba` — outbound text; general route, no package/RAG lookup.
2. `Tarama dosyası lazım` — package intent; asks naturally for an exact `P{Pitch}+{Chipset}+{Decoder}` key.
3. `P1.86+1065+2018 dosyalarını gönder` — this key was read from the active registry at acceptance time; package registry and every active asset must reach document transport.
4. `A35 kaç portludur?` — exact A35 Ready specification and document/page provenance.
5. `Türkiye'nin başkenti neresi?` — general Gemini route, no package/RAG lookup.

The code agent does not send these messages or files. Full production acceptance remains waiting until the user confirms the physical results.
