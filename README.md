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

Technical controls in this repository do not constitute regulatory
certification.
