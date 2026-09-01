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
