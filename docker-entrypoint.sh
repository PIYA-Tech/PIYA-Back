#!/bin/sh
set -eu

# A direct PIYA API container owns its schema by default. Run migrations in a
# short-lived process before starting the web host so readiness can never race
# an unapplied schema. Advanced deployments with a separate migration job may
# explicitly opt out.
if [ "${PIYA_SKIP_CONTAINER_MIGRATION:-false}" != "true" ]; then
    echo "PIYA container: applying pending database migrations"
    env \
        PIYA_GENERATE_OPENAPI=false \
        Database__AutoMigrate=true \
        Database__MigrateOnly=true \
        dotnet PIYA_API.dll
fi

echo "PIYA container: starting API"
exec env \
    PIYA_GENERATE_OPENAPI=false \
    Database__AutoMigrate=false \
    Database__MigrateOnly=false \
    dotnet PIYA_API.dll
