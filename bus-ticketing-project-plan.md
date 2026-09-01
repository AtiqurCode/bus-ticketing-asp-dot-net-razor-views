# Bus Ticketing Booking Platform — Project Plan

## 1. Overview

A mobile-responsive web-based bus ticket booking platform where customers can book tickets without creating an account (phone + name only), view their booking history by phone number, and pay online or offline. Admins manage routes, buses, schedules, bookings, and reports through role-based screens — all within the same React web app.

**Stack:** React (web, fully mobile-responsive) + Spring Boot (Java) backend + MongoDB + single VPS deployment.

---

## 2. Tech Stack

| Layer | Technology |
|---|---|
| Frontend (Customer + Admin) | React (Vite), TypeScript, React Router, TanStack Query, Tailwind CSS (mobile-first responsive design) |
| Backend | Spring Boot (Java 21), Spring Web |
| Database | MongoDB |
| ODM | Spring Data MongoDB |
| Auth (Admin/Staff only) | Spring Security + JWT |
| Realtime seat lock | WebSockets (Spring WebSocket / STOMP) |
| Background jobs (trip auto-generation) | Spring Scheduler (@Scheduled) + Quartz (if more control needed) |
| Payments | Manual mFS transaction ID entry (bKash / Nagad / Rocket / Other) for now |
| SMS | Local SMS gateway (e.g., BulkSMSBD, Alpha SMS) — for ticket confirmation |
| Deployment | Single VPS — Nginx reverse proxy, Docker Compose (Spring Boot + MongoDB + frontend build), Let's Encrypt SSL |
| PDF Ticket | OpenPDF or iText (backend-generated e-ticket) |

---

## 3. User Roles

| Role | Access |
|---|---|
| **Customer** | No login. Identified by phone number. Books tickets, views own booking history. Uses the customer side of the web app. |
| **Counter Staff** | Login required. Books tickets on behalf of walk-in customers, marks offline payments, views/searches bookings for assigned route(s). Uses the staff/admin side of the same web app. |
| **Super Admin** | Full access — manage buses, routes, schedules, fares, staff accounts, all bookings, reports, refunds/cancellations. |

---

## 4. Core Features

### 4.1 Customer (Public, No Auth)
- Search trips by route (from → to), date
- View available buses, timings, fare, and **live seat map**
- Select seat(s) visually (locked temporarily via WebSocket while booking is in progress, e.g. 5-min hold)
- Enter name + phone number (+ optional email) to complete booking
- Choose payment:
  - **Pay Online**: select mFS provider (bKash / Nagad / Rocket / other), enter the **Transaction ID** from their own payment, submit for verification
  - **Pay at Counter**: reserve seat, pay later — with hold expiry
- Receive e-ticket via SMS link / downloadable PDF (with QR code for boarding validation)
- **"My Tickets" page**: enter phone number → view booking history (upcoming & past trips), download ticket again, cancel (per policy)

### 4.2 Counter Staff
- Login (username/password)
- Search & book tickets for walk-in customers (same booking flow as customer)
- Mark payment as "Cash Received" or record mFS transaction ID on customer's behalf
- View today's bookings for their counter/route
- Cancel/reschedule a booking (with reason log)

### 4.3 Super Admin
- Login (role-based)
- **Bus & Fleet Management**: add/edit buses, seat layout, class (AC/Non-AC), operator
- **Route Management**: origin, destination, stops (from pre-loaded Bangladesh location list), distance, duration
- **Schedule Template Management**: define a recurring time pattern per route (e.g., every 2 hours from 6 AM–10 PM, or every 4 hours) — system auto-generates actual Trips for the next 7 rolling days from these templates
- **Manual Trip Management**: view/override/cancel any auto-generated trip, or add one-off trips outside the template
- **Settings**: configure recurring generation rules — interval (every N hours), operating start/end time, which routes/buses are active in the rotation, how many days ahead to generate (default 7)
- **Staff Management**: create/manage counter staff accounts, assign counters
- **Booking Management**: view/search/filter all bookings (by date, route, phone, status, payment type)
- **Payment Verification**: review submitted mFS transaction IDs and mark bookings as verified/paid
- **Cancellation/Refund handling**
- **Reports & Analytics**:
  - Revenue reports (daily/monthly/route-wise)
  - Occupancy rate per trip
  - Payment mode breakdown (online vs counter)
  - Cancellation rate
  - Export to Excel/PDF
