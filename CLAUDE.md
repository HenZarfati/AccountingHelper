# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

```powershell
# Build the solution
dotnet build AccountingHelper.sln

# Run the WinForms app
dotnet run --project AccountingHelper.UI/AccountingHelper.UI.csproj

# Publish (Release)
dotnet publish AccountingHelper.UI/AccountingHelper.UI.csproj --configuration Release
```

The VSCode launch config (`F5`) builds first then runs `AccountingHelper.UI/bin/Debug/net8.0-windows/AccountingHelper.UI.exe`.

## Git Workflow

After every code change, always:

1. Stage all changes: `git add -A`
2. Commit with a short descriptive message: `git commit -m "describe what changed"`
3. Push to GitHub: `git push origin main`

Do this at the end of every task, without being asked.

**GitHub permissions:** Never delete a repository, branch, or any remote resource without explicit user approval. Always ask first before any destructive GitHub action.

## Architecture

Two-project solution:

- **AccountingHelper.Core** (net8.0 class library) — domain models only. `Models/Transaction.cs` defines `Transaction` (Id, Description, Amount, Date, Type) and the `TransactionType` enum (Income / Expense). No external dependencies.
- **AccountingHelper.UI** (net8.0-windows WinForms) — entry point via `Program.cs` → `Form1`. References `AccountingHelper.Core`. All UI and application logic lives here.

Business logic and data access belong in `AccountingHelper.Core`; `AccountingHelper.UI` should only wire up the UI layer and call into Core.
