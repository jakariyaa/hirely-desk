# CV Management Platform — Phase 0

Blazor Web App (Interactive Server) + EF Core 10 + PostgreSQL 18. Phase 0 delivers the
scaffold, data model, Identity + OAuth wiring, MudBlazor themed shell with i18n hooks,
Serilog, the initial migration, and an idempotent seed. No domain features yet.

## Prerequisites

- .NET SDK 10.0.111+
- podman 6.1.1+ + podman-compose 1.6.0+ (plain compose-spec, no `deploy:` sections)
- `dotnet-ef` 10.x (`dotnet tool install --global dotnet-ef --version 10.*`)

## Quickstart

```bash
podman-compose up -d
podman exec cvplatform-db pg_isready -U cvplatform -d cvplatform

cd src/CvPlatform.Web
dotnet user-secrets set "ConnectionStrings:Default" "Host=localhost;Port=5434;Database=cvplatform;Username=cvplatform;Password=cvplatform"
dotnet user-secrets set "Seed:AdminPassword" "pick-a-strong-password"
dotnet user-secrets set "Seed:DemoPassword" "pick-a-strong-password"

dotnet ef database update --project ../CvPlatform.Infrastructure --startup-project .
dotnet run --urls "http://localhost:5199"
```

Seed runs automatically at startup (idempotent): attribute categories, built-in `Me.*`
attributes, the IELTS `Band` dropdown (bands 0–9), a demo position, the Admin account,
and a demo candidate.

## User-secrets keys

| Key | Purpose |
|---|---|
| `ConnectionStrings:Default` | Npgsql connection string (host port **5434**) |
| `Seed:AdminPassword` | Password for `admin@cvplatform.local` (role `Admin`) |
| `Seed:DemoPassword` | Password for `candidate@cvplatform.local` |
| `Authentication:Google:ClientId` / `Authentication:Google:ClientSecret` | Google OAuth (optional) |
| `Authentication:Facebook:AppId` / `Authentication:Facebook:AppSecret` | Facebook OAuth (optional) |

Nothing secret lives in `appsettings.json`. OAuth handlers register only when their keys
are present; local password login always works. To enable Google locally:

```bash
dotnet user-secrets set "Authentication:Google:ClientId" "<id>"
dotnet user-secrets set "Authentication:Google:ClientSecret" "<secret>"
```

## Accounts (after seed)

- Admin: `admin@cvplatform.local` (role `Admin`, has a profile row)
- Demo candidate: `candidate@cvplatform.local` (has a profile row)

## Layout

```
CvPlatform.slnx
├─ src/CvPlatform.Core            // entities, enums, IAppDbContext / IAppDbContextFactory
├─ src/CvPlatform.Infrastructure  // AppDbContext, interceptor, configurations, migrations
├─ src/CvPlatform.Web            // composition root, Identity, MudBlazor shell, seed
└─ tests/CvPlatform.Tests        // xUnit + FluentAssertions
```

Core never references Infrastructure. Services (later phases) inject
`IAppDbContextFactory` only. The `VersionIncrementInterceptor` is registered on the
`AddDbContextFactory` options — the only registration that fires with factory-created
contexts. Theme/language are read from `Blazored.LocalStorage` only in
`OnAfterRenderAsync(firstRender)` (JS interop is unavailable during prerender).

## Port note

`compose.yml` maps `"5434:5432"` (host 5434 avoids clashes with a local Postgres; the
container still listens on 5432). The image reference is the fully-qualified
`docker.io/library/postgres:18-alpine` (same image; podman has no short-name default
registry here), and the data volume mounts at `/var/lib/postgresql` because the
postgres:18 image refuses a mount directly on `/var/lib/postgresql/data`.

## Deferred to later phases

Attribute engine, profiles, positions + access rules, CV workflow, search/discussion/
likes UI, PDF/QR/export, bUnit + Testcontainers suites.
