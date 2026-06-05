--liquibase formatted sql

--changeset finance:007-balance-checkpoints
CREATE TABLE IF NOT EXISTS balance_checkpoints (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id uuid NOT NULL REFERENCES "AspNetUsers"("Id") ON DELETE CASCADE,
    account_id uuid NOT NULL REFERENCES accounts(id) ON DELETE CASCADE,
    date date NOT NULL,
    balance numeric(19,4) NOT NULL,
    notes text,
    expected_balance numeric(19,4) NOT NULL,
    discrepancy numeric(19,4) NOT NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS ix_balance_checkpoints_user_account_date ON balance_checkpoints (user_id, account_id, date DESC);

DROP TRIGGER IF EXISTS trg_balance_checkpoints_updated_at ON balance_checkpoints;
CREATE TRIGGER trg_balance_checkpoints_updated_at BEFORE UPDATE ON balance_checkpoints FOR EACH ROW EXECUTE FUNCTION set_updated_at();

--changeset finance:007-export-metadata
CREATE TABLE IF NOT EXISTS export_files (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id uuid NOT NULL REFERENCES "AspNetUsers"("Id") ON DELETE CASCADE,
    export_type varchar(50) NOT NULL,
    filters jsonb NOT NULL DEFAULT '{}'::jsonb,
    s3_object_key text NOT NULL,
    content_type varchar(100) NOT NULL DEFAULT 'text/csv',
    file_name varchar(255) NOT NULL,
    size_bytes bigint NOT NULL DEFAULT 0,
    created_at timestamptz NOT NULL DEFAULT now(),
    expires_at timestamptz
);
CREATE INDEX IF NOT EXISTS ix_export_files_user_created ON export_files (user_id, created_at DESC);

--changeset finance:007-user-finance-profile
CREATE TABLE IF NOT EXISTS user_finance_profiles (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id uuid NOT NULL REFERENCES "AspNetUsers"("Id") ON DELETE CASCADE,
    date_of_birth date,
    annual_income numeric(19,4),
    income_type varchar(50) NOT NULL DEFAULT 'other',
    dependents integer NOT NULL DEFAULT 0,
    financial_goals jsonb NOT NULL DEFAULT '[]'::jsonb,
    category_mappings jsonb NOT NULL DEFAULT '{}'::jsonb,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT ux_user_finance_profiles_user UNIQUE (user_id),
    CONSTRAINT ck_user_finance_profiles_income_type CHECK (income_type IN ('salaried', 'freelance', 'mixed', 'retired', 'student', 'other')),
    CONSTRAINT ck_user_finance_profiles_annual_income CHECK (annual_income IS NULL OR annual_income >= 0),
    CONSTRAINT ck_user_finance_profiles_dependents CHECK (dependents >= 0)
);

DROP TRIGGER IF EXISTS trg_user_finance_profiles_updated_at ON user_finance_profiles;
CREATE TRIGGER trg_user_finance_profiles_updated_at BEFORE UPDATE ON user_finance_profiles FOR EACH ROW EXECUTE FUNCTION set_updated_at();

--changeset finance:007-report-indexes
CREATE INDEX IF NOT EXISTS ix_transactions_user_date_status_type ON transactions (user_id, date, status, type);
CREATE INDEX IF NOT EXISTS ix_transactions_user_account_status_date ON transactions (user_id, account_id, status, date);
CREATE INDEX IF NOT EXISTS ix_transaction_splits_transaction_category ON transaction_splits (transaction_id, category, classification);
