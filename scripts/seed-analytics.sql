CREATE TABLE IF NOT EXISTS customers (
  id SERIAL PRIMARY KEY,
  name VARCHAR(100) NOT NULL,
  email VARCHAR(255) UNIQUE NOT NULL,
  country VARCHAR(100) NOT NULL,
  created_at TIMESTAMP NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS products (
  id SERIAL PRIMARY KEY,
  name VARCHAR(150) NOT NULL,
  price NUMERIC(10,2) NOT NULL,
  stock INTEGER NOT NULL,
  created_at TIMESTAMP NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS orders (
  id SERIAL PRIMARY KEY,
  customer_id INTEGER REFERENCES customers(id),
  customer_name VARCHAR(100) NOT NULL,
  status VARCHAR(50) NOT NULL,
  total_amount NUMERIC(10,2) NOT NULL,
  created_at TIMESTAMP NOT NULL DEFAULT NOW()
);

TRUNCATE TABLE orders, customers, products RESTART IDENTITY CASCADE;

INSERT INTO customers (name, email, country) VALUES
('Alice', 'alice@example.com', 'NL'),
('Bob', 'bob@example.com', 'NL'),
('Charlie', 'charlie@example.com', 'BE'),
('Dina', 'dina@example.com', 'DE');

INSERT INTO products (name, price, stock) VALUES
('Laptop Pro 14', 1499.00, 12),
('Wireless Mouse', 39.99, 120),
('Mechanical Keyboard', 129.50, 45),
('USB-C Dock', 89.00, 60);

INSERT INTO orders (customer_id, customer_name, status, total_amount, created_at) VALUES
(1, 'Alice', 'completed', 150.00, NOW() - INTERVAL '10 days'),
(1, 'Alice', 'pending', 75.50, NOW() - INTERVAL '2 days'),
(2, 'Bob', 'completed', 200.00, NOW() - INTERVAL '5 days'),
(2, 'Bob', 'cancelled', 50.00, NOW() - INTERVAL '1 day'),
(3, 'Charlie', 'pending', 300.00, NOW() - INTERVAL '3 days'),
(4, 'Dina', 'completed', 420.00, NOW() - INTERVAL '4 days');
