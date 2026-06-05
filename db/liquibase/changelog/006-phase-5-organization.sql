--liquibase formatted sql

--changeset finance:006-tags
CREATE TABLE IF NOT EXISTS tags (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id uuid NOT NULL REFERENCES "AspNetUsers"("Id") ON DELETE CASCADE,
    name varchar(100) NOT NULL,
    color varchar(20),
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now()
);
CREATE UNIQUE INDEX IF NOT EXISTS ux_tags_user_name ON tags (user_id, lower(name));

DROP TRIGGER IF EXISTS trg_tags_updated_at ON tags;
CREATE TRIGGER trg_tags_updated_at BEFORE UPDATE ON tags FOR EACH ROW EXECUTE FUNCTION set_updated_at();

CREATE TABLE IF NOT EXISTS transaction_tags (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    transaction_id uuid NOT NULL REFERENCES transactions(id) ON DELETE CASCADE,
    tag_id uuid NOT NULL REFERENCES tags(id) ON DELETE CASCADE,
    created_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT ux_transaction_tags UNIQUE (transaction_id, tag_id)
);
CREATE INDEX IF NOT EXISTS ix_transaction_tags_tag ON transaction_tags (tag_id, transaction_id);

--changeset finance:006-recurring-rules splitStatements:false
CREATE TABLE IF NOT EXISTS recurring_rules (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id uuid NOT NULL REFERENCES "AspNetUsers"("Id") ON DELETE CASCADE,
    name varchar(255) NOT NULL,
    account_id uuid REFERENCES accounts(id) ON DELETE SET NULL,
    type varchar(50) NOT NULL,
    classification varchar(50) NOT NULL DEFAULT 'unknown',
    amount numeric(19,4) NOT NULL,
    currency char(3) NOT NULL DEFAULT 'USD',
    category varchar(255),
    merchant_keyword varchar(255),
    frequency varchar(50) NOT NULL,
    next_expected date NOT NULL,
    last_matched_date date,
    amount_tolerance numeric(7,4) NOT NULL DEFAULT 0.20,
    tags jsonb NOT NULL DEFAULT '[]'::jsonb,
    is_active boolean NOT NULL DEFAULT true,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT ck_recurring_rules_type CHECK (type IN ('income', 'expense', 'transfer', 'adjustment')),
    CONSTRAINT ck_recurring_rules_classification CHECK (classification IN ('business', 'personal', 'mixed', 'ignored', 'unknown')),
    CONSTRAINT ck_recurring_rules_frequency CHECK (frequency IN ('daily', 'weekly', 'biweekly', 'monthly', 'quarterly', 'yearly')),
    CONSTRAINT ck_recurring_rules_amount CHECK (amount > 0),
    CONSTRAINT ck_recurring_rules_tolerance CHECK (amount_tolerance >= 0 AND amount_tolerance <= 1)
);
CREATE INDEX IF NOT EXISTS ix_recurring_rules_user_active_expected ON recurring_rules (user_id, is_active, next_expected);

DROP TRIGGER IF EXISTS trg_recurring_rules_updated_at ON recurring_rules;
CREATE TRIGGER trg_recurring_rules_updated_at BEFORE UPDATE ON recurring_rules FOR EACH ROW EXECUTE FUNCTION set_updated_at();

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_transactions_recurring_rule') THEN
        ALTER TABLE transactions ADD CONSTRAINT fk_transactions_recurring_rule
            FOREIGN KEY (recurring_rule_id) REFERENCES recurring_rules(id) ON DELETE SET NULL;
    END IF;
END;
$$;
CREATE INDEX IF NOT EXISTS ix_transactions_recurring_rule ON transactions (recurring_rule_id, date);

--changeset finance:006-notes-reminders
CREATE TABLE IF NOT EXISTS notes (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id uuid NOT NULL REFERENCES "AspNetUsers"("Id") ON DELETE CASCADE,
    title varchar(255) NOT NULL,
    body text,
    amount_hint numeric(19,4),
    merchant_hint varchar(255),
    date_hint date,
    matched_transaction_id uuid REFERENCES transactions(id) ON DELETE SET NULL,
    status varchar(50) NOT NULL DEFAULT 'unmatched',
    remind_on date,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT ck_notes_status CHECK (status IN ('unmatched', 'matched', 'dismissed')),
    CONSTRAINT ck_notes_amount_hint CHECK (amount_hint IS NULL OR amount_hint >= 0)
);
CREATE INDEX IF NOT EXISTS ix_notes_user_status_remind ON notes (user_id, status, remind_on);

DROP TRIGGER IF EXISTS trg_notes_updated_at ON notes;
CREATE TRIGGER trg_notes_updated_at BEFORE UPDATE ON notes FOR EACH ROW EXECUTE FUNCTION set_updated_at();

CREATE TABLE IF NOT EXISTS reminders (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id uuid NOT NULL REFERENCES "AspNetUsers"("Id") ON DELETE CASCADE,
    type varchar(50) NOT NULL,
    source_id uuid,
    title varchar(255) NOT NULL,
    message text,
    due_on date NOT NULL,
    status varchar(50) NOT NULL DEFAULT 'pending',
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT ck_reminders_type CHECK (type IN ('note', 'recurring_rule', 'reconciliation', 'custom')),
    CONSTRAINT ck_reminders_status CHECK (status IN ('pending', 'dismissed', 'completed'))
);
CREATE UNIQUE INDEX IF NOT EXISTS ux_reminders_pending_source ON reminders (user_id, type, source_id) WHERE status = 'pending' AND source_id IS NOT NULL;
CREATE INDEX IF NOT EXISTS ix_reminders_user_status_due ON reminders (user_id, status, due_on);

DROP TRIGGER IF EXISTS trg_reminders_updated_at ON reminders;
CREATE TRIGGER trg_reminders_updated_at BEFORE UPDATE ON reminders FOR EACH ROW EXECUTE FUNCTION set_updated_at();
