# Hirely Desk

Hirely Desk is a CV management and recruitment platform. Candidates maintain reusable profiles and create position-specific CVs. Recruiters manage positions, access rules, and candidate CVs.

## Features

- Candidate, Recruiter, and Administrator roles
- Typed attribute library and reusable position templates
- Public and access-controlled positions
- Candidate profiles, projects, CV publishing, search, discussions, and likes
- PDF, XLSX, and CSV exports
- Backblaze B2 direct image uploads
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
ALLOWED_HOST=hirelydesk.example.com
POSTGRES_PASSWORD=replace-with-a-strong-database-password
ADMIN_PASSWORD=replace-with-a-strong-admin-password
DEMO_PASSWORD=replace-with-a-strong-demo-password
B2_REGION=us-west-004
B2_BUCKET_NAME=hirelydesk-images
B2_APPLICATION_KEY_ID=replace-with-a-bucket-scoped-key-id
B2_APPLICATION_KEY=replace-with-a-bucket-scoped-application-key
B2_KEY_PREFIX=users
B2_PRESIGNED_URL_LIFETIME_SECONDS=300
B2_DOWNLOAD_URL_LIFETIME_SECONDS=60
B2_MAX_UPLOAD_BYTES=5242880
```

The optional OAuth and Gmail variables are `GOOGLE_CLIENT_ID`, `GOOGLE_CLIENT_SECRET`, `FACEBOOK_APP_ID`, `FACEBOOK_APP_SECRET`, `GMAIL_ADDRESS`, `GMAIL_APP_PASSWORD`, `GMAIL_FROM_NAME`, and `GMAIL_REQUIRE_CONFIRMED_ACCOUNT`.

Start the deployment from the repository root:

```bash
docker compose -f compose.production.yml up -d --build
docker compose -f compose.production.yml ps
```

The web service is published on `127.0.0.1:5192`; place an HTTPS reverse proxy in front of it. PostgreSQL is not published on a host port. `ALLOWED_HOST` controls the allowed hostname. The deployment persists PostgreSQL data, application logs, and ASP.NET Data Protection keys in named Docker volumes.

Useful commands:

```bash
docker compose -f compose.production.yml logs -f web
docker compose -f compose.production.yml down
```

Use `down -v` only when intentionally deleting the production database, logs, and Data Protection keys.

## Configuration

Required configuration:

| Key | Purpose |
| --- | --- |
| `ConnectionStrings:Default` | PostgreSQL connection string |
| `Database:SkipMigrate` | Set to `true` to prevent automatic startup migrations; defaults to `false` |
| `Seed:AdminPassword` | Initial administrator password; minimum 8 characters |
| `Seed:DemoPassword` | Initial demo-user password; minimum 8 characters |

Optional integrations are enabled only when fully configured:

- Google or Facebook authentication
- Backblaze B2 private image storage: `B2:Region`, `B2:BucketName`, `B2:ApplicationKeyId`, `B2:ApplicationKey`, `B2:KeyPrefix`, `B2:PresignedUrlLifetimeSeconds`, `B2:DownloadUrlLifetimeSeconds`, and `B2:MaxUploadBytes`
- Gmail confirmation email: `Gmail:Address`, `Gmail:AppPassword`, `Gmail:FromName`, and `Gmail:RequireConfirmedAccount`

Use user secrets locally and an approved production secret-management solution in deployment. Seed passwords are not reset when the configuration changes.

B2 is optional for local development, but the production Compose file requires it. The bucket is private and its CORS rule must allow the exact application origin, the `PUT` method, and the `content-type` request header. `content-type` is not a CORS-safelisted value for `image/png`, so the browser always issues an `OPTIONS` preflight; a missing or incorrect rule makes the upload fail with an opaque browser network error. A suitable policy is:

```json
{
  "CORSRules": [
    {
      "AllowedOrigins": ["https://hirelydesk.jakariya.eu.org"],
      "AllowedMethods": ["PUT", "GET", "HEAD"],
      "AllowedHeaders": ["*"],
      "ExposeHeaders": ["ETag"],
      "MaxAgeSeconds": 3000
    }
  ]
}
```

Match `AllowedOrigins` to the real deployed origin for each environment. The bucket-scoped B2 application key needs both `writeFiles` for the browser PUT and `readFiles` for the server-side `HeadObject` verification on upload completion and export/download reads. The authenticated upload endpoints are rate limited to 5 requests per minute, and each upload attempt consumes two requests (presign + complete). `B2:Region` produces an HTTPS S3 endpoint in the form `https://s3.<region>.backblazeb2.com`.

Profile images support JPEG, PNG, and WebP files up to 5 MiB by default. Authenticated users request a short-lived upload URL, upload directly from the browser, and complete the upload so the server can verify its object key, content type, and size. The application stores only B2 object keys, serves images through authorization-protected endpoints, signs short-lived GET URLs after authorization, and reads export images through the server-side S3 client. Presigned URLs are bearer tokens and must not be persisted or logged. `B2:PresignedUrlLifetimeSeconds` and `B2:DownloadUrlLifetimeSeconds` must each be between 1 second and 7 days; `B2:MaxUploadBytes` must be between 1 byte and 25 MiB.

The `ImageObjectKeys` migration only renames the existing `image_url` column to `image_object_key`; it does not copy objects or transform legacy Cloudinary URLs. Before applying it in an environment with existing images, copy each legacy object into the private B2 bucket, verify the copy, and replace each stored legacy URL with its canonical object key in the expected user profile path. Apply the schema migration only after that backfill is complete.

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

Do not commit passwords, OAuth credentials, Gmail app passwords, B2 secrets, or `.env` files.
