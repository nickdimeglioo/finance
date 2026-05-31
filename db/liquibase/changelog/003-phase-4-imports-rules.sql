--liquibase formatted sql

--changeset finance:003-import-batches
CREATE TABLE IF NOT EXISTS import_batches (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id uuid NOT NULL REFERENCES "AspNetUsers"("Id") ON DELETE CASCADE,
    account_id uuid NOT NULL REFERENCES accounts(id) ON DELETE RESTRICT,
    institution varchar(255),
    original_file_name varchar(512) NOT NULL,
    content_type varchar(255) NOT NULL,
    s3_object_key text NOT NULL,
    status varchar(50) NOT NULL DEFAULT 'uploaded',
    row_count integer NOT NULL DEFAULT 0,
    accepted_count integer NOT NULL DEFAULT 0,
    duplicate_count integer NOT NULL DEFAULT 0,
    error_count integer NOT NULL DEFAULT 0,
    metadata jsonb NOT NULL DEFAULT '{}'::jsonb,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT ck_import_batches_status CHECK (status IN ('uploaded', 'parsed', 'previewed', 'committed', 'failed', 'cancelled')),
    CONSTRAINT ck_import_batches_counts CHECK (row_count >= 0 AND accepted_count >= 0 AND duplicate_count >= 0 AND error_count >= 0)
);

CREATE INDEX IF NOT EXISTS ix_import_batches_user_created ON import_batches (user_id, created_at DESC);
CREATE INDEX IF NOT EXISTS ix_import_batches_user_status ON import_batches (user_id, status);
CREATE INDEX IF NOT EXISTS ix_import_batches_account ON import_batches (account_id);

DROP TRIGGER IF EXISTS trg_import_batches_updated_at ON import_batches;
CREATE TRIGGER trg_import_batches_updated_at
BEFORE UPDATE ON import_batches
FOR EACH ROW
EXECUTE FUNCTION set_updated_at();

--changeset finance:003-import-preview-rows
CREATE TABLE IF NOT EXISTS import_preview_rows (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    import_batch_id uuid NOT NULL REFERENCES import_batches(id) ON DELETE CASCADE,
    row_number integer NOT NULL,
    raw_data jsonb NOT NULL DEFAULT '{}'::jsonb,
    raw_description text,
    cleaned_description text,
    date date,
    amount numeric(19,4),
    type varchar(50),
    category varchar(255),
    classification varchar(50) NOT NULL DEFAULT 'unknown',
    import_hash varchar(128),
    is_duplicate boolean NOT NULL DEFAULT false,
    accepted boolean NOT NULL DEFAULT true,
    errors jsonb NOT NULL DEFAULT '[]'::jsonb,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT uq_import_preview_rows_batch_row UNIQUE (import_batch_id, row_number),
    CONSTRAINT ck_import_preview_rows_amount CHECK (amount IS NULL OR amount >= 0),
    CONSTRAINT ck_import_preview_rows_type CHECK (type IS NULL OR type IN ('income', 'expense', 'transfer', 'adjustment', 'opening_balance')),
    CONSTRAINT ck_import_preview_rows_classification CHECK (classification IN ('business', 'personal', 'mixed', 'ignored', 'unknown'))
);

CREATE INDEX IF NOT EXISTS ix_import_preview_rows_batch ON import_preview_rows (import_batch_id, row_number);
CREATE INDEX IF NOT EXISTS ix_import_preview_rows_hash ON import_preview_rows (import_hash);
CREATE INDEX IF NOT EXISTS ix_import_preview_rows_duplicate ON import_preview_rows (import_batch_id, is_duplicate);

DROP TRIGGER IF EXISTS trg_import_preview_rows_updated_at ON import_preview_rows;
CREATE TRIGGER trg_import_preview_rows_updated_at
BEFORE UPDATE ON import_preview_rows
FOR EACH ROW
EXECUTE FUNCTION set_updated_at();

--changeset finance:003-import-templates
CREATE TABLE IF NOT EXISTS import_templates (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id uuid NOT NULL REFERENCES "AspNetUsers"("Id") ON DELETE CASCADE,
    institution varchar(255),
    name varchar(255) NOT NULL,
    column_map jsonb NOT NULL DEFAULT '{}'::jsonb,
    date_format varchar(64),
    amount_format varchar(64),
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS ix_import_templates_user_institution ON import_templates (user_id, institution);

DROP TRIGGER IF EXISTS trg_import_templates_updated_at ON import_templates;
CREATE TRIGGER trg_import_templates_updated_at
BEFORE UPDATE ON import_templates
FOR EACH ROW
EXECUTE FUNCTION set_updated_at();

--changeset finance:003-import-rules
CREATE TABLE IF NOT EXISTS import_rules (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id uuid NOT NULL REFERENCES "AspNetUsers"("Id") ON DELETE CASCADE,
    name varchar(255) NOT NULL,
    pattern text NOT NULL,
    maps_to_type varchar(50),
    maps_to_category varchar(255),
    maps_to_classification varchar(50),
    maps_to_description text,
    priority integer NOT NULL DEFAULT 100,
    is_active boolean NOT NULL DEFAULT true,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT ck_import_rules_type CHECK (maps_to_type IS NULL OR maps_to_type IN ('income', 'expense', 'transfer', 'adjustment', 'opening_balance')),
    CONSTRAINT ck_import_rules_classification CHECK (maps_to_classification IS NULL OR maps_to_classification IN ('business', 'personal', 'mixed', 'ignored', 'unknown'))
);

CREATE INDEX IF NOT EXISTS ix_import_rules_user_priority ON import_rules (user_id, priority, name);
CREATE INDEX IF NOT EXISTS ix_import_rules_user_active ON import_rules (user_id, is_active);

DROP TRIGGER IF EXISTS trg_import_rules_updated_at ON import_rules;
CREATE TRIGGER trg_import_rules_updated_at
BEFORE UPDATE ON import_rules
FOR EACH ROW
EXECUTE FUNCTION set_updated_at();

--changeset finance:003-classification-rules
CREATE TABLE IF NOT EXISTS classification_rules (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id uuid NOT NULL REFERENCES "AspNetUsers"("Id") ON DELETE CASCADE,
    name varchar(255) NOT NULL,
    rule_type varchar(50) NOT NULL,
    field_target varchar(50) NOT NULL,
    value text NOT NULL,
    classification varchar(50) NOT NULL,
    also_set_category varchar(255),
    priority integer NOT NULL DEFAULT 100,
    is_active boolean NOT NULL DEFAULT true,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT ck_classification_rules_rule_type CHECK (rule_type IN ('keyword_contains', 'merchant_exact', 'category_is', 'amount_gte', 'amount_lte', 'amount_range')),
    CONSTRAINT ck_classification_rules_field_target CHECK (field_target IN ('description', 'merchant', 'category', 'amount')),
    CONSTRAINT ck_classification_rules_classification CHECK (classification IN ('business', 'personal', 'mixed', 'ignored', 'unknown'))
);

CREATE INDEX IF NOT EXISTS ix_classification_rules_user_priority ON classification_rules (user_id, priority, name);
CREATE INDEX IF NOT EXISTS ix_classification_rules_user_active ON classification_rules (user_id, is_active);

DROP TRIGGER IF EXISTS trg_classification_rules_updated_at ON classification_rules;
CREATE TRIGGER trg_classification_rules_updated_at
BEFORE UPDATE ON classification_rules
FOR EACH ROW
EXECUTE FUNCTION set_updated_at();
