# Scenarios folder

This folder is the default location for **Gherkin feature files** and for **recorded** `.feature` files when **Runner:ScenariosPath** is set to `Scenarios` in config.

- **How to run**: See the root [README.md](../README.md) for setup, config, and running the app. Use the AutonomateQA UI to paste or upload a `.feature` (or paste scenario steps), set the target URL, and run.
- **Adding scenarios**: Add your own `.feature` files here. Use placeholders like `{{Username}}` and `{{Password}}` in steps; configure secrets via User Secrets or `test_secrets.json` (see [CUSTOMIZATION.md](../CUSTOMIZATION.md)).
- **Recording**: When you use “Record New Test” in the runner, the saved `.feature` file is written here if **Runner:ScenariosPath** is `Scenarios`.
