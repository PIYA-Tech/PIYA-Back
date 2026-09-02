# PIYA backend API

ASP.NET Core 9 API for PIYA healthcare coordination. PostgreSQL is the system
of record and Redis supports distributed cache, authentication challenges, and
multi-instance operation.

## Requirements

- .NET SDK 9
- PostgreSQL 16
- Redis 7

The root Docker Compose stack is the quickest way to provide the data services.
For direct execution, configure values from `.env.example` through environment
variables or user secrets. Do not commit a populated environment or appsettings
file.

## Build and run

```bash
dotnet restore PIYA_API.sln
dotnet build PIYA_API.sln
dotnet run --project PIYA_API
```

The development HTTP profile listens on `http://localhost:5254`.

Health endpoints:

- `GET /api/health/live` — process liveness
- `GET /api/health/ready` — readiness including configured dependencies
- `GET /api/health/version` — build information

## Tests

Run the standard suite:

```bash
dotnet test PIYA_API.Tests/PIYA_API.Tests.csproj \
  --configuration Release \
  --filter "Category!=LoadTest"
```

Load tests are intentionally separate because they need an explicitly prepared
target:

```bash
dotnet test PIYA_API.Tests/PIYA_API.Tests.csproj \
  --filter "Category=LoadTest"
```

Check dependency advisories:

```bash
dotnet list PIYA_API/PIYA_API.csproj package --vulnerable --include-transitive
dotnet list PIYA_API.Tests/PIYA_API.Tests.csproj package --vulnerable --include-transitive
```

## Database migrations

The Docker entrypoint runs a migration-only process before launching the HTTP
server. A direct Docker or Coolify deployment therefore applies pending
migrations before accepting traffic, independently of platform configuration
precedence. The readiness probe still verifies that no migration is pending,
so a failed migration cannot be published as a healthy release.

For a deployment that has a separate migration job, set
`PIYA_SKIP_CONTAINER_MIGRATION=true` on the API container and run the same image
once with `Database__MigrateOnly=true` before rolling out the API. Do not set
`Database__MigrateOnly=true` on the long-running API service because it exits
after migration by design.

The repository-local tool manifest pins the EF CLI version:

```bash
dotnet tool restore
dotnet tool run dotnet-ef migrations list \
  --project PIYA_API --startup-project PIYA_API
dotnet tool run dotnet-ef database update \
  --project PIYA_API --startup-project PIYA_API
```

Do not edit an already-deployed migration or bypass the EF history table. Add a
new migration, review both directions, and test it against a restored copy of
the target schema.

## Authentication model

- Browser refresh tokens are returned only as secure `HttpOnly` cookies.
- Explicit iOS, Android, and Desktop requests receive refresh tokens in the
  response body for platform-protected storage.
- Two-factor login uses a short-lived challenge; enrollment must be confirmed.
- Medical and pharmacy routes pair role policies with ownership, treatment,
  hospital, company, or active staff-assignment checks.
- Initial prescription dispensing requires a valid one-time patient QR grant.

See the workspace `docs/ARCHITECTURE.md` and `docs/SECURITY.md` for trust
boundaries and operating requirements.

## Storage and recovery

Local uploads and ASP.NET Data Protection keys must be mounted on durable
volumes. S3-compatible document storage is configured with `Storage__S3__*`
environment variables. Root-level backup scripts capture the database, local
uploads, and Data Protection keys into encrypted, checksummed artifacts.

For a Coolify deployment built directly from `BackEnd/Dockerfile`, add
persistent storage with container destination `/app/keys` (for example, a
named volume `piya_api_keys`) before enabling 2FA. Keep that volume across every
redeploy and rollback. `DataProtection__KeyPath=/app/keys` chooses the directory;
the environment variable does not make the directory persistent by itself.
If the key volume is lost, existing encrypted authenticator secrets cannot be
recovered: the user must sign in with a saved backup code or have 2FA disabled
through an audited operator recovery, then enroll a new authenticator secret.

Technical controls in this repository do not constitute regulatory
certification.
