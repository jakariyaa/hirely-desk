# Hirely Desk

Hirely Desk is a CV management and recruitment platform. Candidates maintain reusable profiles and create position-specific CVs. Recruiters manage positions, access rules, and candidate CVs.

## Features

- Candidate, Recruiter, and Administrator roles
- Typed attribute library and reusable position templates
- Public and access-controlled positions
- Candidate profiles, projects, CV publishing, search, discussions, and likes
- PDF, XLSX, and CSV exports
- Backblaze B2 image uploads
- Token-protected aggregated-results API with an Odoo 18 read-only viewer
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

The optional OAuth and Gmail variables are `GOOGLE_CLIENT_ID`, `GOOGLE_CLIENT_SECRET`, `FACEBOOK_APP_ID`, `FACEBOOK_APP_SECRET`, `GMAIL_ADDRESS`, `GMAIL_APP_PASSWORD`, `GMAIL_FROM_NAME`, and `GMAIL_REQUIRE_CONFIRMED_ACCOUNT`, plus `SALESFORCE_INSTANCE_URL`, `SALESFORCE_CLIENT_ID`, and `SALESFORCE_CLIENT_SECRET`.

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

## Odoo integration

The platform exposes a token-protected API with aggregated position results, and ships an Odoo 18 application that imports and displays them. The Odoo app is a read-only viewer of imported data.

### Endpoint and token

- `GET /api/v1/positions/summary` returns the aggregated summary of exactly one position.
- Authentication: `Authorization: Bearer <token>`.
- Tokens are generated **per position** on the position form in Hirely Desk. Several tokens can be active for one position; each can be revoked independently. Only the SHA-256 hash of a token is stored, so the token is shown exactly once at generation time.
- The response contains only aggregate statistics — average/min/max for numeric attributes, most popular values for text-like attributes, true/false counts for booleans, earliest/latest dates, and filled counts. Individual candidate data is never exposed.
- The endpoint is rate limited to 60 requests per minute per token (partitioned by the token hash, with a remote-IP fallback).

Example:

```bash
curl -H "Authorization: Bearer cvp_..." \
  http://localhost:5191/api/v1/positions/summary
```

### Generate a token

1. Sign in as a Recruiter or Administrator.
2. Open a position (`/positions/{id}`).
3. Choose **API token** in the page header.
4. Optionally name the token, then choose **Generate token** and copy it — it is shown only once.
5. The dialog also lists existing tokens with created/last-used dates and lets you revoke one.

### Run the Odoo instance

Start the local PostgreSQL and the application (listening on all interfaces so the container can reach it):

```bash
docker compose up -d db
dotnet run --project src/CvPlatform.Web --launch-profile http --urls http://0.0.0.0:5191
```

If the app binds only to loopback, the Odoo container cannot reach `host.docker.internal:5191`; `--urls http://0.0.0.0:5191` avoids that.

Start Odoo (PostgreSQL for Odoo, Odoo 18 LTS, and the custom module mounted from `odoo/addons`):

```bash
docker compose -f compose.odoo.yml up -d
docker compose -f compose.odoo.yml ps
```

Open <http://localhost:8069> and create the first database (any name). The master password is only needed to manage databases.

Log in with login `admin` and password `admin`. The `odoo` / `odoo` values in `compose.odoo.yml` are the PostgreSQL credentials, not the Odoo web login.

### Install the Odoo application

1. Open **Apps**, remove the default `Apps` filter, and search for **Hirely Desk Position Viewer**.
2. Choose **Activate**. From the terminal, the equivalent command routes through the container entrypoint so the database credentials are supplied automatically:

   ```bash
   docker compose -f compose.odoo.yml run --rm odoo \
     odoo -d <database> -i hirely_position_viewer --stop-after-init --no-http
   ```

3. The **Hirely Desk** app appears in the main menu.

The API base URL defaults to `http://host.docker.internal:5191`. Change it in **Settings → Technical → System Parameters → `hirely_position_viewer.api_url`** if the application runs elsewhere.

### Import results

1. Generate a token for a position in Hirely Desk (see above) and copy it.
2. In Odoo, open **Hirely Desk → Import from Hirely Desk**.
3. Leave the prefilled URL or enter your Hirely Desk base URL, paste the token, and choose **Import**.
4. Odoo opens the imported position. **Positions** lists every imported position, and each position's **Attributes** tab shows per-attribute aggregates; open an attribute for its detailed aggregate section and most popular values.

Re-importing with the same token updates the existing position in place (matched by the Hirely Desk position id), so the viewer always shows the freshest aggregates. The same import action is also available from the position list's **Action** menu.

### Read-only guarantees

- Odoo models for imported content reject create/update/delete from the UI with a clear message; only the import wizard writes.
- The viewer group has read-only access rights for imported models and full access only to the transient import wizard.
- Tokens stay in the wizard form only; they are never persisted in Odoo.

### Troubleshooting

- **Could not reach the API** — confirm the app is running with `--urls http://0.0.0.0:5191` and that `host.docker.internal` resolves inside the container (`docker compose -f compose.odoo.yml exec odoo getent hosts host.docker.internal`).
- **The API token was rejected** — the token is invalid or revoked; generate a new one on the position form.
- **Rate limit reached** — the endpoint allows 60 requests per minute per token; wait a minute and retry.

## Google Drive / Gmail support-ticket integration

Users can file a support ticket from any page (the Help icon in the header, or "Create support ticket" in the footer). The app writes a JSON file to a Google Drive folder; a Drive change notification pings a webhook in this app, which reads the file and sends a nicely formatted HTML email to the admins via the Gmail API.

