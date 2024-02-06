SET timezone = 'America/Los_Angeles';

DROP TABLE IF EXISTS billingstatements;
DROP TABLE IF EXISTS users;
DROP TABLE IF EXISTS accounts;

CREATE TABLE accounts (
    id UUID PRIMARY KEY,
    email VARCHAR(255) UNIQUE,
    first_name VARCHAR(50) NOT NULL,
    last_name VARCHAR(50) NOT NULL,
    balance MONEY NOT NULL,
    currency VARCHAR(3) NOT NULL,
    status VARCHAR(50) NOT NULL,
    daily_debit_limit MONEY,
    daily_debit_accrued MONEY NOT NULL,
    transfer_recipients JSONB NOT NULL,
    maintenance_fee_qualifying_deposit_found BOOLEAN NOT NULL,
    maintenance_fee_daily_balance_threshold BOOLEAN NOT NULL,
    events JSONB NOT NULL,
    in_progress_transfers JSONB NOT NULL,
    in_progress_transfers_count INT NOT NULL,
    card_locked BOOLEAN NOT NULL,
    created_at TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP,
    last_debit_at TIMESTAMPTZ,
    last_billing_cycle_at TIMESTAMPTZ
);

CREATE INDEX accounts_in_progress_transfers_count_idx ON accounts(in_progress_transfers_count);
CREATE INDEX accounts_last_billing_cycle_at_idx ON accounts(last_billing_cycle_at);

CREATE TABLE users (
    first_name VARCHAR(50) NOT NULL,
    last_name VARCHAR(50) NOT NULL,
    email VARCHAR(255) UNIQUE,
    account_id UUID UNIQUE REFERENCES accounts (id) ON DELETE CASCADE,
    created_at TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP,
    id UUID PRIMARY KEY DEFAULT gen_random_uuid()
);

CREATE TABLE billingstatements (
    name VARCHAR(100) NOT NULL,
    account_id UUID NOT NULL REFERENCES accounts (id) ON DELETE CASCADE,
    last_persisted_event_sequence_number BIGINT NOT NULL,
    transactions JSONB NOT NULL,
    balance MONEY NOT NULL,
    month INT NOT NULL,
    year INT NOT NULL,
    account_snapshot BYTEA NOT NULL,
    created_at TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP,
    id UUID PRIMARY KEY DEFAULT gen_random_uuid()
);
