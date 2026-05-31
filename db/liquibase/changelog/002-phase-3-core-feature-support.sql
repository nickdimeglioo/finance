--liquibase formatted sql

--changeset finance:002-transaction-direction-and-user-fks
ALTER TABLE transactions
    ADD COLUMN IF NOT EXISTS direction varchar(20);

UPDATE transactions
SET direction = CASE
    WHEN type IN ('income', 'opening_balance') THEN 'inflow'
    WHEN type = 'expense' THEN 'outflow'
    ELSE 'neutral'
END
WHERE direction IS NULL;

ALTER TABLE transactions
    ALTER COLUMN direction SET DEFAULT 'neutral';

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'ck_transactions_direction'
    ) THEN
        ALTER TABLE transactions
            ADD CONSTRAINT ck_transactions_direction CHECK (direction IN ('inflow', 'outflow', 'neutral'));
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'fk_accounts_user'
    ) THEN
        ALTER TABLE accounts
            ADD CONSTRAINT fk_accounts_user FOREIGN KEY (user_id) REFERENCES "AspNetUsers"("Id") ON DELETE CASCADE;
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'fk_transactions_user'
    ) THEN
        ALTER TABLE transactions
            ADD CONSTRAINT fk_transactions_user FOREIGN KEY (user_id) REFERENCES "AspNetUsers"("Id") ON DELETE CASCADE;
    END IF;
END;
$$;

CREATE INDEX IF NOT EXISTS ix_transactions_user_type_date ON transactions (user_id, type, date);
CREATE INDEX IF NOT EXISTS ix_transactions_user_direction_date ON transactions (user_id, direction, date);
CREATE INDEX IF NOT EXISTS ix_transactions_user_non_voided_date ON transactions (user_id, date) WHERE is_void = false;
CREATE INDEX IF NOT EXISTS ix_transactions_transfer_partner ON transactions (transfer_partner_id);

