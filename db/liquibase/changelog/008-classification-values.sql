--liquibase formatted sql

--changeset finance:008-classification-values
ALTER TABLE transactions DROP CONSTRAINT IF EXISTS ck_transactions_classification;
ALTER TABLE transactions
    ADD CONSTRAINT ck_transactions_classification
    CHECK (classification IN ('personal', 'business', 'transfer', 'investment', 'tax', 'reimbursement', 'exclude', 'mixed', 'ignored', 'unknown'));

ALTER TABLE transaction_splits DROP CONSTRAINT IF EXISTS ck_transaction_splits_classification;
ALTER TABLE transaction_splits
    ADD CONSTRAINT ck_transaction_splits_classification
    CHECK (classification IN ('personal', 'business', 'transfer', 'investment', 'tax', 'reimbursement', 'exclude', 'mixed', 'ignored', 'unknown'));

ALTER TABLE import_preview_rows DROP CONSTRAINT IF EXISTS ck_import_preview_rows_classification;
ALTER TABLE import_preview_rows
    ADD CONSTRAINT ck_import_preview_rows_classification
    CHECK (classification IN ('personal', 'business', 'transfer', 'investment', 'tax', 'reimbursement', 'exclude', 'mixed', 'ignored', 'unknown'));

ALTER TABLE import_rules DROP CONSTRAINT IF EXISTS ck_import_rules_classification;
ALTER TABLE import_rules
    ADD CONSTRAINT ck_import_rules_classification
    CHECK (maps_to_classification IS NULL OR maps_to_classification IN ('personal', 'business', 'transfer', 'investment', 'tax', 'reimbursement', 'exclude', 'mixed', 'ignored', 'unknown'));

ALTER TABLE classification_rules DROP CONSTRAINT IF EXISTS ck_classification_rules_classification;
ALTER TABLE classification_rules
    ADD CONSTRAINT ck_classification_rules_classification
    CHECK (classification IN ('personal', 'business', 'transfer', 'investment', 'tax', 'reimbursement', 'exclude', 'mixed', 'ignored', 'unknown'));

ALTER TABLE recurring_rules DROP CONSTRAINT IF EXISTS ck_recurring_rules_classification;
ALTER TABLE recurring_rules
    ADD CONSTRAINT ck_recurring_rules_classification
    CHECK (classification IN ('personal', 'business', 'transfer', 'investment', 'tax', 'reimbursement', 'exclude', 'mixed', 'ignored', 'unknown'));
