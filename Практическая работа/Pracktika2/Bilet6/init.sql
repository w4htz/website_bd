CREATE TABLE shipments (
    id SERIAL PRIMARY KEY,
    code VARCHAR(50) NOT NULL,
    status VARCHAR(20) NOT NULL DEFAULT 'new'
);
INSERT INTO shipments (code, status) VALUES ('A1','new'), ('B2','new');