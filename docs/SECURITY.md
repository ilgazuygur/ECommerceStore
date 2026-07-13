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
| 24 | Cross-user chat access (IDOR) | Every conversation/message/request query includes the authenticated user ownership predicate; foreign IDs return a non-disclosing 404 | `AssistantController`; `ShoppingAssistantService`; `AssistantApiTests`, `ShoppingAssistantServiceTests` |
| 25 | Duplicate assistant work/replies | Unique `(ConversationId, ClientRequestId)` request row, replay of completed responses, fresh-processing 202, and failed/stale atomic retries | `AssistantRequestConfiguration`; `ShoppingAssistantService`; assistant idempotency tests |
| 26 | Stale worker overwrites a newer assistant attempt | Every processing transition gets a new `ProcessingAttemptId`; completion/failure conditionally updates only the matching processing owner, with reply/products/state in one transaction | `ShoppingAssistantService`; `Stale_worker_cannot_complete_after_new_attempt_takes_ownership` |
| 27 | Duplicate/out-of-order chat sequences | Conditional `LastSequence` optimistic allocation plus unique `(ConversationId, Sequence)` database invariant; no `MAX + 1` | `ChatConversationConfiguration`; assistant concurrency tests |
| 28 | Prompt injection / unrestricted data access | Read-only allow-listed tools with typed bounded arguments; public catalogue service only; system instruction treats catalogue descriptions as untrusted; no SQL, order, user, admin, or mutation tool | `ProductAssistantTools`; `ShoppingAssistantService`; tool tests |
| 29 | Hallucinated or hidden product cards | Cards are created only from executed tool results and live public records; history re-resolves active products and returns only a safe unavailable name snapshot otherwise | `ProductQueryService`; `ShoppingAssistantService`; grounding/history tests |
| 30 | Assistant XSS | Provider/database text is rendered through `textContent` and created DOM nodes; no `innerHTML`; existing strict same-origin script CSP remains active | `wwwroot/js/assistant.js`; `Program.cs`; browser console verification |
| 31 | Assistant CSRF / API redirect confusion | Global anti-forgery remains active and JavaScript sends `RequestVerificationToken`; assistant cookie challenges return RFC 7807 JSON 401/403 while MVC redirects remain unchanged | `Program.cs`; `EnabledUserCookieEvents`; `AssistantApiTests` |
| 32 | Assistant abuse / resource exhaustion | Per-user fixed-window rate limit, prompt/result/history/tool-loop limits, provider timeout, cancellation, and bounded concurrency retries | `Program.cs`; `AssistantOptions`; orchestration/provider tests |
| 33 | AI credential leakage | Key exists only in options populated from user secrets/environment; response bodies and keys are excluded from exceptions/logs/tests | `OpenAiCompatibleAssistantClient`; `AssistantProviderTests` |
| 34 | Foreign-currency price misrepresentation | Explicit USD/TRY/EUR token and locale-separator parser; foreign requests short-circuit; no conversion or TL-to-USD reinterpretation | `CurrencyAmountParser`; assistant currency tests |

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
`wwwroot/js/site.js` / `wwwroot/js/assistant.js` and uses `data-*` attributes, so
no inline script or inline event handler exists. `style-src` allows inline styles because Bootstrap sets
element styles at runtime; this is a lower-risk allowance than inline scripts.
`img-src` permits external HTTPS images because products may reference an external
image URL.

## Secrets handling

- No API keys, SMTP passwords, tokens, or private keys are committed.
- The web project defines a `UserSecretsId`; store the SMTP password with
  `dotnet user-secrets set "Smtp:Password" "…"` or the `Smtp__Password`
  environment variable.
- A real shopping-assistant key uses `Assistant:ApiKey` in user secrets or
  `Assistant__ApiKey` in the environment. `appsettings.json` intentionally has no
  key property/value.
- SQLite databases, generated emails, generated PDFs, and runtime-uploaded images
  are git-ignored.
