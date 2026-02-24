# AutonomateQA

AutonomateQA is a generic, AI-assisted UI test runner that executes Gherkin steps against a live browser using Playwright. Each step is mapped to a Playwright action (click, fill, etc.) using an AI provider (OpenAI or Google Gemini) and the page’s accessibility snapshot, so you can run the same scenarios against different apps by changing the target URL and secrets.

## Features

- **Generic Gherkin execution**: Paste or upload `.feature` content; steps such as “I click the ‘…’ button” are resolved from the page’s Aria snapshot.
- **AI-driven locators**: The AI chooses the best selector (role, placeholder, text, or CSS from DOM hints) so tests stay resilient to minor UI changes.
- **Recording**: Record a flow in the browser and save it as a Gherkin `.feature` file.
- **Batch run**: Run many scenarios from one or more `.feature` files under **Runner:ScenariosPath**. Use the **Batch Run** panel to select a feature file (or “All feature files”), load scenarios, and enqueue up to **Runner:BatchRunMaxPerRequest** (default 100) per request. For larger sets, run by file, increase the cap in config, or use multiple feature paths via the API.
- **Environment and application selection**: Choose an **Environment** in the UI (target URL is filled from that env’s **BaseUrl**); optionally choose an **Application** when you have multiple apps. Test data is loaded from the selected environment’s CSV; execution history and result details show Environment and Application. **Feature-specific CSV**: when you run with an environment (and optionally application) selected, the runner looks for `TestData/{FeatureName}.{Environment}.{ApplicationName}.csv` (e.g. `Login.SIT.MyApp.csv`); if that file exists it is used; otherwise it tries `TestData/{FeatureName}.{Environment}.csv`, then the environment’s default CSV. Set **TestData:ApplicationName** as the default app and **TestData:Applications** (array) to populate the Application dropdown. **Re-run** uses the original run’s environment and application. The runner avoids using Production CSV for non-Production runs (e.g. SIT); see [CSV-resolution-for-batch-runs.md](docs/CSV-resolution-for-batch-runs.md) for details.
- **@ignore / @manual**: Scenarios tagged with `@ignore` or `@manual` appear in the Feature files & scenarios tab but are **excluded from execution** (single run and batch run). Use these tags to keep scenarios visible without running them.
- **Verification steps**: Steps that contain “ is displayed” or “ is visible” are treated as **verification**: the AI checks the page snapshot instead of performing an action.
- **Configurable**: Base URL, timeouts, AI provider, scenario path, batch cap, and **all OpenAI/Gemini endpoints and model names** are driven by configuration (no hardcoded API URLs or model names in code).

## Prerequisites

