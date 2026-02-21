# Security & Coding Standards Scan

This document summarizes the security and coding-standards review performed before the first push. Use it as a checklist for future changes.

---

## Security

### Addressed in this pass

| Item | Fix |
|------|-----|
| **SaveTestCase URL** | `SaveTestCase` now validates `model.Url` with `UrlValidator.IsSafeUrl()` so internal/localhost URLs cannot be stored. |
| **Error response disclosure** | `StartRecording` no longer returns `StackTrace` to the client; only `ex.Message` is returned. Full exception is logged server-side. |
| **URL in logs** | Navigation log in `UiTestService` now logs only scheme + host (e.g. `https://example.com`) instead of the full URL, to avoid leaking query params or tokens. |
| **Recorder input in logs** | For recorder events with action `input`/`fill`, the `Value` field is logged as `[REDACTED]` to avoid recording passwords or other sensitive input. |

### Already in place

| Item | Status |
|------|--------|
| **URL validation** | `UrlValidator.IsSafeUrl()` used for `TriggerTest` and `StartRecording`; blocks localhost, private IPs (RFC 1918), and cloud metadata IP. |
| **Secrets** | API keys and test secrets come from config/User Secrets; no hardcoded credentials. |
| **Prompt injection** | Prompts instruct the AI to treat Gherkin as data only; no execution of user input as code. |
| **Reasoning log** | Steps are masked with `MaskLiterals` before being written to the reasoning log and DB. |
| **Path traversal** | Recorder filename is sanitized (`Path.GetFileName`, invalid chars); write path is validated to stay under the allowed directory. |
| **SQL** | EF Core only (parameterized); no raw SQL or user-controlled query building. |
| **Rate limiting** | Fixed-window rate limiter applied to `TriggerTest` to reduce DoS risk. |

### Known vulnerabilities and limitations

| Risk | Description | Mitigation |
|------|-------------|------------|
| **No authentication** | Any user who can reach the app can trigger tests, start recording, save test cases, and view results. Hangfire dashboard is also unauthenticated. | Add authentication (e.g. ASP.NET Core Identity or API keys) and protect Hangfire; restrict access by network (e.g. VPN / private subnet) if acceptable. |
| **Unbounded request/DB size** | Very large payloads can cause high memory use or DB bloat. | **Mitigated:** `Url` is limited to 2048 and `GherkinScript` to 100,000 characters on `TriggerTestViewModel` and `TestCase`. Run a new EF migration to apply column limits if the DB already exists. |
| **SSRF via DNS rebinding** | `UrlValidator` blocks URLs whose *host* is an IP (e.g. `https://10.0.0.1`). It does not resolve hostnames. A URL like `https://evil.com` is allowed even if `evil.com` resolves to a private IP, so the browser could be directed to internal services. | For stricter SSRF protection, resolve the hostname server-side and run the same IP checks on the resolved address; or restrict allowed hosts to a allowlist. |
| **XSS via media paths** | The AutonomateQA UI sets `innerHTML` with `item.screenshotPath` and `item.videoPath`. These values are currently set only server-side (e.g. `/screenshots/...`). If they were ever derived from user input or DB in an unsafe way, that could lead to XSS. | Keep screenshot/video paths server-controlled only. If you ever allow user-influenced paths, sanitize or use safe setters (e.g. `setAttribute`) instead of `innerHTML`. |
| **Dependency vulnerabilities** | Third-party packages may have known CVEs. | Run `dotnet list package --vulnerable` and upgrade or replace vulnerable packages. |

### Recommendations for production

| Item | Recommendation |
|------|----------------|
| **Hangfire dashboard** | Protect with authorization (e.g. `app.UseHangfireDashboard("/hangfire", new DashboardOptions { Authorization = new[] { new HangfireAuthFilter() } });`) so only allowed users can view jobs. |
| **API keys in URL** | Gemini REST API uses `key=` in query string; keys could appear in proxy/logs. Prefer Vertex AI (header-based auth) in production if key leakage is a concern. |
| **HTTPS** | Ensure the app is served over HTTPS in production; HSTS is enabled for non-Development. |

---

## Coding standards

### Current state

| Area | Status |
|------|--------|
| **Async naming** | Async methods use the `Async` suffix consistently. |
| **CancellationToken** | Passed through in async service and provider methods. |
| **Null handling** | Nullable reference types and null checks used where needed; optional config uses `?.Value ?? new T()`. |
| **Structured logging** | Mix of string interpolation and structured `_logger.LogX("...", arg)`. Prefer structured logging (message template with placeholders) for consistency and filtering. |
| **HttpClient** | New `HttpClient()` per request in AI providers. Acceptable for low request volume; for high throughput consider `IHttpClientFactory` to avoid socket exhaustion. |
| **Constants** | Action types, selector types, and Gherkin keywords are in `Constants/` to avoid magic strings. |
| **Configuration** | Settings in `appsettings.json` and `Configuration/` classes; no hardcoded timeouts or URLs in business logic. |

### Suggestions for future

- Use structured logging everywhere: `_logger.LogInformation("Message {Key}", value)` instead of `$"Message {value}"`.
- Consider `IHttpClientFactory` for OpenAI/Gemini if you add more AI calls or run under load.
- Add unit tests for `UrlValidator` edge cases (already present in `UrlValidatorTests`); keep coverage for new validation paths.

---

## Pre-push checklist

- [ ] No `test_secrets.json` or real credentials in the repo (see `.gitignore` and README).
- [ ] No project-specific URLs, usernames, or internal hostnames in code or committed config.
- [ ] `appsettings.json` has empty API keys and placeholder values only.
- [ ] URL validation and error-response fixes above are in place.
- [ ] Run `dotnet build` and fix any warnings you care to enforce.
