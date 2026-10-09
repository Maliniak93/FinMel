-- Same isolation as Skarbiec.AppHost AddServiceDatabase: one role per database, PUBLIC's CONNECT revoked.
CREATE DATABASE "identity_db";
CREATE USER "identity_user" WITH PASSWORD 'skarbiec-local-identity';
REVOKE CONNECT ON DATABASE "identity_db" FROM PUBLIC;
GRANT CONNECT, TEMP ON DATABASE "identity_db" TO "identity_user";
ALTER DATABASE "identity_db" OWNER TO "identity_user";

CREATE DATABASE "portfolio_db";
CREATE USER "portfolio_user" WITH PASSWORD 'skarbiec-local-portfolio';
REVOKE CONNECT ON DATABASE "portfolio_db" FROM PUBLIC;
GRANT CONNECT, TEMP ON DATABASE "portfolio_db" TO "portfolio_user";
ALTER DATABASE "portfolio_db" OWNER TO "portfolio_user";

CREATE DATABASE "marketdata_db";
CREATE USER "marketdata_user" WITH PASSWORD 'skarbiec-local-marketdata';
REVOKE CONNECT ON DATABASE "marketdata_db" FROM PUBLIC;
GRANT CONNECT, TEMP ON DATABASE "marketdata_db" TO "marketdata_user";
ALTER DATABASE "marketdata_db" OWNER TO "marketdata_user";

CREATE DATABASE "reporting_db";
CREATE USER "reporting_user" WITH PASSWORD 'skarbiec-local-reporting';
REVOKE CONNECT ON DATABASE "reporting_db" FROM PUBLIC;
GRANT CONNECT, TEMP ON DATABASE "reporting_db" TO "reporting_user";
ALTER DATABASE "reporting_db" OWNER TO "reporting_user";
