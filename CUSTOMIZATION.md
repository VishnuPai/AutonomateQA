# Customization Guide

This document explains how to make AutonomateQA fit your project while keeping the core generic and reusable.

## 1. Feature files and scenarios

- **Where to put features**: Use the folder set in **Runner:ScenariosPath** (e.g. `Scenarios/`). You can add any number of `.feature` files there.
- **Running a scenario**: In the runner UI, either paste the contents of a `.feature` file (or a single scenario) into the Gherkin script box, or upload the file. The target URL is the one in the “Target URL” field (or your **Runner:BaseUrl** default).
- **Structure**: The runner skips lines that are only structure (e.g. `Feature:`, `Scenario:`, `Scenario Outline:`) and runs the steps. Use concrete steps (e.g. “When I type '{{Username}}' into the 'Username' field”) so the AI can map them to the page.

## 2. Secrets and placeholders

- **Placeholders in Gherkin**: Use `{{KeyName}}` in your steps (e.g. `{{Username}}`, `{{Password}}`). The runner replaces these with values from secrets.
- **User Secrets (recommended)**  
  Set keys under the `TestSecrets` section so they map to placeholder names:
  ```bash
  dotnet user-secrets set "TestSecrets:Username" "your_user"
  dotnet user-secrets set "TestSecrets:Password" "your_pass"
  ```
  For `{{BCUsername}}` use `TestSecrets:BCUsername`, etc.
- **File fallback**: If no User Secrets are set, the runner loads `test_secrets.json` from the app root. Use the same key names as placeholders (e.g. `"Username"`, `"Password"`). Do not commit this file.
- **Adding new placeholders**: Add new keys to User Secrets or `test_secrets.json` and use `{{YourKey}}` in Gherkin; no code change required.

## 3. Default URL and scenario folder

- **Runner:BaseUrl**: Set this in `appsettings.json` (or env `Runner__BaseUrl`) to pre-fill the “Target URL” field in the UI. Leave empty for a generic placeholder.
- **Runner:ScenariosPath**: Set to a folder name (e.g. `Scenarios`) so recorded `.feature` files are saved there. The folder is created if missing. Empty = save under the app directory.

## 4. AI prompts (action and verification)

- **Location**: `AiProviders/PromptBuilder.cs` – `BuildActionPrompt` (how to choose click/fill/selectors) and `BuildVerifyPrompt` (how to verify “Then” steps).
- **Shipped version**: The repo contains generic prompts (no project-specific rules) so it’s safe for a first push. You can add project-specific behaviour locally without committing it.
- **Action prompt**: Edit the rules in `BuildActionPrompt` to add patterns (e.g. “click the logo” → use Role Img or Link in header, or CSS for a logo container’s child; “click the X button” → Button with name X). Keep the JSON schema at the end.
- **Verification prompt**: Edit the rules in `BuildVerifyPrompt` to add domain rules (e.g. “minicart is shown” → DOM id/class containing cart; “language selector” → any language link; “submenu list X” → menu options). Keep the Passed/Reasoning JSON schema.

## 5. DOM identifier keywords

- **Location**: `Services/PlaywrightVisionService.cs` – the in-page script that collects `id` and `class` values for the snapshot.
- **Keywords**: The script keeps elements whose `id` or `class` contains one of the listed keywords (e.g. `cart`, `minicart`, `menu`, `header`, `logo`, `brand`). This helps the AI find elements that have no accessible name (e.g. a minicart icon, logo container).
- **Adding keywords**: Add your app’s identifiers to the `keywords` array so they appear in the “[DOM identifiers present on page]” section of the snapshot and the AI can use them for selectors or verification.

## 6. Project-specific config

- **Same codebase, different projects**: Use `appsettings.Development.json` or environment-specific files to set different **Runner:BaseUrl**, **Runner:ScenariosPath**, or AI settings per environment.
- **Optional**: For multiple “projects” (e.g. different products), you can add a config key like `Runner:ProjectName` and, if you extend the app, load an optional `appsettings.{ProjectName}.json` or use it only for documentation/organization. The current design keeps a single config and uses BaseUrl + ScenariosPath + secrets to switch context.

## 7. Extending for your project

- **New scenarios**: Add new `.feature` files under your Scenarios folder and run them via paste/upload. No code change needed.
- **New step phrasing**: Prefer reusing existing step patterns (“I click the ‘X’ button”, “I type … into the ‘Y’ field”) so the existing prompts work. If you need a new pattern (e.g. “I open the HE.106 menu”), add a clear rule or example in `PromptBuilder.BuildActionPrompt` and, if useful, a DOM keyword so the snapshot exposes the right element.
- **Stability**: If tests are flaky, increase **Playwright:PostActionDelayMs** or **WaitForLoadStateAfterClickMs** in config. Tuning prompts and DOM keywords (steps 4 and 5) also improves reliability.

Keeping core logic in `Services/`, `AiProviders/`, and `Configuration/` generic, and putting your features, secrets, and config in the places above, lets anyone clone the repo and customize it for their own project without forking the engine.
