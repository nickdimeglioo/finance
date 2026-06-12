--liquibase formatted sql

--changeset finance:010-budget-goals
CREATE TABLE IF NOT EXISTS budget_goals (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id uuid NOT NULL REFERENCES "AspNetUsers"("Id") ON DELETE CASCADE,
    name varchar(255) NOT NULL,
    kind varchar(20) NOT NULL,
    account_id uuid REFERENCES accounts(id) ON DELETE SET NULL,
    category varchar(255),
    classification varchar(50),
    tag_names jsonb NOT NULL DEFAULT '[]'::jsonb,
    starts_on date NOT NULL,
    ends_on date NOT NULL,
    target_amount numeric(19,4) NOT NULL,
    currency char(3) NOT NULL DEFAULT 'USD',
    include_splits boolean NOT NULL DEFAULT true,
    is_active boolean NOT NULL DEFAULT true,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT ck_budget_goals_kind CHECK (kind IN ('budget', 'goal')),
    CONSTRAINT ck_budget_goals_target CHECK (target_amount > 0),
    CONSTRAINT ck_budget_goals_range CHECK (ends_on >= starts_on)
);
CREATE INDEX IF NOT EXISTS ix_budget_goals_user_active_kind ON budget_goals (user_id, is_active, kind);
CREATE INDEX IF NOT EXISTS ix_budget_goals_user_range ON budget_goals (user_id, starts_on, ends_on);

DROP TRIGGER IF EXISTS trg_budget_goals_updated_at ON budget_goals;
CREATE TRIGGER trg_budget_goals_updated_at BEFORE UPDATE ON budget_goals FOR EACH ROW EXECUTE FUNCTION set_updated_at();

--changeset finance:010-receipt-attachments
CREATE TABLE IF NOT EXISTS receipt_attachments (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id uuid NOT NULL REFERENCES "AspNetUsers"("Id") ON DELETE CASCADE,
    transaction_id uuid REFERENCES transactions(id) ON DELETE SET NULL,
    title varchar(255) NOT NULL,
    notes text,
    original_file_name varchar(512) NOT NULL,
    stored_file_name varchar(512) NOT NULL,
    content_type varchar(255) NOT NULL DEFAULT 'application/octet-stream',
    s3_object_key text NOT NULL,
    size_bytes bigint NOT NULL,
    amount_hint numeric(19,4),
    merchant_hint varchar(255),
    date_hint date,
    status varchar(50) NOT NULL DEFAULT 'unmatched',
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT ck_receipt_attachments_status CHECK (status IN ('unmatched', 'matched', 'dismissed')),
    CONSTRAINT ck_receipt_attachments_amount_hint CHECK (amount_hint IS NULL OR amount_hint >= 0),
    CONSTRAINT ck_receipt_attachments_size CHECK (size_bytes > 0)
);
CREATE INDEX IF NOT EXISTS ix_receipt_attachments_user_status ON receipt_attachments (user_id, status, created_at DESC);
CREATE INDEX IF NOT EXISTS ix_receipt_attachments_transaction ON receipt_attachments (transaction_id);

DROP TRIGGER IF EXISTS trg_receipt_attachments_updated_at ON receipt_attachments;
CREATE TRIGGER trg_receipt_attachments_updated_at BEFORE UPDATE ON receipt_attachments FOR EACH ROW EXECUTE FUNCTION set_updated_at();
