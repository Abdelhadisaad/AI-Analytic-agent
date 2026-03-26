CREATE TABLE IF NOT EXISTS customers (
  id SERIAL PRIMARY KEY,
  name VARCHAR(100) NOT NULL,
  email VARCHAR(255) UNIQUE NOT NULL,
  country VARCHAR(100) NOT NULL,
  created_at TIMESTAMP NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS orders (
  id SERIAL PRIMARY KEY,
  customer_id INTEGER,
  customer_name VARCHAR(100) NOT NULL,
  status VARCHAR(50) NOT NULL,
  total_amount NUMERIC(10,2) NOT NULL,
  created_at TIMESTAMP NOT NULL DEFAULT NOW()
);

TRUNCATE TABLE orders, customers RESTART IDENTITY CASCADE;

INSERT INTO customers (name, email, country) VALUES
('Finance One', 'f1@example.com', 'NL'),
('Finance Two', 'f2@example.com', 'NL');

INSERT INTO orders (customer_id, customer_name, status, total_amount, created_at) VALUES
(1, 'Finance One', 'completed', 999.00, NOW() - INTERVAL '7 days'),
(2, 'Finance Two', 'pending', 450.00, NOW() - INTERVAL '2 days');
