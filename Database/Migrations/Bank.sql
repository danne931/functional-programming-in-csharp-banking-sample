begin;

SET timezone = 'America/Los_Angeles';

DROP TABLE IF EXISTS billingstatements;
DROP TABLE IF EXISTS users;
DROP TABLE IF EXISTS merchantalias;
DROP TABLE IF EXISTS merchant;
DROP TABLE IF EXISTS accounts;
DROP TABLE IF EXISTS transactionnote;
DROP TABLE IF EXISTS transactioncategory;
DROP TABLE IF EXISTS category;

CREATE TABLE accounts (
    id UUID PRIMARY KEY,
    email VARCHAR(255) UNIQUE,
    first_name VARCHAR(50) NOT NULL,
    last_name VARCHAR(50) NOT NULL,
    balance MONEY NOT NULL,
    currency VARCHAR(3) NOT NULL,
    status VARCHAR(50) NOT NULL,
    daily_debit_limit MONEY,
    -- TODO: Compute accrued amounts in view.
    --       Storing accrued amounts here results in stale values.
    daily_debit_accrued MONEY NOT NULL,
    daily_internal_transfer_accrued MONEY NOT NULL,
    daily_domestic_transfer_accrued MONEY NOT NULL,
    transfer_recipients JSONB NOT NULL,
    internal_transfer_senders JSONB NOT NULL,
    maintenance_fee_qualifying_deposit_found BOOLEAN NOT NULL,
    maintenance_fee_daily_balance_threshold BOOLEAN NOT NULL,
    events JSONB NOT NULL,
    in_progress_transfers JSONB NOT NULL,
    in_progress_transfers_count INT NOT NULL,
    card_locked BOOLEAN NOT NULL,
    created_at TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP,
    last_debit_at TIMESTAMPTZ,
    last_internal_transfer_at TIMESTAMPTZ,
    last_domestic_transfer_at TIMESTAMPTZ,
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

CREATE TABLE category (
    category_id SMALLSERIAL PRIMARY KEY,
    name VARCHAR(100) NOT NULL,
    created_at TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP
);

INSERT INTO category (name)
VALUES
    ('Advertising'),
    ('Airlines'),
    ('Alcohol and Bars'),
    ('Books and Newspaper'),
    ('Car Rental'),
    ('Charity'),
    ('Clothing'),
    ('Conferences'),
    ('Education'),
    ('Electronics'),
    ('Entertainment'),
    ('Facilities Expenses'),
    ('Fees'),
    ('Food Delivery'),
    ('Fuel and Gas'),
    ('Gambling'),
    ('Government Services'),
    ('Grocery'),
    ('Ground Transportation'),
    ('Insurance'),
    ('Internet and Telephone'),
    ('Legal'),
    ('Lodging'),
    ('Medical'),
    ('Memberships'),
    ('Office Supplies'),
    ('Parking'),
    ('Political'),
    ('Professional Services'),
    ('Restaurants'),
    ('Retail'),
    ('Rideshare and Taxis'),
    ('Shipping'),
    ('Software'),
    ('Taxes'),
    ('Travel'),
    ('Utilities'),
    ('Vehicle Expenses'),
    ('Other');

CREATE TABLE transactioncategory (
    id BIGSERIAL PRIMARY KEY,
    category_id SMALLSERIAL REFERENCES category (category_id),
    transaction_id UUID UNIQUE NOT NULL,
    created_at TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE transactionnote (
    id BIGSERIAL PRIMARY KEY,
    transaction_id UUID UNIQUE NOT NULL,
    note TEXT NOT NULL,
    created_at TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP
);

commit;
