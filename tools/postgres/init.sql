-- Pre-create tables so Debezium can create the filtered publication
-- before the order-service starts.
-- The order-service uses CREATE TABLE IF NOT EXISTS, so there's no conflict.

CREATE TABLE IF NOT EXISTS orders (
    id uuid PRIMARY KEY,
    description text NOT NULL,
    status text NOT NULL,
    created_at_utc timestamp with time zone NOT NULL,
    published_at_utc timestamp with time zone NULL
);

CREATE INDEX IF NOT EXISTS ix_orders_status
    ON orders (status);

CREATE TABLE IF NOT EXISTS outbox_messages (
    id uuid PRIMARY KEY,
    order_id uuid NOT NULL,
    payload text NOT NULL,
    aggregate_type text NOT NULL DEFAULT 'Order',
    event_type text NOT NULL DEFAULT 'OrderCreated',
    idempotency_key text NOT NULL,
    traceparent text NULL,
    tracestate text NULL,
    created_at_utc timestamp with time zone NOT NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS uix_outbox_messages_idempotency_key
    ON outbox_messages (idempotency_key);

CREATE INDEX IF NOT EXISTS ix_outbox_messages_created
    ON outbox_messages (created_at_utc DESC);
