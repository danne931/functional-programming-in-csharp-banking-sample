SET timezone = 'America/Los_Angeles';

DROP TABLE IF EXISTS billingstatements;
DROP TABLE IF EXISTS users;

CREATE TABLE users (
    first_name VARCHAR(50) NOT NULL,
    last_name VARCHAR(50) NOT NULL,
    email VARCHAR(255) UNIQUE,
    account_id UUID UNIQUE,
    created_at TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP,
    id UUID PRIMARY KEY DEFAULT gen_random_uuid()
);

CREATE TABLE billingstatements (
    name VARCHAR(100) NOT NULL,
    account_id UUID NOT NULL REFERENCES users (account_id) ON DELETE CASCADE,
    transactions JSONB NOT NULL,
    balance MONEY NOT NULL,
    month INT NOT NULL,
    year INT NOT NULL,
    created_at TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP,
    id UUID PRIMARY KEY DEFAULT gen_random_uuid()
);
