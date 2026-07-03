-- Application schema and technical account bootstrap.
CREATE TABLE IF NOT EXISTS app_events (
    id BIGSERIAL PRIMARY KEY,
    event_type TEXT NOT NULL,
    payload JSONB NOT NULL DEFAULT '{}'::jsonb,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_app_events_created_at ON app_events (created_at DESC);

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'app_tech') THEN
        CREATE ROLE app_tech LOGIN PASSWORD 'ChangeMe_OnFirstRotation!';
    END IF;
END
$$;

GRANT CONNECT ON DATABASE appdb TO app_tech;
GRANT USAGE ON SCHEMA public TO app_tech;
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO app_tech;
GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO app_tech;
ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO app_tech;
ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT USAGE, SELECT ON SEQUENCES TO app_tech;
