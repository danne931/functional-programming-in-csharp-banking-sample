begin;

CREATE EXTENSION IF NOT EXISTS pg_trgm;

DROP VIEW IF EXISTS daily_purchase_accrued;
DROP VIEW IF EXISTS monthly_purchase_accrued;
DROP VIEW IF EXISTS daily_purchase_accrued_by_card;
DROP VIEW IF EXISTS monthly_purchase_accrued_by_card;

DROP TABLE IF EXISTS balance_history;
DROP TABLE IF EXISTS billingstatement;
DROP TABLE IF EXISTS payment_platform;
DROP TABLE IF EXISTS payment_third_party;
DROP TABLE IF EXISTS payment;
DROP TABLE IF EXISTS transfer_internal;
DROP TABLE IF EXISTS transfer_domestic;
DROP TABLE IF EXISTS transfer;
DROP TABLE IF EXISTS transfer_domestic_recipient;
DROP TABLE IF EXISTS merchant;
DROP TABLE IF EXISTS ancillarytransactioninfo;
DROP TABLE IF EXISTS transaction;
DROP TABLE IF EXISTS card;
DROP TABLE IF EXISTS org_permissions;
DROP TABLE IF EXISTS account;
DROP TABLE IF EXISTS employee_event;
DROP TABLE IF EXISTS employee;
DROP TABLE IF EXISTS organization;
DROP TABLE IF EXISTS category;

DROP TYPE IF EXISTS money_flow CASCADE;
DROP TYPE IF EXISTS monthly_time_series_filter_by CASCADE;
DROP TYPE IF EXISTS time_frame CASCADE;
DROP TYPE IF EXISTS employee_status;
DROP TYPE IF EXISTS employee_role;
DROP TYPE IF EXISTS account_depository;
DROP TYPE IF EXISTS account_status;
DROP TYPE IF EXISTS auto_transfer_rule_frequency;
DROP TYPE IF EXISTS card_status;
DROP TYPE IF EXISTS card_type;
DROP TYPE IF EXISTS platform_payment_status;
DROP TYPE IF EXISTS third_party_payment_status;
DROP TYPE IF EXISTS payment_type;
DROP TYPE IF EXISTS payment_network;
DROP TYPE IF EXISTS domestic_transfer_recipient_account_depository;
DROP TYPE IF EXISTS domestic_transfer_recipient_status;
DROP TYPE IF EXISTS domestic_transfer_status;
DROP TYPE IF EXISTS internal_transfer_status;
DROP TYPE IF EXISTS transfer_category;

-- Drop Akka event sourcing tables.
-- These tables are initiated in Infrastructure/Akka.fs.
-- They are created automatically when starting up the app so no need
-- create those tables here.
DROP TABLE IF EXISTS tags;
DROP TABLE IF EXISTS akka_snapshots;
DROP TABLE IF EXISTS akka_event_journal;

CREATE TABLE organization (
   org_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
   org_name VARCHAR(100) UNIQUE NOT NULL,
   created_at TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP
);
CREATE INDEX org_search_idx ON organization USING gist (org_name gist_trgm_ops);

CREATE TYPE account_depository AS ENUM ('Checking', 'Savings');
CREATE TYPE account_status AS ENUM ('Pending', 'Active', 'Closed', 'ReadyForDelete');
CREATE TYPE auto_transfer_rule_frequency AS ENUM ('PerTransaction', 'Daily', 'TwiceMonthly');
CREATE TABLE account (
   account_id UUID PRIMARY KEY,
   routing_number INT NOT NULL,
   account_number BIGINT UNIQUE NOT NULL,
   account_name VARCHAR(50) NOT NULL,
   depository account_depository NOT NULL,
   balance MONEY NOT NULL,
   currency VARCHAR(3) NOT NULL,
   status account_status NOT NULL,
   auto_transfer_rule JSONB,
   auto_transfer_rule_frequency auto_transfer_rule_frequency,
   domestic_transfer_recipients JSONB NOT NULL,
   maintenance_fee_qualifying_deposit_found BOOLEAN NOT NULL,
   maintenance_fee_daily_balance_threshold BOOLEAN NOT NULL,
   in_progress_internal_transfers JSONB NOT NULL,
   in_progress_internal_transfers_count INT NOT NULL,
   in_progress_domestic_transfers JSONB NOT NULL,
   in_progress_domestic_transfers_count INT NOT NULL,
   failed_domestic_transfers JSONB NOT NULL,
   failed_domestic_transfers_count INT NOT NULL,
   org_id UUID NOT NULL REFERENCES organization,
   created_at TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP,
   last_billing_cycle_at TIMESTAMPTZ
);

