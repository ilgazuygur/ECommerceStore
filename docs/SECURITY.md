# Security Overview and Threat-Control Table

This document maps the application's threats to the controls that mitigate them
and the code/test evidence for each. It complements the security summary in the
[README](../README.md).

## Threat-control table

| # | Threat | Control | Evidence |
|---|--------|---------|----------|
| 1 | Cross-site request forgery on state-changing actions | Global `AutoValidateAntiforgeryTokenAttribute`; forms use the anti-forgery tag helper; AI draft fetch sends the token | `Program.cs`; `AdminAuthorizationTests` |
| 2 | Unauthorized admin access | Area-wide `[Authorize(Roles = Administrator)]` on `AdminControllerBase` + `AdminAreaAuthorizationConvention` | `Areas/Admin/Controllers/AdminControllerBase.cs`; `AdminAuthorizationTests`; runtime smoke (anonymous→login, customer→access-denied) |
| 3 | Order/invoice IDOR | Owner-scoped queries; non-owner receives a non-disclosing 404 | `Services/Orders/*`; `OrderQueryServiceTests`, `InvoiceServiceTests` |
| 4 | Mass assignment / over-posting | Dedicated per-action input models with explicit mapping | `ViewModels/Admin/AdminViewModels.cs`; `AdminProductService.Apply` |
| 5 | Open redirect on login return URL | Local-URL-only return handling | `Controllers/AccountController.cs` |
| 6 | Stored/reflected XSS | Razor output encoding by default; strict CSP with `script-src 'self'` (no inline script); email/PDF built from encoded snapshot values | `Program.cs` (CSP middleware); `wwwroot/js/site.js`; all views |
| 7 | Clickjacking | `X-Frame-Options: DENY` + CSP `frame-ancestors 'none'` | `Program.cs` |
| 8 | MIME sniffing | `X-Content-Type-Options: nosniff` | `Program.cs` |
| 9 | SQL injection | Parameterized EF Core LINQ; no raw SQL string concatenation | `Services/**` |
| 10 | Sort/filter injection | Sorting bound to an allow-listed enum (`ProductSort`) | `Services/Catalog/ProductQueryService.cs`; `ProductQueryServiceTests` |
| 11 | Malicious file upload (web shell, polyglot, bomb) | Extension + MIME + independently decoded format must agree; byte/width/height/pixel limits; trailing-data rejection; re-encode to canonical raster; random name; create-new write inside contained root | `Services/Images/ProductImageService.cs`; `ProductImageServiceTests`; runtime upload smoke |
| 12 | Path traversal on upload/delete | Filename validation; `Path.GetFullPath` containment checks; managed-filename regex on delete | `Services/Images/ProductImageService.cs`; `ProductImageServiceTests` |
| 13 | Orphaned/unsafe image references on failure | Compensation: delete new file on DB/concurrency failure; delete old file only after successful replacement and only if unreferenced | `Services/Admin/AdminProductService.cs`; `AdminServiceTests` |
| 14 | SSRF / credential leak via external image URL | External URLs must be absolute HTTPS, credential-free, length-bounded; rendered via `img-src 'self' https:` | `AdminProductService.Validate`; `AdminServiceTests` |
| 15 | Payment data exposure | No PAN/CVV/expiry stored or logged; only sanitized result + non-sensitive metadata | `Services/Payments/*`; `FakePaymentServiceTests`; checkout tests |
| 16 | Checkout replay / double charge | Per-user idempotency key; duplicate submit returns the original order | `Services/Checkout/*`; `CheckoutServiceTests` |
| 17 | Stock oversell under concurrency | Atomic transactional decrement; optimistic concurrency (`Version`); rollback on failure | `Services/Checkout/*`, `Services/Admin/*`; `CheckoutServiceTests`, `AdminServiceTests` |
| 18 | Total tampering | Order total recomputed server-side at checkout; no client-supplied price trusted | `Services/Checkout/*`; `CartServiceTests` |
| 19 | Session/auth transition abuse | Anonymous cart merges once into the authenticated cart; security-stamp validation rejects disabled sessions | `Services/Cart/CartMergeService.cs`; `Services/Common/EnabledUserCookieEvents.cs`; `CartServiceTests`, `AdminServiceTests` |
| 20 | Information disclosure via errors | Production exception handler shows a friendly page with a correlation reference and no diagnostics; custom 404/access-denied | `Program.cs`; `Controllers/HomeController.cs`; `Views/Home/*`, `Views/Account/AccessDenied.cshtml` |
| 21 | Secret leakage | No secrets in source/config; SMTP password via user secrets or environment variables | `appsettings.json` (empty SMTP); `.gitignore` |
| 22 | Insecure cookies in Production | `HttpOnly`, `SameSite=Lax`, `SecurePolicy=Always` in Production; HSTS + HTTPS redirection | `Program.cs` |
| 23 | Email/PDF failure corrupting orders | Side effects run after commit and are isolated; a failure never rolls back a committed order | `Services/Orders/OrderConfirmationDispatcher.cs`; `OrderConfirmationDispatcherTests`, `EmailServiceTests` |

## Content-Security-Policy

```
default-src 'self';
script-src 'self';
style-src 'self' 'unsafe-inline';
img-src 'self' https: data:;
font-src 'self';
connect-src 'self';
form-action 'self';
base-uri 'self';
object-src 'none';
frame-ancestors 'none'
```

`script-src` intentionally omits `'unsafe-inline'`: all behavior lives in
`wwwroot/js/site.js` and uses `data-*` attributes, so no inline script or inline
event handler exists. `style-src` allows inline styles because Bootstrap sets
element styles at runtime; this is a lower-risk allowance than inline scripts.
`img-src` permits external HTTPS images because products may reference an external
image URL.

## Secrets handling

- No API keys, SMTP passwords, tokens, or private keys are committed.
- The web project defines a `UserSecretsId`; store the SMTP password with
  `dotnet user-secrets set "Smtp:Password" "…"` or the `Smtp__Password`
  environment variable.
- SQLite databases, generated emails, generated PDFs, and runtime-uploaded images
  are git-ignored.
