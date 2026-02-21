# AutonomateQA

AutonomateQA is a generic, AI-assisted UI test runner that executes Gherkin steps against a live browser using Playwright. Each step is mapped to a Playwright action (click, fill, etc.) using an AI provider (OpenAI or Google Gemini) and the page’s accessibility snapshot, so you can run the same scenarios against different apps by changing the target URL and secrets.

## Features

- **Generic Gherkin execution**: Paste or upload `.feature` content; steps like “I click the ‘Sign In’ button” are resolved from the page’s Aria snapshot.
- **AI-driven locators**: The AI chooses the best selector (role, placeholder, text, or CSS from DOM hints) so tests stay resilient to minor UI changes.
- **Recording**: Record a flow in the browser and save it as a Gherkin `.feature` file.
- **Configurable**: Base URL, timeouts, AI provider, and scenario output path are driven by configuration.

## Prerequisites

- [.NET SDK](https://dotnet.microsoft.com/download) (project targets .NET 10; adjust if needed)
- [Node.js](https://nodejs.org/) (for Playwright browser install)
- Playwright browsers: run `pwsh bin/Debug/net10.0/playwright.ps1 install` from the project directory after building, or use your OS’s Playwright install steps.

## Quick start

1. **Clone and build**
   ```bash
   cd UiTestRunner
   dotnet restore
   dotnet build
   ```

2. **Install Playwright browsers** (once)
   ```bash
   pwsh bin/Debug/net10.0/playwright.ps1 install
   ```
   Or from repo root: `npx playwright install chromium`

3. **Configure**
   - Copy or edit `appsettings.json` (and optionally `appsettings.Development.json`).
   - Set **Runner:BaseUrl** to your app’s start URL (e.g. login page) if you want it as the default in the UI.
   - Set **Runner:ScenariosPath** to the folder where recorded `.feature` files are saved (e.g. `Scenarios`).
   - Set **AiProvider** to `OpenAI` or `Gemini` and provide the corresponding API keys (see [Secrets](#secrets)).

4. **Secrets**
   - **User Secrets** (recommended):  
     `dotnet user-secrets set "TestSecrets:Username" "your_user"`  
     `dotnet user-secrets set "TestSecrets:Password" "your_pass"`  
     Use the same `TestSecrets:KeyName` pattern for any placeholder your Gherkin uses (e.g. `{{Username}}`, `{{Password}}`).
   - **Fallback**: Add a `test_secrets.json` in the project root (do not commit). Format: `{ "Username": "…", "Password": "…" }`. See [CUSTOMIZATION.md](CUSTOMIZATION.md) for more.

5. **Run**
   ```bash
   dotnet run
   ```
   Open the app in the browser (e.g. `https://localhost:5001`), go to the AutonomateQA (Test Runner) page, enter or confirm the target URL, paste or upload a Gherkin script (or leave empty for a default flow), and run.

## Configuration reference

| Key | Description | Env override |
|-----|-------------|--------------|
| **Runner:BaseUrl** | Default URL shown in the runner UI | `Runner__BaseUrl` |
| **Runner:ScenariosPath** | Folder (relative to app root) for saved recordings | `Runner__ScenariosPath` |
| **AiProvider** | `OpenAI` or `Gemini` | `AiProvider` |
| **OpenAI:ApiKey** | OpenAI API key | `OpenAI__ApiKey` |
| **OpenAI:ActionModels**, **VerifyModels**, **GherkinModels** | Array of model names (e.g. `gpt-4o`, `gpt-4o-mini`) | `OpenAI__ActionModels__0`, etc. |
| **Gemini:ApiKey** | Google AI Studio API key (optional if using VertexAI) | `Gemini__ApiKey` |
| **Gemini:ActionModels**, **VerifyModels**, **GherkinModels** | Array of model names for REST API | `Gemini__ActionModels__0`, etc. |
| **VertexAI:ProjectId**, **Location**, **ModelId** | Vertex AI settings (single model) | `VertexAI__*` |
| **Playwright:NavigationTimeoutMs**, **InteractionTimeoutMs**, **PostActionDelayMs**, etc. | Timeouts and viewport | `Playwright__*` |
| **RateLimiting:WindowMinutes**, **PermitLimit** | Rate limit for triggering tests | `RateLimiting__*` |

## Project layout

- **Core (generic)**: `Services/` (UiTestService, PlaywrightVisionService, TestDataManager, TestRecorderService), `AiProviders/`, `Configuration/`, `Steps/`, `Background/`.
- **Your scenarios**: Put `.feature` files in the folder specified by **Runner:ScenariosPath** (e.g. `Scenarios/`). Recordings are saved there when **ScenariosPath** is set.
- **Secrets**: User Secrets or `test_secrets.json`; keys match placeholders in Gherkin (e.g. `{{Username}}` → `TestSecrets:Username` or `Username` in JSON).

## Customization

To tailor the runner to your project (different steps, prompts, or DOM hints), see **[CUSTOMIZATION.md](CUSTOMIZATION.md)** for:

- Adding and organizing feature files  
- Adjusting AI action/verification prompts  
- Adding DOM identifier keywords for your app  
- Project-specific config and secrets  

For a security and coding-standards review, see **[SECURITY_AND_STANDARDS.md](SECURITY_AND_STANDARDS.md)**.

## Database (app.db) and migrations

- **Back up** `app.db` before updating the app; migrations only add or alter columns and do not delete existing rows by design.
- **If you see a DB error in the console** when starting (e.g. "no such column" or "duplicate column"), copy the full error message. Common cases:
  - **"no such column: GherkinScript"** – migrations did not run. Ensure no process has the DB locked, then run the app again so `db.Database.Migrate()` can apply pending migrations.
  - **"duplicate column name: GherkinScript"** – the column was added but the migration was not recorded. You can mark it as applied:  
    `sqlite3 app.db "INSERT INTO __EFMigrationsHistory (MigrationId, ProductVersion) VALUES ('20260220200000_AddGherkinScriptToTestResult', '10.0.3');"`
- **If all execution history disappeared** – the app does not clear the table. Data is only lost if `app.db` was deleted or replaced (e.g. after a failed migration or a clean run). Restore from a backup of `app.db` if you have one.

## Before pushing to GitHub

- **Do not commit** `test_secrets.json` or any file with real credentials, API keys, or internal URLs. Use **User Secrets** or keep `test_secrets.json` local only; it is listed in `.gitignore`.
- **If you already committed `test_secrets.json`**: Remove it from the repo (but keep the file on disk):  
  `git rm --cached test_secrets.json`  
  Then commit. Consider rotating any exposed passwords or keys.
- **Optional**: Copy `test_secrets.example.json` to `test_secrets.json` and fill in your values locally; the example file is safe to commit.
- **appsettings.json** is safe as shipped (empty API key, placeholder VertexAI project). Do not commit `appsettings.json` with real API keys; use User Secrets or environment variables for CI/production.

## License

Use and modify as needed for your project.