CREATE INDEX account_in_progress_domestic_transfers_count_idx ON account(in_progress_domestic_transfers_count);
CREATE INDEX account_last_billing_cycle_at_idx ON account(last_billing_cycle_at);

CREATE TABLE org_permissions (
   requires_employee_invite_approval BOOLEAN NOT NULL,
   social_transfer_discovery_account_id UUID REFERENCES account,
   org_id UUID NOT NULL UNIQUE REFERENCES organization,
   id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
   created_at TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP
);

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
   name VARCHAR(100) UNIQUE NOT NULL,
   created_at TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE merchant (
   org_id UUID NOT NULL REFERENCES organization,
   name VARCHAR(100) NOT NULL,
   alias VARCHAR(100),
   created_at TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP,

   PRIMARY KEY (org_id, name)
);

CREATE TYPE employee_status AS ENUM (
  'PendingInviteConfirmation',
  'PendingInviteApproval',
  'PendingRestoreAccessApproval',
  'Active',
  'Closed',
  'ReadyForDelete'
);
CREATE TYPE employee_role AS ENUM ('Admin', 'Scholar', 'CardOnly');
CREATE TABLE employee (
   employee_id UUID PRIMARY KEY,
   email VARCHAR(255) UNIQUE NOT NULL,
   first_name VARCHAR(50) NOT NULL,
   last_name VARCHAR(50) NOT NULL,
   search_query TEXT,
   role employee_role NOT NULL,
   status employee_status NOT NULL,
   pending_purchases JSONB NOT NULL,
   onboarding_tasks JSONB NOT NULL,
   cards JSONB NOT NULL,
   invite_token UUID,
   invite_expiration TIMESTAMPTZ,
   auth_provider_user_id UUID,
   org_id UUID NOT NULL REFERENCES organization,
   created_at TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP
);
CREATE INDEX employee_email_idx ON employee(email);
CREATE INDEX employee_invite_token_idx ON employee(invite_token);
CREATE INDEX employee_search_query_idx ON employee USING gist (search_query gist_trgm_ops);

CREATE OR REPLACE FUNCTION update_search_query() RETURNS TRIGGER AS $$
BEGIN
  NEW.search_query = concat_ws(' ', NEW.first_name, NEW.last_name, NEW.email);
  RETURN NEW;
END;
$$ LANGUAGE plpgsql;

CREATE TRIGGER update_search_query_trigger
BEFORE INSERT OR UPDATE ON employee
FOR EACH ROW
EXECUTE FUNCTION update_search_query();

CREATE TABLE employee_event (
   name VARCHAR(50) NOT NULL,
   timestamp TIMESTAMPTZ NOT NULL,
   event_id UUID PRIMARY KEY,
   employee_id UUID NOT NULL REFERENCES employee,
   correlation_id UUID NOT NULL,
   initiated_by_id UUID NOT NULL REFERENCES employee(employee_id),
   event JSONB NOT NULL,
   org_id UUID NOT NULL REFERENCES organization
);

CREATE TYPE card_status AS ENUM ('Active', 'Frozen', 'Closed');
CREATE TYPE card_type AS ENUM ('Debit', 'Credit');
CREATE TABLE card (
   card_number_last_4 VARCHAR(4) NOT NULL,
   daily_purchase_limit MONEY NOT NULL,
   monthly_purchase_limit MONEY NOT NULL,
   virtual BOOLEAN NOT NULL,
   card_status card_status NOT NULL,
   card_type card_type NOT NULL,
   card_nickname VARCHAR(50),
   last_purchase_at TIMESTAMPTZ,
   exp_month INT NOT NULL,
   exp_year INT NOT NULL,
   card_id UUID PRIMARY KEY,
   employee_id UUID NOT NULL REFERENCES employee,
   account_id UUID NOT NULL REFERENCES account,
   org_id UUID NOT NULL REFERENCES organization,
   created_at TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP
);