- [.NET SDK](https://dotnet.microsoft.com/download) (project targets .NET 10; adjust if needed)
- [Node.js](https://nodejs.org/) (for Playwright browser install)
- Playwright browsers: run `pwsh bin/Debug/net10.0/playwright.ps1 install` from the project directory after building, or use your OS’s Playwright install steps.

## Quick start

1. **Clone and build**
   ```bash
   cd <project-directory>
   dotnet restore
   dotnet build
   ```

2. **Install Playwright browsers** (once)
   ```bash
   pwsh bin/Debug/net10.0/playwright.ps1 install
   ```
   Or from repo root: `npx playwright install chromium`

3. **Configure**
   - Copy `appsettings.example.json` to `appsettings.json` (the latter is gitignored and not in the repo). Edit `appsettings.json` with your URLs, AI provider settings, and **Environments**. Optionally use `appsettings.Development.json`, a gitignored override, or environment-specific files such as `appsettings.{Environment}.json` to override **Environments** and **Runner** when present.
   - Set **Runner:BaseUrl** to the start URL of the target application if you want it as the default in the UI.
   - Set **Runner:ScenariosPath** to the folder where recorded `.feature` files are saved.
   - Set **AiProvider** to `OpenAI` or `Gemini` and configure the provider (see [Secrets](#secrets) and [Configuration reference](#configuration-reference)). For OpenAI, set **OpenAI:ApiEndpoint** (or **OpenAI:ApiBaseUrl** for Azure) and the model arrays **OpenAI:ActionModels**, **VerifyModels**, **GherkinModels**; no endpoints or model names are hardcoded.

4. **Secrets and test data**
   - **User Secrets** (recommended for credentials):  
     `dotnet user-secrets set "TestSecrets:Username" "your_user"`  
     `dotnet user-secrets set "TestSecrets:Password" "your_pass"`  
     Use the same `TestSecrets:KeyName` pattern for any placeholder your Gherkin uses (e.g. `{{Username}}`, `{{Password}}`).
   - **Per-environment test data**: Use the **Environment** (and optionally **Application**) dropdowns in the UI; each environment’s **Environments:{Env}:CsvPath** in `appsettings.json` points to a default CSV for that env. Set **TestData:ApplicationName** as the default app and **TestData:Applications** (array of app names) to show multiple apps in the Application dropdown. Feature-specific CSVs follow `TestData/{FeatureName}.{Environment}.{ApplicationName}.csv` (e.g. `Login.SIT.MyApp.csv`); when that file exists it is used; otherwise the runner tries `TestData/{FeatureName}.{Environment}.csv`. See [ENVIRONMENT_SETUP.md](ENVIRONMENT_SETUP.md) and [docs/CSV-resolution-for-batch-runs.md](docs/CSV-resolution-for-batch-runs.md).
   - **Fallback**: Add a `test_secrets.json` in the project root (do not commit). Format: `{ "Username": "…", "Password": "…" }`. See [CUSTOMIZATION.md](CUSTOMIZATION.md) for more.

5. **Run**
   ```bash
   dotnet run
   ```
   Open the app in the browser, go to the Test Runner page. To run as a **Windows Service** (always on), see [docs/Windows-Service.md](docs/Windows-Service.md). The **Target URL** is empty until you select an **Environment** (which fills the URL from config) or type a URL; a URL is required to run. Optionally select **Environment** and **Application** to use that env’s test data CSV (and feature-specific CSV when available). Paste or upload a Gherkin script (or leave empty for a default flow), then run.

## Configuration reference

| Key | Description | Env override |
|-----|-------------|--------------|
| **Urls** | Listening URL(s) for Kestrel (e.g. `http://localhost:5045`). When run as a Windows Service, set this so the app listens on the desired port (launchSettings.json is not used). | `ASPNETCORE_URLS` |
| **ConnectionStrings:DefaultConnection** | Database connection string. Default (when empty) is `Data Source=app.db` for SQLite. For SQL Server use e.g. `Server=.;Database=UiTestRunner;Integrated Security=true;TrustServerCertificate=true` or specify server, user, and password. | `ConnectionStrings__DefaultConnection` |
| **Database:Provider** | `Sqlite` (default) or `SqlServer`. Determines which EF Core provider is used. Migrations run for both; use SQL Server for shared or production databases. | `Database__Provider` |
| **Runner:BaseUrl** | Default URL shown in the runner UI | `Runner__BaseUrl` |
| **Runner:ScenariosPath** | Folder (relative to app root) for saved recordings and batch-run feature files | `Runner__ScenariosPath` |
| **Runner:BatchRunMaxPerRequest** | Max scenarios to enqueue per batch run request (default 100). Increase in config or use multiple feature paths via the API for larger runs. | `Runner__BatchRunMaxPerRequest` |
| **AiProvider** | `OpenAI` or `Gemini` | `AiProvider` |
| **OpenAI:ApiKey** | OpenAI API key | `OpenAI__ApiKey` |
| **OpenAI:ApiEndpoint** | Full chat-completions URL when using public OpenAI (required if **ApiBaseUrl** is not set). | `OpenAI__ApiEndpoint` |
| **OpenAI:ApiBaseUrl** | Azure/custom base URL (e.g. `https://your-instance.openai.azure.com/openai/deployments`). When set, **ApiEndpoint** is ignored and the request URL is built from this base + model + path + **ApiVersion**. | `OpenAI__ApiBaseUrl` |
| **OpenAI:ApiVersion** | Azure API version (optional; default used if omitted). | `OpenAI__ApiVersion` |
| **OpenAI:ActionModels**, **VerifyModels**, **GherkinModels** | Arrays of model or deployment names; must be set in config (no hardcoded defaults). | `OpenAI__ActionModels__0`, etc. |
| **TestData:ApplicationName** | Optional. Default application name used in CSV resolution and as the default in the Application dropdown. | `TestData__ApplicationName` |
| **TestData:Applications** | Optional. Array of application names for the Application dropdown. Combined with **TestData:ApplicationName** to build feature-specific paths: `TestData/{FeatureName}.{Environment}.{ApplicationName}.csv`. | `TestData__Applications__0`, etc. |
| **Environments** | Optional. Map of environment keys to **BaseUrl** and **CsvPath** for the UI dropdown and per-run test data. See [ENVIRONMENT_SETUP.md](ENVIRONMENT_SETUP.md). | — |
| **Gemini:ApiKey** | Google AI Studio API key (optional if using VertexAI) | `Gemini__ApiKey` |
| **Gemini:ActionModels**, **VerifyModels**, **GherkinModels** | Array of model names for REST API | `Gemini__ActionModels__0`, etc. |
| **VertexAI:ProjectId**, **Location**, **ModelId** | Vertex AI settings (single model) | `VertexAI__*` |
| **Playwright:NavigationTimeoutMs**, **InteractionTimeoutMs**, **PostActionDelayMs**, **MaxAriaSnapshotLength**, **FailVerificationWhenDialogOpen** | Timeouts, viewport, max Aria snapshot length; set to 0 for no limit. **FailVerificationWhenDialogOpen** (default true): fail verification when a dialog/modal is open and the step asserts a final page state (e.g. redirected). See [Handling unexpected popups](docs/Handling-unexpected-popups.md). | `Playwright__*` |
| **RateLimiting:WindowMinutes**, **PermitLimit** | Rate limit for triggering tests | `RateLimiting__*` |

## Project layout

- **Core (generic)**: `Services/` (UiTestService, PlaywrightVisionService, TestDataManager, TestRecorderService), `AiProviders/`, `Configuration/`, `Steps/`, `Background/`.
- **Scenarios**: Put `.feature` files in the folder specified by **Runner:ScenariosPath**. Recordings are saved there when **ScenariosPath** is set.
- **Secrets**: User Secrets or `test_secrets.json`; keys match placeholders in Gherkin (e.g. `{{Username}}` → `TestSecrets:Username` or `Username` in JSON).

## Customization

To tailor the runner (different steps, prompts, or DOM hints), see **[CUSTOMIZATION.md](CUSTOMIZATION.md)** for:

- Adding and organizing feature files  
- Adjusting AI action/verification prompts  
- Adding DOM identifier keywords  
- Project-specific config and secrets  

For a security and coding-standards review, see **[SECURITY_AND_STANDARDS.md](SECURITY_AND_STANDARDS.md)**.

## Optimizing AI token usage

Each step sends the current page’s Aria snapshot to the AI; on complex pages the snapshot can be very large and drive high prompt token usage.

- **MaxAriaSnapshotLength** (default `10000`): The Aria snapshot sent to the AI is truncated to this many characters. Set in `Playwright:MaxAriaSnapshotLength` or `Playwright__MaxAriaSnapshotLength`. Use `0` to send the full snapshot. Values in the 8,000–12,000 range usually cut cost significantly while keeping enough context for header/main content.
- **Shorter scenarios**: Splitting long scenarios into smaller ones reduces total steps and thus total tokens.
- **Token usage per run**: The execution history and details modal show tokens per run and total across all runs so you can monitor cost.

## Database and migrations

- **Provider**: The app supports **SQLite** (default) and **SQL Server**. Set **Database:Provider** to `SqlServer` and **ConnectionStrings:DefaultConnection** to your SQL Server connection string (e.g. `Server=.;Database=UiTestRunner;Integrated Security=true;TrustServerCertificate=true`) to use SQL Server. The same EF Core migrations apply to both providers.
- **Back up** your database before updating the app; migrations only add or alter columns and do not delete existing rows by design.
- **If you see a DB error in the console** when starting (e.g. "no such column" or "duplicate column"), copy the full error message. Common cases:
  - **"no such column: …"** (e.g. GherkinScript, Environment) – migrations or the startup fallback may not have run. Ensure no process has the DB locked, then run the app again so `db.Database.Migrate()` can apply. For SQLite, an in-process column fallback also runs; for SQL Server use migrations only.
  - **"duplicate column name: GherkinScript"** – the column was added but the migration was not recorded. You can mark it as applied:  
    `sqlite3 app.db "INSERT INTO __EFMigrationsHistory (MigrationId, ProductVersion) VALUES ('20260220200000_AddGherkinScriptToTestResult', '10.0.3');"`
- **If all execution history disappeared** – the app does not clear the table. Data is only lost if `app.db` was deleted or replaced (e.g. after a failed migration or a clean run). Restore from a backup of `app.db` if you have one.

## Before pushing to GitHub

- **Do not commit** `test_secrets.json` or any file with real credentials, API keys, or internal URLs. Use **User Secrets** or keep `test_secrets.json` local only; it is listed in `.gitignore`.
- **If you already committed `test_secrets.json`**: Remove it from the repo (but keep the file on disk):  
  `git rm --cached test_secrets.json`  
  Then commit. Consider rotating any exposed passwords or keys.
- **Optional**: Copy `test_secrets.example.json` to `test_secrets.json` and fill in your values locally; the example file is safe to commit.
- **appsettings.json** is safe as shipped (empty API keys, no endpoints or model names in the repo). Do not commit real API keys or internal endpoints; use User Secrets, a gitignored override (e.g. `appsettings.AIHub.json`), or environment variables for CI/production.

## License

AutonomateQA is licensed under the **MIT License**. See [LICENSE](LICENSE) for the full text. You may use, modify, and distribute it for your project under the terms of that license.
