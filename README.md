# Hirely Desk

Hirely Desk is a CV management and recruitment platform. Candidates maintain reusable profiles and create position-specific CVs. Recruiters manage positions, access rules, and candidate CVs.

## Features

- Candidate, Recruiter, and Administrator roles
- Typed attribute library and reusable position templates
- Public and access-controlled positions
- Candidate profiles, projects, CV publishing, search, discussions, and likes
- PDF, XLSX, and CSV exports
- Cloudinary image uploads
- English and Polish localization

## Technology

.NET 10 · Blazor Web App with Interactive Server · MudBlazor · ASP.NET Core Identity · PostgreSQL 18 · Entity Framework Core 10 · Npgsql · Serilog · Markdig · FluentValidation · QuestPDF · QRCoder · ClosedXML

## Requirements

- .NET SDK 10.0+
- Docker Engine with Compose, or Podman with Compose
- Docker-API-compatible runtime for PostgreSQL integration tests
- `dotnet-ef` for migrations:

  ```bash
  dotnet tool install --global dotnet-ef --version 10.*
  ```

## Local development

Start PostgreSQL from the repository root:

```bash
docker compose up -d db
```

Podman users may run `podman-compose up -d db`. The database is available at `localhost:5434` with the development credentials defined in `compose.yml`.

Configure the required user secrets:

```bash
dotnet user-secrets set --project src/CvPlatform.Web \
  "ConnectionStrings:Default" \
  "Host=localhost;Port=5434;Database=cvplatform;Username=cvplatform;Password=cvplatform"
dotnet user-secrets set --project src/CvPlatform.Web \
  "Seed:AdminPassword" "replace-with-a-strong-password"
dotnet user-secrets set --project src/CvPlatform.Web \
  "Seed:DemoPassword" "replace-with-a-strong-password"
```

Run the application:

```bash
dotnet run --project src/CvPlatform.Web --launch-profile http
```

Open <http://localhost:5191>. Pending migrations and seed data are applied at startup.

## Production deployment

`compose.production.yml` builds the application with `Dockerfile`, runs PostgreSQL privately, applies migrations, and persists database and application logs.

Create `.env` beside `compose.production.yml` and do not commit it:

```dotenv
POSTGRES_PASSWORD=replace-with-a-strong-database-password
ADMIN_PASSWORD=replace-with-a-strong-admin-password
DEMO_PASSWORD=replace-with-a-strong-demo-password
```

Start the deployment from the repository root:

```bash
docker compose -f compose.production.yml up -d --build
docker compose -f compose.production.yml ps
```

The web service is published on `127.0.0.1:5000`; place an HTTPS reverse proxy in front of it. PostgreSQL is not published on a host port. The default allowed hostname is `hirelydesk.jakariya.eu.org`; update `AllowedHosts` in `compose.production.yml` when required.

Useful commands:

```bash
docker compose -f compose.production.yml logs -f web
docker compose -f compose.production.yml down
```

Use `down -v` only when intentionally deleting the production database and logs.

## Configuration

Required configuration:

| Key | Purpose |
| --- | --- |
| `ConnectionStrings:Default` | PostgreSQL connection string |
| `Seed:AdminPassword` | Initial administrator password; minimum 8 characters |
| `Seed:DemoPassword` | Initial demo-user password; minimum 8 characters |

Optional integrations are enabled only when fully configured:

- Google or Facebook authentication
- Signed Cloudinary uploads: `Cloudinary:CloudName`, `ApiKey`, and `ApiSecret`
- Gmail confirmation email: `Gmail:Address` and `AppPassword`

Use user secrets locally and an approved production secret-management solution in deployment. Seed passwords are not reset when the configuration changes.

## Testing and migrations

```bash
dotnet restore CvPlatform.slnx
dotnet build CvPlatform.slnx
dotnet test CvPlatform.slnx
```

PostgreSQL integration tests require a Docker-API-compatible container runtime. Other tests use in-memory or SQLite databases.

Create or apply migrations with `CvPlatform.Web` as the startup project:

```bash
dotnet ef migrations add MigrationName \
  --project src/CvPlatform.Infrastructure \
  --startup-project src/CvPlatform.Web

dotnet ef database update \
  --project src/CvPlatform.Infrastructure \
  --startup-project src/CvPlatform.Web
```

## Repository layout

```text
src/CvPlatform.Core             Domain entities and interfaces
src/CvPlatform.Application      Use cases, DTOs, validators, and services
src/CvPlatform.Infrastructure   EF Core, migrations, and integrations
src/CvPlatform.Web              Blazor UI, Identity, and composition root
tests/CvPlatform.Tests          Unit, component, and integration tests
compose.yml                     Local PostgreSQL
compose.production.yml          Production Docker Compose deployment
Dockerfile                      Production image build
```

Do not commit passwords, OAuth credentials, Gmail app passwords, Cloudinary secrets, or `.env` files.
