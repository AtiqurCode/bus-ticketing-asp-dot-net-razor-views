# Build log

Running notes on how the platform is being built, milestone by milestone.
The feature scope lives in [`../bus-ticketing-project-plan.md`](../bus-ticketing-project-plan.md);
this file records the decisions and what actually shipped.

## Stack decisions (differ from the plan document)

The plan was written for React + Spring Boot + MongoDB. The build uses:

| Plan | Actual | Why |
|---|---|---|
| React SPA + separate backend | One ASP.NET Core **Blazor Server** app | "Use only ASP.NET" for both ends |
| MongoDB | **PostgreSQL** + EF Core 10 | Relational fits the seat/booking/trip integrity needs; transactions are first-class |
| JWT auth | **Cookie auth** via ASP.NET Core Identity | The right model for server-rendered Blazor circuits |
| STOMP/WebSocket seat lock | Blazor circuit + a broadcast service; DB row version (`xmin`) as the real guard | No extra protocol; the database is the source of truth |
| Quartz scheduler | `BackgroundService` + `PeriodicTimer` | Enough for a rolling top-up job |

Interactivity model: **static SSR by default, islands of `InteractiveServer`** where a
screen needs live state (seat map, admin grids). Keeps first render fast and avoids
circuit overhead on read-only pages.

## Milestone 1 — Foundation

- Converted the `dotnet new mvc` scaffold to Blazor Server; removed MVC Views/Controllers
  and the Bootstrap/jQuery client libs.
- **Domain model** (`Domain/`): Location, Bus (+ `SeatMap` jsonb value object), BusRoute,
  ScheduleTemplate, Trip, TripSeat, Booking (+ owned BookingSeat), Payment,
  CancellationPolicy (+ rules), AuditLog, AppSettings (singleton row), StaffUser/StaffRole.
  UUIDv7 keys, `DateTimeOffset` instants, `xmin` optimistic concurrency on Trip/TripSeat.
- **Data** (`Data/`): `AppDbContext : IdentityDbContext`, per-aggregate `IEntityTypeConfiguration`,
  snake_case naming (incl. the Identity tables), enums stored as strings.
- **DatabaseInitializer**: creates the DB as **UTF-8 / C locale** (the local cluster
  default is WIN1252 — would corrupt Bangla), migrates, seeds roles, super-admin,
  settings, the standard cancellation policy, and the Bangladesh location list
  (64 districts + curated terminals, English + Bangla names).
- **Localization**: `IStringLocalizer<SharedResource>`, `SharedResource.resx` +
  `.bn.resx`, cookie culture provider, `/culture/set` GET endpoint, pill switcher.
- **Design system** (`wwwroot/css/app.css`): tokens (colour, type, space, radius,
  shadow), base elements, buttons/fields/cards/badges/alerts. Inter + Hind Siliguri
  web fonts. Component styling is scoped `.razor.css`.
- **Shells**: public (`MainLayout` + sticky header w/ CSS-only mobile menu + footer),
  `AuthLayout` (focused sign-in), `AdminLayout` (fixed sidebar, responsive drawer).
- **Pages**: designed landing page with the trip-search box (datalist autocomplete
  from seeded locations) + feature trio + popular-routes strip; placeholder
  Search / MyTickets / Contact; 404 + `/error`.
- **Auth**: `/staff/login` (static SSR `EditForm` → `SignInManager`), `/staff/logout`
  (confirm + POST), `/staff/denied`, revalidating auth-state provider, `BackOffice`
  and `SuperAdminOnly` policies, login writes an audit entry + `LastLoginAt`.
- **Ops**: `/healthz`, `/healthz/ready`; `.editorconfig`; local `dotnet-ef` tool.

Gotcha fixed: an `@layout AdminLayout` line in `Components/Admin/_Imports.razor` made
`AdminLayout` its own parent layout → infinite render → thread-pool starvation. Layout
components now live in `Components/Layout/` where that import doesn't reach them.

## Milestone 2 — Admin core

- **Services** (`Services/Admin/`): `LocationService`, `BusService`, `RouteService`,
  `StaffService` — list/get/create/update/delete with business validation, FK-guarded
  deletes, and an audit entry on every mutation. `OperationResult` for expected failures.
  `SeatMapFactory` seeds a starting seat plan from a preset.
- **Shared components** (`Components/Admin/Shared/`): `PageHeading`, `EmptyState`,
  `StatusPill`, `ConfirmDialog`, `LocationPicker` (division-grouped select).
