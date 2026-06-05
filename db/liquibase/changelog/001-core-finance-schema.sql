--liquibase formatted sql

--changeset finance:001-core-extensions
CREATE EXTENSION IF NOT EXISTS pgcrypto;

--changeset finance:001-updated-at-trigger splitStatements:false
CREATE OR REPLACE FUNCTION set_updated_at()
RETURNS trigger AS $$
BEGIN
    NEW.updated_at = now();
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

--changeset finance:001-accounts
CREATE TABLE IF NOT EXISTS accounts (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id uuid NOT NULL,
    institution varchar(255),
    nickname varchar(255) NOT NULL,
    type varchar(50) NOT NULL,
    currency char(3) NOT NULL DEFAULT 'USD',
    opening_balance numeric(19,4) NOT NULL DEFAULT 0,
    credit_limit numeric(19,4),
    interest_rate numeric(9,6),
    status varchar(50) NOT NULL DEFAULT 'active',
    include_in_dashboard boolean NOT NULL DEFAULT true,
    notes text,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT ck_accounts_type CHECK (type IN ('checking', 'savings', 'credit_card', 'loan', 'investment', 'cash', 'other')),
    CONSTRAINT ck_accounts_status CHECK (status IN ('active', 'archived', 'closed')),
    CONSTRAINT ck_accounts_opening_balance CHECK (opening_balance >= 0),
    CONSTRAINT ck_accounts_credit_limit CHECK (credit_limit IS NULL OR credit_limit >= 0),
    CONSTRAINT ck_accounts_interest_rate CHECK (interest_rate IS NULL OR interest_rate >= 0)
);

CREATE INDEX IF NOT EXISTS ix_accounts_user_status ON accounts (user_id, status);
CREATE INDEX IF NOT EXISTS ix_accounts_user_dashboard ON accounts (user_id, include_in_dashboard);

DROP TRIGGER IF EXISTS trg_accounts_updated_at ON accounts;
CREATE TRIGGER trg_accounts_updated_at
BEFORE UPDATE ON accounts
FOR EACH ROW
EXECUTE FUNCTION set_updated_at();

--changeset finance:001-transactions
CREATE TABLE IF NOT EXISTS transactions (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id uuid NOT NULL,
    account_id uuid NOT NULL REFERENCES accounts(id) ON DELETE RESTRICT,
    date date NOT NULL,
    posted_at date,
    description text NOT NULL,
    merchant varchar(255),
    type varchar(50) NOT NULL,
    classification varchar(50) NOT NULL DEFAULT 'unknown',
    category varchar(255),
    amount numeric(19,4) NOT NULL,
    currency char(3) NOT NULL DEFAULT 'USD',
    status varchar(50) NOT NULL DEFAULT 'posted',
    source varchar(50) NOT NULL DEFAULT 'manual',
    import_hash varchar(128),
    is_void boolean NOT NULL DEFAULT false,
    is_split boolean NOT NULL DEFAULT false,
    transfer_partner_id uuid REFERENCES transactions(id) ON DELETE SET NULL,
    recurring_rule_id uuid,
    metadata jsonb NOT NULL DEFAULT '{}'::jsonb,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT ck_transactions_type CHECK (type IN ('income', 'expense', 'transfer', 'adjustment', 'opening_balance')),
    CONSTRAINT ck_transactions_classification CHECK (classification IN ('business', 'personal', 'mixed', 'ignored', 'unknown')),
    CONSTRAINT ck_transactions_status CHECK (status IN ('pending', 'posted', 'reconciled', 'voided')),
    CONSTRAINT ck_transactions_source CHECK (source IN ('manual', 'import', 'system')),
    CONSTRAINT ck_transactions_amount CHECK (amount >= 0),
    CONSTRAINT ck_transactions_transfer_not_self CHECK (transfer_partner_id IS NULL OR transfer_partner_id <> id)
);

CREATE INDEX IF NOT EXISTS ix_transactions_user_date ON transactions (user_id, date);
CREATE INDEX IF NOT EXISTS ix_transactions_user_account_date ON transactions (user_id, account_id, date);
CREATE INDEX IF NOT EXISTS ix_transactions_user_category ON transactions (user_id, category);
CREATE INDEX IF NOT EXISTS ix_transactions_user_classification ON transactions (user_id, classification);
CREATE INDEX IF NOT EXISTS ix_transactions_user_import_hash ON transactions (user_id, import_hash);
CREATE INDEX IF NOT EXISTS ix_transactions_user_status ON transactions (user_id, status);
CREATE INDEX IF NOT EXISTS ix_transactions_description_search ON transactions USING gin (to_tsvector('simple', coalesce(description, '') || ' ' || coalesce(merchant, '')));

DROP TRIGGER IF EXISTS trg_transactions_updated_at ON transactions;
CREATE TRIGGER trg_transactions_updated_at
BEFORE UPDATE ON transactions
FOR EACH ROW
EXECUTE FUNCTION set_updated_at();

--changeset finance:001-transaction-splits
CREATE TABLE IF NOT EXISTS transaction_splits (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    transaction_id uuid NOT NULL REFERENCES transactions(id) ON DELETE CASCADE,
    category varchar(255) NOT NULL,
    classification varchar(50) NOT NULL DEFAULT 'unknown',
    amount numeric(19,4) NOT NULL,
    notes text,
    metadata jsonb NOT NULL DEFAULT '{}'::jsonb,
    created_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT ck_transaction_splits_classification CHECK (classification IN ('business', 'personal', 'mixed', 'ignored', 'unknown')),
    CONSTRAINT ck_transaction_splits_amount CHECK (amount >= 0)
);

CREATE INDEX IF NOT EXISTS ix_transaction_splits_transaction ON transaction_splits (transaction_id);
CREATE INDEX IF NOT EXISTS ix_transaction_splits_category ON transaction_splits (category);
CREATE INDEX IF NOT EXISTS ix_transaction_splits_classification ON transaction_splits (classification);
