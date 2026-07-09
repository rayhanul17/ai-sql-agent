-- ============================================================
--  Read-only login used by the agent at query time.
--  Even if the app-level SQL guard were bypassed, this role
--  cannot write — the database itself refuses INSERT/UPDATE/DELETE/DDL.
--  The app connects as this user for the default demo database.
-- ============================================================

CREATE ROLE agent_readonly WITH LOGIN PASSWORD 'readonly_pass';

-- Allow connecting and reading the public schema, nothing more.
GRANT CONNECT ON DATABASE agentdb TO agent_readonly;
GRANT USAGE ON SCHEMA public TO agent_readonly;
GRANT SELECT ON ALL TABLES IN SCHEMA public TO agent_readonly;

-- Apply SELECT automatically to any tables created later in this schema.
ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT SELECT ON TABLES TO agent_readonly;