- **Dashboard**: today's bookings, revenue snapshot, upcoming trips at a glance
- Audit log of admin/staff actions

---

## 5. Bangladesh Location Data

- Preload a master list of Bangladesh locations (Division → District → Upazila/Town, or at minimum major bus stop cities/terminals) into a `Locations` table.
- Used as the selectable origin/destination in Route creation and in the public search dropdowns (searchable, autocomplete).
- Source: can be seeded from a static dataset (e.g., BBS division/district list) plus manually curated bus terminal/counter points per city.
- Admin can add/edit custom stop points (bus counters, terminals) tied to a location.

## 6. Recurring Schedule Engine (Auto Trip Generation)

Instead of manually creating a trip for every date, admin defines a **Schedule Template** per route, and the system auto-generates real `Trips` on a rolling 7-day window.

**Schedule Template fields:**
- Route (from → to)
- Bus/Bus type assigned to this template
- Start time & end time of daily operation (e.g., 06:00–22:00)
- Interval (every 2 hours / every 4 hours / custom)
- Fare
- Active/Inactive toggle
- Days of week it applies (optional — e.g., exclude Friday)

**How generation works:**
- A scheduled background job (runs daily, e.g., via Spring's `@Scheduled` or Quartz) checks all active templates and ensures Trips exist for the next 7 days.
- Each run "tops up" the rolling window — e.g., today it ensures Day+7 exists, so there's always a 7-day booking horizon.
- If admin edits a template (changes interval/time), it only affects future not-yet-generated trips — already-booked trips stay untouched.
- Manually added one-off trips or manual cancellations are never overwritten by the generator.

**Settings module** (Admin):
- Default generation window (7 days, configurable)
- Global default interval options available when creating templates (2h, 3h, 4h, custom)
- Time zone setting
- Toggle to pause auto-generation entirely (maintenance mode)

## 7. Suggested Improvements Beyond Original Scope

- **Seat hold/lock mechanism** (WebSockets) — prevents two customers booking the same seat simultaneously
- **Manual payment verification** — since there's no gateway integration yet, admin/staff verify submitted mFS Transaction IDs before confirming the booking as paid
- **QR-coded e-ticket** — for boarding staff to scan and validate at the bus door
- **Cancellation/refund policy engine** — configurable rules (e.g., free cancel up to 6 hrs before departure)
- **Trip status tracking** — mark trip as Scheduled / Departed / Completed / Cancelled
- **Notification system** — SMS reminders before departure
- **Rate limiting on public booking API** — prevent abuse since there's no auth wall

---

## 8. High-Level Database Schema (MongoDB Collections)

```
locations       { _id, division, district, name, type[City/Terminal/Counter], parentLocationId? }

buses           { _id, name, operator, seatLayout: [...], class, totalSeats }

routes          { _id, originLocationId, destinationLocationId, distanceKm }

scheduleTemplates {
  _id, routeId, busId, startTime, endTime, intervalMinutes,
  fare, daysOfWeek: [...], isActive
}

trips           {
  _id, busId, routeId, scheduleTemplateId?, departureTime, arrivalTime,
  fare, status, isManualOverride,
  seats: [ { seatNumber, status[Available/Held/Booked] } ]   // embedded for fast reads
}

bookings        {
  _id, tripId, passengerName, passengerPhone, seatNumbers: [...],
  totalAmount, paymentMode[Online/Counter],
  paymentStatus[Pending/Verified/Rejected], bookingStatus,
  bookedByStaffId?, createdAt,
  payment: { mfsProvider, transactionId, verifiedByUserId?, verifiedAt?, status }  // embedded
}

staffUsers      { _id, name, username, passwordHash, role[SuperAdmin/CounterStaff], counterId? }

settings        { _id, key, value }   // e.g. generationWindowDays, defaultIntervalOptions, timeZone, autoGenerationPaused

auditLogs       { _id, userId, action, entity, timestamp }
```

Notes:
- `seats` are embedded inside `trips` since they're always read/written together per trip (fast lookups for seat map).
- `payment` details are embedded inside `bookings` (1:1 relationship, no need for a separate collection).
- Indexes: `bookings.passengerPhone`, `trips.departureTime` + `trips.routeId`, `locations.name` (text index for autocomplete search).

---

## 9. API Structure (High-Level)

```
Public:
  GET  /api/locations/search?q=
  GET  /api/trips/search?from=&to=&date=
  GET  /api/trips/{id}/seats
  POST /api/bookings/hold-seat
  POST /api/bookings
  GET  /api/bookings/history?phone=
  POST /api/payments/submit-transaction   (mfsProvider, transactionId)

Staff/Admin (JWT-protected):
  POST /api/auth/login
  CRUD /api/admin/locations
  CRUD /api/admin/buses
  CRUD /api/admin/routes
  CRUD /api/admin/schedule-templates
  CRUD /api/admin/trips              (manual override/one-off)
  CRUD /api/admin/staff
  GET  /api/admin/settings
  PUT  /api/admin/settings
  GET  /api/admin/bookings
  POST /api/admin/bookings/{id}/cancel
  POST /api/admin/payments/{id}/verify
  POST /api/admin/payments/{id}/reject
  GET  /api/admin/reports/revenue
  GET  /api/admin/reports/occupancy
```

---

## 10. Non-Functional Requirements

- Seat booking must be transaction-safe (no double-booking) — use DB transactions + row locking or WebSocket seat-hold
- Fully mobile-responsive UI (works seamlessly on phone, tablet, desktop) — most customers will book from phone browsers
- Bangla + English language support (if targeting Bangladesh market)
- Basic rate limiting / CAPTCHA on public booking endpoint
- Daily DB backup on VPS
- HTTPS enforced everywhere

---

## 11. Development Phases

**Phase 1 — Foundation (Week 1–2)**
- Project setup (React web app + Spring Boot backend), DB schema, base entities
- Seed Bangladesh Locations dataset
- Admin auth (JWT), role setup

**Phase 2 — Admin Core (Week 3–4)**
- Bus, Route CRUD (using Locations)
- Staff management
- Settings module (generation window, interval options, time zone)

**Phase 3 — Recurring Schedule Engine (Week 5)**
- Schedule Template CRUD
- Background job for rolling 7-day Trip auto-generation
- Manual trip override/one-off trip support

**Phase 4 — Public Booking Flow (Week 6–8)**
- Trip search, seat map UI, seat hold (WebSockets)
- Booking creation
- Payment flow: mFS provider selection + transaction ID submission (online), counter payment recording (offline)
- Admin/staff payment verification screen

**Phase 5 — Customer History + Tickets (Week 9)**
- "My Tickets" page, PDF/QR e-ticket generation, SMS sending

**Phase 6 — Reports & Admin Dashboard (Week 10)**
- Revenue/occupancy reports, dashboard widgets, export

**Phase 7 — Testing & Deployment (Week 11)**
- End-to-end testing, cross-device/responsive QA (phone, tablet, desktop), VPS setup (Docker + Nginx + SSL), UAT, go-live

---

## 12. Open Decisions to Confirm Later
- Whether to move to a full payment gateway (SSLCommerz/direct bKash-Nagad API) later, replacing manual transaction ID entry
- Which SMS provider
- Single currency/region or multi-route operator support
- Cancellation/refund policy specifics
- How long a booking stays "Pending" before auto-cancellation if payment isn't verified
- Source/format for the Bangladesh location dataset (division/district list vs. curated terminal list)
- Whether interval-based schedules can differ by day (e.g., more frequent on Friday) or stay uniform
