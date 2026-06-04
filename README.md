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
id,title,description,status,priority,effort,scope,phase,created_at,updated_at
```

Completed items from each flow should include a pass marker in `status`, for example `done - pass 2`. `MASTER_TASKS.txt` is the cross-pass ledger of completed task IDs.

## Expected Dev Commands

These commands apply once the app is scaffolded:

```bash
cd client && npm run dev -- --host 0.0.0.0 --port 5273
cd server && dotnet run --urls http://localhost:8353
```

Liquibase commands should be run from `db/liquibase` after `liquibase.properties` is configured for the finance database.

Local services:

```bash
docker compose -f docker-compose.local.yml up -d
```

The frontend source is present under `client/`, but Node/npm must be installed before running frontend install/build commands in this shell.