- **Screens** (all `InteractiveServer`; forms use `RenderModes.ServerNoPrerender` so
  hydration lag can't eat the first keystrokes):
  - Buses — list + form with the **visual seat-layout editor** (`SeatLayoutEditor`):
    preset generate, per-cell add/remove/type, add/remove rows & columns, renumber.
  - Routes — list + form (origin/destination pickers, distance, duration).
  - Locations — list/search/filter + form; cities seeded, admin adds terminals/counters.
  - Staff (super admin) — list + form, role + counter assignment, password reset,
    activate/deactivate (bumps the security stamp to drop live sessions).
  - Settings (super admin) — typed edit of the `AppSettings` singleton.
  - Audit log (super admin) — paged, searchable, action filter.
- Dashboard now shows fleet/route/location/staff counts and setup shortcuts.
- `AdminNav` grows per milestone so every link resolves; sections role-gated with
  `<AuthorizeView Policy=…>`.

Gotcha fixed: scoped `.razor.css` doesn't reach a child component's root element
(`<NavLink>`), so `AdminNav`'s link styling needs `::deep`. `@rendermode` can't live
in `_Imports.razor` — needs `@using static …RenderMode` and a per-page directive.

Verified end-to-end with a Playwright script (`scratchpad/drive.mjs`): login → create
a bus + seat map → routes → staff → settings → audit, 0 console errors, plus mobile.

## Milestone 3 — Recurring schedule engine

- **`ScheduleTemplateService`** — CRUD for the recurring pattern (route, bus,
  first/last departure, interval, fare, operating days, default counters).
  Delete is blocked once trips exist — deactivate instead.
- **`TripGenerationService`** — the engine:
  - `TopUpAsync` walks every active template and materialises `Trip` + `TripSeat`
    rows for each operating day in `[today, today + GenerationWindowDays]`.
  - Dedup on `(RouteId, DepartureTime)` — an existing row (generated, manually
    overridden, or cancelled) blocks that minute, so the generator never
    double-books or overwrites a hand-made trip.
  - `RegenerateAsync(templateId)` drops future unbooked generator-owned trips
    and rebuilds them — the explicit "I changed the pattern" action.
  - `AdvanceStatusesAsync` moves trips Scheduled → Departed → Completed by clock.
- **`TripGenerationBackgroundService`** — `PeriodicTimer` every 15 min: advance
  statuses each tick, top up every 6 h. Honours `AppSettings.AutoGenerationPaused`.
- **`TripService`** — manual trip CRUD + paged/filtered listing (route, date,
  status, upcoming-only), one-off create, edit (flips `IsManualOverride` on),
  cancel with reason (frees non-booked seats), manual status override.
- **Screens**: `/staff/schedules` (list + "Generate now"), `/staff/schedules/{id}`
  (form with a live departure-times preview + "Regenerate upcoming trips"),
  `/staff/trips` (filtered list + cancel modal), `/staff/trips/{id}` (one-off form).
  Shared `WeekdayPicker`.

**Bug fixed:** Npgsql `timestamptz` only accepts `DateTimeOffset` at offset 0.
Building departures as `new DateTimeOffset(local, +06:00)` threw on save. Added
`IAppClock.ToInstant(date, time)` / `ToInstant(DateTime)` which converts a
platform-zone wall clock to a UTC instant; all trip times go through it.

Verified with `scratchpad/drive-m3.mjs`: schedule create → 54 trips generated
(9/day × 6 days, Friday excluded), one-off trip, trip cancel. 0 errors.

## Milestone 4a — Public booking flow

- **`TripSearchService`** — resolves a free-text place to matching location ids
  (a city also matches its terminals), finds Scheduled future trips on the date
  with an available-seat count.
- **`SeatMapBroadcaster`** (singleton) — in-process pub/sub keyed by trip. Every
  open seat map subscribes; hold / release / book fires `Notify(tripId)` and the
  other circuits re-fetch. Live seat locking with no SignalR hub.
- **`SeatHoldService`** — `GetContextAsync` (sweeps expired holds, returns the
  seat views + trip info + this session's hold expiry), `ToggleAsync` (hold /
  release one seat, 6-seat cap, optimistic-concurrency guarded), `ExtendAsync`,
  `ReleaseAllAsync`, `SweepExpiredAsync`.
- **`BookingService`** — `CreateAsync` re-verifies every seat is still held by
  this token, generates a phone-friendly reference, writes Booking + BookingSeats
  + Payment, flips seats to Booked, sets the payment-window `HoldExpiresAt`.
  Plus `GetByReferenceAsync`, `HistoryByPhoneAsync`, `ResubmitPaymentAsync`,
  and `ExpireStaleAsync` (unpaid Reserved bookings past their window → Expired,
  seats freed).
- **`BookingMaintenanceBackgroundService`** — every 60 s sweeps abandoned holds
  and expires stale reservations.
- **Screens** (customer, bilingual): `/search` (results + day bar), `/book/{id}`
  (`SeatChart` grid + hold countdown + passenger/payment step, InteractiveServer
  no-prerender, releases the hold on dispose), `/booking/{ref}` (e-ticket stub
  with status banner; `PaymentResubmit` island for rejected online payments).
- `PhoneNumber.Normalize` → local `01XXXXXXXXX`. `BookingReference` uses an
  unambiguous alphabet.

Renamed the Blazor `SeatMap` component to `SeatChart` and the services namespace
to `BusTicketing.Services.Bookings` to stop clashing with the `SeatMap` value
object and `Booking` entity.

Verified with `scratchpad/drive-m4.mjs`: search → seat map → 2-seat hold (timer
shown) → online (bKash + txn id) and pay-at-counter bookings both land on the
e-ticket with the right status; **two browsers on one trip — a seat held in one
shows as "being booked" in the other**. 0 console errors.

## Milestone 4b — Payment verification, booking management, staff sales

- **`PaymentReviewService`** — queue of bookings needing a decision (default
  filter: pending), scoped to a counter's own boarding/dropping point for
  counter staff. `VerifyAsync` confirms the booking and clears the hold;
  `RejectAsync` keeps the booking Reserved with a note so the passenger can
  resubmit; `RecordCounterPaymentAsync` is the "cash/mFS taken in person" path.
- **`/staff/payments`** — tabbed (Pending/Rejected/Verified/All) review queue;
  online rows show provider/transaction id/sender/submitted time with
  Verify/Reject; counter rows show Record payment (provider + optional txn id).
- **`BookingAdminService`** + **`/staff/bookings`** (searchable, filterable by
  status/payment/route) and **`/staff/bookings/{id}`** (full detail, staff
  cancel with reason — frees the seats and notifies the live seat map).
- **`/staff/sell`** — walk-in sale: compact search → `SeatChart` (same hold
  service as the public flow) → passenger + Cash/mFS-now/Reserve payment.
  Cash and mFS-now confirm immediately (`MarkPaidNow`); Reserve creates a
  counter-pay booking staff settle later from Payments.

Verified with `scratchpad/drive-m4b.mjs`: verified an online payment, recorded
a counter cash payment, browsed bookings list → detail, and ran a full walk-in
sale that lands Confirmed/Paid immediately. 0 console errors.

## Milestone 5 — Tickets, cancellation policy, PDF/QR, SMS, boarding scan

- **My Tickets** (`/my-tickets`) — real implementation: phone lookup, upcoming
  vs past, view ticket / download PDF / cancel actions per booking.
- **Cancellation policy engine**: `CancellationPolicy.RefundPercentFor(hours)`
  (already modelled in M1) is now wired up — `CancellationPolicyService` for
  the admin editor (`/staff/cancellation-policy`, tiered hours→refund% with a
  live preview), and `BookingService.PreviewCancellationAsync` /
  `CancelByCustomerAsync` for the self-service flow (phone number is the only
  credential; shows the exact refund before confirming).
- **QR-coded PDF e-ticket**: `TicketPdfService` (QuestPDF) + `QrCodeGenerator`
  (QRCoder) render a branded ticket — route, QR, reference, seats, passenger,
  fare, payment status. Bengali text (passenger names, route labels) needed a
  font that actually carries those glyphs, so **Hind Siliguri is embedded and
  registered with QuestPDF at startup** (`Resources/Fonts/*.ttf`,
  `PdfFonts.RegisterEmbeddedFonts()`) rather than relying on a system font.
  Served at `GET /tickets/{reference}.pdf`.
- **SMS hook**: `ISmsSender` is the single seam a real gateway (BulkSMSBD,
  Alpha SMS, …) plugs into later. `LoggingSmsSender` is the honest default —
  it logs the message and records `Sent = false` with a "no gateway
  configured" reason rather than pretending to deliver. `SmsService` persists
  every attempt to `SmsLog` (audit trail) and is called on booking created,
  payment verified/rejected, and booking cancelled.
- **Staff boarding scan** (`/staff/scan`) — a plain autofocused text input
  that a USB/Bluetooth QR scanner types into like a keyboard (no camera/JS
  needed); shows a clear valid/invalid verdict with passenger and trip detail.
- Settings gained `PublicBaseUrl` (builds the ticket link SMS carries; blank
  until the domain is live).

**Two real bugs found and fixed** (both the same root cause): adding a new
child entity to an *already-tracked* parent's collection navigation
(`policy.Rules.Add(new CancellationRule {…})`, `trip.Seats.Add(new TripSeat
{…})`) without an explicit `context.Add()` — EF's change-tracker fixup sees
the client-generated `Entity.Id` (a non-empty Guid) and assumes the row
already exists, so it emits an `UPDATE` instead of an `INSERT` and throws
`DbUpdateConcurrencyException` ("0 rows affected"). Fixed by adding the new
rows straight to the `DbSet` (`db.CancellationRules.AddRange(...)`,
`db.TripSeats.AddRange(...)`) so they're unconditionally tracked as `Added`.
Also hit and fixed: EF's `PendingModelChangesWarning` false-positives against
this model (owned-JSON seat map + xmin rowversion don't round-trip the
snapshot hash identically) — confirmed no real diff via `dotnet ef migrations
add` and suppressed the specific warning.

Verified with `scratchpad/drive-m5.mjs`: My Tickets lookup → PDF download (200,
real bytes) → self-service cancel with a correct 90% refund preview → admin
policy edit → boarding scan of a live reference → settings save. Confirmed via
DB: refund tiers persisted correctly and `sms_logs` carries an honest record
of every notification attempt. 0 console errors.

## UX pass — admin responsiveness + icon system + public simplification

Prompted by "admin side not responsive, few features UI-broken, make public simpler".
Audited every page at 390px and 1360px with Playwright (`scratchpad/audit.mjs`).

- **Icon system** (`Components/Shared/Icon.razor`): one 24px stroke-icon set
  (currentColor SVG), replacing the mixed emoji / geometric-unicode glyphs
  (`▚ ↔ ৳ 🚌 ✎ 🗑 ⦸`…) that rendered differently on every machine and read as
  broken. Applied across the admin nav, every list-page action button, the
  scan verdict and the confirmation banner.
- **Admin data tables → stacked cards below 780px**: a global CSS rule turns
  `table.data` into labelled cards (`td[data-label]::before`); each list page
  now carries `data-label` on its cells. Ends the unusable horizontal scroll on
  phones. List page sizes dropped 40→20 (trips, bookings) / 25 (audit).
- **Admin topbar** rebuilt for mobile — was cramped/wrapping ("Sign out" on two
  lines); now a burger + title + language + an icon sign-out. Sidebar is a
  slide-in drawer with a scrim.
- **Drawer gotcha**: a page's `@rendermode` does *not* flow up to its layout, so
  `AdminLayout` renders as static SSR — a Blazor `@onclick` on the burger did
  nothing. Switched to a 4-line inline `window.AdminNav` toggle; nav links call
  `AdminNav.close()`. Verified: burger opens, nav-click navigates *and* closes.
- **Dashboard** now shows real day-to-day numbers (bookings + revenue today,
  payments awaiting review, trips in the next 24h, unpaid reservations) as
  clickable tiles, plus fleet counts; the stale "screens arrive next" copy is
  gone and the setup card only shows before the fleet exists.
- **Public**: search date defaults to *tomorrow* after 18:00 local (most of
  today's buses have already left, so "no buses" was the common first
  impression). Toolbars stack cleanly; stat tiles go 2-up on small screens.

Re-audited: 0 horizontal-overflow, 0 page errors across 24 pages × 2 viewports.

## Follow-ups from the implementation review

- **Seat-hold countdown in `/staff/sell`.** The walk-in flow held seats for the
  full `SeatHoldMinutes` window with no visible timer — staff could lose a
  selection mid-sale with no warning. Ported the `holdclock` from `BookTrip`:
  a 1-second `PeriodicTimer` reads `TripBookingContext.MyHoldExpiresAt`, shows
  `m:ss` in the Selection panel (Seats step) and above the passenger form
  (Details step), turns red under 60s, and on lapse releases the hold, drops
  back to the Seats step and shows "Your seat hold expired — please choose the
  seats again." "Continue" now also calls `ExtendAsync` so the details form
  starts from a full window. Ticker is torn down on trip change, on a completed
  sale, and on dispose. Verified with Playwright at 1360/390 incl. a real
  1-minute-window expiry.

## Phase 7 (partial) — containerisation

Multi-stage [`Dockerfile`](../Dockerfile) + [`docker-compose.yml`](../docker-compose.yml)
(app + `postgres:18-alpine`), `.dockerignore`, `.env.example`.
`docker compose up --build` → working stack on `:8080`, DB on `:5433`.

- **Runtime image**: `mcr.microsoft.com/dotnet/aspnet:10.0` (Debian — ICU +
  tzdata included, needed for the `bn` culture and `Asia/Dhaka`). Adds
  `libfontconfig1` for QuestPDF's SkiaSharp renderer and `curl` for the
  healthcheck. Runs as the non-root `app` user on port 8080.
- **First boot in the container** is the same path as host Postgres — the app
  connects to the `postgres` db, `CREATE DATABASE busticketing ENCODING 'UTF8'`,
  migrates, seeds. Verified: `datname=busticketing enc=UTF8`, 95 locations.
- **Reverse-proxy ready**: added `UseForwardedHeaders` (X-Forwarded-For/Proto,
  known-proxies cleared since the container is only reachable through nginx) and
  gated `UseHttpsRedirection` + `UseHsts` behind `Hosting:HttpsRedirection`
  (default on outside Development, `false` in the image — nginx owns the
  redirect). README carries the nginx server block + certbot steps.
- **Data-protection key ring** persisted to the `dp-keys` volume
  (`DataProtection:KeyPath` → `PersistKeysToFileSystem` + `SetApplicationName`).
  Without it every container restart invalidated auth cookies and antiforgery
  tokens. Verified: signed-in session survives `docker restart`.

**Bug found and fixed while building the image** — the embedded Bengali font
never reached the runtime. `PdfFonts` read `HindSiliguri-*.ttf` from
`AppContext.BaseDirectory/Resources/Fonts`, but the csproj registered them with
`<Content Update="Resources\Fonts\*.ttf" …>` — and `.ttf` is not a default
Content item under the Web SDK (only `.json` / `.config` / `wwwroot` are), so
`Update` matched nothing and `dotnet publish` dropped them. Ticket PDFs with
Bangla passenger names were rendering tofu. Fixed by shipping the fonts as
`<EmbeddedResource>` and loading them from the manifest
(`Assembly.GetManifestResourceStream`) — packaging-independent. Verified in the
container: PDF 200, valid, `/FontFile2` subset present, `রফিকুল ইসলাম` renders.

**Also fixed** — a `--no-restore` on the Docker `dotnet publish` dropped the
SDK's implicit Blazor asset package, so `_framework/blazor.web.js` 404'd and no
interactive page worked in the container. Publish now restores.

Verified end-to-end against a fresh stack (`scratchpad/docker-e2e.mjs`): create
bus + seat map → route → schedule → generate 65 trips → live seat map + hold
countdown → booking with a Bengali name → QR/PDF e-ticket. 0 console errors.

### `seed-demo` — sample data for testing

`Data/Seed/DemoDataSeeder.cs` — 4 buses (one per class), 8 real corridors
(Dhaka ↔ Chattogram / Sylhet / Khulna / Rajshahi / Cox's Bazar / Barishal with
real distances), 7 schedule templates, then `TripGenerationService.TopUpAsync`
to materialise ~290 trips across the window. Idempotent — bails once a bus
exists. Cities are looked up from the seeded locations, so it runs after
`LocationSeeder`.

Wired into `DatabaseInitializer.RunAsync(…, includeDemoData)`. `Program.cs`
turns it on from either `Seed:DemoData=true` (a flag on a normal startup) or the
`seed-demo` command-line arg (`dotnet run -- seed-demo` — seeds then `return`s
before `app.Run()`, so it's a one-shot command). Compose exposes it as
`SEED_DEMO` in `.env`; `docker compose run --rm app seed-demo` is the one-shot
against a running stack. Verified all three paths — public search shows the
generated departures, seat maps and bookings work.

**Bug fixed — super admin could get stuck with no role.** `SeedSuperAdminAsync`
returned early if the `admin` user already existed, so any boot that created the
user but didn't finish `AddToRoleAsync` (interrupted first boot, a mid-init
`docker restart`, a swallowed failure) left an account that signs in fine but
hits "not allowed" on every screen — and no later boot ever fixed it. Now it's
**self-healing**: find-or-create the user, then every boot check
`IsInRoleAsync(SuperAdmin)` and grant it if missing, logging the result.
`SeedRolesAsync` checks its `CreateAsync` results too. Verified: strip the role
row → restart → re-granted; five fresh `down -v` boots all land `admin →
SuperAdmin`.
