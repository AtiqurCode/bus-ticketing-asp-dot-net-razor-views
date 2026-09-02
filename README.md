# TicketBari — Bus Ticketing Platform

A mobile-first bus ticket booking platform for Bangladesh. Customers book without
an account (name + phone only); counter staff and administrators run the back
office. Full **English + বাংলা** throughout.

Built as a single **ASP.NET Core Blazor Server** app on **.NET 10**, backed by
**PostgreSQL**. The feature scope lives in
[`bus-ticketing-project-plan.md`](bus-ticketing-project-plan.md); the running
notes on how it was built are in [`docs/build-log.md`](docs/build-log.md).

---

## Contents

- [Stack](#stack)
- [Run with Docker](#run-with-docker) — one command, nothing to install
- [Run locally](#run-locally) — for development
- [Sample data for testing](#sample-data-for-testing)
- [Configuration](#configuration)
- [Project structure](#project-structure)
- [How the project is planned](#how-the-project-is-planned)
- [Architecture notes](#architecture-notes)
- [Database & migrations](#database--migrations)
- [Deploying to a VPS](#deploying-to-a-vps)

---

## Stack

| Layer | Choice |
|---|---|
| Web (UI + API in one) | ASP.NET Core **Blazor Server** — interactive server components where a screen needs live state, static SSR everywhere else |
| Data | **PostgreSQL** + EF Core 10 + Npgsql, snake_case schema |
| Auth | ASP.NET Core **Identity**, cookie auth — staff/admin only; customers are just a phone number |
| Realtime seat lock | Blazor circuit + an in-process broadcast service; Postgres `xmin` row version is the real guard |
| Background jobs | `BackgroundService` + `PeriodicTimer` (trip generation, hold/booking sweeps) |
| i18n | `.resx` resources (`SharedResource` + `.bn`), cookie-based culture, `/culture/set` |
| PDF / QR | QuestPDF + QRCoder, Hind Siliguri font embedded for Bengali glyphs |
| Excel export | ClosedXML (referenced; reports module not yet built) |

> **This differs from the plan document**, which was written for React + Spring
> Boot + MongoDB. The reasons for each substitution are in
> [`docs/build-log.md`](docs/build-log.md#stack-decisions-differ-from-the-plan-document).

---

## Run with Docker

Needs only Docker (Desktop or Engine + Compose v2). No .NET SDK, no local
Postgres.

```bash
cp .env.example .env          # then edit the passwords
docker compose up --build
```

| | URL | Notes |
|---|---|---|
| App | http://localhost:8080 | staff sign-in at `/staff/login` |
| Database | `localhost:5433` | `postgres` / the password in `.env`, for `psql` or a GUI |

On first boot the app creates the `busticketing` database **as UTF-8**, applies
migrations, and seeds the 64 districts + major terminals, the super-admin account
(`admin`), default settings, and the standard cancellation policy — the same path
as running on host Postgres.

```bash
docker compose logs -f app        # follow the app
docker compose down               # stop (keeps data)
docker compose down -v            # stop and wipe the database + key ring
```

**What's in the two containers**

- **`db`** — `postgres:18-alpine`, data on the `db-data` volume.
- **`app`** — the multi-stage [`Dockerfile`](Dockerfile): `dotnet publish` on the
  SDK image, then the ASP.NET runtime image (Debian — ships ICU + tzdata, which
  the `bn` culture and the `Asia/Dhaka` clock need). Adds `libfontconfig1` for
  QuestPDF's renderer. Runs as the non-root `app` user on port 8080. The
  data-protection key ring is on the `dp-keys` volume so logins survive a
  redeploy.

---

## Run locally

**Prerequisites** — .NET 10 SDK, and PostgreSQL 14+ on `127.0.0.1:5432`. The
default connection string in [`appsettings.json`](appsettings.json) expects user
`postgres` with a blank password.

```bash
dotnet tool restore     # once — restores the pinned dotnet-ef
dotnet run
```

Browse to the URL it prints (`http://localhost:5258` by default). To point at a
different database, override the connection string with user secrets or a
git-ignored `appsettings.Local.json`:

```json
{ "ConnectionStrings": { "Postgres": "Host=…;Database=busticketing;Username=…;Password=…" } }
```

---

## Sample data for testing

A fresh database has locations + the `admin` account only — nothing bookable. To
load a **sample fleet, routes, schedules and a week of trips** (4 buses of every
class, 8 real corridors like Dhaka ↔ Chattogram / Sylhet / Cox's Bazar, ~290
trips), use `seed-demo`. It's idempotent — it does nothing once a bus exists.

**Local**

```bash
dotnet run -- seed-demo      # seeds, prints a summary, exits
dotnet run                   # then start the app normally
```

**Docker** — either set the flag before the first `up`:

```bash
echo "SEED_DEMO=true" >> .env
docker compose up --build
```

…or run it as a one-shot against an already-running stack:

```bash
docker compose run --rm app seed-demo
```

Everything is created through the same paths the admin UI uses, so the generated
trips show up in search, the seat maps work, and bookings/PDF tickets all
function. For a real deployment leave `SEED_DEMO` unset.

---

## Configuration

Everything is standard ASP.NET configuration — `appsettings.json`, environment
variables (`__` for nesting), user secrets. The ones that matter:

| Key / env var | Default | Purpose |
|---|---|---|
| `ConnectionStrings__Postgres` | host Postgres, blank password | the database |
| `Seed__SuperAdminUsername` | `admin` | seeded on first boot |
| `Seed__SuperAdminPassword` | `ChangeMe!2026` | **change after first login** |
| `Seed__DemoData` | `false` | `true` loads the sample fleet/routes/schedules on boot (see above) |
| `Localization__DefaultCulture` | `en` | `en` or `bn` |
| `Hosting__HttpsRedirection` | on outside Development | off in the container — nginx owns the redirect + HSTS |
| `DataProtection__KeyPath` | unset (framework default) | set to a mounted dir to persist the key ring |

Runtime, per-platform settings (seat-hold minutes, generation window, time zone,
currency, support phone, public base URL for SMS links, cancellation tiers) are
edited in-app at **Settings** and **Cancellation policy** — not in config files.

Health probes: `GET /healthz` (liveness), `GET /healthz/ready` (database
reachable).

---

## Project structure

```
Domain/                 Entities, enums, value objects — no framework dependencies
  Entity.cs               UUIDv7 key + timestamps base
  Bus / BusRoute / Trip / TripSeat / Booking / Payment / …
  SeatMap.cs              jsonb value object — the bus's physical layout
  CancellationPolicy.cs   tiered hours-before-departure → refund %
  Enums.cs                TripStatus, BookingStatus, PaymentStatus, WeekDays …

Data/
  AppDbContext.cs         IdentityDbContext, snake_case, enums as strings
  Configurations/         one IEntityTypeConfiguration per aggregate + indexes
  Migrations/             EF Core migrations
  Seed/
    DatabaseInitializer   create DB (UTF-8) → migrate → seed roles/admin/settings
    LocationSeeder        64 districts + curated terminals, en + bn names
    bangladesh-locations.json

Services/
  Admin/                  Location / Bus / Route / Staff / SeatMapFactory
  Auth/                   policies, revalidating auth-state provider
  Scheduling/             ScheduleTemplateService, TripGenerationService,
                          TripFactory, the background generator
  Booking/                TripSearchService, SeatHoldService, SeatMapBroadcaster,
                          BookingService, BookingAdminService, PaymentReviewService,
                          CancellationPolicyService, maintenance background service
  Ticketing/              QR + QuestPDF e-ticket, GET /tickets/{ref}.pdf, embedded fonts
  Notifications/          ISmsSender seam, SmsService (+ SmsLog audit), message templates
  Localization/           /culture/set endpoint
  SettingsService · AppClock · AuditService · OperationResult

Components/
  Layout/                 public shell, auth shell, admin shell (responsive drawer)
  Pages/                  public: Home, Search, BookTrip, BookingConfirmation, MyTickets, Contact
  Booking/                SeatChart, PaymentResubmit
  Account/                staff sign in / out
  Admin/                  back-office, one folder per area (Buses, Routes, Schedules,
                          Trips, Bookings, Payments, Staff, Locations, Sell, …)
    Shared/               PageHeading, EmptyState, StatusPill, ConfirmDialog, LocationPicker
  Shared/Icon.razor       one stroke-icon set used across the admin

Resources/                SharedResource.resx (+ .bn), embedded Hind Siliguri .ttf
wwwroot/css/app.css       design system — tokens + primitives; component styles are scoped .razor.css

Dockerfile · docker-compose.yml · .dockerignore · .env.example
```

---

## How the project is planned

The plan document ([`bus-ticketing-project-plan.md`](bus-ticketing-project-plan.md))
lays out seven phases. Build progress against them:

| Phase | Scope | Status |
|---|---|---|
| **1 — Foundation** | Blazor Server + Postgres, domain model, auth, i18n, Bangladesh locations seed, design system | ✅ done |
| **2 — Admin core** | Bus + seat-layout editor, routes, locations, staff (+ counter assignment), settings, audit log | ✅ done |
| **3 — Schedule engine** | Schedule templates, rolling trip auto-generation, manual/one-off trips, status advance | ✅ done |
| **4 — Public booking** | Trip search, live seat map + hold/lock, booking, mFS + counter payment, staff payment verification, walk-in sell | ✅ done |
| **5 — Tickets & customer** | My Tickets (phone lookup), QR/PDF e-ticket, SMS seam, cancellation-policy engine + self-service cancel, boarding scan | ✅ done |
| **6 — Reports & dashboard** | Revenue / occupancy / payment-mix / cancellation-rate reports, Excel/PDF export | ⏳ dashboard tiles done; reports module not started |
| **7 — Hardening & deploy** | Rate limiting, CAPTCHA, SMS reminders, **Docker**, nginx + TLS | 🔶 Docker done; rate limiting / reminders / reverse-proxy config pending |

Each phase closed with an end-to-end Playwright pass at phone and desktop widths;
`docs/build-log.md` records what shipped and the bugs found on the way.

**Roles** (from the plan):

- **Customer** — no login, identified by phone. Search, book, pay, view history, cancel per policy.
- **Counter staff** — login. Sell to walk-ins, take offline payments, browse bookings, cancel with a reason.
- **Super admin** — everything: fleet, routes, schedules, fares, staff, all bookings, payment verification, refunds, settings, audit.

---

## Architecture notes

- **Interactivity model** — static SSR by default; islands of `InteractiveServer`
  (with `RenderModes.ServerNoPrerender` on forms, so hydration lag can't eat the
  first keystrokes) where a screen has live state. A page's `@rendermode` does
  **not** flow up to its layout.
- **Seat locking without SignalR** — `SeatMapBroadcaster` is an in-process
  pub/sub keyed by trip id. Every open seat map subscribes; hold / release / book
  fires `Notify(tripId)` and the other circuits re-fetch. The real guard against
  a double-book is the Postgres `xmin` row version on `trip_seats` — a concurrent
  write loses on `SaveChanges` and the caller retries.
- **Time** — `IAppClock` converts platform-zone wall-clock to UTC instants;
  Npgsql `timestamptz` only accepts `DateTimeOffset` at offset 0.
- **Trip generation** — a `BackgroundService` tops up a rolling
  `GenerationWindowDays` horizon from active templates every few hours and
  advances `Scheduled → Departed → Completed` every 15 minutes. It never
  overwrites a manually-edited or cancelled trip.
- **Auditing** — every back-office mutation writes an `AuditLog` row; the writer
  never throws into the caller.
- **SMS** — `ISmsSender` is the single seam a real gateway plugs into. The
  default `LoggingSmsSender` logs and records `Sent = false` rather than
  pretending to deliver; every attempt is persisted to `sms_logs`.

---

## Database & migrations

`dotnet-ef` is a pinned local tool.

```bash
dotnet tool restore
dotnet ef migrations add <Name> --output-dir Data/Migrations
dotnet ef database update
```

Migrations are also applied automatically on app startup
(`DatabaseInitializer`), so a fresh environment needs no manual step.

The database is created as **UTF-8 / C locale** explicitly — the initializer
connects to the `postgres` database first and runs `CREATE DATABASE … ENCODING
'UTF8'` so Bangla text is never mangled by a cluster's non-UTF-8 template.

---

## Deploying to a VPS

The plan's target is a single VPS: nginx terminates TLS and reverse-proxies to
the app container.

1. `cp .env.example .env` and set strong `POSTGRES_PASSWORD` / `SUPERADMIN_PASSWORD`.
2. `docker compose up -d --build`.
3. Point nginx at `127.0.0.1:8080` and let it own the 80→443 redirect + HSTS
   (the app ships with `Hosting__HttpsRedirection=false` and honours
   `X-Forwarded-Proto`):

   ```nginx
   server {
     server_name ticketbari.example;
     location / {
       proxy_pass http://127.0.0.1:8080;
       proxy_set_header Host $host;
       proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
       proxy_set_header X-Forwarded-Proto $scheme;
       proxy_set_header Upgrade $http_upgrade;          # Blazor circuit WebSocket
       proxy_set_header Connection "upgrade";
     }
   }
   ```
4. `certbot --nginx -d ticketbari.example` for TLS.
5. Set the public URL in-app at **Settings → Public base URL** so SMS ticket
   links are correct.
6. Sign in as `admin`, change the password, add your fleet and routes.

Still to do before real traffic — see
[`docs/build-log.md`](docs/build-log.md): rate limiting on the public endpoints,
the reports module, an outbox for SMS, and a checked-in test project.

Notes:
- Back up the `db-data` volume (`pg_dump` on a schedule).
- The data-protection key ring on `dp-keys` is stored unencrypted — keep the
  volume on the host's disk, not a shared mount.
