--liquibase formatted sql

--changeset finance:009-transfer-link-suggestions
CREATE TABLE IF NOT EXISTS transaction_transfer_link_suggestions (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id uuid NOT NULL,
    transaction_id uuid NOT NULL REFERENCES transactions(id) ON DELETE CASCADE,
    target_account_id uuid REFERENCES accounts(id) ON DELETE SET NULL,
    candidate_transaction_id uuid REFERENCES transactions(id) ON DELETE SET NULL,
    link_mode varchar(20) NOT NULL DEFAULT 'suggest',
    match_window_days integer NOT NULL DEFAULT 7,
    candidate_count integer NOT NULL DEFAULT 0,
    status varchar(30) NOT NULL DEFAULT 'suggested',
    message text,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT ck_transfer_link_suggestions_mode CHECK (link_mode IN ('auto', 'suggest')),
    CONSTRAINT ck_transfer_link_suggestions_window CHECK (match_window_days BETWEEN 0 AND 45),
    CONSTRAINT ck_transfer_link_suggestions_candidate_count CHECK (candidate_count >= 0),
    CONSTRAINT ck_transfer_link_suggestions_status CHECK (status IN ('suggested', 'unresolved', 'dismissed'))
);

CREATE INDEX IF NOT EXISTS ix_transfer_link_suggestions_transaction
    ON transaction_transfer_link_suggestions (user_id, transaction_id, status);

DROP TRIGGER IF EXISTS trg_transfer_link_suggestions_updated_at ON transaction_transfer_link_suggestions;
CREATE TRIGGER trg_transfer_link_suggestions_updated_at
BEFORE UPDATE ON transaction_transfer_link_suggestions
FOR EACH ROW
EXECUTE FUNCTION set_updated_at();