CREATE TYPE money_flow AS ENUM ('In', 'Out');
CREATE TABLE transaction (
   name VARCHAR(50) NOT NULL,
   amount MONEY,
   money_flow money_flow,
   -- NOTE:
   -- Source may be a merchant name, transfer recipient name, etc. depending on the transaction type.
   -- This property is used only for analytics queries.
   source VARCHAR(100),
   timestamp TIMESTAMPTZ NOT NULL,
   transaction_id UUID PRIMARY KEY,
   account_id UUID NOT NULL REFERENCES account ON DELETE CASCADE,
   initiated_by_id UUID NOT NULL REFERENCES employee(employee_id),
   correlation_id UUID NOT NULL,
   card_id UUID REFERENCES card,
   event JSONB NOT NULL,
   org_id UUID NOT NULL REFERENCES organization
);

CREATE INDEX transaction_accrued_amount_view_query_idx
ON transaction(amount, name, timestamp);

CREATE TABLE ancillarytransactioninfo (
   note TEXT,
   category_id SMALLSERIAL REFERENCES category,
   transaction_id UUID PRIMARY KEY REFERENCES transaction ON DELETE CASCADE
);
ALTER TABLE ancillarytransactioninfo
ALTER COLUMN category_id DROP NOT NULL;

CREATE TABLE balance_history(
   account_id UUID NOT NULL REFERENCES account ON DELETE CASCADE,
   date DATE NOT NULL,
   balance NUMERIC NOT NULL,
   id SERIAL PRIMARY KEY,
   CONSTRAINT account_date UNIQUE (account_id, date)
);

CREATE TYPE transfer_category AS ENUM (
  'InternalWithinOrg',
  'InternalAutomatedWithinOrg',
  'InternalBetweenOrgs',
  'Domestic'
);
CREATE TABLE transfer(
   transfer_id UUID PRIMARY KEY,
   initiated_by_id UUID NOT NULL REFERENCES employee(employee_id),
   amount MONEY NOT NULL,
   transfer_category transfer_category NOT NULL,
   scheduled_at TIMESTAMPTZ NOT NULL,
   sender_org_id UUID NOT NULL REFERENCES organization(org_id),
   sender_account_id UUID NOT NULL REFERENCES account(account_id),
   memo TEXT,
   created_at TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP
);

CREATE TYPE internal_transfer_status AS ENUM (
   'Scheduled',
   'Pending',
   'Approved',
   'Deposited',
   'Failed'
);
CREATE TABLE transfer_internal(
   transfer_id UUID PRIMARY KEY REFERENCES transfer,
   transfer_status internal_transfer_status NOT NULL,
   transfer_status_detail JSONB NOT NULL,
   recipient_org_id UUID NOT NULL REFERENCES organization(org_id),
   recipient_account_id UUID REFERENCES account(account_id),
   created_at TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP
);

CREATE TYPE domestic_transfer_recipient_account_depository
AS ENUM ('Checking', 'Savings'); 

CREATE TYPE domestic_transfer_recipient_status
AS ENUM ('Confirmed', 'InvalidAccount', 'Closed');

CREATE TYPE payment_network AS ENUM ('ACH');

CREATE TABLE transfer_domestic_recipient(
   recipient_account_id UUID PRIMARY KEY,
   first_name VARCHAR(50) NOT NULL,
   last_name VARCHAR(50) NOT NULL,
   nickname VARCHAR(100),
   routing_number INT NOT NULL,
   account_number BIGINT UNIQUE NOT NULL,
   recipient_status domestic_transfer_recipient_status NOT NULL,
   depository domestic_transfer_recipient_account_depository NOT NULL,
   payment_network payment_network NOT NULL,
   created_at TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP
);

CREATE TYPE domestic_transfer_status AS ENUM (
   'Scheduled',
   'Outgoing',
   'InProgress',
   'Complete',
   'Failed'
);

CREATE TABLE transfer_domestic(
   transfer_id UUID PRIMARY KEY REFERENCES transfer,
   transfer_status domestic_transfer_status NOT NULL,
   transfer_status_detail JSONB NOT NULL,
   recipient_account_id UUID REFERENCES transfer_domestic_recipient,
   created_at TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP
);

