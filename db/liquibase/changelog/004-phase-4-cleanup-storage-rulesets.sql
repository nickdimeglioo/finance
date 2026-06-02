--liquibase formatted sql

--changeset finance:004-guid-primary-key-defaults
ALTER TABLE accounts ALTER COLUMN id SET DEFAULT gen_random_uuid();
ALTER TABLE transactions ALTER COLUMN id SET DEFAULT gen_random_uuid();
ALTER TABLE transaction_splits ALTER COLUMN id SET DEFAULT gen_random_uuid();
ALTER TABLE import_batches ALTER COLUMN id SET DEFAULT gen_random_uuid();
ALTER TABLE import_preview_rows ALTER COLUMN id SET DEFAULT gen_random_uuid();
ALTER TABLE import_templates ALTER COLUMN id SET DEFAULT gen_random_uuid();
ALTER TABLE import_rules ALTER COLUMN id SET DEFAULT gen_random_uuid();
ALTER TABLE classification_rules ALTER COLUMN id SET DEFAULT gen_random_uuid();

--changeset finance:004-storage-files
CREATE TABLE IF NOT EXISTS storage_files (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id uuid NOT NULL REFERENCES "AspNetUsers"("Id") ON DELETE CASCADE,
    original_file_name varchar(512) NOT NULL,
    stored_file_name varchar(512) NOT NULL,
    content_type varchar(255) NOT NULL,
    s3_object_key text NOT NULL,
    size_bytes bigint NOT NULL,
    purpose varchar(50) NOT NULL DEFAULT 'import',
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT ck_storage_files_size CHECK (size_bytes >= 0),
    CONSTRAINT ck_storage_files_purpose CHECK (purpose IN ('import', 'attachment', 'export', 'other'))
);

CREATE INDEX IF NOT EXISTS ix_storage_files_user_name ON storage_files (user_id, stored_file_name);
CREATE INDEX IF NOT EXISTS ix_storage_files_user_created ON storage_files (user_id, created_at DESC);

DROP TRIGGER IF EXISTS trg_storage_files_updated_at ON storage_files;
CREATE TRIGGER trg_storage_files_updated_at
BEFORE UPDATE ON storage_files
FOR EACH ROW
EXECUTE FUNCTION set_updated_at();

--changeset finance:004-import-rule-sets
CREATE TABLE IF NOT EXISTS import_rule_sets (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id uuid NOT NULL REFERENCES "AspNetUsers"("Id") ON DELETE CASCADE,
    name varchar(255) NOT NULL,
    institution varchar(255),
    is_active boolean NOT NULL DEFAULT true,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS ix_import_rule_sets_user_active ON import_rule_sets (user_id, is_active, name);

DROP TRIGGER IF EXISTS trg_import_rule_sets_updated_at ON import_rule_sets;
CREATE TRIGGER trg_import_rule_sets_updated_at
BEFORE UPDATE ON import_rule_sets
FOR EACH ROW
EXECUTE FUNCTION set_updated_at();

ALTER TABLE import_rules
    ADD COLUMN IF NOT EXISTS rule_set_id uuid,
    ADD COLUMN IF NOT EXISTS source_field varchar(255),
    ADD COLUMN IF NOT EXISTS target_field varchar(50),
    ADD COLUMN IF NOT EXISTS value_transform varchar(50) NOT NULL DEFAULT 'copy';

ALTER TABLE import_rules
    ALTER COLUMN pattern DROP NOT NULL;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint WHERE conname = 'fk_import_rules_rule_set'
    ) THEN
        ALTER TABLE import_rules
            ADD CONSTRAINT fk_import_rules_rule_set FOREIGN KEY (rule_set_id) REFERENCES import_rule_sets(id) ON DELETE CASCADE;
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint WHERE conname = 'ck_import_rules_target_field'
    ) THEN
        ALTER TABLE import_rules
            ADD CONSTRAINT ck_import_rules_target_field CHECK (target_field IS NULL OR target_field IN ('date', 'description', 'merchant', 'amount', 'type', 'category', 'classification'));
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint WHERE conname = 'ck_import_rules_value_transform'
    ) THEN
        ALTER TABLE import_rules
            ADD CONSTRAINT ck_import_rules_value_transform CHECK (value_transform IN ('copy', 'amount_positive', 'amount_negative'));
    END IF;
END;
$$;

CREATE INDEX IF NOT EXISTS ix_import_rules_rule_set_priority ON import_rules (rule_set_id, priority, name);
