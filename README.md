# ScanGo.Api

.NET 10 + ASP.NET Core + EF Core + PostgreSQL. Single repo chứa DbContext + migrations + API + services (gọn cho mini project).

## Stack

| Layer | Tech |
|---|---|
| Runtime | .NET 10 |
| Web framework | ASP.NET Core (Minimal API + Controllers) |
| ORM | EF Core 10 + Npgsql provider |
| DB | PostgreSQL 16 |
| Naming | `UseSnakeCaseNamingConvention` (snake_case columns/tables) |
| Testing | xUnit + FluentAssertions 6 + NSubstitute + Testcontainers + WebApplicationFactory |

Database schema: xem [`../DB_SCHEMA.md`](../DB_SCHEMA.md).

## Repo layout

```
scango-api/
├── ScanGo.Api.slnx                    # solution
├── ScanGo.Api/                        # web API project
│   ├── Database/
│   │   ├── Entities/                  # 12 entity classes
│   │   ├── Migrations/                # EF Core generated
│   │   ├── ScanGoDbContext.cs
│   │   └── ScanGoDbContextFactory.cs  # design-time, dùng cho `dotnet ef`
│   ├── Features/                      # TBD next PR (auth, conversations, ...)
│   ├── Program.cs
│   ├── appsettings.json
│   └── appsettings.Development.json.example
├── ScanGo.Api.Tests/                  # xUnit
│   └── Database/
│       ├── PostgresFixture.cs         # Testcontainers Postgres + migrate
│       └── SchemaTests.cs             # schema smoke tests
├── Dockerfile                         # multi-stage build
└── README.md
```

## Local dev

### 1. Postgres

```bash
docker run -d --name scango-pg \
  -e POSTGRES_DB=scango \
  -e POSTGRES_USER=scango \
  -e POSTGRES_PASSWORD=scango \
  -p 5433:5432 \
  postgres:16-alpine
```

### 2. Connection string

Copy `appsettings.Development.json.example` → `appsettings.Development.json`. Hoặc set env:
```bash
export DATABASE_URL="Host=localhost;Port=5433;Database=scango;Username=scango;Password=scango"
```

### 3. Run

```bash
cd ScanGo.Api
dotnet run
```

API listens on `http://localhost:5xxx`. Migrations chạy tự động on startup (env `DB_AUTO_MIGRATE=false` để tắt).

### 4. Tests

```bash
# Docker Desktop phải đang chạy (Testcontainers spin Postgres thật)
dotnet test
```

## Database migrations

### Generate migration sau khi đổi entity

```bash
cd ScanGo.Api
dotnet ef migrations add <DescriptiveName> --output-dir Database/Migrations
```

### Apply manual (dev)

```bash
dotnet ef database update
```

### Revert migration

```bash
dotnet ef migrations remove   # nếu chưa apply
dotnet ef database update <PreviousMigrationName>   # rollback nếu đã apply
```

## Production deploy

1. Build Docker image: `docker build -t scango-api .`
2. Run với env:
   ```
   DATABASE_URL=Host=<neon-host>;Database=...;Username=...;Password=...;SSL Mode=Require
   ASPNETCORE_ENVIRONMENT=Production
   ASPNETCORE_URLS=http://+:8080
   DB_AUTO_MIGRATE=true     # for single-instance Fly.io / Render setup
   ```
3. Push lên Fly.io: `fly deploy` (sau khi `fly launch` cấu hình app).

## Environment variables

| Var | Required | Default | Notes |
|---|---|---|---|
| `DATABASE_URL` hoặc `ConnectionStrings__Postgres` | yes | — | Npgsql connection string |
| `DB_AUTO_MIGRATE` | no | `true` | Set `false` cho multi-instance |
| `ASPNETCORE_ENVIRONMENT` | no | `Production` | `Development` / `Production` |
| `ASPNETCORE_URLS` | no | `http://+:8080` | |

## Schema status

12/12 tables defined (initial migration `20260523065753_InitialSchema`):
- `users`, `refresh_tokens`, `email_verifications`, `password_resets`
- `conversations`, `messages`
- `usage_events`, `usage_summaries`, `credit_ledger`
- `payment_orders`, `audit_log`, `deletion_requests`

Tất cả constraints (CHECK, UNIQUE, FK), indexes, defaults theo `DB_SCHEMA.md` §0–§12.

## Next PRs

- PR2 — Auth (register / login / refresh / google / verify / forgot / reset / me)
- PR3 — Conversation feature (port từ Node BE: scan/ask/stream SSE)
- PR4 — Credit metering (quota check + ledger writes)
- PR5 — Payment QR + admin verify
- PR6 — Admin dashboard endpoints