CREATE TYPE payment_type AS ENUM ('Platform', 'ThirdParty');
CREATE TABLE payment(
   payment_id UUID PRIMARY KEY,
   initiated_by_id UUID NOT NULL REFERENCES employee(employee_id),
   amount MONEY NOT NULL,
   memo TEXT NOT NULL,
   payment_type payment_type NOT NULL,
   expiration TIMESTAMPTZ NOT NULL,
   payee_org_id UUID NOT NULL REFERENCES organization(org_id),
   payee_account_id UUID NOT NULL REFERENCES account(account_id),
   created_at TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP
);

CREATE TYPE platform_payment_status AS ENUM (
   'Unpaid',
   'Paid',
   'Deposited',
   'Cancelled',
   'Declined'
);
CREATE TABLE payment_platform(
   payment_id UUID PRIMARY KEY REFERENCES payment,
   status platform_payment_status NOT NULL,
   payer_org_id UUID NOT NULL REFERENCES organization(org_id),
   -- An organization who conducts business on the platform
   -- may choose to pay by one of their accounts.  
   -- Alternatively, they may choose to pay by ACH or card (TODO)
   pay_by_account UUID REFERENCES account(account_id),
   --pay_by_card ...
   --pay_by_ach ...
   created_at TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP
);

CREATE TYPE third_party_payment_status AS ENUM (
   'Unpaid',
   'Deposited',
   'Cancelled'
);

-- An organization who does not conduct business on the platform
-- will receive an email requesting a payment.
-- They will be redirected to a secure form to pay by ACH or card.
-- TODO: 
-- This table will be developed more after researching
-- Plaid and seeing what data will be necessary for paying by ACH/card.
CREATE TABLE payment_third_party(
   payment_id UUID PRIMARY KEY REFERENCES payment,
   status_tp third_party_payment_status NOT NULL,
   payer_email VARCHAR(255) NOT NULL,
   payer_name VARCHAR(100) NOT NULL,
   --pay_by_card ...
   --pay_by_ach ...
   created_at TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP
);

CREATE OR REPLACE PROCEDURE seed_balance_history()
AS $$
BEGIN
   INSERT INTO balance_history(account_id, date, balance)
   WITH RECURSIVE date_series AS (
      SELECT
         account_id,
         MIN(timestamp::date) AS start_date,
         (CURRENT_DATE - interval '1 day')::date AS end_date
      FROM transaction
      GROUP BY account_id

      UNION ALL

      SELECT
         account_id,
         (start_date + interval '1 day')::date,
         end_date
      FROM date_series
      WHERE start_date < end_date
   ),
   transactions_by_date AS (
      SELECT
         account_id,
         timestamp::date AS date,
         SUM(CASE WHEN money_flow = 'In' THEN amount::numeric ELSE -amount::numeric END) AS daily_diff
      FROM transaction
      GROUP BY account_id, timestamp::date
   )
   SELECT
      ds.account_id,
      ds.start_date AS date,

      COALESCE(
         SUM(tbd.daily_diff) OVER (
            PARTITION BY ds.account_id  -- calculate daily_diff sum for each account separately
            ORDER BY ds.start_date      -- calculate sum in chronological order
         ),
         0
      ) AS balance
   FROM date_series ds
   LEFT JOIN transactions_by_date tbd ON ds.account_id = tbd.account_id AND ds.start_date = tbd.date
   ORDER BY ds.account_id, ds.start_date
   ON CONFLICT(account_id, date) DO NOTHING;
END
$$ LANGUAGE plpgsql;

/*
NOTE: A balance history record is created daily for the previous day.
Shortly after midnight we need to compute the balance for the previous
day (CURRENT_DATE - 1).  We get the balance from balance history record
pertaining to (CURRENT_DATE - 2) summed up with the computed diff of
transaction amounts pertaining to (CURRENT_DATE - 1).
*/
CREATE OR REPLACE PROCEDURE update_balance_history_for_yesterday()
AS $$
DECLARE
   yesterday CONSTANT DATE := CURRENT_DATE - '1 day'::interval;
