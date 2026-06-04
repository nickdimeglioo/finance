--liquibase formatted sql

--changeset finance:005-rulesets
CREATE TABLE IF NOT EXISTS rulesets (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id uuid NOT NULL REFERENCES "AspNetUsers"("Id") ON DELETE CASCADE,
    name varchar(255) NOT NULL,
    description text,
    version integer NOT NULL DEFAULT 1,
    source_config jsonb NOT NULL DEFAULT '{}'::jsonb,
    rules jsonb NOT NULL DEFAULT '{}'::jsonb,
    is_active boolean NOT NULL DEFAULT true,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT ck_rulesets_version CHECK (version >= 1)
);

CREATE INDEX IF NOT EXISTS ix_rulesets_user_active_name ON rulesets (user_id, is_active, name);

DROP TRIGGER IF EXISTS trg_rulesets_updated_at ON rulesets;
CREATE TRIGGER trg_rulesets_updated_at
BEFORE UPDATE ON rulesets
FOR EACH ROW
EXECUTE FUNCTION set_updated_at();

--changeset finance:005-import-jobs
CREATE TABLE IF NOT EXISTS import_jobs (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id uuid NOT NULL REFERENCES "AspNetUsers"("Id") ON DELETE CASCADE,
    account_id uuid NOT NULL REFERENCES accounts(id) ON DELETE RESTRICT,
    ruleset_id uuid NOT NULL REFERENCES rulesets(id) ON DELETE RESTRICT,
    ruleset_version integer NOT NULL,
    file_name varchar(512) NOT NULL,
    status varchar(50) NOT NULL DEFAULT 'pending',
    total_rows integer NOT NULL DEFAULT 0,
    success_rows integer NOT NULL DEFAULT 0,
    skipped_rows integer NOT NULL DEFAULT 0,
    error_rows integer NOT NULL DEFAULT 0,
    errors jsonb NOT NULL DEFAULT '[]'::jsonb,
    is_dry_run boolean NOT NULL DEFAULT true,
    started_at timestamptz NOT NULL DEFAULT now(),
    completed_at timestamptz,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT ck_import_jobs_status CHECK (status IN ('pending', 'processing', 'complete', 'failed')),
    CONSTRAINT ck_import_jobs_counts CHECK (total_rows >= 0 AND success_rows >= 0 AND skipped_rows >= 0 AND error_rows >= 0),
    CONSTRAINT ck_import_jobs_ruleset_version CHECK (ruleset_version >= 1)
);

CREATE INDEX IF NOT EXISTS ix_import_jobs_user_started ON import_jobs (user_id, started_at DESC);
CREATE INDEX IF NOT EXISTS ix_import_jobs_ruleset ON import_jobs (ruleset_id, started_at DESC);

DROP TRIGGER IF EXISTS trg_import_jobs_updated_at ON import_jobs;
CREATE TRIGGER trg_import_jobs_updated_at
BEFORE UPDATE ON import_jobs
FOR EACH ROW
EXECUTE FUNCTION set_updated_at();

--changeset finance:005-transaction-ruleset-audit
ALTER TABLE transactions
    ADD COLUMN IF NOT EXISTS unique_id varchar(255),
    ADD COLUMN IF NOT EXISTS ruleset_id uuid,
    ADD COLUMN IF NOT EXISTS ruleset_version integer,
    ADD COLUMN IF NOT EXISTS matched_classification_rule_id varchar(255),
    ADD COLUMN IF NOT EXISTS subcategory varchar(255),
    ADD COLUMN IF NOT EXISTS tags jsonb NOT NULL DEFAULT '[]'::jsonb,
    ADD COLUMN IF NOT EXISTS raw_row jsonb NOT NULL DEFAULT '{}'::jsonb;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint WHERE conname = 'fk_transactions_ruleset'
    ) THEN
        ALTER TABLE transactions
            ADD CONSTRAINT fk_transactions_ruleset FOREIGN KEY (ruleset_id) REFERENCES rulesets(id) ON DELETE SET NULL;
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint WHERE conname = 'ck_transactions_ruleset_version'
    ) THEN
        ALTER TABLE transactions
            ADD CONSTRAINT ck_transactions_ruleset_version CHECK (ruleset_version IS NULL OR ruleset_version >= 1);
    END IF;
END;
$$;

CREATE UNIQUE INDEX IF NOT EXISTS ux_transactions_user_unique_id
    ON transactions (user_id, unique_id)
    WHERE unique_id IS NOT NULL;

CREATE INDEX IF NOT EXISTS ix_transactions_user_ruleset ON transactions (user_id, ruleset_id, date);
