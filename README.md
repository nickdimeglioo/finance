# Financial Tracker

Personal finance tracker planned around a React frontend, ASP.NET Core backend, PostgreSQL, Liquibase migrations, and object storage with local and S3-compatible providers.

## Planning Sources

- `financial-tracker-build-phases-revised-csharp-postgres-s3.md`: current build plan.
- `financial-tracker-plan.md`: older rough product outline. Use it for product intent only; its Express/SQLite/local-first stack is outdated.
- Reusable infrastructure reference: `C:\Users\nicky\Desktop\projects\pipeline\saas-template`.

## Local Ports

Reserved for this app so it can run beside the pipeline project:

| Service | Port |
| --- | --- |
| Frontend Vite | `5273` |
| Backend API | `8353` |
| PostgreSQL | `5438` |
| MinIO/S3 API, optional | `9000` |
| MinIO console, optional | `9011` |

## Infrastructure Decisions

- Use the pipeline template's auth/login/account patterns instead of rebuilding auth.
- Use the template's `SqlMapper`/`IOrmMapperService` style for finance domain data unless the linked project dictates otherwise.
- Use Liquibase under `db/liquibase` for database migrations.
- Store import files and generated exports through the object-storage abstraction; local development defaults to ignored repo-root `storage/`, while the S3-compatible provider is retained for MinIO or remote storage later.
- Keep deterministic finance rules explainable and overridable.

## Task Board

The original project task board lives in `task-board.csv` and should stay unchanged unless explicitly requested. Later implementation passes should create pass-specific CSVs, for example `task-board-pass-2.csv`, with headers:

```csv
insert_type,id,title,description,status,priority,effort,scope,phase
```

Use `task-tracker/app.py` to upload mutations. Numeric status `5` means complete. `MASTER_TASKS.txt` is the cross-pass ledger of completed task IDs.

## Phase 5 Organization Features

Phase 5 adds deterministic organization and recurring-transaction workflows:

- Tags: CRUD manager at `/tags`, transaction tag assignment, tag counts, and transaction filtering.
- Recurring rules and subscriptions: CRUD at `/subscriptions`, amount/date/merchant matching after imports, monthly-normalized cost summaries, and three-occurrence recurring suggestions after ruleset imports.
- Notes: CRUD at `/notes`, merchant/amount/date scoring, transaction-side match acceptance, dismissal, and optional reminder dates.
- Reminders: nightly hosted worker, pending reminder API, dashboard actions, and a dashboard nav badge.
- Rulesets: empty rulesets remain valid and every existing rule can be edited as JSON from its ruleset resource page.

The Phase 5 schema is in `db/liquibase/changelog/006-phase-5-organization.sql`. It creates `tags`, `transaction_tags`, `recurring_rules`, `notes`, and `reminders`.

## Expected Dev Commands

These commands apply once the app is scaffolded:

```bash
cd client && npm run dev -- --host 0.0.0.0 --port 5273
cd server && Database__ConnectionString="Host=localhost;Port=5438;Database=finance_tracker;Username=finance;Password=finance-dev-password;Include Error Detail=true" dotnet run --urls http://localhost:8353
```

Liquibase commands should be run from `db/liquibase` after `liquibase.properties` is configured for the finance database.

If the local Liquibase executable does not have Java 17+, run it through Docker:

```bash
docker run --rm --network host -v "$PWD/db/liquibase:/liquibase/workdir" -w /liquibase/workdir liquibase/liquibase:4.31.1 --defaults-file=liquibase.properties update
```

Local services:

```bash
docker compose -f docker-compose.local.yml up -d
```

The frontend source is present under `client/`, but Node/npm must be installed before running frontend install/build commands in this shell.