BEGIN
   INSERT INTO balance_history (account_id, date, balance)
   SELECT
      account.account_id,
      yesterday as date,

      COALESCE(bh.balance, 0)
      +
      COALESCE(
         SUM(
            CASE
            WHEN t.money_flow = 'In' THEN t.amount::numeric
            ELSE -t.amount::numeric
            END
         ),
         0
      ) AS balance
   FROM account
   LEFT JOIN transaction t
      ON t.account_id = account.account_id
      AND t.money_flow IS NOT NULL
      AND t.timestamp::date = yesterday
   JOIN balance_history bh
      ON bh.account_id = account.account_id
      -- Join on the balance_history date 1 day prior to yesterday.
      AND bh.date = yesterday - interval '1 day'
   GROUP BY account.account_id, bh.balance
   ON CONFLICT(account_id, date) DO NOTHING;
END
$$ LANGUAGE plpgsql;

CREATE OR REPLACE FUNCTION money_flow_time_series_daily(
   orgId UUID,
   startDate TIMESTAMPTZ,
   endDate TIMESTAMPTZ
)
RETURNS TABLE (
   day DATE,
   amount_in NUMERIC,
   amount_out NUMERIC,
   account_id UUID
) AS $$
BEGIN
   RETURN QUERY
   SELECT
      ds.day::date,

      COALESCE(
         SUM(t.amount::numeric) filter(where t.money_flow = 'In'),
         0
      ) AS amount_in,

      COALESCE(
         SUM(t.amount::numeric) filter(where t.money_flow = 'Out'),
         0
      ) AS amount_out,

      ids.account_id
   FROM generate_series(startDate, endDate, '1 day'::interval) AS ds(day)
   CROSS JOIN (
      SELECT account.account_id
      FROM account
      WHERE account.org_id = orgId
   ) ids
   LEFT JOIN transaction t
      ON t.account_id = ids.account_id
      AND t.timestamp::date = ds.day::date
      AND t.money_flow IS NOT NULL
      -- Exclude internal transfers within an org while
      -- still fetching internal transfers between orgs.
      AND t.name NOT IN(
         'InternalTransferWithinOrgPending',
         'InternalTransferWithinOrgRejected',
         'InternalTransferWithinOrgDeposited',
         'InternalAutomatedTransferPending',
         'InternalAutomatedTransferRejected',
         'InternalAutomatedTransferDeposited'
      )
   GROUP BY ds.day, ids.account_id
   ORDER BY ds.day;
END
$$ LANGUAGE plpgsql;

CREATE TYPE monthly_time_series_filter_by AS ENUM ('Org', 'Account');

CREATE OR REPLACE FUNCTION money_flow_time_series_monthly(
   filterBy monthly_time_series_filter_by,
   filterId UUID, -- Either org_id or account_id depending on filterBy parameter
   lookbackMonths INT
)
RETURNS TABLE (
   month DATE,
   amount_in NUMERIC,
   amount_out NUMERIC
) AS $$
BEGIN
   RETURN QUERY
   SELECT
      months.month::date,

      COALESCE(
         SUM(t.amount::numeric) filter(where t.money_flow = 'In'),
         0
      ) AS amount_in,

      COALESCE(
         SUM(t.amount::numeric) filter(where t.money_flow = 'Out'),
         0
      ) AS amount_out
   FROM generate_series(
      DATE_TRUNC('month', CURRENT_DATE) - (lookbackMonths - 1 || ' months')::interval,
      CURRENT_DATE,
      '1 month'
   ) AS months(month)
   LEFT JOIN LATERAL (
      SELECT t.money_flow, t.amount
      FROM transaction t
      WHERE
         CASE
            WHEN filterBy = 'Org' THEN t.org_id = filterId
            WHEN filterBy = 'Account' THEN t.account_id = filterId
         END
         AND t.money_flow IS NOT NULL
         AND DATE_TRUNC('month', t.timestamp) = months.month
         -- Exclude internal transfers within an org while
         -- still fetching internal transfers between orgs.
         AND t.name NOT IN(
            'InternalTransferWithinOrgPending',
            'InternalTransferWithinOrgRejected',
            'InternalTransferWithinOrgDeposited',
            'InternalAutomatedTransferPending',
            'InternalAutomatedTransferRejected',
            'InternalAutomatedTransferDeposited'
         )
   ) t ON true
   GROUP BY months.month
   ORDER BY months.month;
