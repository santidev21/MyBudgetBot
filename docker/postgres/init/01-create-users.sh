#!/bin/bash
#
# Creates the two least-privilege PostgreSQL roles used by MyBudget-bot.
#
#   mybudget_migrator : owns the public schema and may run DDL. Used only by the
#                       one-shot migrator container.
#   mybudget_app      : runtime role, DML only, no DDL.
#
# The application never connects as the instance superuser.
#
# This script runs once, when the data directory is first initialised. Changing
# DB_APP_PASSWORD or DB_MIGRATOR_PASSWORD later has no effect on an existing
# volume: rotate with ALTER ROLE instead (see docs/DEPLOYMENT.md).
#
set -euo pipefail

psql \
    --set=ON_ERROR_STOP=1 \
    --username "$POSTGRES_USER" \
    --dbname "$POSTGRES_DB" \
    --variable=migrator_user="$DB_MIGRATOR_USER" \
    --variable=migrator_password="$DB_MIGRATOR_PASSWORD" \
    --variable=app_user="$DB_APP_USER" \
    --variable=app_password="$DB_APP_PASSWORD" <<-'SQL'
	CREATE ROLE :"migrator_user" LOGIN PASSWORD :'migrator_password';
	CREATE ROLE :"app_user"      LOGIN PASSWORD :'app_password';

	GRANT CONNECT ON DATABASE :"DBNAME" TO :"migrator_user", :"app_user";

	-- The migrator owns the schema so it can create and alter objects.
	ALTER SCHEMA public OWNER TO :"migrator_user";

	-- The runtime role may use the schema but not create anything in it.
	GRANT USAGE ON SCHEMA public TO :"app_user";
	REVOKE CREATE ON SCHEMA public FROM PUBLIC;

	-- Tables created later by the migrator automatically grant DML to the app role.
	ALTER DEFAULT PRIVILEGES FOR ROLE :"migrator_user" IN SCHEMA public
	    GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO :"app_user";

	ALTER DEFAULT PRIVILEGES FOR ROLE :"migrator_user" IN SCHEMA public
	    GRANT USAGE, SELECT ON SEQUENCES TO :"app_user";
SQL

echo "MyBudget-bot: roles '${DB_MIGRATOR_USER}' and '${DB_APP_USER}' provisioned."
