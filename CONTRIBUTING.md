# Contributing to AutonomateQA

First off, thank you for taking the time to look at the project! We are building the future of self-driving BDD, and we value your input.

## How Can I Contribute?

### 1. Providing Feedback
Since we are currently in the **Functional Coverage** phase, we specifically need feedback on:
* Accuracy of Gherkin-to-Playwright translations.
* Edge cases in complex UI scenarios (Shadow DOMs, Iframes, etc.).
* Thoughts on the "Aria Vision" implementation.

### 2. Reporting Issues
If you find a bug or a scenario where the agent "hallucinates" an interaction, please open an Issue with:
* The Gherkin step that failed.
* A snippet of the UI structure (or ARIA tree) if possible.

### 3. Roadmap & Optimization
Our next phase is **Resource Optimization** (minimizing token spend and API calls). If you have ideas on constraint-based scheduling or semantic caching, we’d love to hear them!

## Development Setup
1. Clone the repo: `git clone https://github.com/VishnuPai/AutonomateQA`
2. Install dependencies: `npm install`
3. Set your `OPENAI_API_KEY` in a `.env` file.
4. Run sample tests: `npx playwright test`

---
*By contributing, you agree that your contributions will be licensed under its Apache 2.0 License.*
