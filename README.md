# speranta-api

API for the website of **Biserica Adventistă de Ziua a Șaptea Speranța Timișoara**
(sperantatm.adventist.ro). It replaces the WordPress site's forms and the few pieces
of content the church changes, and serves the admin.

- **Stack:** .NET 10 (ASP.NET Core minimal APIs) · PostgreSQL 18 · EF Core 10 (Npgsql)
- **Client:** [`speranta-web`](https://github.com/maze-33/speranta-web) (React + Vite)
- **Design:** [Figma](https://www.figma.com/design/NolJGaQ4AKE8Eqwc3ZXTaS)
- **Hosting:** not decided yet. Runs locally.

## What it does

| Area | Public | Admin |
|---|---|---|
| Free books | `POST /forms/book-orders` | list, detail, status, delivery method, internal note, history |
| Kids' activities | `POST /forms/kids-signups` | list, detail, status, internal note |
| Contact | `POST /forms/contact` | list, detail, mark answered |
| Announcements | `GET /announcements`, `GET /announcements/{slug}` | drafts, scheduling, cover image, "show on home" |
| Schedule, kids' groups, phone | `GET /site` | `PUT /admin/schedule` |
| Monthly flyer | `GET /flyer` (redirects to the current PDF, counts downloads) | `POST /admin/flyer` |
| Books on/off and order | `GET /site` | `PATCH /admin/books/{id}`, `PUT /admin/books/order` |
| Admins | | invite by email, remove |

Page texts (the book descriptions, the kids' page, contact details) live in the web
client. The API holds only what an admin changes.

**Statuses** (as approved by the church): orders `New → Confirmed → Delivered | PickedUp`
or `Cancelled`; sign-ups `New → Contacted → Enrolled` or `Withdrawn`; messages
`New → Answered`. Enums travel and are stored as these names; the web client shows the
Romanian labels (Nou, Confirmat, Livrat, Ridicat, Anulat …).

Every submission is saved first, then announced by email to `Email:NotifyTo`
(speranta.azs.tm@gmail.com).

### Email that can't silently stop

The old WordPress site sent through WP Mail SMTP's Google sign-in, whose permission
expired every few days (an OAuth app left in Google's "Testing" mode): emails stopped
without a sign, the notifications from those days were lost, and someone had to test the
form daily. Here:

- **Gmail app password over SMTP**, not a sign-in permission: it does not expire, and stops
  only if the account password changes or 2-Step Verification is turned off.
- **A database outbox** (`outbox_emails`). The request only queues the email; a background
  sender delivers it. A refused send is retried (1, 5, 15, 30 min, then hourly, without
  end), so once the problem is fixed every missed notification arrives. Restarts lose nothing.
- **A sign-in check every 6 hours** (`Email:CheckIntervalHours`) without sending anything, so
  a dead password shows up even on a day with no orders.
- **The admin sees it.** `GET /admin/mail-status` drives a warning banner on every admin page
  (`Ok`, `Failing` since when and why, `NotConfigured`); `POST /admin/mail-status/check`
  checks now and sends what is waiting; `POST /admin/mail-status/test` sends a test email.
  Each submission carries `emailPending`, and its history gets "Email trimis la …" only once
  the email really went out.
- Nothing is lost either way: every submission is in the admin before any email is tried.

**Spam:** each form carries a hidden `website` field (a honeypot, as on the old site): a
bot that fills it gets the same 200 and nothing is saved. Forms are also rate-limited per
IP (10 per 10 minutes by default). There is no CAPTCHA.

## Admin accounts

There is no public sign-up; `/auth/register` answers 404. Admins arrive by invitation:
an admin invites an address, the person gets a link valid for 7 days, opens it in the
web client (`/admin/invitatie?token=…`) and chooses a password. Only the token's SHA-256
is stored.

**The first admin:** put the address in `Bootstrap:AdminEmails` and start the API. An
address without an account gets an invitation, emailed and also written to the log, so
the first install works before SMTP is set up:

```bash
Bootstrap__AdminEmails__0=you@example.com dotnet run --project src/Speranta.Api
```

Sign-in is ASP.NET Identity's bearer tokens under `/auth` (`login`, `refresh`,
`forgotPassword`, `resetPassword`). Passwords need 10 characters, no symbol rules.
Removing an admin deletes the account, and every admin request checks the account still
exists, so it takes effect at once rather than when their token expires.

## Running it

```bash
docker compose up -d                 # Postgres on localhost:5434
dotnet tool restore                  # pins dotnet-ef
dotnet dotnet-ef database update -p src/Speranta.Infrastructure -s src/Speranta.Api
dotnet run --project src/Speranta.Api   # http://localhost:5280
```

Port 5434 so it can run beside Amicus (5433) and VerseMate (5432). `GET /health`
round-trips the database. OpenAPI is at `/openapi/v1.json` in Development.

A fresh database starts with what the live site showed on 2026-11-08: the six books,
the service times, the two kids' groups and the kids' phone number.

## Configuration

| Key | Meaning |
|---|---|
| `ConnectionStrings:Postgres` | required |
| `Email:Host`, `Port`, `User`, `Password` | SMTP; for Gmail use an app password. Empty = log instead of send |
| `Email:NotifyTo` | where new submissions are announced |
| `Email:WebUrl` | the web client's base URL, for links in emails |
| `Storage:Path` | folder for uploads (flyers, covers); relative to the content root |
| `DataProtection:KeyPath` | persist token keys, or every restart signs admins out |
| `Cors:Origins`, `Cors:OriginSuffixes` | browser origins allowed to call the API |
| `Bootstrap:AdminEmails` | see above |
| `RateLimits:*` | per-IP limits: global, auth, forms |

## Database

Code-first EF Core migrations, committed under `src/Speranta.Infrastructure/Migrations`.
Change an entity, then:

```bash
dotnet dotnet-ef migrations add <Name> -p src/Speranta.Infrastructure -s src/Speranta.Api
```

Read what EF generated before committing; never edit a migration that has run anywhere.
Migrations are not applied at startup. CI fails if the model has changes with no migration.

## Tests

```bash
docker compose up -d
dotnet test
```

The integration tests host the real app against a real Postgres (`speranta_test`, created
on first run). `SPERANTA_TEST_POSTGRES` overrides the server connection string, as CI does.

## Layout

```
src/Speranta.Domain          entities and enums. No EF, no web.
src/Speranta.Infrastructure  DbContext, migrations, Identity types.
src/Speranta.Api             host, endpoints, mail, file storage.
tests/Speranta.Api.Tests     integration tests (real Postgres) and a few unit tests.
```
