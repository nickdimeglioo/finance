# Financial Tracker — Revised Phased Build Plan

> Personal finance web app built on reusable project infrastructure  
> Stack: React frontend · C# / ASP.NET Core backend · PostgreSQL database · S3-compatible object storage  
> Assumptions: auth, ORM, shared backend utilities, and many UI elements/components already exist in a separate reusable project and will be linked into this app later.  
> Revised: May 2026

---

## Table of Contents

1. [Phase 1 — Foundation: App Shell, Backend Integration & Environment](#phase-1--foundation-app-shell-backend-integration--environment)
2. [Phase 2 — Domain Model, Data Access & Shared App Contracts](#phase-2--domain-model-data-access--shared-app-contracts)
3. [Phase 3 — Core Features: Accounts, Transactions & Dashboard](#phase-3--core-features-accounts-transactions--dashboard)
4. [Phase 4 — Import Pipeline, S3 Storage & Rules Engine](#phase-4--import-pipeline-s3-storage--rules-engine)
5. [Phase 5 — Recurring Rules, Tags, Notes & Subscriptions](#phase-5--recurring-rules-tags-notes--subscriptions)
6. [Phase 6 — Reports, Charts, Exports & Reconciliation](#phase-6--reports-charts-exports--reconciliation)
7. [Phase 7 — Financial Guidance Engine, Settings & Production Polish](#phase-7--financial-guidance-engine-settings--production-polish)

---

## Project-Wide Assumptions

This plan intentionally does **not** rebuild generic infrastructure that already exists in the separate reusable project. The financial tracker should consume that shared project rather than duplicating it.

### Project-Specific Infrastructure Decisions

- Use Liquibase for PostgreSQL migrations. Keep changelogs under `db/liquibase/changelog/` with a master changelog, following the reusable pipeline project pattern where practical.
- Reserve finance-specific local ports so this app can run beside the existing pipeline project:
  - Frontend Vite dev server: `5273`
  - Backend ASP.NET Core API: `8353`
  - PostgreSQL: `5432`
  - MinIO/S3 API: `9000`
  - MinIO console: `9011`
- Keep project work tracked in `task-board.csv` with these exact headers:
  `id,title,description,status,priority,effort,scope,phase,created_at,updated_at`
- Mark completed task-board rows with a pass marker in `status`, for example `done - pass 1` for the initial setup/documentation flow.
- Do not push to a git remote during setup unless explicitly requested.

### Existing / Reusable Pieces

Assume these are already available or will be linked in later:

- Authentication and authorization system
- Current-user context / user identity access on the backend
- ORM and database migration tooling
- Base API conventions, validation patterns, error response patterns, and service registration style
- Frontend app shell patterns where applicable
- Shared UI elements/components such as buttons, inputs, modals, tables, badges, dialogs, loaders, and empty states
- Shared frontend API client conventions, if already present

### App-Specific Work in This Plan

The phases below focus on the financial tracker domain only:

- Finance-specific PostgreSQL schema/entities
- Accounts, transactions, splits, imports, rules, tags, notes, subscriptions, reconciliation, reports, and guidance
- S3-backed upload/storage flows for import files and generated exports where useful
- React pages and feature wiring using the existing component library
- C# backend services/controllers/endpoints using the existing ORM/auth patterns

---

## Phase 1 — Foundation: App Shell, Backend Integration & Environment

Stand up the financial tracker project using the reusable infrastructure as the base. By the end of this phase the React app and C# backend boot, connect to PostgreSQL, can access the authenticated user, and can reach S3-compatible storage.

### 1.1 Repository & Runtime Structure

- Set up the app structure around a React frontend and C# backend:
  - `/client` — React frontend, likely Vite unless the reusable project dictates otherwise
  - `/server` — ASP.NET Core API project
  - `/shared` or linked package references — reusable auth, ORM, UI, and utility projects
- Configure local development so the frontend and backend run together with one command or documented paired commands
- Add environment-based configuration for:
  - PostgreSQL connection string
  - S3 bucket name
  - S3 region / endpoint
  - S3 access credentials or local dev credentials
  - frontend API base URL
- Add a basic health endpoint that confirms:
  - API is running
  - PostgreSQL connection is reachable
  - S3 storage configuration is valid enough to list/write/test, depending on environment

### 1.2 C# Backend Scaffold

- Create the ASP.NET Core API project using the existing backend conventions
- Register reusable infrastructure modules:
  - existing auth module
  - existing ORM/database module
  - validation/error-handling module, if available
  - logging/telemetry conventions, if available
- Establish versioned route grouping, for example:
  - `/api/v1/accounts`
  - `/api/v1/transactions`
  - `/api/v1/imports`
  - `/api/v1/reports`
  - `/api/v1/settings`
- Add a finance-specific application layer structure:
  - `Features/Accounts`
  - `Features/Transactions`
  - `Features/Imports`
  - `Features/Rules`
  - `Features/Reports`
  - `Features/Guidance`
- Add a shared domain utility area for money, dates, import hashes, and user scoping helpers

### 1.3 PostgreSQL & ORM Integration

- Use the existing ORM and migration tooling rather than creating a custom SQL runner
- Configure the finance tracker database context/module against PostgreSQL
- Establish common entity conventions:
  - IDs use the reusable project standard, preferably UUIDs if that is already the project pattern
  - money values use fixed precision decimals, not floating point
  - dates use `date` for transaction dates and `timestamptz` for audit timestamps
  - structured flexible fields use `jsonb`
  - every user-owned entity includes `user_id` / owner reference from the shared auth system
- Add global/user-scoping safeguards so users can only access their own financial data
- Add initial migration containing the core finance schema:
  - `accounts`
  - `transactions`
  - `transaction_splits`
- Do **not** create a local `users` table unless the reusable auth system requires app-local profile extension tables

### 1.4 Existing Auth Integration

- Do not rebuild registration, login, password hashing, JWT issuance, cookies, or auth screens if those already exist
- Wire the backend financial endpoints to the existing authenticated-user mechanism
- Add a small `/api/v1/auth/me` compatibility endpoint only if the React shell needs a finance-app-specific current-user payload
- On the frontend, use the existing auth/session provider if available
- Add protected routing for finance pages using the existing auth guard pattern
- Confirm every finance API call includes the auth credentials/token expected by the shared project

### 1.5 S3 Storage Integration

- Add a storage abstraction in the backend, for example `IObjectStorageService`, backed by S3-compatible storage
- Define storage prefixes up front:
  - `imports/raw/{userId}/{importBatchId}/...`
  - `imports/parsed/{userId}/{importBatchId}/...` if parsed artifacts are stored
  - `exports/{userId}/{exportId}/...`
  - `attachments/{userId}/...` only if later needed
- Support local development against either real S3, MinIO, LocalStack, or the reusable project’s storage abstraction
- Store object keys and metadata in PostgreSQL; do not store large files in the database
- Add a basic smoke test endpoint or internal diagnostic that can write and delete a temporary object in non-production environments

### 1.6 Frontend App Shell

- Create the finance tracker route tree inside the React app:
  - `/dashboard`
  - `/accounts`
  - `/accounts/:id`
  - `/transactions`
  - `/imports`
  - `/subscriptions`
  - `/reports`
  - `/settings`
- Use the existing layout/sidebar/navigation components if provided
- Use the existing table, modal, button, input, badge, dialog, loader, and empty-state components instead of rebuilding them
- Add finance-specific navigation labels and placeholders for each major page
- Add a global notification/toast integration only if not already provided by the shared app shell

---

## Phase 2 — Domain Model, Data Access & Shared App Contracts

Define the finance-specific data model, request/response contracts, service layer, and frontend API hooks. This phase replaces the previous low-level component-library work because the app already has reusable UI/components and generic infrastructure.

### 2.1 Core Domain Entities

Create ORM entities/models for the core finance domain.

#### `accounts`

Fields should include:

- `id`
- `user_id`
- `institution`
- `nickname`
- `type` — checking, savings, credit_card, loan, investment, cash, other
- `currency`
- `opening_balance`
- `credit_limit`
- `interest_rate`
- `status` — active, archived, closed
- `include_in_dashboard`
- `notes`
- `created_at`
- `updated_at`

#### `transactions`

Fields should include:

- `id`
- `user_id`
- `account_id`
- `date`
- `posted_at`
- `description`
- `merchant`
- `type` — income, expense, transfer, adjustment, opening_balance
- `classification` — business, personal, mixed, ignored, unknown
- `category`
- `amount`
- `currency`
- `status` — pending, posted, reconciled, voided
- `source` — manual, import, system
- `import_hash`
- `is_void`
- `is_split`
- `transfer_partner_id`
- `recurring_rule_id`
- `metadata` as `jsonb`
- `created_at`
- `updated_at`

#### `transaction_splits`

Fields should include:

- `id`
- `transaction_id`
- `category`
- `classification`
- `amount`
- `notes`
- `metadata` as `jsonb`

### 2.2 Finance-Specific Entity Rules

- Store all money values as positive `decimal` values; transaction direction is determined by `type`
- Use `date` for transaction dates so reports are not distorted by timezone conversion
- Use `timestamptz` for created/updated timestamps
- Use database constraints where practical:
  - amount must be greater than or equal to zero
  - transaction type must be valid
  - classification must be valid
  - transfer partner references must point to another transaction
- Add indexes for common filters:
  - `(user_id, date)`
  - `(user_id, account_id, date)`
  - `(user_id, category)`
  - `(user_id, classification)`
  - `(user_id, import_hash)`
  - full-text or trigram search on description/merchant if useful later

### 2.3 Service Layer Pattern

Use the reusable backend structure, but keep finance logic out of controllers.

Create services such as:

- `AccountService`
- `TransactionService`
- `TransferService`
- `ImportService`
- `ImportRuleService`
- `ClassificationRuleService`
- `RecurringRuleService`
- `ReportService`
- `GuidanceService`
- `ObjectStorageService` / S3 adapter if not already provided

Service responsibilities:

- Accept the authenticated user context explicitly or through the existing current-user abstraction
- Enforce user ownership on every read/write
- Keep database transactions at the service layer where multi-row changes are required
- Return DTOs/view models rather than ORM entities directly
- Use the existing validation/error style from the reusable project

### 2.4 API Contract Conventions

Define request and response DTOs before building screens.

Conventions:

- API routes use `/api/v1/...`
- List endpoints support pagination and filters
- Money values are serialized consistently, preferably as numbers with decimal precision or as strings if that is the shared standard
- Dates are serialized as `YYYY-MM-DD`
- Timestamps are ISO strings
- Errors use the existing reusable project error envelope
- All endpoints are authenticated unless explicitly marked otherwise

Core DTO groups:

- `AccountListItemDto`
- `AccountDetailDto`
- `CreateAccountRequest`
- `UpdateAccountRequest`
- `TransactionListItemDto`
- `TransactionDetailDto`
- `CreateTransactionRequest`
- `UpdateTransactionRequest`
- `TransactionFiltersRequest`
- `PagedResult<T>`

### 2.5 Frontend API & Hooks

Use the reusable API client if one exists. Otherwise create a thin finance API wrapper around the project’s existing request conventions.

Create feature hooks such as:

- `useAccounts()`
- `useAccount(accountId)`
- `useCreateAccount()`
- `useUpdateAccount()`
- `useTransactions(filters)`
- `useTransaction(transactionId)`
- `useCreateTransaction()`
- `useUpdateTransaction()`
- `useVoidTransaction()`

If the shared project already uses React Query, SWR, or another query layer, use that instead of adding a second pattern.

### 2.6 Date, Money & Formatting Utilities

Use existing shared formatting utilities if available. Add finance-specific wrappers only where needed.

Required behavior:

- Display all monetary values with 2 decimal places and the selected currency symbol
- Keep raw amounts separate from display formatting
- Use consistent date formatting across tables, forms, reports, exports, and import previews
- Provide helper functions for:
  - `formatMoney(amount, currency)`
  - `formatDate(date)`
  - `toApiDate(date)`
  - `monthRange(date)`
  - `calculateMonthlyRange(offset)`

---

## Phase 3 — Core Features: Accounts, Transactions & Dashboard

Deliver the core manual-tracking experience: users can create accounts, enter transactions, edit them, and view a useful dashboard.

### 3.1 Accounts CRUD

Backend:

- `GET /api/v1/accounts` — list all accounts for the authenticated user with computed current balance
- `POST /api/v1/accounts` — create account; if `opening_balance` is not zero, create a system `opening_balance` transaction inside the same database transaction
- `GET /api/v1/accounts/{id}` — return account details, computed balance, and recent transactions
- `PUT /api/v1/accounts/{id}` — update nickname, institution, notes, status, type-specific fields, and `include_in_dashboard`
- `DELETE /api/v1/accounts/{id}` or archive endpoint — prefer archive/close behavior over hard delete if transactions exist

Frontend:

- `AccountsPage` using the existing card/list/table components
- Account list shows institution, nickname, type badge, current balance, status, and dashboard inclusion toggle
- `AccountDetailPage` shows account summary, scoped transaction table, and placeholders for balance history and reconciliation
- `CreateAccountModal` / `EditAccountModal` using existing form components
- Conditional fields by account type:
  - credit card: credit limit, interest rate
  - savings/loan: interest rate
  - investment: optional metadata later

### 3.2 Transactions CRUD

Backend:

- `GET /api/v1/transactions` — paginated and filterable by account, type, classification, category, status, tag, date range, amount range, search string, and `includeVoided`
- `POST /api/v1/transactions` — create manual transaction
- `POST /api/v1/transactions/transfer` — create both transfer legs atomically and link them by `transfer_partner_id`
- `GET /api/v1/transactions/{id}` — return a transaction with splits, tags, and linked transfer details
- `PUT /api/v1/transactions/{id}` — update editable fields; prevent unsafe conversion to/from transfer after creation
- `PATCH /api/v1/transactions/{id}/void` — void transaction; if transfer, optionally void both legs
- `PATCH /api/v1/transactions/{id}/status` — support quick status changes such as `posted` to `reconciled`

Frontend:

- `TransactionsPage` using the existing table/data-grid component
- Filter bar for account, type, classification, category, date range, amount range, text search, status, and voided toggle
- `AddTransactionModal` / `EditTransactionModal` using existing form/modal components
- Transfer mode shows the second account selector and creates the paired transaction through the transfer endpoint
- Inline quick-edit for category, classification, tags, and status if the existing table component supports it
- Existing confirm dialog is used for void actions

### 3.3 Transaction Splits

Backend:

- Allow a transaction to be split across multiple categories/classifications
- Validate that split amounts equal the parent transaction amount
- When splits exist, reports should prefer split rows over the parent category/classification
- Updating splits should happen in a single database transaction

Frontend:

- Add split editor inside the transaction modal
- Use existing repeatable-row/input components if available
- Show split indicator in the transaction table
- Transaction detail view shows all split lines clearly

### 3.4 Dashboard

Backend:

- `GET /api/v1/dashboard/summary?from&to` returns:
  - total income
  - total expenses
  - net cash flow
  - total liquid balance across included accounts
  - recent transactions
  - pending reminder count placeholder
- Keep dashboard aggregation server-side so the frontend stays simple

Frontend:

- `DashboardPage` with summary cards for current month by default
- Period picker with presets such as this month, last month, this year, and custom
- Recent transactions table showing the last 10 non-voided rows
- Placeholder chart area for trailing 6-month income vs expense until Phase 6
- Placeholder “Pending Reminders” section until Phase 5

---

## Phase 4 — Import Pipeline, S3 Storage & Rules Engine

Build the import-first workflow that makes the app practical for real bank history. This phase uses S3 for uploaded files and PostgreSQL for import metadata, row previews, rules, and committed transactions.

### 4.1 Import Schema

Add ORM migration/entities for:

#### `import_batches`

- `id`
- `user_id`
- `account_id`
- `institution`
- `original_file_name`
- `content_type`
- `s3_object_key`
- `status` — uploaded, parsed, previewed, committed, failed, cancelled
- `row_count`
- `accepted_count`
- `duplicate_count`
- `error_count`
- `metadata` as `jsonb`
- `created_at`
- `updated_at`

#### `import_preview_rows`

- `id`
- `import_batch_id`
- `row_number`
- `raw_data` as `jsonb`
- `raw_description`
- `cleaned_description`
- `date`
- `amount`
- `type`
- `category`
- `classification`
- `import_hash`
- `is_duplicate`
- `accepted`
- `errors` as `jsonb`

#### `import_templates`

- `id`
- `user_id`
- `institution`
- `name`
- `column_map` as `jsonb`
- `date_format`
- `amount_format`
- `created_at`
- `updated_at`

#### `import_rules`

- regex rules that auto-clean and classify imported rows
- fields: name, pattern, maps_to_type, maps_to_category, maps_to_classification, maps_to_description, priority, is_active

#### `classification_rules`

- post-import and manual-save classification rules
- fields: rule_type, field_target, value, classification, also_set_category, priority, is_active

### 4.2 File Upload to S3

Backend:

- `POST /api/v1/imports/upload` accepts CSV, OFX/QFX if desired later, and PDF where parsing is practical
- Stream the original file to S3 under `imports/raw/{userId}/{importBatchId}/{fileName}`
- Create an `import_batches` row with the S3 key and upload metadata
- Enforce file size limits and allowed content types
- Return the import batch ID and basic file metadata
- Do not store raw uploaded files in PostgreSQL

Frontend:

- `ImportPage` step 1: select account/institution and upload file
- Show upload status, file name, file size, and selected target account
- Use existing upload/input/progress components if available

### 4.3 Parsing & Column Mapping

Backend:

- `POST /api/v1/imports/{batchId}/parse` reads the S3 object, parses the file, and returns detected columns/sample rows
- CSV parsing can use a mature .NET CSV parser such as CsvHelper or the reusable project’s parser if one exists
- PDF parsing should be treated as best-effort; return extracted text or detected rows only when reliable enough
- `POST /api/v1/imports/{batchId}/preview` accepts a column map and optional template save flag
- Apply import rules in priority order
- Compute `import_hash` from stable values such as account ID, date, amount, and cleaned description
- Detect duplicates using existing `import_hash` values and near-match checks
- Store preview rows in `import_preview_rows`

Frontend:

- Step 2: map detected columns to transaction fields
- Support saved templates per institution
- Show sample rows so the user can verify mapping
- Allow date/amount format selection where needed

### 4.4 Import Preview & Commit

Backend:

- `GET /api/v1/imports/{batchId}/preview` returns preview rows with duplicate flags and validation errors
- `PUT /api/v1/imports/{batchId}/preview-rows/{rowId}` updates row-level edits such as category, classification, description, type, and accepted/rejected status
- `POST /api/v1/imports/{batchId}/commit` inserts accepted non-error rows into `transactions` in a single database transaction
- Return summary: `{ imported, skippedDuplicates, rejected, errors }`
- After commit, update the import batch status to `committed`

Frontend:

- Step 3: preview table using the existing table component
- Duplicate rows are clearly marked and can be bulk rejected
- Editable cells for category, type, classification, description, and accepted/rejected status
- Commit button shows a final summary before insert if useful
- Success toast shows imported, skipped, and rejected counts

### 4.5 Import Rules Manager

Backend:

- Full CRUD for import rules
- `POST /api/v1/import-rules/test` accepts a raw description and returns matched rules plus the resulting cleaned/classified output
- Priority reorder endpoint updates rule order in a single transaction

Frontend:

- `ImportRulesPage` or settings tab
- Table of rule name, pattern, mapped fields, priority, and active status
- Create/edit modal using existing form components
- Test panel where the user can paste a raw bank description and see matched output
- Drag/reorder if the existing table system supports it; otherwise use move up/down controls

### 4.6 Classification Rules Manager

Backend:

- Full CRUD for classification rules
- Rule types supported:
  - `keyword_contains`
  - `merchant_exact`
  - `category_is`
  - `amount_gte`
  - `amount_lte`
  - `amount_range`
- `ClassificationRuleService.ApplyAsync(userId, transaction)` runs after import and on manual transaction save
- First matching active rule wins unless the reusable rule system supports chained rules

Frontend:

- Settings tab for classification rules
- Rule list with priority, target field, value, output classification, optional category, and active toggle
- Inline test panel against a sample transaction

---

## Phase 5 — Recurring Rules, Tags, Notes & Subscriptions

Add organization and intelligence features: tags, recurring/subscription tracking, notes/memory, and reminder infrastructure.

### 5.1 Schema Additions

Add ORM migration/entities for:

#### `tags`

- `id`
- `user_id`
- `name`
- `color`
- `created_at`
- `updated_at`

#### `transaction_tags`

- `transaction_id`
- `tag_id`
- composite unique key

#### `recurring_rules`

- `id`
- `user_id`
- `name`
- `account_id`
- `type`
- `classification`
- `amount`
- `currency`
- `category`
- `merchant_keyword`
- `frequency` — daily, weekly, biweekly, monthly, quarterly, yearly
- `next_expected`
- `tags` as `jsonb` or through the tag join table if preferred
- `is_active`
- `created_at`
- `updated_at`

#### `notes`

- `id`
- `user_id`
- `title`
- `body`
- `amount_hint`
- `merchant_hint`
- `date_hint`
- `matched_transaction_id`
- `status` — unmatched, matched, dismissed
- `remind_on`
- `created_at`
- `updated_at`

#### `reminders`

- `id`
- `user_id`
- `type` — note, recurring_rule, reconciliation, custom
- `source_id`
- `title`
- `message`
- `due_on`
- `status` — pending, dismissed, completed
- `created_at`
- `updated_at`

### 5.2 Tags System

Backend:

- `GET /api/v1/tags`
- `POST /api/v1/tags`
- `PUT /api/v1/tags/{id}`
- `DELETE /api/v1/tags/{id}`
- `PUT /api/v1/transactions/{id}/tags` replaces the full tag set
- Transaction list responses include tags

Frontend:

- Tag selector inside transaction create/edit modal
- Inline create tag flow if the existing component system supports it
- `TagsPage` or settings tab for rename, recolor, delete, and transaction count
- Tag filter in the transaction filter bar

### 5.3 Recurring Rules & Subscription Tracking

Backend:

- Full CRUD for recurring rules
- `POST /api/v1/imports/{batchId}/match-recurring` runs after import commit
- Matching logic:
  - merchant keyword match
  - amount within configurable tolerance, default ±20%
  - expected date interval close to rule frequency
- When matched:
  - link transaction to recurring rule
  - update last matched date and next expected date
- `GET /api/v1/subscriptions/status` returns:
  - rule details
  - next expected date
  - last matched date
  - overdue/upcoming/on-track status
  - monthly-normalized cost
- Monthly normalization:
  - daily × 30
  - weekly × 4.33
  - biweekly × 2.165
  - quarterly ÷ 3
  - yearly ÷ 12

Frontend:

- `SubscriptionsPage`
- Summary card for total monthly subscription spend
- Business vs personal subscription split
- Sections for due this month, overdue, and all rules
- Add/edit recurring rule modal
- Post-import suggestion prompt when 3+ similar transactions appear at regular intervals

### 5.4 Notes & Memory System

Backend:

- Full CRUD for notes
- `POST /api/v1/notes/match` scores unmatched notes against a transaction
- Matching score uses:
  - merchant hint substring match
  - amount hint within ±5%
  - date hint within ±7 days
- `PATCH /api/v1/notes/{id}/match` accepts a suggested match
- `PATCH /api/v1/notes/{id}/dismiss` dismisses a suggestion/reminder

Frontend:

- `NotesPage` with tabs for unmatched and all notes
- `NewNoteModal` for title, body, merchant hint, amount hint, date hint, and reminder date
- Transaction edit modal shows a banner when a likely note match exists
- Matched notes are visible from the transaction detail view

### 5.5 Reminder Infrastructure

Backend:

- Use a C# background worker, scheduled hosted service, Hangfire, Quartz, or the reusable project’s background job system
- Nightly reminder job:
  - finds unmatched notes whose `remind_on` is due
  - finds recurring rules whose `next_expected` has passed without a match
  - writes/updates pending reminders in PostgreSQL
- `GET /api/v1/reminders` returns pending reminders
- `PATCH /api/v1/reminders/{id}/dismiss`
- `PATCH /api/v1/reminders/{id}/complete`

Frontend:

- Dashboard pending reminders section
- Sidebar/nav badge showing pending reminder count
- Reminder list item actions: view, dismiss, complete

---

## Phase 6 — Reports, Charts, Exports & Reconciliation

Add reporting, visualizations, exports, and reconciliation workflows. Aggregations should happen server-side against PostgreSQL, while the React frontend handles presentation.

### 6.1 Report Data Endpoints

Backend endpoints:

- `GET /api/v1/reports/cash-flow?months=6`
- `GET /api/v1/reports/category-breakdown?from&to&classification`
- `GET /api/v1/reports/business-personal?from&to`
- `GET /api/v1/reports/tag-breakdown?from&to`
- `GET /api/v1/accounts/{id}/balance-history?months=12`
- `GET /api/v1/subscriptions/trend?months=6`
- `GET /api/v1/reports/net-worth?from&to` if useful once account balances are stable

Rules:

- Reports exclude voided transactions by default
- Reports use split rows when a transaction has splits
- Reports respect user ownership on every query
- Date ranges are inclusive and based on transaction date, not created timestamp
- Large report queries should be indexed and paginated where relevant

### 6.2 Chart Integration

Frontend:

- Use the existing charting choice if the reusable project already has one; otherwise use Recharts
- Wire charts into:
  - Dashboard income vs expense chart
  - Account balance history line chart
  - Reports income vs expense chart
  - Category breakdown chart
  - Business vs personal stacked chart
  - Net cash flow / cumulative cash flow chart
  - Tag breakdown chart
  - Subscription trend chart
- Centralize chart formatting:
  - money axis formatter
  - date/month label formatter
  - tooltip formatter
  - chart color/theme tokens

### 6.3 Reports Page

Frontend:

- `ReportsPage` with tabs or sidebar navigation between report types
- Shared period picker with presets:
  - This Month
  - Last Month
  - This Quarter
  - This Year
  - Last 12 Months
  - Custom
- Shared account selector filter
- Business/personal filter where relevant
- Each report includes:
  - summary metrics
  - chart
  - supporting table
  - export action

### 6.4 Export: CSV, PDF & Stored Exports

Frontend:

- CSV export can be client-side for current table data if the result set is already loaded
- PDF report can be generated client-side if charts are rendered in-browser and the dataset is small enough
- Reuse existing export/download components if available

Backend:

- Add server-side export endpoints where large datasets or persisted files are needed:
  - `POST /api/v1/exports/transactions`
  - `POST /api/v1/exports/report`
  - `GET /api/v1/exports/{exportId}/download-url`
- Generated exports can be written to S3 under `exports/{userId}/{exportId}/...`
- Store export metadata in PostgreSQL:
  - user ID
  - export type
  - filters
  - S3 object key
  - created timestamp
  - expiration timestamp, if applicable
- Use pre-signed S3 URLs for secure download if that matches the reusable storage pattern

### 6.5 Balance Checkpoints & Reconciliation

Add ORM migration/entities for `balance_checkpoints`:

- `id`
- `user_id`
- `account_id`
- `date`
- `balance`
- `notes`
- `expected_balance`
- `discrepancy`
- `created_at`
- `updated_at`

Backend:

- `GET /api/v1/accounts/{id}/checkpoints`
- `POST /api/v1/accounts/{id}/checkpoints` computes expected balance and stores discrepancy
- `GET /api/v1/accounts/{id}/reconcile?from&to` returns unreconciled transactions in the period
- `PATCH /api/v1/transactions/{id}/status` supports posted/reconciled updates
- Checkpoint logic should account for opening balance, non-voided transactions, and transfer behavior

Frontend:

- `CheckpointsPanel` on `AccountDetailPage`
- Add checkpoint form with date and reported closing balance
- Timeline/list of checkpoint history with balanced/discrepancy status
- `ReconcilePage` shows unreconciled transactions, running cleared balance, and checkpoint comparison
- Existing confirm/dialog patterns are used for bulk reconciliation actions

---

## Phase 7 — Financial Guidance Engine, Settings & Production Polish

Complete the app with deterministic financial guidance, settings screens, UX consistency, and production hardening for the C# / Postgres / S3 stack.

### 7.1 Profile Extension

Do not modify the reusable auth user table directly unless that is the established project pattern. Add an app-specific profile extension table/entity.

#### `user_finance_profiles`

- `id`
- `user_id`
- `date_of_birth`
- `annual_income`
- `income_type` — salaried, freelance, mixed, retired, student, other
- `dependents`
- `financial_goals` as `jsonb`
- `category_mappings` as `jsonb` for needs/wants/savings mapping
- `created_at`
- `updated_at`

Backend:

- `GET /api/v1/profile`
- `PUT /api/v1/profile`

Frontend:

- `ProfilePage` under settings
- Fields for age/date of birth, annual income, income type, dependents, financial goals, and category mapping

### 7.2 Financial Guidance Engine

All guidance is deterministic and explainable. No AI or external API is required.

Backend:

- Create `GuidanceService`
- Input:
  - user finance profile
  - pre-aggregated income/expense data
  - account balances
  - recurring/subscription totals
  - loan/debt payments
- Output: `guidance[]`, where each item includes:
  - `id`
  - `title`
  - `status` — on_track, below_target, warning, no_data
  - `message`
  - `supportingMetrics`

Rules to implement:

- Savings Rate — `(income - expenses) / income`
- Emergency Fund — compare savings/liquid balances to 3 months of average expenses
- 50/30/20 Breakdown — compare needs/wants/savings category mapping to target split
- Subscription Load — subscriptions as percentage of income; flag if above chosen threshold, default 8%
- Business Expense Ratio — show business outflows as a percentage of total outflows
- Debt Service — loan payments as a percentage of monthly income; flag if above chosen threshold, default 36%

Endpoint:

- `GET /api/v1/guidance`

Frontend:

- `GuidancePage` or dashboard section
- Card grid with title, status chip, message, and visible supporting numbers
- No hidden thresholds; show the reasoning plainly

### 7.3 Settings Pages

Organize admin/configuration under `/settings`.

Pages/tabs:

- Profile
- Categories Manager
- Tags Manager
- Import Rules
- Classification Rules
- Recurring Rules / Subscription Settings
- Account Settings link or embedded reusable account settings page
- Data / Exports if export history is stored

Categories Manager:

- Global list of category strings used across transactions and splits
- Add category
- Rename category
- Merge categories by reassigning existing transactions/splits
- Delete only if unused, or force reassign before delete

Account Settings:

- Use the reusable auth/account settings system where available
- Do not rebuild username/password/email flows unless the shared project does not already provide them

### 7.4 Final UX Consistency Pass

- Confirm every async page uses existing loading and empty-state components
- Confirm every destructive action uses the existing confirm dialog pattern
- Add global keyboard shortcut for creating a transaction, such as `Ctrl+N` / `Cmd+N`, if it does not conflict with the shell
- Confirm form validation messages are consistent
- Confirm all money values display correctly with currency symbol and 2 decimals
- Confirm dates display consistently across pages, imports, reports, and exports
- Confirm table filters preserve state when navigating back and forth where useful
- Test the full manual flow:
  - create account
  - add transaction
  - edit transaction
  - create transfer
  - void transaction
  - view dashboard
- Test the full import flow with a real bank CSV:
  - upload to S3
  - parse
  - map columns
  - preview
  - reject duplicates
  - commit
  - verify dashboard/report updates

### 7.5 Production Hardening

Backend:

- Use ASP.NET Core production security defaults and reusable project hardening
- Confirm auth is enforced on every finance endpoint
- Confirm all database queries are user-scoped
- Add rate limiting to sensitive endpoints if not already globally configured
- Add request size limits for imports/uploads
- Add file type validation and safe file-name handling
- Add structured logging around imports, commits, exports, and background jobs
- Add health checks for:
  - API
  - PostgreSQL
  - S3/storage
  - background job system, if applicable

Database:

- Add indexes for report/filter-heavy queries
- Add uniqueness constraints where needed, such as per-user tag names and import hashes when appropriate
- Add migration rollback/backup expectations based on the reusable ORM tooling
- Ensure decimal precision is explicit for all money columns

S3:

- Use least-privilege credentials
- Keep buckets private
- Use pre-signed URLs for user downloads
- Consider lifecycle rules for temporary imports and generated exports
- Store only object keys in PostgreSQL, not public URLs

Frontend:

- Configure production API base URL
- Confirm auth/token behavior works in deployed environment
- Confirm large tables paginate instead of over-fetching
- Confirm import previews handle large CSVs gracefully

Documentation:

- Update `README.md` with:
  - prerequisites
  - local setup
  - PostgreSQL setup
  - S3/MinIO/LocalStack setup
  - environment variables
  - dev commands
  - migration commands
  - test import instructions
  - deployment notes

---

## Suggested Build Order Summary

1. Connect React + ASP.NET Core + PostgreSQL + existing auth + S3.
2. Define finance domain entities and migrations through the existing ORM.
3. Build accounts, transactions, transfers, splits, and dashboard summary.
4. Build S3-backed imports, preview rows, import rules, and classification rules.
5. Add tags, recurring/subscription tracking, notes, and reminders.
6. Add reports, charts, exports, and reconciliation.
7. Add deterministic guidance, settings, UX polish, and production hardening.

---

*End of revised phased build plan.*
