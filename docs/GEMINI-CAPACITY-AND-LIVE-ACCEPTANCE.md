# Gemini capacity and live acceptance

The application architecture is independent of Gemini Free Tier limits. A quota or provider outage does not block API startup, WhatsApp persistence, Desktop, SignalR, case management, internal knowledge retrieval, or operator takeover. OpenAI is not selected automatically.

## Capacity configuration

Keep secrets in `appsettings.Local.json` or environment variables; never commit an API key. Supported Gemini settings are:

- `CapacityMode`: an operator label such as `Limited` or `Normal`.
- `MaxConcurrentRequests`: central concurrency bound.
- `TimeoutSeconds`: per-request HTTP timeout.
- `MaxRetries`: retry count, clamped to 0–3.
- `MaxRetryDelaySeconds`: maximum delay used for temporary 429/5xx retries.
- `DailyQuotaAware`: suppresses further calls after a daily-quota response until the next UTC day boundary.

Equivalent environment variables are documented in `.env.example`.

Provider state is reported safely by `GET /api/foundation/status` as `Available`, `RateLimited`, `DailyQuotaExhausted`, `Unavailable`, or `Misconfigured`. It never includes the API key or raw provider error body.

## After quota or billing is upgraded

1. Upgrade Gemini API billing/quota in the external Google project.
2. Verify that the configured project/key received the capacity, or securely configure a replacement key/project.
3. Verify the configured Gemini model is available to that project.
4. Adjust only capacity settings if needed; no code or architecture change is required.
5. Restart the API/Desktop-managed runtime.
6. Run the explicit smoke suite from the repository root:

   ```powershell
   $env:LEDSUPBOT_RUN_LIVE_GEMINI_TESTS='1'
   dotnet test tests/LedSupBot.UnitTests/LedSupBot.UnitTests.csproj -c Release --filter FullyQualifiedName~GeminiLiveVerificationTests
   Remove-Item Env:LEDSUPBOT_RUN_LIVE_GEMINI_TESTS
   ```

7. Run physical WhatsApp acceptance from the allowed test phone.

The standard build, unit suite, and integration suite do not enable this flag and therefore consume zero real Gemini calls.

## Physical WhatsApp acceptance

Send each message only after the prior response is complete and confirm the Desktop source summary:

1. `X4e'nin HDMI girişi var mı?` — exact X4e internal specification plus Gemini reasoning; no sibling model.
2. `X4e kaç tane ethernet çıkışı veriyor?` — exact X4e internal specification plus Gemini reasoning.
3. `4 tane E120 var ama ikinci karttan sonra görüntü yok` — receiver count/model and issue persist before evidence retrieval; troubleshooting asks one useful next check.
4. `Receiver kart ne işe yarıyor?` — concise, natural technical explanation.
5. Ask for the latest/current firmware — conditional web research runs, retains source metadata, and prefers an official manufacturer source.

If the provider reports `DailyQuotaExhausted`, stop the live run and record `BLOCKED_BY_QUOTA`; do not repeatedly probe. A successful live run must be based on actual responses, never a mocked result.