END
$$ LANGUAGE plpgsql;

CREATE OR REPLACE FUNCTION money_flow_top_n_source_by_month(
   orgId UUID,
   flow money_flow,
   topN INT,
   d TIMESTAMPTZ
)
RETURNS TABLE (
   money_flow money_flow,
   amount NUMERIC,
   source VARCHAR
) AS $$
BEGIN
   RETURN QUERY
   SELECT
      flow as money_flow,
      COALESCE(SUM(t.amount::numeric), 0) AS amount,
      t.source
   FROM transaction t
   WHERE
      t.org_id = orgId
      AND t.money_flow = flow
      AND t.timestamp::date
         BETWEEN DATE_TRUNC('month', d)
         AND (DATE_TRUNC('month', d) + INTERVAL '1 month' - INTERVAL '1 day')
      -- Exclude internal transfers within an org while
      -- still fetching internal transfers between orgs.
      AND t.name NOT IN(
         'InternalTransferWithinOrgPending',
         'InternalTransferWithinOrgRejected', 
         'InternalTransferWithinOrgDeposited',
         'InternalAutomatedTransferPending',
         'InternalAutomatedTransferRejected',
         'InternalAutomatedTransferDeposited'
      )
   GROUP BY t.source
   ORDER BY amount DESC
   LIMIT topN;
END
$$ LANGUAGE plpgsql;

CREATE OR REPLACE FUNCTION purchase_top_n_employees_by_month(
   orgId UUID,
   topN INT,
   d TIMESTAMPTZ
)
RETURNS TABLE (
   employee_id UUID,
   employee_name TEXT,
   amount NUMERIC
) AS $$
BEGIN
   RETURN QUERY
   SELECT
      e.employee_id,
      employee.first_name || ' ' || employee.last_name as employee_name,
      COALESCE(SUM(t.amount::numeric), 0) AS amount
   FROM employee_event e
   JOIN transaction t using(correlation_id)
   JOIN employee using(employee_id)
   WHERE
      e.org_id = orgId
      AND e.name = 'DebitApproved'
      AND e.timestamp::date
         BETWEEN DATE_TRUNC('month', d)
         AND (DATE_TRUNC('month', d) + INTERVAL '1 month' - INTERVAL '1 day')
   GROUP BY e.employee_id, employee_name
   ORDER BY amount DESC
   LIMIT topN;
END
$$ LANGUAGE plpgsql;

CREATE VIEW daily_purchase_accrued AS
SELECT
   account_id,
   COALESCE(SUM(amount::numeric), 0) as amount_accrued
FROM transaction
WHERE
   amount IS NOT NULL
   AND name = 'DebitedAccount'
   AND timestamp::date = CURRENT_DATE
GROUP BY account_id;

CREATE VIEW monthly_purchase_accrued AS
SELECT
   account_id,
   COALESCE(SUM(amount::numeric), 0) as amount_accrued
FROM transaction
WHERE
   amount IS NOT NULL
   AND name = 'DebitedAccount'
   AND timestamp::date >= date_trunc('month', CURRENT_DATE)
GROUP BY account_id;

CREATE VIEW daily_purchase_accrued_by_card AS
SELECT
   card_id,
   COALESCE(SUM(amount::numeric), 0) as amount_accrued
FROM transaction
WHERE
   amount IS NOT NULL
   AND name = 'DebitedAccount'
   AND timestamp::date = CURRENT_DATE
GROUP BY card_id;

CREATE VIEW monthly_purchase_accrued_by_card AS
SELECT
   card_id,
   COALESCE(SUM(amount::numeric), 0) as amount_accrued
FROM transaction
WHERE
   amount IS NOT NULL
   AND name = 'DebitedAccount'
   AND timestamp::date >= date_trunc('month', CURRENT_DATE)
GROUP BY card_id;

CREATE TYPE time_frame AS ENUM ('Day', 'Month');

