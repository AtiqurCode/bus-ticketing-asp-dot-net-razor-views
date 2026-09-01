# TicketBari — Bus Ticketing Platform

A mobile-first bus ticket booking platform for Bangladesh. Customers book without an
account (name + phone only); counter staff and administrators run the back office.

Built as a single **ASP.NET Core Blazor Server** app on **.NET 10**, backed by
**PostgreSQL**. See [`bus-ticketing-project-plan.md`](bus-ticketing-project-plan.md)
for the full feature scope and [`docs/`](docs) for build notes.

## Stack

| | |
|---|---|
| Web | ASP.NET Core Blazor Server (interactive server components where needed, static SSR elsewhere) |
| Data | EF Core 10 + Npgsql, snake_case schema |
| Auth | ASP.NET Core Identity, cookie auth — staff/admin only |
| i18n | English + বাংলা, `.resx` resources, cookie-based culture |
| PDF / QR / Excel | QuestPDF · QRCoder · ClosedXML |

## Prerequisites

- .NET 10 SDK
- PostgreSQL 14+ running on `127.0.0.1:5432`

The default connection string (`appsettings.json`) expects user `postgres` with a
blank password. Override locally with user secrets or `appsettings.Local.json`
(git-ignored):

```json
{ "ConnectionStrings": { "Postgres": "Host=...;Database=busticketing;Username=...;Password=..." } }
```

## Running

```bash
dotnet run
```

On first start the app **creates the `busticketing` database as UTF-8**, applies
migrations, and seeds:

- the 64 Bangladesh districts plus major bus terminals (`Data/Seed/bangladesh-locations.json`)
- a super-admin account — username **`admin`**, password from `Seed:SuperAdminPassword`
  in `appsettings.json` (**change it after first login**)
- default platform settings and a standard cancellation policy

Then browse to the shown URL. Staff sign in at `/staff/login`.

Health probes: `/healthz` (liveness), `/healthz/ready` (DB reachable).

## Database migrations

```bash
dotnet ef migrations add <Name> --output-dir Data/Migrations
dotnet ef database update
```

`dotnet-ef` is pinned as a local tool (`dotnet tool restore` first).

## Project layout

```
Domain/            Entities, enums, value objects — no framework dependencies
Data/              AppDbContext, EF configurations, migrations, seeders
Services/          Application services (settings, audit, clock, auth, localization)
Components/
  Layout/          Public, auth, and admin shells
  Pages/           Public-facing pages
  Admin/           Back-office pages (authorized, AdminLayout)
  Account/         Sign in / out
Resources/         SharedResource.resx (+ .bn)
wwwroot/css/       Design system (app.css) — component styles are scoped .razor.css
```