### Google Cloud setup

1. Create a project in Google Cloud Console, enable the **Google Drive API** and the **Gmail API**.
2. OAuth consent screen: External, add your Gmail as a test user.
3. Credentials → Create OAuth client ID → type **Web application** → note the client ID and secret.
4. Create a Drive folder `Support Tickets` (and a `Processed` subfolder); copy both folder IDs from their URLs.

### One-time refresh token

Mint a refresh token once and store it as a secret. The quickest way is a tiny throwaway console app (or the OAuth playground at https://developers.google.com/oauthplayground with "Use your own OAuth credentials"):

- Scopes: `https://www.googleapis.com/auth/drive`, `https://www.googleapis.com/auth/gmail.send`
- Redirect URI `http://localhost`, grant offline access, copy the refresh token.

### Configure the app

```bash
dotnet user-secrets set "Google:ClientId" "..." --project src/CvPlatform.Web
dotnet user-secrets set "Google:ClientSecret" "..." --project src/CvPlatform.Web
dotnet user-secrets set "Google:RefreshToken" "..." --project src/CvPlatform.Web
dotnet user-secrets set "Google:DriveFolderId" "<Support Tickets folder id>" --project src/CvPlatform.Web
dotnet user-secrets set "Google:DriveProcessedFolderId" "<Processed folder id>" --project src/CvPlatform.Web
dotnet user-secrets set "Google:WebhookUrl" "https://<your-public-host>/api/v1/integrations/drive/webhook" --project src/CvPlatform.Web
dotnet user-secrets set "Google:WebhookToken" "<random shared secret>" --project src/CvPlatform.Web
dotnet user-secrets set "Support:AdminEmails" "admin@example.com" --project src/CvPlatform.Web
```

Partial configuration fails startup validation; with nothing set the feature stays disabled (the ticket dialog reports the upload could not be sent, Drive sweep does nothing, no watch is registered).

### Local demo (no public address)

Drive requires an HTTPS webhook, so expose the local app with a tunnel:

```bash
docker compose up -d db
ngrok http 5191          # copy the https URL
dotnet user-secrets set "Google:WebhookUrl" "https://<ngrok-host>/api/v1/integrations/drive/webhook" --project src/CvPlatform.Web
dotnet run --project src/CvPlatform.Web --launch-profile http
```

At startup the app registers a Drive `changes.watch` channel against that URL (it logs the channel id and expiry; renew happens on the next restart — channels expire, up to ~7 days).

### Demo walkthrough

1. Sign in, open any page, click the Help icon (or the footer link).
2. Enter a summary, pick a priority, submit. The dialog fills **Reported by** (user + role), **Position** (position title when invoked from a `/positions/{id}...` page, otherwise empty), **Link** (current page URL), **Priority**, **Summary**, and **Admins** from `Support:AdminEmails`.
3. A `ticket-*.json` appears in the Drive folder within a second.
4. Drive notifies `POST /api/v1/integrations/drive/webhook`; the app validates `X-Goog-Channel-Token`, then processes pending files.
5. Each admin receives a formatted HTML email (subject `[Support] [{Priority}] {Summary}`); the processed file moves to the `Processed` folder so nothing is emailed twice.

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
- Salesforce CRM sync: `Salesforce:InstanceUrl`, `Salesforce:ClientId`, `Salesforce:ClientSecret`, and optional `Salesforce:ApiVersion` (default `v61.0`)
- Google Drive ticket upload + Gmail notifications: `Google:ClientId`, `Google:ClientSecret`, `Google:RefreshToken`, `Google:DriveFolderId`, `Google:DriveProcessedFolderId`, `Google:WebhookUrl` (https, public), `Google:WebhookToken`, and `Support:AdminEmails`

Use user secrets locally and an approved production secret-management solution in deployment. Seed passwords are not reset when the configuration changes.

B2 is optional for local development, but the production Compose file requires it. The bucket is private and no CORS rule is needed: the server performs the upload `PUT` itself, so the browser never talks to B2. The bucket-scoped B2 application key needs both `writeFiles` for the upload `PUT` and `readFiles` for the server-side `HeadObject` verification on upload completion and for export/download reads. `B2:Region` produces an HTTPS S3 endpoint in the form `https://s3.<region>.backblazeb2.com`.

Profile images support JPEG, PNG, and WebP files up to 5 MiB by default. `InputFile` hands the file to the Blazor circuit, the server signs a short-lived upload URL with `IImageStorage`, streams the bytes to B2 through `IImageUploadClient` (with a progress bar), and verifies the object key, content type, and size before the value is saved. Files larger than `B2:MaxUploadBytes` are rejected with a message instead of being scaled down. The application stores only B2 object keys, serves images through authorization-protected endpoints, signs short-lived GET URLs after authorization, and reads export images through the server-side S3 client. Presigned URLs are bearer tokens and must not be persisted or logged. `B2:PresignedUrlLifetimeSeconds` and `B2:DownloadUrlLifetimeSeconds` must each be between 1 second and 7 days; `B2:MaxUploadBytes` must be between 1 byte and 25 MiB.

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
odoo/addons/hirely_position_viewer  Odoo 18 read-only viewer application
compose.yml                     Local PostgreSQL
compose.odoo.yml                Local Odoo 18 rollout for the viewer
compose.production.yml          Production Docker Compose deployment
Dockerfile                      Production image build
```

Do not commit passwords, OAuth credentials, Gmail app passwords, B2 secrets, or `.env` files.
