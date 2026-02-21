# AutonomateQA Tests

This directory contains unit tests for the AutonomateQA project.

## Running Tests

Run tests using the .NET CLI:

```bash
dotnet test
```

Run tests with code coverage:

```bash
dotnet test /p:CollectCoverage=true /p:CoverletOutputFormat=opencover
```

## Test Structure

- `Helpers/` - Tests for helper classes (e.g., `UrlValidator`)
- `Services/` - Tests for service classes (e.g., `TestDataManager`)
- `Constants/` - Tests to verify constants are correctly defined

## Test Framework

- **xUnit** - Testing framework
- **Moq** - Mocking framework for dependencies
- **Microsoft.Extensions.Configuration** - For testing configuration-dependent code