CREATE OR REPLACE FUNCTION transfer_accrued(
   orgId UUID,
   timeFrame time_frame
)
RETURNS TABLE (
   account_id UUID,
   internal_transfer_accrued NUMERIC,
   domestic_transfer_accrued NUMERIC
) AS $$
BEGIN
  RETURN QUERY
  SELECT
     account.account_id,

     COALESCE(
        SUM(
           CASE
           WHEN t.name IN(
              'InternalTransferWithinOrgPending',
              'InternalTransferBetweenOrgsPending',
              'PlatformPaymentPaid',
              'InternalAutomatedTransferPending'
           )
           THEN t.amount::numeric

           WHEN t.name IN(
              'InternalTransferWithinOrgRejected',
              'InternalTransferBetweenOrgsRejected',
              'InternalAutomatedTransferRejected'
           )
           THEN -t.amount::numeric

           ELSE 0
           END
        ),
        0
     ) AS internal_transfer_accrued,

     COALESCE(
        SUM(
           CASE 
           WHEN t.name = 'DomesticTransferPending' THEN t.amount::numeric
           WHEN t.name = 'DomesticTransferRejected' THEN -t.amount::numeric
           ELSE 0
           END
        ),
        0
     ) AS domestic_transfer_accrued
  FROM transaction t
  JOIN transfer ON t.correlation_id = transfer.transfer_id
  JOIN account using(account_id)
  WHERE
     t.org_id = orgId
     AND t.amount IS NOT NULL
     AND t.name IN (
        'InternalAutomatedTransferPending', 'InternalAutomatedTransferRejected',
        'InternalTransferWithinOrgPending', 'InternalTransferWithinOrgRejected',
        'InternalTransferBetweenOrgsPending', 'InternalTransferBetweenOrgsRejected',
        'DomesticTransferPending', 'DomesticTransferRejected',
        'PlatformPaymentPaid'
     )
     AND (account.last_billing_cycle_at IS NULL OR t.timestamp > account.last_billing_cycle_at)
     AND
       CASE
       WHEN timeFrame = 'Day'
       THEN transfer.scheduled_at::date = CURRENT_DATE

       WHEN timeFrame = 'Month'
       THEN transfer.scheduled_at::date >= date_trunc('month', CURRENT_DATE)
       END
  GROUP BY account.account_id;
END
$$ LANGUAGE plpgsql;

/**
 * SEED DATA:
 * NOTE:
 * This seed data is necessary for the app to work in any environment
 * (local, production, staging, etc.).
 *
 * Seed data specific to local dev environment & demonstration purposes
 * is created via actor messages in Account.App/AccountSeederActor.fs
**/

/**
 * Create a "system" user to represent transactions which do not originate
 * from a human user.  Used in BillingCycleCommand, MaintenanceFeeCommand, etc.
**/
INSERT INTO organization (org_name) VALUES ('system');
INSERT INTO employee (
   employee_id,
   email,
   first_name,
   last_name,
   role,
   status,
   pending_purchases,
   onboarding_tasks,
   cards,
   org_id
)
VALUES (
    -- This employee_id is defined in Lib.SharedClientServer/Constants.fs as
    -- SYSTEM_USER_ID.  Account commands such as BillingCycle, which do not originate
    -- from a human, are created with initiated_by_id set to SYSTEM_USER_ID.
   '029528ee-a120-4301-b8b5-e9c60d859346',
   'system@gmail.com',
   'system',
   'system',
   'Admin',
   'Active',
   '{}'::jsonb,
   '{}'::jsonb,
   '{}'::jsonb,
   (SELECT org_id FROM organization WHERE org_name = 'system')
);

-- Allows users to filter transactions by these predefined categories.
-- NOTE: No implementation of custom category creation at this time.
INSERT INTO category (name)
VALUES
   ('Airlines'),
   ('Alcohol and Bars'),
   ('Books'),
   ('Car Rental'),
   ('Charity'),
   ('Clothing'),
   ('Conferences'),
   ('Education'),
   ('Electronics'),
   ('Entertainment'),
   ('Food Delivery'),
   ('Grocery'),
   ('Internet and Telephone'),
   ('Legal'),
   ('Lodging'),
   ('Medical'),
   ('Memberships'),
   ('Office Supplies'),
   ('Parking'),
   ('Restaurants'),
   ('Retail'),
   ('Rideshare and Taxis'),
   ('Shipping'),
   ('Software'),
   ('Utilities'),
   ('Vehicle Expenses'),
   ('Other');

commit;
