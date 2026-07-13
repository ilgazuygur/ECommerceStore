# Final Verification Record

This document records what was actually built, run, and observed through the
shopping-assistant integration and final acceptance. It distinguishes checks that
were **performed** from checks that were **not performed**. Nothing here is
assumed.

- **Date:** 2026-07-13
- **Host:** macOS (Apple Silicon), .NET SDK 8.0.422
- **Repository branch:** `feat/shopping-assistant-integration`
- **Local run URL used for verification:** `http://127.0.0.1:5187`

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
| Passed | **161** |
| Failed | **0** |
| Skipped | **0** |

Focused assistant namespace tests: 49 passed; assistant HTTP integration tests: 6 passed.
Focused secure-image tests (`ProductImageServiceTests`): 15 passed.
Admin-service tests including upload persistence/compensation: passed.
The suite uses real SQLite (in-memory and temporary file) and
`WebApplicationFactory`; EF Core's in-memory provider is not used to assert
transaction/concurrency behavior.

## 3. Migration

| Check | Result |
|-------|--------|
| `dotnet ef database update --project src/ECommerceStore.Web` (existing dev DB) | Applied `20260713115617_AddShoppingAssistant` after an online timestamped backup |
| Fresh migration against disposable SQLite databases | Applied initial + assistant migrations through HTTP integration tests |
| Migration against a temporary copy of the existing dev DB | Applied; product/category/user counts stayed **14/4/5**; `PRAGMA foreign_key_check` clean |
| Local pre-migration backup | `App_Data/ecommerce-pre-assistant-20260713-150734.db` (git-ignored) |
| `Products` table has `ImageKind` + `ImageLocation` columns | Confirmed |
| Assistant schema | Conversation/message/product-reference/request tables and indexes confirmed |
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

## 8. Shopping assistant (performed live and automated)

| Workflow | Result |
|----------|--------|
| Anonymous widget | Polished sign-in state; assistant API returns JSON 401 without redirect |
| Authenticated first use | Creates a conversation and persists it per user |
| Combined live filters | “in-stock electronics under $100” returned only the two current in-stock matches and live USD prices |
| Product cards | Server-grounded links, images/placeholders, category, current price, and current stock rendered safely |
| Multi-turn references | “Which of those is cheaper?” reloaded both structured references and selected the current cheaper product |
| Foreign currency | “under 2.000 TL” returned the supported-USD message and did not run a misleading price query |
| Retry/idempotency | Same UUID replay, fresh 202, failed retry, concurrent duplicate, and stale takeover covered by real SQLite tests |
| Stale ownership | Worker A was rejected after Worker B took a new attempt token; exactly one assistant message persisted |
| Independent processing | A blocked provider in one conversation did not hold a database transaction or block another conversation |
| Hidden product history | Inactive product details were removed and replaced with a non-linking unavailable snapshot |
| Security API behavior | Ownership 404, antiforgery 400, JSON 401, prompt/tool bounds, and per-user limiter verified |

The deterministic mock required no external key. The optional OpenAI-compatible
transport was verified with stub HTTP handlers for tool serialization/parsing,
authentication failures, malformed responses, timeouts, caller cancellation, and
key non-disclosure; no live external provider call was made.

## 9. Responsive checks (performed live in a browser)

| Viewport | Pages checked | Result |
|----------|---------------|--------|
| 1440×900 | Home, catalog, product detail, login, 404 | Correct |
| 768×1024 | Admin orders | Correct after the tablet nav fix committed in this pass |
| 390×844  | Catalog + mobile nav toggle (Bootstrap collapse under CSP) | Correct; menu expands/collapses |
| Default desktop | Assistant popup, long history, cards, focus behavior | Header and composer remain fixed; message pane alone scrolls |
| 390×844 | Assistant bottom drawer | Horizontal conversation history, body lock, scrollable messages, visible composer, product cards |

Assistant keyboard checks: focus moves into the dialog, Tab is trapped, Enter
sends, Shift+Enter remains available for multiline input, Escape closes, focus
returns to the launcher, and the mobile body lock is removed.

## 10. Accessibility checks (performed)

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

## 11. Security headers (verified via response inspection)

Present on both dynamic pages and static files:

- `Content-Security-Policy` (`script-src 'self'`, `img-src 'self' https: data:`,
  `object-src 'none'`, `frame-ancestors 'none'`, …)
- `X-Content-Type-Options: nosniff`
- `X-Frame-Options: DENY`
- `Referrer-Policy: strict-origin-when-cross-origin`

No CSP console violations were observed on the storefront, assistant desktop or
mobile UI, login/validation page, or admin product form. The final browser console
was empty after correcting the validation partial's jQuery load order.

## 12. Known warnings

- `NU1900` restore/build warnings from the offline NuGet vulnerability service.
- "Failed to determine the https port for redirect" when the app is bound only to
  an HTTP URL.

## 13. Known limitations

- Payment is deterministic/fake. The assistant defaults to its deterministic
  mock. Its real OpenAI-compatible transport is implemented and stub-tested but
  was not exercised against an external paid provider.
- The assistant has bounded recent context and no access to orders or customer
  data. It does not perform currency conversion.
- Docker is not required or provided.

## 14. Checks not performed

- **Windows execution.** All builds/tests/migrations/runtime checks above were
  performed on macOS (Apple Silicon). The Windows commands in the README are
  provided for portability but were not executed in this environment.
- **Third-party automated accessibility scan** (axe/Lighthouse) — see §9.
- **Manual browser click-through of the full purchase path** — covered by the
  automated test suite instead (see §5).
