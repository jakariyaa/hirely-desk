# Hirely Desk

Hirely Desk is a CV management and recruitment platform. Candidates maintain reusable professional profiles and create CVs tailored to position requirements. Recruiters manage positions, define access rules, and review eligible candidate CVs.

## Features

- Role-based authentication with Candidate, Recruiter, and Administrator roles
- Reusable attribute library with typed values, categories, and dropdown options
- Position and position-template management
- Public and rule-restricted positions
- Candidate profiles, projects, and generated CVs
- CV publishing, full-text search, discussions, and recruiter likes
- PDF CV export and CSV/XLSX position exports
- Cloudinary image uploads
- English and Polish localization, persisted theme preferences, and Serilog logging
- Optimistic concurrency for versioned records

## Technology stack

| Area | Technology |
| --- | --- |
| Application | .NET 10, C# |
| Web UI | Blazor Web App with Interactive Server render mode |
| UI components | MudBlazor |
| Authentication | ASP.NET Core Identity, Google OAuth, Facebook OAuth |
| Database | PostgreSQL 18, Entity Framework Core 10, Npgsql |
| Markdown | Markdig with HTML sanitization |
| Files and exports | Cloudinary, QuestPDF, QRCoder, ClosedXML |
| Logging | Serilog |
| Testing | xUnit, AwesomeAssertions, bUnit, Testcontainers |

## Requirements

- .NET SDK 10.0 or later
- PostgreSQL 18, or Docker/Podman with Compose support
- .NET Entity Framework CLI tools for creating and applying migrations:

  ```bash
  dotnet tool install --global dotnet-ef --version 10.*
  ```

The supplied `compose.yml` is intended for local development. It exposes PostgreSQL on `localhost:5434` and uses development-only credentials.

## Quick start

1. Start the local database from the repository root:

   ```bash
   podman-compose up -d db
   ```

   Docker users can use `docker compose up -d db` instead.

2. Configure the required local secrets. The application requires a connection string and passwords for its seeded accounts:

   ```bash
   dotnet user-secrets set --project src/CvPlatform.Web \
     "ConnectionStrings:Default" \
     "Host=localhost;Port=5434;Database=cvplatform;Username=cvplatform;Password=cvplatform"

   dotnet user-secrets set --project src/CvPlatform.Web \
     "Seed:AdminPassword" "replace-with-a-strong-password"

   dotnet user-secrets set --project src/CvPlatform.Web \
     "Seed:DemoPassword" "replace-with-a-strong-password"
   ```

3. Run the web application:

   ```bash
   dotnet run --project src/CvPlatform.Web --launch-profile http
   ```

   Open [http://localhost:5191](http://localhost:5191). On startup, the application applies pending EF Core migrations and runs the idempotent seed process.

## Seeded development accounts

The seed process creates these accounts. Their passwords are the values configured by `Seed:AdminPassword` and `Seed:DemoPassword`.

| Account | Role | Email |
| --- | --- | --- |
| Administrator | Admin and Recruiter | `admin@cvplatform.local` |
| Demo candidate | Candidate | `candidate@cvplatform.local` |
| Demo recruiter | Recruiter | `recruiter@cvplatform.local` |

The seed process also creates built-in profile attributes, attribute categories, a public demo position, a restricted Warsaw-only position, and demo candidate data. Seeding is safe to run repeatedly.

## Configuration

Configuration can be supplied through environment variables, user secrets, or another ASP.NET Core configuration provider. Use user secrets for local credentials and API keys; do not commit them to the repository.

| Key | Required | Description |
| --- | --- | --- |
| `ConnectionStrings:Default` | Yes | PostgreSQL connection string |
| `Seed:AdminPassword` | Yes | Seeded administrator password; minimum 8 characters |
| `Seed:DemoPassword` | Yes | Seeded candidate and recruiter password; minimum 8 characters |
| `Database:SkipMigrate` | No | Set to `true` to prevent automatic startup migrations |
| `Authentication:Google:ClientId` and `ClientSecret` | No | Enables Google sign-in when both are set |
| `Authentication:Facebook:AppId` and `AppSecret` | No | Enables Facebook sign-in when both are set |
| `Cloudinary:CloudName` and `UploadPreset` | No | Enables unsigned image uploads |
| `Cloudinary:CloudName`, `ApiKey`, and `ApiSecret` | No | Enables signed Cloudinary uploads |
| `Cloudinary:Folder` | No | Upload folder; defaults to `cvplatform` |
| `Gmail:Address` and `AppPassword` | No | Enables confirmation email delivery |
| `Gmail:FromName` | No | Sender display name; defaults to `Hirely Desk` |
| `Gmail:RequireConfirmedAccount` | No | Requires email confirmation when Gmail is configured |

Optional integrations are disabled when their configuration is absent. The application uses a no-op email sender when Gmail is not configured, and image upload controls are unavailable when Cloudinary is not configured.

For example, to enable Google sign-in locally:

```bash
dotnet user-secrets set --project src/CvPlatform.Web \
  "Authentication:Google:ClientId" "your-client-id"
dotnet user-secrets set --project src/CvPlatform.Web \
  "Authentication:Google:ClientSecret" "your-client-secret"
```

## Development commands

Run commands from the repository root:

```bash
# Restore and build
dotnet restore CvPlatform.slnx
dotnet build CvPlatform.slnx

# Run the complete test suite
dotnet test CvPlatform.slnx

# Run tests for the test project only
dotnet test tests/CvPlatform.Tests/CvPlatform.Tests.csproj
```

The PostgreSQL integration tests use Testcontainers and require a working Docker- or Podman-compatible container runtime. Most other tests use in-memory or SQLite databases.

## Entity Framework migrations

Migrations are stored in `src/CvPlatform.Infrastructure/Migrations`. The web application applies pending migrations automatically unless `Database:SkipMigrate` is enabled.

Create a migration after changing the data model:

```bash
dotnet ef migrations add MigrationName \
  --project src/CvPlatform.Infrastructure \
  --startup-project src/CvPlatform.Web
```

Apply migrations manually when needed:

```bash
dotnet ef database update \
  --project src/CvPlatform.Infrastructure \
  --startup-project src/CvPlatform.Web
```

## Repository layout

```text
CvPlatform.slnx
├── src/
│   ├── CvPlatform.Core/             Entities, enums, domain logic, and interfaces
│   ├── CvPlatform.Application/      Use cases, DTOs, validators, and services
│   ├── CvPlatform.Infrastructure/   EF Core, PostgreSQL, migrations, and integrations
│   └── CvPlatform.Web/              Blazor UI, Identity, endpoints, and composition root
├── tests/
│   └── CvPlatform.Tests/            Unit, component, and integration tests
└── compose.yml                      Local PostgreSQL service
```

Application services use the Core-owned database interfaces and context factory. Infrastructure contains the concrete EF Core implementation, while Web is the composition root. The application does not expose a general-purpose REST API; its primary interface is the interactive Blazor web application.

## Security notes

- Never commit passwords, OAuth credentials, Gmail app passwords, or Cloudinary secrets.
- The credentials in `compose.yml` are for local development only.
- Configure HTTPS, trusted hosts, production database credentials, and an appropriate secret provider before deployment.
- Keep `Database:SkipMigrate` and migration execution policy explicit in production environments.

## License

No license has been declared for this repository.
