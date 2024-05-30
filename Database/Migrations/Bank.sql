begin;

DROP TABLE IF EXISTS ancillarytransactioninfo;
DROP TABLE IF EXISTS transaction;
DROP TABLE IF EXISTS billingstatement;
DROP TABLE IF EXISTS merchantalias;
DROP TABLE IF EXISTS account;
DROP TABLE IF EXISTS organization;
DROP TABLE IF EXISTS category;

DROP TYPE IF EXISTS money_flow;

CREATE TABLE organization (
   org_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
   name VARCHAR(100) UNIQUE NOT NULL,
   created_at TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE account (
   account_id UUID PRIMARY KEY,
   email VARCHAR(255) UNIQUE NOT NULL,
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
   org_id UUID NOT NULL REFERENCES organization,
   created_at TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP,
   last_debit_at TIMESTAMPTZ,
   last_internal_transfer_at TIMESTAMPTZ,
   last_domestic_transfer_at TIMESTAMPTZ,
   last_billing_cycle_at TIMESTAMPTZ
);

CREATE INDEX account_in_progress_transfers_count_idx ON account(in_progress_transfers_count);
CREATE INDEX account_last_billing_cycle_at_idx ON account(last_billing_cycle_at);

CREATE TABLE billingstatement (
   name VARCHAR(100) NOT NULL,
   account_id UUID NOT NULL REFERENCES account ON DELETE CASCADE,
   last_persisted_event_sequence_number BIGINT NOT NULL,
   transactions JSONB NOT NULL,
   balance MONEY NOT NULL,
   month INT NOT NULL,
   year INT NOT NULL,
   account_snapshot BYTEA NOT NULL,
   created_at TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP,
   id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
   org_id UUID NOT NULL REFERENCES organization
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

CREATE TYPE money_flow AS ENUM ('none', 'in', 'out');
CREATE TABLE transaction (
   name VARCHAR(50) NOT NULL,
   amount MONEY,
   money_flow money_flow,
   timestamp TIMESTAMPTZ NOT NULL,
   transaction_id UUID PRIMARY KEY,
   account_id UUID NOT NULL REFERENCES account ON DELETE CASCADE,
   correlation_id UUID NOT NULL,
   event JSONB NOT NULL,
   org_id UUID NOT NULL REFERENCES organization
);

CREATE TABLE ancillarytransactioninfo (
   note TEXT,
   category_id SMALLSERIAL REFERENCES category,
   transaction_id UUID PRIMARY KEY REFERENCES transaction ON DELETE CASCADE
);
ALTER TABLE ancillarytransactioninfo
ALTER COLUMN category_id DROP NOT NULL;

commit;
