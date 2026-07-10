# Final Verification Record

This document records what was actually built, run, and observed during the
secure-image phase completion and final acceptance. It distinguishes checks that
were **performed** from checks that were **not performed**. Nothing here is
assumed.

- **Date:** 2026-07-11
- **Host:** macOS (Apple Silicon), .NET SDK 8.0.422
- **Repository branch:** `main`
- **Local run URL used for verification:** `http://127.0.0.1:5080`

---

## 1. Build

| Check | Result |
|-------|--------|
| `dotnet build --configuration Debug` | **Succeeded**, 0 errors |
| `dotnet build --configuration Release` | **Succeeded**, 0 errors |
| Warnings | Only `NU1900` (NuGet vulnerability-data fetch failed in the offline build host); no compiler warnings |

## 2. Tests

Command: `dotnet test --configuration Release`

| Metric | Count |
|--------|-------|
| Passed | **106** |
| Failed | **0** |
| Skipped | **0** |

Focused secure-image tests (`ProductImageServiceTests`): 15 passed.
Admin-service tests including upload persistence/compensation: passed.
The suite uses real SQLite (in-memory and temporary file) and
`WebApplicationFactory`; EF Core's in-memory provider is not used to assert
transaction/concurrency behavior.

## 3. Migration

| Check | Result |
|-------|--------|
| `dotnet ef database update --project src/ECommerceStore.Web` (existing dev DB) | Applied; "already up to date" on re-run |
| Fresh migration against a disposable SQLite database | Applied `20260710102327_InitialCreate`; **17 tables** created |
| `Products` table has `ImageKind` + `ImageLocation` columns | Confirmed |
| Separate invoice table | Absent by design (invoices regenerate from snapshots) |

## 4. Seed and startup

| Check | Result |
|-------|--------|
| App startup against the migrated dev database | Listens on the configured URL; no unhandled exception in logs |
| Startup against a fresh disposable database | Seeded and served pages |
| Seeded products (fresh DB) | **12** |
| Development administrator seeded (fresh DB) | 1 (`admin@localstore.test`) |
| Idempotency | Seeding re-run is safe (guarded by existence checks) |
| Log inspection during runtime checks | No hidden exception; only the benign "Failed to determine the https port" warning when bound to HTTP only |

## 5. Customer workflows

| Workflow | How verified | Result |
|----------|--------------|--------|
| Storefront home | Browser (1440×900) | Custom design renders; featured/latest/categories present |
| Product listing + sort | Browser | Renders; sort control present with `<noscript>` fallback |
| Product detail | Browser | Renders; placeholder for image-less products |
| Custom 404 | Browser (`/this-page-does-not-exist`) | Branded "Page not found" page, HTTP 404 |
| Registration | Scripted HTTP (smoke) | Creates a Customer account |
| Cart, checkout, orders, invoice, ownership (IDOR) | Automated tests (`CartServiceTests`, `CheckoutServiceTests`, `OrderQueryServiceTests`, `InvoiceServiceTests`) | Pass |

> Note: the cart→checkout→order→invoice purchase path was verified through the
> automated test suite rather than a manual browser click-through.

## 6. Administrator workflows

| Workflow | How verified | Result |
|----------|--------------|--------|
| Anonymous → admin route | Scripted HTTP + browser | Redirected to `/account/login` |
| Customer → admin route | Scripted HTTP | Redirected to `/account/access-denied` |
| Administrator login | Browser | Succeeds; admin layout renders |
| Product create form | Browser | Renders with upload field and AI draft button |
| AI "Draft description" | Browser + scripted HTTP | Deterministic mock draft fills the editable field; nothing auto-saved |
| Orders list (tablet) | Browser (768×1024) | Renders; nav layout fixed during this pass |
| Product CRUD / transitions / guards / customer disable | Automated tests (`AdminServiceTests`, `AdminAuthorizationTests`) | Pass |

## 7. Upload workflows (runtime smoke, 17 checks, all passed)

Performed with an authenticated admin session against the running app, under the
strict CSP:

- Valid PNG upload accepted; product created.
- Exactly one new managed file created with a random 32-hex name (client filename
  not reused).
- Uploaded image served publicly with an `image/*` content type.
- Admin listing reveals no absolute filesystem path.
- Invalid bytes rejected; previous managed image preserved.
- Valid replacement accepted; new file created; previous file removed after
  success; unrelated marker file left untouched.
- Fallback placeholder asset served.
- Anonymous upload POST denied; authenticated customer denied admin create.

The same 17-check script passed both before and after the CSP/security-header
change.

## 8. Responsive checks (performed live in a browser)

| Viewport | Pages checked | Result |
|----------|---------------|--------|
| 1440×900 | Home, catalog, product detail, login, 404 | Correct |
| 768×1024 | Admin orders | Correct after the tablet nav fix committed in this pass |
| 390×844  | Catalog + mobile nav toggle (Bootstrap collapse under CSP) | Correct; menu expands/collapses |

## 9. Accessibility checks (performed)

A DOM-based accessibility audit was run via the browser on the catalog and
product-detail pages. Findings:

- `<html lang="en">` present; skip link present; `main`/`nav` landmarks present.
- All images have `alt`; all form controls have an accessible name (label,
  `aria-label`, or wrapping label).
- Exactly one `<h1>` per page; no skipped heading levels.
- `:focus` styles are present in the stylesheet (keyboard visibility).

> This was a DOM-property audit, **not** a third-party automated scanner run
> (axe/Lighthouse). A CDN-hosted scanner cannot be injected because the app's own
> Content-Security-Policy blocks external scripts and the build host is offline.

## 10. Security headers (verified via response inspection)

Present on both dynamic pages and static files:

- `Content-Security-Policy` (`script-src 'self'`, `img-src 'self' https: data:`,
  `object-src 'none'`, `frame-ancestors 'none'`, …)
- `X-Content-Type-Options: nosniff`
- `X-Frame-Options: DENY`
- `Referrer-Policy: strict-origin-when-cross-origin`

No CSP console violations were observed on the storefront, catalog, mobile nav,
or admin product form.

## 11. Known warnings

- `NU1900` restore/build warnings from the offline NuGet vulnerability service.
- "Failed to determine the https port for redirect" when the app is bound only to
  an HTTP URL.

## 12. Known limitations

- Payment and AI are deterministic mocks; no real provider is integrated.
- Docker is not required or provided.

## 13. Checks not performed

- **Windows execution.** All builds/tests/migrations/runtime checks above were
  performed on macOS (Apple Silicon). The Windows commands in the README are
  provided for portability but were not executed in this environment.
- **Third-party automated accessibility scan** (axe/Lighthouse) — see §9.
- **Manual browser click-through of the full purchase path** — covered by the
  automated test suite instead (see §5).
