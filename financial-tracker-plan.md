# Financial Tracker App — Full Product Plan

> Local-first personal finance web app  
> Stack: React (Vite) · Express.js · SQLite  
> Authored from planning session — May 2026

-----

## Table of Contents

1. [Project Philosophy](#1-project-philosophy)
1. [Tech Stack](#2-tech-stack)
1. [Data Model](#3-data-model)
1. [Feature Modules](#4-feature-modules)
1. [Business Logic Rules](#5-business-logic-rules)
1. [UI & Navigation Structure](#6-ui--navigation-structure)
1. [Charts & Reporting](#7-charts--reporting)
1. [Financial Guidance Engine](#8-financial-guidance-engine)
1. [Future Considerations](#9-future-considerations)

-----

## 1. Project Philosophy

- **Local-first**: All data lives in a SQLite file on the host machine. No cloud sync, no external databases, no SaaS dependency.
- **Single-user or household**: Authentication is local (username + hashed password), not tied to any OAuth provider. Designed primarily for one person, but a login wall keeps data private if multiple people share a machine.
- **No black boxes**: Every classification decision (business vs personal, category assignment, duplicate detection) is transparent and overridable by the user.
- **Import-first workflow**: The app is designed around the reality that most people have months of existing bank statements. Bulk import with preview is a first-class feature, not an afterthought.
- **Offline always**: The app runs as a local Express server accessed via browser (`localhost`). No internet connection required after initial setup.

-----

## 2. Tech Stack

### Frontend

|Concern               |Choice                               |
|----------------------|-------------------------------------|
|Framework             |React 18 (Vite)                      |
|Routing               |React Router v6                      |
|Styling               |Tailwind CSS                         |
|Charts                |Recharts                             |
|State management      |Zustand (lightweight, no boilerplate)|
|Table / data grid     |TanStack Table v8                    |
|Form handling         |React Hook Form                      |
|Date handling         |date-fns                             |
|CSV parsing (client)  |PapaParse                            |
|PDF export            |jsPDF + jsPDF-AutoTable              |
|Notifications / toasts|react-hot-toast                      |

### Backend

|Concern        |Choice                         |
|---------------|-------------------------------|
|Runtime        |Node.js                        |
|Framework      |Express.js                     |
|Database       |SQLite via `better-sqlite3`    |
|Auth           |JWT (jsonwebtoken) + bcrypt    |
|PDF parsing    |pdf-parse                      |
|File uploads   |multer                         |
|Scheduled tasks|node-cron (for reminders check)|

### Dev tooling

- Vite for frontend dev server and build
- Concurrently to run frontend + backend together in dev
- ESLint + Prettier
- Optional: Electron wrapper later if a desktop .exe / .app is ever wanted

-----

## 3. Data Model

### 3.1 `users`

```sql
CREATE TABLE users (
  id          INTEGER PRIMARY KEY AUTOINCREMENT,
  username    TEXT NOT NULL UNIQUE,
  password    TEXT NOT NULL,       -- bcrypt hash
  created_at  TEXT NOT NULL DEFAULT (datetime('now'))
);
```

-----

### 3.2 `accounts`

A first-class entity. Every transaction belongs to an account.

```sql
CREATE TABLE accounts (
  id                   INTEGER PRIMARY KEY AUTOINCREMENT,
  user_id              INTEGER NOT NULL REFERENCES users(id),
  nickname             TEXT NOT NULL,
  institution          TEXT,                          -- e.g. "Chase", "Stripe"
  account_type         TEXT NOT NULL,                 -- checking | savings | credit_card | loan | investment | cash | other
  account_number       TEXT,                          -- masked, display only (e.g. "••••4821")
  currency             TEXT NOT NULL DEFAULT 'USD',
  opening_balance      REAL NOT NULL DEFAULT 0,
  opening_date         TEXT,                          -- ISO date
  credit_limit         REAL,                          -- credit cards only
  interest_rate        REAL,                          -- savings / loans
  status               TEXT NOT NULL DEFAULT 'active', -- active | closed | hidden
  include_in_dashboard INTEGER NOT NULL DEFAULT 1,    -- 1 = yes, 0 = exclude
  notes                TEXT,
  created_at           TEXT NOT NULL DEFAULT (datetime('now')),
  updated_at           TEXT NOT NULL DEFAULT (datetime('now'))
);
```

**Notes on `include_in_dashboard`:**  
Allows the user to exclude accounts from the top-level totals — useful for loan accounts, old closed accounts, or accounts being tracked for reference only.

-----

### 3.3 `transactions`

The central table. Every financial event is a row here.

```sql
CREATE TABLE transactions (
  id                  INTEGER PRIMARY KEY AUTOINCREMENT,
  user_id             INTEGER NOT NULL REFERENCES users(id),
  account_id          INTEGER NOT NULL REFERENCES accounts(id),

  -- Type & classification
  type                TEXT NOT NULL,
    -- income | expense | deposit | withdrawal | transfer | opening_balance
  classification      TEXT NOT NULL DEFAULT 'personal',  -- business | personal
  category            TEXT,                              -- user-defined category

  -- Amounts
  amount              REAL NOT NULL,                     -- always positive; direction from type
  currency            TEXT NOT NULL DEFAULT 'USD',
  exchange_rate       REAL,                              -- only for outgoing foreign-currency transactions
  base_amount         REAL,                              -- amount converted to account's home currency

  -- Dates
  transaction_date    TEXT NOT NULL,                     -- when it was initiated / authorized (ISO date)
  posted_date         TEXT,                              -- when it cleared / hit the balance (ISO date)

  -- Identifiers
  reference_number    TEXT,                              -- bank ref, check number, invoice ID, etc. (user-defined or imported)
  import_hash         TEXT,                              -- deterministic hash for duplicate detection on import

  -- Description
  description         TEXT,                              -- merchant name, payee, or manual label

  -- Status & flags
  status              TEXT NOT NULL DEFAULT 'posted',    -- pending | posted | reconciled | voided
  is_void             INTEGER NOT NULL DEFAULT 0,        -- soft delete flag; never hard-delete financial records
  is_split            INTEGER NOT NULL DEFAULT 0,        -- 1 = this transaction has split line items

  -- Transfer linking
  transfer_partner_id INTEGER REFERENCES transactions(id),
    -- if type = 'transfer', both legs point at each other; excluded from P&L

  -- Recurring rule
  recurring_rule_id   INTEGER REFERENCES recurring_rules(id),  -- optional link

  -- Import metadata
  source              TEXT NOT NULL DEFAULT 'manual',    -- manual | import | recurring

  created_at          TEXT NOT NULL DEFAULT (datetime('now')),
  updated_at          TEXT NOT NULL DEFAULT (datetime('now'))
);
```

**Key design decisions:**

- `amount` is always positive. Direction is inferred from `type` (income/deposit = inflow, expense/withdrawal = outflow).
- `transfer` type rows are always created in pairs and linked via `transfer_partner_id`. They are excluded from all revenue/expense reporting.
- `opening_balance` type is a synthetic row created automatically when an account is added with a non-zero starting balance. It anchors the running math without polluting P&L.
- `is_void = 1` keeps the row in the database for audit trail but excludes it from all calculations and views by default.
- `import_hash` is a SHA-256 (or similar) of: account_id + transaction_date + amount + description. Used for duplicate detection during import.

-----

### 3.4 `transaction_splits`

When a transaction is flagged `is_split = 1`, line items are stored here.

```sql
CREATE TABLE transaction_splits (
  id             INTEGER PRIMARY KEY AUTOINCREMENT,
  transaction_id INTEGER NOT NULL REFERENCES transactions(id),
  category       TEXT NOT NULL,
  classification TEXT NOT NULL DEFAULT 'personal',  -- business | personal
  amount         REAL NOT NULL,
  notes          TEXT,
  created_at     TEXT NOT NULL DEFAULT (datetime('now'))
);
```

-----

### 3.5 `tags`

```sql
CREATE TABLE tags (
  id         INTEGER PRIMARY KEY AUTOINCREMENT,
  user_id    INTEGER NOT NULL REFERENCES users(id),
  name       TEXT NOT NULL,
  color      TEXT,           -- hex color for UI badge
  created_at TEXT NOT NULL DEFAULT (datetime('now')),
  UNIQUE(user_id, name)
);
```

-----

### 3.6 `transaction_tags`

Many-to-many join.

```sql
CREATE TABLE transaction_tags (
  transaction_id INTEGER NOT NULL REFERENCES transactions(id),
  tag_id         INTEGER NOT NULL REFERENCES tags(id),
  PRIMARY KEY (transaction_id, tag_id)
);
```

-----

### 3.7 `balance_checkpoints`

Point-in-time balance snapshots used for reconciliation.

```sql
CREATE TABLE balance_checkpoints (
  id         INTEGER PRIMARY KEY AUTOINCREMENT,
  account_id INTEGER NOT NULL REFERENCES accounts(id),
  date       TEXT NOT NULL,     -- ISO date (typically end of statement period)
  balance    REAL NOT NULL,     -- closing balance as reported by bank
  notes      TEXT,              -- e.g. "March statement close"
  created_at TEXT NOT NULL DEFAULT (datetime('now'))
);
```

**Purpose:**  
When a checkpoint exists, the app can compute the *expected* balance by summing all posted transactions from the opening balance to that date, then compare to the reported checkpoint. Any discrepancy surfaces a reconciliation warning.

-----

### 3.8 `classification_rules`

Rules that auto-classify transactions as business or personal. Evaluated in priority order on import and on manual entry.

```sql
CREATE TABLE classification_rules (
  id              INTEGER PRIMARY KEY AUTOINCREMENT,
  user_id         INTEGER NOT NULL REFERENCES users(id),
  priority        INTEGER NOT NULL DEFAULT 0,   -- lower = evaluated first
  rule_type       TEXT NOT NULL,
    -- keyword_contains | merchant_exact | category_is | amount_gte | amount_lte | amount_range
  field_target    TEXT NOT NULL DEFAULT 'description',  -- description | category | amount
  value           TEXT NOT NULL,                -- the keyword, category name, or amount threshold
  classification  TEXT NOT NULL,               -- business | personal
  also_set_category TEXT,                      -- optionally auto-assign a category too
  is_active       INTEGER NOT NULL DEFAULT 1,
  created_at      TEXT NOT NULL DEFAULT (datetime('now'))
);
```

-----

### 3.9 `import_rules`

Regex rules used during statement parsing to classify and label incoming transactions.

```sql
CREATE TABLE import_rules (
  id             INTEGER PRIMARY KEY AUTOINCREMENT,
  user_id        INTEGER NOT NULL REFERENCES users(id),
  name           TEXT NOT NULL,          -- human-readable rule name
  pattern        TEXT NOT NULL,          -- regex applied to the raw description field
  maps_to_type   TEXT,                   -- override transaction type
  maps_to_category TEXT,                 -- override category
  maps_to_classification TEXT,           -- override business/personal
  maps_to_description TEXT,             -- clean up merchant name (e.g. "AMZN*12345" → "Amazon")
  priority       INTEGER NOT NULL DEFAULT 0,
  is_active      INTEGER NOT NULL DEFAULT 1,
  created_at     TEXT NOT NULL DEFAULT (datetime('now'))
);
```

-----

### 3.10 `recurring_rules`

Definitions of expected recurring transactions (subscriptions, salary, rent, etc.).

```sql
CREATE TABLE recurring_rules (
  id              INTEGER PRIMARY KEY AUTOINCREMENT,
  user_id         INTEGER NOT NULL REFERENCES users(id),
  name            TEXT NOT NULL,
  account_id      INTEGER REFERENCES accounts(id),
  type            TEXT NOT NULL,     -- income | expense
  classification  TEXT NOT NULL DEFAULT 'personal',
  amount          REAL,              -- expected amount (approximate is fine)
  currency        TEXT NOT NULL DEFAULT 'USD',
  category        TEXT,
  merchant_keyword TEXT,             -- used to auto-match incoming transactions
  frequency       TEXT NOT NULL,     -- daily | weekly | monthly | yearly
  next_expected   TEXT,              -- ISO date of next expected occurrence
  tags            TEXT,              -- JSON array of tag IDs to auto-apply
  is_active       INTEGER NOT NULL DEFAULT 1,
  created_at      TEXT NOT NULL DEFAULT (datetime('now'))
);
```

-----

### 3.11 `notes`

Pre-purchase or in-the-moment notes. Surfaced at end of month. Can be auto-matched to an imported transaction.

```sql
CREATE TABLE notes (
  id                     INTEGER PRIMARY KEY AUTOINCREMENT,
  user_id                INTEGER NOT NULL REFERENCES users(id),
  title                  TEXT NOT NULL,
  body                   TEXT,
  amount_hint            REAL,      -- approximate expected amount
  merchant_hint          TEXT,      -- keyword to help auto-match
  date_hint              TEXT,      -- ISO date of expected transaction
  matched_transaction_id INTEGER REFERENCES transactions(id),
  status                 TEXT NOT NULL DEFAULT 'unmatched',  -- unmatched | matched | dismissed
  remind_on              TEXT,      -- ISO date to surface as reminder
  created_at             TEXT NOT NULL DEFAULT (datetime('now')),
  updated_at             TEXT NOT NULL DEFAULT (datetime('now'))
);
```

-----

### 3.12 `user_profile`

Financial context used by the guidance engine.

```sql
CREATE TABLE user_profile (
  id              INTEGER PRIMARY KEY AUTOINCREMENT,
  user_id         INTEGER NOT NULL UNIQUE REFERENCES users(id),
  date_of_birth   TEXT,          -- ISO date
  annual_income   REAL,          -- gross
  income_type     TEXT,          -- salaried | freelance | mixed | retired | student
  dependents      INTEGER DEFAULT 0,
  financial_goals TEXT,          -- JSON array: e.g. ["emergency_fund", "debt_payoff", "investing"]
  updated_at      TEXT NOT NULL DEFAULT (datetime('now'))
);
```

-----

## 4. Feature Modules

### 4.1 Authentication

- Local username + password only
- Passwords hashed with bcrypt (salt rounds: 12)
- JWT stored in `httpOnly` cookie (not localStorage)
- Single-session assumption (no multi-device sync)
- No password reset email (local app — reset via CLI script that clears the hash)

-----

### 4.2 Accounts

**Account list view:**

- Shows all accounts with current computed balance, type badge, and institution
- Toggle `include_in_dashboard` directly from the list
- Filter by status (active / hidden / closed)

**Account detail view:**

- Transaction list filtered to this account
- Balance over time mini-chart
- List of attached balance checkpoints / reconciliation history
- Quick-add transaction form in context

**Creating an account:**

- User sets opening balance + opening date
- System automatically creates a synthetic `opening_balance` transaction to anchor the math
- Account type determines which fields are shown (credit limit only on credit_card, interest rate on savings/loan)

**Balance math:**

- Computed balance = opening_balance + sum of all non-voided posted transactions (inflows minus outflows) through present
- Transfer legs are included for account-level balance (money did move) but excluded from P&L

-----

### 4.3 Transactions

**Transaction list:**

- Sortable and filterable by: date, type, classification, category, account, tag, status, amount range
- Voided transactions hidden by default with a “show voided” toggle
- Inline quick-edit for category, classification, and tags
- Status badge with one-click transition (posted → reconciled)

**Adding a transaction:**

- Required: account, type, amount, transaction_date, description
- Optional: posted_date, reference_number, currency (+ exchange rate auto-shows if currency ≠ account currency), category, classification, tags, notes (free text), recurring_rule_id, split toggle
- Transfer type shows a second account selector and auto-creates the partner leg

**Void vs delete:**

- “Void” sets `is_void = 1` and `status = 'voided'` — row stays, is excluded from all math
- Hard delete is not offered in the UI; only accessible via a developer/admin mode

**Duplicate detection (manual entry):**

- On save, check for existing non-voided transaction on same account with same amount, same date (±1 day), and similar description
- Show a warning modal: “A similar transaction exists — are you sure?”
- User can proceed, cancel, or view the potential duplicate

-----

### 4.4 Statement Import

**Upload:**

- Accepts CSV or PDF (for PDF, text is extracted and user maps columns manually or via saved template)
- File never leaves the machine

**Column mapping:**

- Map uploaded columns to: transaction_date, posted_date, description, amount, credit/debit indicator
- Save column map as a named template per institution (e.g. “Chase Checking CSV”)

**Import rules preview:**

- After mapping, each row is processed through `import_rules` regex patterns
- Preview table shows:
  - Raw description → cleaned description
  - Auto-detected type, category, classification
  - Duplicate flag (⚠️ if import_hash already exists in DB)
  - Accept / reject toggle per row
- User can edit any cell before confirming
- “Reject all duplicates” bulk action

**Import commit:**

- Only accepted rows are written
- Summary shown: X imported, Y skipped (duplicates), Z manually rejected

-----

### 4.5 Import Rules Manager

- List of all regex rules with name, pattern, what it maps to, and priority
- Test panel: paste a sample description and see which rules match and in what order
- Drag-to-reorder priority
- Enable / disable rules without deleting them

-----

### 4.6 Subscriptions & Recurring Rules

**Subscription detection:**

- On import and after manual entry, the system can suggest recurring rules by detecting transactions with similar description and amount appearing at regular intervals
- User confirms or dismisses suggestions

**Recurring rule list:**

- Shows name, frequency, amount, next expected date
- “Active this month” section highlights rules where next_expected is in current month
- Missing transaction alert: if next_expected passes without a matched transaction, surface a warning
- Match logic: when a new transaction arrives that matches `merchant_keyword` and is within ±20% of expected amount, auto-link it to the rule and update `next_expected`

**Subscription cost breakdown:**

- Monthly cost view (normalizes yearly/weekly to monthly equivalent)
- Total subscription spend per month, with business vs personal split

-----

### 4.7 Classification Rules (Business vs Personal)

- Rule-based engine; not AI-guessed
- Rule types:
  - `keyword_contains`: description contains “AWS” → business
  - `merchant_exact`: exact merchant name match
  - `category_is`: all transactions in category “Software Tools” → business
  - `amount_gte` / `amount_lte` / `amount_range`: e.g. amounts > $5,000 → review
- Rules evaluated top-down by priority; first match wins
- Fallback: any unmatched transaction defaults to `personal`
- User can always manually override classification on any transaction regardless of rules

-----

### 4.8 Tags

- User creates tags with a name and optional color
- Tags are applied to transactions (many-to-many)
- Tags can be used to filter anywhere in the app: transaction list, reports, dashboard
- Suggested use cases: bank name, client, project code, product line, tax category, reimbursable
- Tags do not affect any financial math — they are purely organizational

-----

### 4.9 Notes & Memory

**Creating a note:**

- Title (required), body (optional), merchant_hint, amount_hint, date_hint, remind_on date

**End-of-month reminder:**

- A system check (via node-cron or on app load) surfaces all `unmatched` notes whose `remind_on` date has arrived
- Shown as a notification badge on the Notes section

**Auto-match logic:**

- When a new transaction is imported or entered, check all unmatched notes for:
  - `merchant_hint` appears in transaction description (case-insensitive substring)
  - `amount_hint` within ±5% of transaction amount
  - `date_hint` within ±7 days of transaction_date
  - If 2 or more conditions match → suggest a match to the user
  - User confirms → note status becomes `matched`, `matched_transaction_id` is set
  - Confirmed match adds the note body as a transaction note field

-----

### 4.10 Balance Checkpoints (Reconciliation)

- User enters a closing balance from their bank statement for a given date
- App computes expected balance: `opening_balance + sum(all posted transactions up to that date)`
- Status shown:
  - ✅ **Balanced** — computed matches checkpoint (within $0.01 rounding tolerance)
  - ⚠️ **Discrepancy of $X** — prompts user to review transactions in the period
- Checkpoints are displayed on the account page in chronological order
- Reconciliation workflow: user can filter transactions to “unreconciled in period” and mark them as reconciled one by one until balance matches

-----

## 5. Business Logic Rules

### 5.1 Transaction Type Semantics

|Type                 |P&L Effect     |Account Balance Effect              |
|---------------------|---------------|------------------------------------|
|`income`             |Revenue +amount|Balance +amount                     |
|`expense`            |Expense +amount|Balance −amount                     |
|`deposit`            |Revenue +amount|Balance +amount                     |
|`withdrawal`         |Expense +amount|Balance −amount                     |
|`transfer` (from leg)|No P&L effect  |Balance −amount                     |
|`transfer` (to leg)  |No P&L effect  |Balance +amount                     |
|`opening_balance`    |No P&L effect  |Balance set to opening_balance value|


> `income` vs `deposit` and `expense` vs `withdrawal`: These are intentionally kept separate so the user can distinguish earned revenue from capital movements (e.g. moving savings into a new account vs receiving a client payment). They behave identically in math but are categorized separately in reports.

### 5.2 Currency / Exchange Rate

- Each account has a home currency
- If a transaction is entered or imported in a different currency, the user supplies the exchange rate
- Exchange rate is only required when money is flowing **out** (expense, withdrawal, transfer from leg)
- `base_amount` = `amount × exchange_rate` and is stored for consistent reporting in the account’s home currency
- Reports always display in home currency using `base_amount`

### 5.3 Duplicate Detection (Import)

A transaction is considered a duplicate if a non-voided transaction already exists with the same:

- `account_id`
- `amount` (exact match)
- `transaction_date` (within 1-day tolerance for settlement timing)
- `description` (fuzzy: Levenshtein distance < 0.2, or exact `import_hash` match)

Hash is pre-computed on import row to allow O(1) lookups.

### 5.4 Transfer Balance Safety

- Transfers are always created as a pair atomically (SQLite transaction wrapping both INSERTs)
- If either insert fails, neither is committed
- Deleting/voiding one leg prompts: “This is one leg of a transfer. Void both?”

-----

## 6. UI & Navigation Structure

```
/ (Dashboard)
  ├── Summary cards: total income, total expenses, net, account balances
  ├── Monthly cash flow chart
  ├── Recent transactions
  └── Pending notes / reminders

/accounts
  ├── Account list
  └── /accounts/:id (account detail + transactions)

/transactions
  ├── All transactions (filterable/sortable)
  ├── Quick-add button
  └── /transactions/:id (detail / edit)

/import
  ├── Upload statement
  ├── Column mapping
  └── Preview & confirm

/import/rules
  └── Import rules manager

/subscriptions
  ├── Recurring rule list
  ├── Monthly cost summary
  └── Missing transaction alerts

/tags
  └── Tag manager

/notes
  ├── All notes
  ├── Unmatched notes list
  └── New note form

/reports
  ├── Income vs Expense (period picker)
  ├── Category breakdown
  ├── Business vs Personal split
  ├── Subscription trend
  ├── Tag breakdown
  └── Export (PDF / CSV)

/settings
  ├── Classification rules
  ├── Categories manager
  ├── Profile (for guidance engine)
  └── Account (username / password change)
```

-----

## 7. Charts & Reporting

### Charts (Recharts)

|Chart                      |Location      |Type          |
|---------------------------|--------------|--------------|
|Monthly income vs expense  |Dashboard     |Grouped bar   |
|Account balance over time  |Account detail|Line          |
|Category breakdown         |Reports       |Donut / pie   |
|Business vs personal split |Reports       |Stacked bar   |
|Subscription cost over time|Subscriptions |Line          |
|Net cash flow trend        |Reports       |Area chart    |
|Tag breakdown              |Reports       |Horizontal bar|

### Report exports

- **PDF report**: Title, date range, summary table, charts (rendered to canvas then embedded), transaction list
- **CSV export**: Filtered transaction list with all visible columns
- Reports are generated client-side (jsPDF) — no server involvement

-----

## 8. Financial Guidance Engine

Takes inputs from `user_profile` plus derived data from transactions and produces plain-language benchmarks and suggestions.

### Inputs

- Age (derived from date_of_birth)
- Annual income (user-entered)
- Trailing 3-month average: total income, total expenses, subscription spend, business expenses
- Account balances

### Guidance rules (examples)

**Savings rate:**

> “You’re saving approximately X% of your income. Financial benchmarks suggest Y% for your age bracket. You’re [on track / below target].”

**Emergency fund:**

> “Based on your average monthly expenses of $X, a 3-month emergency fund would be $Y. Your current liquid savings total $Z — that’s [N weeks] of coverage.”

**Subscription load:**

> “Subscriptions are costing $X/month, which is Y% of your income. Common guidance is to keep this under 5–8%.”

**50/30/20 breakdown:**

> “Based on your last 3 months, your spending breaks down as: Needs X%, Wants Y%, Savings Z%. The 50/30/20 guideline targets 50 / 30 / 20.”

**Business expense ratio (if applicable):**

> “Business expenses make up X% of your total outflows. If you’re self-employed, ensure these are being tracked for tax purposes.”

**Debt service (if loan accounts present):**

> “Your loan payments represent X% of monthly income. Generally, total debt payments should stay under 36% of gross income.”

All rules are hard-coded and deterministic — no API call, no AI inference. Values and thresholds are sourced from standard personal finance guidelines (50/30/20, Dave Ramsey emergency fund, Fidelity savings benchmarks by age).

-----

## 9. Future Considerations

These were identified during planning as valuable but are out of scope for the initial build. Listed in rough priority order.

### High value (recommended next)

- **Net worth tracker** — aggregate all account balances (assets) minus any loan balances (liabilities) into a single figure, tracked over time
- **Budget envelopes** — allocate a monthly target per category; show how much is remaining as transactions come in
- **Transaction splitting** — divide one import row across multiple categories (e.g. a supermarket run that’s part groceries, part office supplies) — table `transaction_splits` is already modeled
- **Receipt / attachment storage** — attach a photo or PDF to a transaction, stored in a `/data/attachments/` folder alongside the SQLite file
- **Merchant normalization** — rules to collapse messy bank description strings (“AMZN*MKT12345”, “AMAZON MARKETPLACE”, “AMAZON.COM”) into a single clean merchant name

### Medium value

- **Cash flow forecast** — project next 30/60/90 days based on known recurring income and subscription rules
- **Tax category flagging** — mark individual transactions or categories as tax-deductible; generate a year-end tax summary export
- **Keyboard shortcuts + command palette** — quick-add transaction (Ctrl+N), jump to account (Ctrl+K)
- **Multi-currency dashboard** — if accounts in multiple currencies, show net worth in a selected base currency with live-ish rate (user-set, not fetched)

### Lower priority / optional

- **Savings goals** — set a target amount and date, track progress against current savings balance
- **Dark / light mode** — Tailwind makes this straightforward with `dark:` class variants
- **Encrypted backup** — export entire SQLite file as an AES-encrypted `.backup` file; import and decrypt to restore
- **Electron wrapper** — package the app as a native `.exe` / `.app` with no need to run a terminal; the data model requires no changes
- **Multi-user** — the `user_id` foreign key is already on every table; multi-user is architecturally possible with role additions

-----

*End of plan.*