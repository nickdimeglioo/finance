# AGENTS.md

Repo-specific working conventions for this finance tracker. Prefer these rules over generic defaults.

## Project Shape

- Target app: personal finance tracker with React frontend, ASP.NET Core backend, PostgreSQL, Liquibase migrations, and S3-compatible object storage.
- Source plans:
  - `financial-tracker-build-phases-revised-csharp-postgres-s3.md` is the current build plan.
  - `financial-tracker-plan.md` is historical product context. Use it for intent, but do not copy its old SQLite/Express/local-first stack.
- Reusable template source: `C:\Users\nicky\Desktop\projects\pipeline\saas-template`.
- Prefer reusing standalone pieces from the pipeline template instead of rebuilding generic infrastructure.

## Local Ports

Use finance-specific ports so this app can run beside the pipeline project:

- Frontend Vite dev server: `http://localhost:5273`
- Backend ASP.NET Core API: `http://localhost:8353`
- PostgreSQL local port: `5438`
- MinIO/S3 API: `http://localhost:9010`
- MinIO console: `http://localhost:9011`

The pipeline template uses backend `8243` and common Vite/MinIO defaults, so avoid those ports here.

## Reusable Template Conventions

- Backend reusable candidates:
  - `api/SaasTemplate/SqlMapper`: custom ORM mapper and model attributes.
  - `api/SaasTemplate/Email`: email abstractions and no-op/provider pattern.
  - `api/SaasTemplate/RedisCache`: optional cache module if finance workflows later need it.
  - `api/SaasTemplate/Authentication`: auth, identity, account flows, 2FA, and current-user patterns.
  - `db/liquibase`: Liquibase structure and changelog style.
  - `PipelineRunner.Server/Services/StorageManagementService.cs` and storage config for S3-compatible storage patterns.
- Frontend reusable candidates:
  - `web/src/types/schema.ts` as the central contract/types pattern.
  - `web/src/index.css` for CSS variables, theme tokens, and layout utility conventions.
  - `web/src/lib/http.ts`, `web/src/services/*`, auth store, user settings store, and existing components.

## Backend Rules

- Use ASP.NET Core and C# for the API.
- Keep request flow explicit: controller validates HTTP shape and calls a service; service owns user scoping, permission checks, loading, mutations, transactions, and cache decisions; data access goes through the finance data contract/SqlMapper boundary; interceptors are reserved for audit and pre/post-processing concerns only.
- The in-repo SQL mapper lives at `server/SqlMapper`.
- The application-facing data contract lives at `server/FinanceTracker.Data.Contracts` and is implemented in the API by `FinanceSqlMapperDataSession`.
- API startup must initialize the local mapper runtime (`SqlMapperRuntime.Configure`) so underscore mapping, `DateOnly`/`TimeOnly`, JSON/JSONB handlers, and finance connection defaults are active before services query or save data.
- Prefer `OrmMapper`/SqlMapper raw execution helpers and query builders (`QuerySelect`, `From`, mapper transactions) behind the finance data contract. Direct Dapper usage should stay inside the mapper project or mapper initialization code.
- Use the existing auth/login/account infrastructure where possible. Do not create a finance-local users table unless the shared auth model requires a profile extension.
- Use `IOrmMapperService`/SqlMapper for finance domain data unless the linked template establishes a different app-wide standard.
- EF Core should remain primarily for auth/identity if that is how the reusable template is linked.
- Keep finance logic in services, not controllers.
- Controllers should return typed DTOs and explicit HTTP responses.
- Finance data must always be scoped by authenticated user.
- Multi-row writes such as account opening balances, transfers, split updates, imports, and commits must be transactional.
- Money values must use fixed precision decimals, not floats/doubles.
- Transaction dates should be `date`; audit timestamps should be `timestamptz`.
- Flexible metadata should use `jsonb`.
- Prefer deterministic, explainable rules over hidden inference.

## Database And Liquibase

- Use Liquibase for all PostgreSQL schema changes.
- Keep changelogs under `db/liquibase/changelog/` with a master changelog that includes ordered files.
- Use timestamped or numbered migration files consistently.
- Migrations should include constraints, indexes, and data backfills needed by the feature in the same phase.
- Prefer idempotent SQL where practical.
- Core extensions likely needed early: `pgcrypto` for `gen_random_uuid()` and optionally trigram/full-text extensions when search arrives.
- Do not store uploaded files or generated exports in PostgreSQL; store S3 object keys and metadata.

## Frontend Rules

- Use React + TypeScript + Vite.
- Use shared app shell, auth/session provider, API client, service modules, table/modal/button/input/badge/dialog/loader/empty-state components when available.
- Put finance contracts in `client/src/types/schema.ts` or the local equivalent that mirrors the template convention.
- API calls should go through a service module, not direct page-level fetch calls.
- Use CSS variables/theme tokens from the shared design system; avoid hardcoded one-off palettes.
- Build operational finance screens directly. Do not make a marketing landing page.
- Tables, filters, import preview, reconciliation, and reports should be dense, readable, and efficient.

## Product Rules

- Treat imports as a first-class workflow: upload, parse, map, preview, edit, duplicate-detect, commit.
- Every classification/import decision must be visible and manually overridable.
- Void financial rows instead of hard deleting them in the UI.
- Transfers are two linked rows and excluded from P&L while still affecting account balances.
- Splits must sum exactly to the parent transaction amount.
- Reports exclude voided rows by default and use split rows where available.
- Guidance is deterministic and must expose thresholds and supporting numbers.

## Task Board

- The original baseline task board is `task-board.csv`. Do not edit it unless the user explicitly asks.
- Each implementation pass that adds or completes work should create its own pass-specific mutation CSV, for example `task-board-pass-4.csv`.
- New pass-specific CSVs should use the task tracker API schema:
  `insert_type,id,title,description,status,priority,effort,scope,phase`
- `insert_type` is `insert`, `update`, or `delete`. Leave `id` blank for inserts; provide the task tracker database id for updates/deletes.
- `status`, `priority`, and `effort` are numeric. Do not include `created_at` or `updated_at` in new pass-specific task CSVs.
- Use `task-tracker/app.py` for task database operations. Its local instructions live in `task-tracker/SKILL.md`.
- Record pass completion markers in `MASTER_TASKS.txt`, for example `FT-P4-0001 - done in pass 4`, instead of encoding pass names into numeric task status.
- Maintain `MASTER_TASKS.txt` as the cross-pass ledger of completed task IDs and the pass that completed them.

## Git

- This repo may be initialized locally, but do not push unless explicitly asked.
- Keep generated build output, secrets, local databases, object-storage data, and IDE caches out of git.
- Do not commit unrelated user changes.
- Before publishing later, review `git status`, create an intentional commit, then push/open a PR only when requested.

## Build/Test Commands

Expected commands once the project is scaffolded:

- Frontend dev: `cd client && npm run dev -- --host 0.0.0.0 --port 5273`
- Frontend build: `cd client && npm run build`
- Backend dev: `cd server && dotnet run --urls http://localhost:8353`
- Backend build/test: `cd server && dotnet build && dotnet test`
- Full solution Release build: `dotnet build FinanceTracker.sln -c Release`
- Liquibase status/update: run from `db/liquibase` using this repo's properties once configured.
