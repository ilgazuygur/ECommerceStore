# ECommerceStore

A secure, server-rendered e-commerce application built with ASP.NET Core MVC on
.NET 8. It ships a complete storefront, customer accounts, a hybrid shopping
cart, a transactional fake checkout, PDF invoices, order-confirmation email with
a Development file fallback, a full administrator management area, secure product
image uploads, and a provider-independent AI seam backed by a deterministic mock.

The whole application runs locally with **no Docker, no external database, no
paid services, and no real payment or AI provider**.

---

## Table of contents

- [Main features](#main-features)
- [Technology stack](#technology-stack)
- [Solution structure](#solution-structure)
- [Prerequisites](#prerequisites)
- [Quick start](#quick-start)
- [Fresh-clone setup (macOS and Windows)](#fresh-clone-setup)
- [Development administrator and test cards](#development-administrator-and-test-cards)
- [Configuration, secrets, and SMTP](#configuration-secrets-and-smtp)
- [Database and migrations](#database-and-migrations)
- [Secure product image handling](#secure-product-image-handling)
- [Checkout, orders, invoices, and email](#checkout-orders-invoices-and-email)
- [AI architecture](#ai-architecture)
- [Security overview](#security-overview)
- [Testing](#testing)
- [Troubleshooting](#troubleshooting)
- [Known warnings and limitations](#known-warnings-and-limitations)
- [Planning documents](#planning-documents)

---

## Main features

### Customer features

- Home page with featured and latest products, category strip, and value band.
- Product listing with keyword search, category filter, minimum/maximum price,
  in-stock filter, sorting, pagination, and query-string preservation.
- Product detail pages with related products, stock status, and pricing.
- Registration, login, logout, and disabled-account enforcement with safe
  return URLs and anti-forgery protection.
- Hybrid cart: an anonymous session cart that merges into the persistent
  authenticated cart on login/registration; add, update, increment, decrement,
  remove, and clear; server-side totals with USD 10.00 shipping on a non-empty
  cart; stock and inactive-product validation.
- Authenticated checkout with a dummy payment form, idempotent duplicate-submit
  protection, atomic stock decrement, and order/price/address snapshots.
- "My orders" list and order details, scoped to the owner (IDOR-protected).
- Downloadable PDF invoices regenerated deterministically from immutable order
  snapshots.
- Friendly empty states, validation messages, and success/failure feedback.
- Missing product images fall back to a bundled placeholder.

### Administrator features

- Separate admin layout and navigation behind Administrator-role authorization.
- Dashboard metrics (products, orders, revenue, customers).
- Product management: create/edit/activate/deactivate/delete with slug
  uniqueness, price/stock validation, optimistic concurrency, and safe
  deactivation of products referenced by orders.
- Category management with unique name/slug handling and referenced-delete
  protection.
- Order management: search, status filter, and a concurrency-checked status
  transition table that never mutates payment state.
- Customer management: search, profile, order history, and disable/re-enable
  with security-stamp rotation, self-disable protection, and last-enabled-
  administrator protection.
- Secure product image uploads (see below).
- Optional admin-only AI "Draft product description" that returns editable,
  unsaved text.

---

## Technology stack

| Concern              | Technology                                   |
|----------------------|----------------------------------------------|
| Language / runtime   | C# on .NET 8                                  |
| Web framework        | ASP.NET Core MVC (server-rendered Razor)      |
| Authentication       | ASP.NET Core Identity                         |
| Data access          | Entity Framework Core 8                        |
| Database             | SQLite (file-based, local)                    |
| UI                   | Bootstrap 5 + a custom CSS design system      |
| PDF invoices         | QuestPDF (Community license)                  |
| Email transport      | MailKit (SMTP) with a Development file fallback |
| Image processing     | SixLabors.ImageSharp                          |
| Testing              | xUnit with real SQLite integration tests      |

JavaScript is intentionally minimal: only the Bootstrap bundle, jQuery
validation, and a small progressive-enhancement `site.js`. There is no SPA
framework.

---

## Solution structure

```
ECommerceStore.sln
├── src/
│   └── ECommerceStore.Web/            ASP.NET Core MVC application
│       ├── Areas/Admin/              Administrator area (controllers + views)
│       ├── Controllers/              Storefront, account, cart, checkout, orders
│       ├── Data/                     DbContext, migrations, seed initializer
│       ├── Models/                   Catalog, identity, and order entities
│       ├── Services/                 Catalog, cart, checkout, orders, email,
│       │                             invoices, images, AI, admin services
│       ├── ViewModels/               Dedicated input/output models
│       ├── Views/                    Razor views and shared layout
│       └── wwwroot/                  Static assets (css, js, lib, images)
└── tests/
    └── ECommerceStore.Tests/         xUnit unit and integration tests
```

Two projects only: a single web application and a single test project. Controllers
are thin; business rules live in services.

---

## Prerequisites

- **.NET SDK 8.0** (the repository pins `8.0.422` in `global.json` with
  `rollForward: latestPatch`; any 8.0.4xx patch works).
- Git.
- No database server, Docker, or other services are required.

Verify the SDK:

```bash
dotnet --version
```

---

## Quick start

From the repository root:

```bash
# 1. Restore the local EF Core CLI tool (dotnet-ef)
dotnet tool restore

# 2. Restore NuGet dependencies
dotnet restore

# 3. Create/upgrade the SQLite database (migration-first)
dotnet ef database update --project src/ECommerceStore.Web

# 4. Run the application
dotnet run --project src/ECommerceStore.Web
```

The console prints the listening URL (for example `http://localhost:5080` or an
`https://localhost:xxxxx` address, depending on the launch profile). Open it in a
browser. The catalog is seeded automatically on first run.

To run on a fixed URL:

```bash
dotnet run --project src/ECommerceStore.Web --urls "http://localhost:5080"
```

Run the tests:

```bash
dotnet test --configuration Release
```

---

## Fresh-clone setup

The same four commands work on both operating systems from a clean clone. The
application applies migrations explicitly (it does not auto-migrate at startup),
so run the migration command before the first launch.

### macOS (Apple Silicon or Intel)

```bash
git clone <your-repository-url> ECommerceStore
cd ECommerceStore
dotnet tool restore
dotnet restore
dotnet ef database update --project src/ECommerceStore.Web
dotnet run --project src/ECommerceStore.Web
```

### Windows (PowerShell)

```powershell
git clone <your-repository-url> ECommerceStore
cd ECommerceStore
dotnet tool restore
dotnet restore
dotnet ef database update --project src/ECommerceStore.Web
dotnet run --project src/ECommerceStore.Web
```

All filesystem access uses `Path.Combine` and `IWebHostEnvironment`, so paths are
portable across macOS and Windows. There is no LocalDB, Bash, or PowerShell
dependency in application code.

> Cross-platform note: the automated and manual verification recorded in
> [`docs/FINAL_VERIFICATION.md`](docs/FINAL_VERIFICATION.md) was performed on
> macOS (Apple Silicon). The Windows commands above are provided for portability;
> Windows execution has not been performed in this environment.

---

## Development administrator and test cards

In the **Development** environment the seeder creates a development
administrator:

| Field    | Value                     |
|----------|---------------------------|
| Email    | `admin@localstore.test`   |
| Password | `Admin123!`               |

> These credentials exist **only** in Development and must never be used in
> Production. No predictable administrator is seeded in Production.

The dummy checkout accepts standard test card values (any valid future expiry and
any 3-digit CVV):

| Outcome  | Card number             |
|----------|-------------------------|
| Success  | `4242 4242 4242 4242`   |
| Declined | `4000 0000 0000 0002`   |

A declined payment creates no order and leaves the cart and stock unchanged. Full
card numbers and CVVs are never persisted or logged.

---

## Configuration, secrets, and SMTP

Configuration lives in `src/ECommerceStore.Web/appsettings.json`
(`appsettings.Development.json` overrides in Development). Key sections:

- `ConnectionStrings:DefaultConnection` — SQLite data source
  (`Data Source=App_Data/ecommerce.db`, resolved relative to the content root).
- `Store` — store name, currency, shipping fee, low-stock threshold, page sizes.
- `Uploads` — image size/dimension/pixel limits and the relative upload root.
- `AI` — `Enabled` flag and `Provider` (defaults to `Mock`).
- `Smtp` — SMTP host/port/credentials for order and welcome email.

### SMTP setup

By default `Smtp:Host` is empty, so in **Development** the application writes each
outgoing email to a file instead of sending it (see below). To send real email,
configure the SMTP section. **Never commit SMTP passwords.** Use environment
variables or .NET user secrets.

### .NET user secrets

The web project has a `UserSecretsId`, so you can store the SMTP password (and any
other secret) outside source control:

```bash
cd src/ECommerceStore.Web
dotnet user-secrets set "Smtp:Host" "smtp.example.com"
dotnet user-secrets set "Smtp:Username" "apikey"
dotnet user-secrets set "Smtp:Password" "<your-smtp-password>"
```

### Environment variables

Standard ASP.NET Core environment variables apply, for example:

- `ASPNETCORE_ENVIRONMENT` — `Development` (default for `dotnet run`) or
  `Production`.
- `Smtp__Password` — the double-underscore form overrides `Smtp:Password`.
- `ConnectionStrings__DefaultConnection` — overrides the SQLite path.

### Development email output directory

When SMTP is not configured (or fails) in Development, emails are written to:

```
src/ECommerceStore.Web/App_Data/Emails/
```

These files are git-ignored. Registration and checkout still succeed even if
email delivery fails; the failure is logged without leaking secrets.

---

## Database and migrations

- The database is SQLite at `src/ECommerceStore.Web/App_Data/ecommerce.db`
  (git-ignored). The `App_Data` directory is created automatically.
- The schema is defined by EF Core migrations under
  `src/ECommerceStore.Web/Data/Migrations`.
- Apply migrations with:

  ```bash
  dotnet ef database update --project src/ECommerceStore.Web
  ```

- Seeding is idempotent: roles, the Development administrator (Development only),
  and 12+ varied catalog products (featured, low-stock, and out-of-stock) are
  created if missing and are safe to run repeatedly.
- Money is stored and computed with `decimal` (two decimals, `AwayFromZero`
  rounding) — never floating point.

To start from a clean database, stop the app and delete the `App_Data`
`.db`/`.db-*` files, then re-run the migration command.

---

## Secure product image handling

Administrators can attach a product image by uploading a file **or** supplying an
external HTTPS URL. Uploads are validated defensively before anything is stored:

- Allowed types are JPEG, PNG, and WebP only. The extension, the declared MIME
  type, and the **independently decoded** image format must all agree.
- Double extensions, path-separator/traversal filenames, and control characters
  are rejected.
- Byte size, width, height, and total pixel count are bounded (defaults: 5 MiB,
  10000×10000, 40 MP) to resist decompression bombs.
- Files with trailing/polyglot data after the image are rejected.
- Accepted images are **re-encoded** to a canonical raster and written with
  `FileMode.CreateNew` under a random 32-hex filename inside the contained upload
  root (`wwwroot/uploads/products/`). The client filename is never reused.
- On a database or concurrency failure the newly written file is deleted; on a
  successful replacement the previous managed file is removed only after commit
  (and never if another product still references it). Unrelated files are never
  touched.
- External image URLs must be absolute HTTPS, credential-free, and length-bounded.
- Missing or broken images fall back to `/images/product-placeholder.svg`.

Runtime-uploaded images are git-ignored and never committed.

---

## Checkout, orders, invoices, and email

- **Checkout** requires authentication, recomputes the order total server-side,
  and uses a per-user idempotency key so a duplicate submit returns the original
  order instead of charging twice. Stock is decremented atomically inside a
  transaction; a concurrent last-item purchase cannot oversell, and any failure
  rolls back without clearing the cart.
- **Payments** use a deterministic fake service (success/decline test cards
  above). No full card number, CVV, or expiry is ever stored or logged — only a
  sanitized result and non-sensitive metadata.
- **Orders** capture immutable snapshots of product, price, and address at
  purchase time. Order and invoice access is owner-scoped; another customer
  receives a non-disclosing 404.
- **PDF invoices** are generated on demand by QuestPDF from the order snapshots,
  so they regenerate identically and are never stored as public files.
- **Order-confirmation email** is composed after the order commits. In
  Development it is written to the email output directory; a delivery failure is
  isolated and never rolls back a committed order.

---

## AI architecture

AI is an **optional seam**, not a dependency of any core commerce flow:

- `IAIService` is a provider-independent interface. The shipped implementation is
  `MockAIService` — deterministic, offline, and requiring no API key.
- The admin product form has an optional "Draft with AI (editable)" button that
  posts to an admin-only, anti-forgery-protected endpoint and fills the editable
  Full description field. Nothing is saved automatically and no other field is
  changed.
- Disabling AI (`AI:Enabled = false`) hides the button and leaves every required
  commerce flow working unchanged.

### Future AI-provider integration point

To add a real provider, implement a new `IAIService` (for example an OpenAI/Azure
provider that reads its key from user secrets or environment variables — never
source control) and register it in `Program.cs`, selected by `AIServiceOptions.Provider`.
A real provider must additionally address prompt injection, output encoding, rate
limiting, cost controls, timeouts/retries, and the privacy of submitted catalog
text. None of that is required for the mock. This seam is documented in
`Services/AI/AIService.cs`.

---

## Security overview

- **Anti-forgery** is enforced globally on all unsafe (POST) actions via
  `AutoValidateAntiforgeryTokenAttribute`.
- **Authorization**: the admin area requires the Administrator role (enforced by a
  base controller and an area convention); orders and invoices are owner-scoped.
- **Input models** are dedicated per action with explicit mapping to prevent mass
  assignment / over-posting.
- **Output encoding**: Razor encodes output by default; email and PDF content is
  built from encoded/snapshot values.
- **Queries** are parameterized through EF Core; sorting uses an allow-listed enum.
- **Response headers** (set on every response, including static files):
  - `Content-Security-Policy` with `script-src 'self'` (no inline script),
    `img-src 'self' https: data:` (for external product images),
    `object-src 'none'`, and `frame-ancestors 'none'`.
  - `X-Content-Type-Options: nosniff`
  - `X-Frame-Options: DENY`
  - `Referrer-Policy: strict-origin-when-cross-origin`
- **Cookies** are `HttpOnly` with `SameSite=Lax`; in Production they require HTTPS
  (`CookieSecurePolicy.Always`), and HSTS + HTTPS redirection are enabled.
- **Payment privacy**: no PAN/CVV/expiry is stored or logged.
- **Production errors** show a friendly page with a correlation reference and no
  diagnostic detail; custom 404 and access-denied pages are provided.
- **Secrets** are never committed; SMTP passwords use user secrets or environment
  variables.

---

## Testing

- The suite uses **xUnit** with **real SQLite** integration tests (in-memory and
  temporary-file SQLite), `WebApplicationFactory` for HTTP/authorization tests,
  and deterministic fakes for the clock, payment, email, and image services. EF
  Core's in-memory provider is **not** used to assert SQLite transaction or
  concurrency behavior.
- Coverage includes money rounding, catalog queries, cart mutations and merge,
  checkout idempotency/decline/stock-race/rollback, order and invoice ownership,
  PDF generation, email fallback, admin CRUD/transitions/concurrency/guards,
  the AI mock, secure image validation and compensation, seeding idempotency,
  and admin authorization.

Run the full suite:

```bash
dotnet test --configuration Release
```

**Latest result (Release, macOS Apple Silicon): 106 passed, 0 failed, 0 skipped.**

See [`docs/FINAL_VERIFICATION.md`](docs/FINAL_VERIFICATION.md) for the full
build/migration/startup/runtime verification record.

---

## Troubleshooting

- **`no such table` / empty database** — run
  `dotnet ef database update --project src/ECommerceStore.Web` before the first
  launch. Migrations are applied explicitly, not at startup.
- **`dotnet ef` not found** — run `dotnet tool restore` (the EF CLI is a local
  tool pinned in `.config/dotnet-tools.json`).
- **"Failed to determine the https port for redirect" warning** — harmless; it
  appears when you run with only an HTTP URL.
- **HTTPS certificate prompt in Development** — run `dotnet dev-certs https --trust`
  once, or use the `http` launch profile / an explicit `--urls http://...`.
- **Port already in use** — pass a free port with
  `--urls "http://localhost:<port>"`.
- **Emails not arriving** — expected without SMTP configured; check
  `src/ECommerceStore.Web/App_Data/Emails/` in Development.

---

## Known warnings and limitations

- `NU1900` restore warnings ("Error occurred while getting package vulnerability
  data") appear when the build host cannot reach the NuGet vulnerability service.
  They are network-environment warnings only and do not affect the build.
- Payment and AI are intentionally mock implementations; no real provider is
  integrated. See the AI integration point above.
- Docker is **not** required and is not provided; the local path is the supported
  workflow.
- Windows execution was not performed in this environment (see the cross-platform
  note); the code is written to be portable.

---

## Planning documents

The approved planning documents are the permanent source of truth and remain in
the repository root:

- [`REQUIREMENTS.md`](REQUIREMENTS.md)
- [`ARCHITECTURE.md`](ARCHITECTURE.md)
- [`DATABASE_DESIGN.md`](DATABASE_DESIGN.md)
- [`IMPLEMENTATION_PLAN.md`](IMPLEMENTATION_PLAN.md)
- [`TASKS.md`](TASKS.md)
- [`TEST_PLAN.md`](TEST_PLAN.md)

Additional documentation lives in [`docs/`](docs/).
