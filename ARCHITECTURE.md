# ECommerceStore Architecture

## 1. Status, scope, and design principles

This is a planning artifact. It does not authorize implementation. The design targets a dependable teaching-quality production shape: conventional ASP.NET Core MVC, explicit services, EF Core, and one relational database. It intentionally avoids microservices, CQRS/mediator frameworks, generic repositories, event buses, and extra class-library boundaries that would add ceremony without current value.

Principles:

1. Server-rendered MVC and progressive enhancement.
2. Small controllers; business and authorization rules in focused services.
3. Dedicated input/output view models; entities never used as form models.
4. Server-authoritative prices, stock, identity, ownership, and totals.
5. One database transaction for all core checkout state; email/PDF after commit.
6. Cross-platform APIs and SQLite-compatible concurrency techniques.
7. Safe, observable failure without leaking sensitive data.
8. No required network service beyond optional SMTP.

## 2. Technology baseline

| Concern | Choice | Reason |
|---|---|---|
| Runtime | .NET 8 / ASP.NET Core 8 | Required, LTS, installed locally |
| Web | ASP.NET Core MVC with Razor views | Required; beginner-readable server rendering |
| Persistence | EF Core 8 + SQLite | Required; Docker-free, cross-platform |
| Identity | ASP.NET Core Identity with roles | Required; mature hashing/cookie/role support |
| UI | Bootstrap 5 + small JavaScript modules | Required; responsive without SPA complexity |
| Email | MailKit behind `IEmailService` | SMTP-compatible and cross-platform |
| PDF | QuestPDF behind `IInvoicePdfService` | Code-first, deterministic, cross-platform |
| Tests | xUnit + ASP.NET Core integration test support | Required |

Package versions will be selected only after approval. The implementation must confirm that each version supports `net8.0`, macOS ARM64, and Windows. No package installation occurs in planning.

## 3. Proposed solution structure

Use two projects—one application, one test project—to keep boundaries visible without enterprise-level fragmentation.

```text
ECommerceStore.sln
src/
  ECommerceStore.Web/
    Areas/
      Admin/
        Controllers/
        ViewModels/
        Views/
    Controllers/
    Data/
      Configurations/
      Migrations/
      Seed/
    Models/
      Identity/
      Catalog/
      Cart/
      Orders/
    ViewModels/
      Account/
      Catalog/
      Cart/
      Checkout/
      Orders/
    Services/
      Catalog/
      Cart/
      Orders/
      Payments/
      Email/
      Pdf/
      Images/
      AI/
      Common/
    Views/
      Shared/
    wwwroot/
      css/
      js/
      images/
      uploads/products/
    App_Data/
      Emails/                 # runtime-generated, later ignored by Git
      ecommerce.db            # runtime-generated, later ignored by Git
tests/
  ECommerceStore.Tests/
    Unit/
    Integration/
    Web/
    Security/
    TestInfrastructure/
```

`App_Data` runtime artifacts and uploaded development images are addressed by the later `.gitignore`; `.gitignore` itself is not created during planning.

Rejected alternatives:

- **One project per layer**: stronger compile-time boundaries but excessive navigation/boilerplate at this size. Reconsider only if the domain grows substantially.
- **Generic repository/unit-of-work wrappers**: duplicate EF Core and obscure transactions/query capabilities. Services use the typed `ApplicationDbContext` directly.
- **Minimal APIs/SPA**: conflict with the required MVC approach and add client/server state complexity.

## 4. Responsibilities and dependency rules

### 4.1 Controllers

Controllers perform HTTP concerns only: bind a dedicated view model, verify model state, obtain current user ID, call one service operation, translate its typed result into view/redirect/status/TempData, and never calculate totals or directly mutate unrelated entities. POST actions use anti-forgery. Controllers do not accept `UserId`, roles, prices, totals, stock, payment result, or order ownership from clients.

### 4.2 Services

- `IProductQueryService`: public search/filter/sort/pagination and product-detail projections.
- `ICartService`: anonymous/database cart reads and mutations, price/stock validation, totals.
- `ICartMergeService`: transactional session-to-user cart merge.
- `ICheckoutService`: idempotency, fake payment coordination, stock claims, order snapshots, transaction.
- `IOrderQueryService`: owner-scoped customer order queries and admin projections.
- `IOrderStatusService`: validates/administers status transitions with concurrency checks.
- `IPaymentService`: request-scoped fake-card validation returning sanitized results.
- `IEmailService`: sends an already-rendered email message and returns a typed outcome.
- `IEmailComposer`: renders welcome/order subjects, text, and HTML from safe models.
- `IInvoicePdfService`: produces PDF bytes/stream from immutable invoice model.
- `IProductImageService`: validates, saves, replaces, and safely deletes product images.
- `IAIService`: optional provider-independent draft generation.
- `IClock` and number generator abstractions: deterministic tests for time/numbers.

Service results are explicit (success, validation problem, not found, forbidden where disclosure is safe, conflict, retryable side-effect failure). Expected business failures are not exception-driven.

### 4.3 Entities and EF configuration

Entities express persistent state and small invariants. Fluent configurations define lengths, indexes, precision, relationships, delete behavior, and concurrency. Entities are not returned directly to Razor views and are not bound from requests.

### 4.4 View models

Input models contain only editable fields plus validation. Display models are immutable projections shaped for a view. Checkout card properties are kept only in a POST input model, annotated to avoid accidental logging, never copied to entities, and removed from `ModelState` before a failed view is rendered.

### 4.5 Dependency direction

Controllers → interfaces/services → EF Core/infrastructure implementations → database/files/SMTP. Razor views depend only on view models and tag helpers. Services may share small pure calculators/policies but do not call controllers or views.

## 5. Storefront request and data flows

### 5.1 Catalog discovery

`CatalogController` creates a normalized `ProductQuery` from allow-listed query parameters. `IProductQueryService` builds one `AsNoTracking` EF query: active product and active category predicate, optional keyword/category/price/stock predicates, selected ordering plus `Id` as deterministic tiebreaker, count, then `Skip/Take`. It projects directly to cards and returns normalized filter metadata for URL generation.

SQLite default `LIKE`/`NOCASE` behavior is reliable mainly for ASCII. The initial keyword implementation uses parameterized `EF.Functions.Like` over name, short description, and full description; `%`, `_`, and the escape character in user input are escaped so the keyword is literal. Product/category name columns use `NOCASE` for the requested A–Z ordering. Full-text search and culture-aware Unicode collation are future scope and the README later states the basic search limitation.

### 5.2 Product detail

Lookup uses normalized slug and requires active product/category. Related products are active, same-category, exclude current ID, and capped at four. Pricing is projected through one shared `PricingPolicy`, and out-of-stock state disables POST UI while the POST independently validates stock.

### 5.3 Navigation cart count

A lightweight view component calls `ICartService.GetCountAsync` for session or authenticated database cart. Count is total valid units. It must avoid loading full product descriptions/images and degrade to zero if corrupt anonymous session data is repaired.

## 6. Identity, authentication, and authorization

### 6.1 Identity model

`ApplicationUser : IdentityUser` adds `FirstName`, `LastName`, `IsEnabled`, `CreatedAtUtc`, and `UpdatedAtUtc`. Identity owns email normalization, unique-email enforcement, password hashes, security stamps, and lockout properties. Public registration always receives the `Customer` role; no public input can choose a role.

Password policy will use Identity defaults as a minimum (length 8, upper/lower/digit/non-alphanumeric) and friendly guidance. Email confirmation is not a login prerequisite because it was not requested; this can be future scope.

### 6.2 Registration and email

Registration flow:

1. Validate dedicated input view model and local return URL.
2. Begin a short database transaction on the same scoped `ApplicationDbContext` used by the Identity store.
3. Use `UserManager.CreateAsync`, add the `Customer` role, and commit only after both succeed; roll back on either failure.
4. Sign in the enabled user after commit.
5. Merge anonymous cart transactionally in its separate operation so a cart problem cannot undo a valid account.
6. Attempt welcome email after account success; record/log delivery result without rolling back registration.

Cart-merge warnings (inactive/stock-capped lines) are friendly and non-sensitive.

### 6.3 Disable/re-enable

Use explicit `IsEnabled`, not lockout alone (lockout may also represent failed-login throttling). Sign-in checks reject disabled accounts. A cookie principal-validation event checks `IsEnabled`; disabling also updates the security stamp, so existing sessions are rejected. Re-enable updates state/stamp but does not auto-login the user. Admins cannot disable their own current account or the last enabled administrator through customer-management actions.

### 6.4 Roles, policies, and resource ownership

- Roles: `Customer`, `Administrator`.
- Admin area convention: require authenticated `Administrator` for all controllers; explicit `[Authorize(Roles = "Administrator")]` remains on the area base/controller for defense in depth.
- Checkout/order area: authenticated users; customer query methods always accept `currentUserId` and include it in the database predicate.
- Non-owner customer resources return 404 to avoid confirming existence. Admin order services use separate admin methods; there is no Boolean `isAdmin` bypass argument on customer query methods.
- Local redirect validation uses `Url.IsLocalUrl`/framework helpers.

## 7. Customer orders

Customer list/detail queries use `AsNoTracking`, owner predicate, and snapshot projections. Lists paginate and order by `CreatedAtUtc DESC, Id DESC`. Invoice endpoints accept the public order number (or ID) plus current user ID and fetch through `IOrderQueryService.GetInvoiceModelForOwnerAsync`; confirmation uses the same owner-scoped path. Catalog joins are never required to render historical data.

## 8. Product query design

`ProductFilterViewModel` contains keyword, category slug/ID, nullable decimal min/max, nullable `InStockOnly`, allow-listed sort enum/string, and page. Normalization trims keyword, bounds lengths/prices/page, rejects min greater than max, and defaults sort to newest. Page size is configuration-bound with a fixed maximum to prevent expensive queries. Query-string tag helpers retain normalized selections. Effective price is expressed in the database query as discount price when valid, otherwise normal price; invalid discounts are prevented at write time.

## 9. Cart architecture

### 9.1 Alternatives and decision

- **All carts in session**: easy, but authenticated carts disappear across browsers/devices and are less reliable.
- **All carts in database**: durable, but anonymous identity/cleanup becomes more complex.
- **Chosen hybrid**: session DTO for anonymous users; database `Cart`/`CartItem` for authenticated users. It directly satisfies preservation and reliable authenticated storage with modest complexity.

### 9.2 Anonymous cart

Session key stores versioned JSON containing only `{ ProductId, Quantity }`. It has bounded line count and quantity. Session cookie contains only an opaque session ID, is Essential, HTTP-only, SameSite=Lax, Secure=Always outside HTTP Development, and has a documented idle timeout. Deserialization validates schema and bounds; invalid data is discarded/repaired and logged without payload values.

### 9.3 Authenticated cart

One cart per user, one line per product (unique indexes). Cart display always joins current product/category and recalculates prices; it does not promise price reservation. Invalid/inactive lines are removed or marked unavailable before checkout with feedback. Writes use transactions/concurrency where competing tabs could race.

### 9.4 Merge algorithm

After successful login/registration:

1. Read and validate a local copy of anonymous items; if empty, stop.
2. Begin database transaction and get/create the user's cart.
3. Load all referenced active products in one query and existing matching lines.
4. For each valid product, compute `min(existing + anonymous, current stock, 1,000,000 absolute safety bound)` using checked arithmetic; drop invalid/inactive/out-of-stock items and record a safe warning.
5. Upsert lines, update timestamps/version, and commit.
6. Only after commit, clear the session key. If commit fails, preserve it for retry.

Because the session cart is cleared after success and each database line is unique, routine repeated hooks do not double-add. A short-lived session merge marker may prevent multiple auth callbacks in the same response; the database transaction remains authoritative.

### 9.5 Totals

`CartCalculator` receives trusted product price data and quantities and returns list-price subtotal, discount total, net merchandise amount, configured shipping, and grand total. Shipping is zero for an empty cart. The proposed nonempty flat fee is in `StoreOptions`; no tax or free-shipping threshold exists in current scope.

## 10. Checkout, transaction, stock, and idempotency

### 10.1 Checkout token

The GET creates a cryptographically random GUID token and includes it as a hidden value tied to the authenticated user when posted. `CheckoutAttempt` has a unique `(UserId, Token)` index and explicit `Processing`, `Failed`, `Succeeded` state. The token is separate from anti-forgery: anti-forgery prevents cross-site requests; the token prevents replay/double order creation.

### 10.2 Transaction flow

The fake payment is pure in-process and fast, so the current implementation keeps one short SQLite transaction around the authoritative workflow. A future network payment provider must replace this boundary with an outbox/saga-like design; it must never be inserted into this open database transaction.

```mermaid
sequenceDiagram
    participant B as Browser
    participant C as CheckoutController
    participant S as CheckoutService
    participant P as FakePaymentService
    participant D as SQLite
    participant X as Post-commit side effects
    B->>C: POST checkout + anti-forgery + token
    C->>S: User ID, address input, transient card input, token
    S->>D: Begin transaction; claim unique attempt
    S->>D: Reload cart/products; calculate trusted totals
    S->>P: Validate fake card (no logging/storage)
    alt payment fails
        S->>D: Save sanitized failed result/attempt; commit
        S-->>C: Failure; cart and stock unchanged
    else payment succeeds
        loop each item in stable ProductId order
            S->>D: UPDATE stock WHERE active AND stock >= qty
        end
        S->>D: Insert order/items/snapshots/payment
        S->>D: Clear cart; mark attempt succeeded; commit
        S->>X: Generate invoice bytes; send email (best effort)
        S-->>C: Existing/new order confirmation
    end
```

Detailed success transaction:

1. Begin EF transaction (`Serializable`; SQLite write locking plus atomic predicates are relied upon).
2. Insert/claim `CheckoutAttempt`. On unique conflict, load it owner-scoped: return its order if succeeded, return the prior friendly failure if failed, or return “processing—retry shortly” if active.
3. Reload database cart, active products, current stock, current prices, and calculate totals. Never use posted summary/price.
4. Call fake payment with transient card data and immediately discard input. On failure, persist only sanitized `PaymentRecord` tied to the attempt, mark failed, commit; no order/stock/cart mutation.
5. On success, update each product in deterministic ID order using `WHERE IsActive = 1 AND StockQuantity >= requested` and increment version; require one affected row each. Any failure throws a recognized stock conflict and rolls back all decrements.
6. Insert `Order`, immutable `OrderItem` snapshots, successful `PaymentRecord`; clear cart lines; set attempt/order link and `Succeeded`.
7. Save and commit. Database unique indexes are the final defense for token/order/invoice collisions.

### 10.3 Failure and recovery

- Validation/catalog/stock failure: roll back; cart remains; show current availability.
- Fake decline: commit sanitized failure attempt only; stock/cart unchanged; generate a fresh token for a new try.
- Duplicate succeeded token: redirect to the existing owner-scoped confirmation; never repeat stock/email as part of core transaction.
- Duplicate processing token: return 409/friendly processing state. A bounded recovery rule may mark a stale attempt abandoned only because current payment is fake and has no external side effect; this action must be logged and tested.
- Database exception: roll back and show correlation ID; no email/PDF.
- PDF/email failure: order remains committed; see §§12–13.

### 10.4 Order/payment states

On fake success: payment status `Succeeded`; initial order status `Paid`. On fake failure: no order; attempt/payment result `Failed`. `Pending` remains available for deliberate admin workflow/import but is not the normal fake-success initial state.

Allowed order transitions (admin service, concurrency-checked):

| From | Allowed to |
|---|---|
| Pending | Paid, Cancelled |
| Paid | Processing, Cancelled |
| Processing | Shipped, Cancelled |
| Shipped | Delivered |
| Delivered | none |
| Cancelled | none |

`Paid`, `Processing`, `Shipped`, and `Delivered` require `PaymentStatus == Succeeded`; changing order status never silently changes payment status. A paid order that is cancelled keeps successful payment metadata because refunds are out of scope. No automatic refund/restock is implied by cancellation. If cancellation restocking/refunding is later desired, it needs a separately approved, idempotent inventory/financial adjustment design.

## 11. Fake payment architecture

`IPaymentService.ProcessAsync(FakePaymentRequest, CancellationToken)` is implemented by `FakePaymentService`. The request object exists only in controller/service memory. The response contains success flag, sanitized result code/message, generated provider reference, optional brand, optional last four, and timestamp—never full PAN/CVV.

Rules:

- Normalize spaces/hyphens only in memory.
- `4242424242424242` + future MM/YY + exactly three CVV digits → success.
- Proposed `4000000000000002` + otherwise valid expiry/CVV → deterministic decline.
- Other numbers or malformed/expired values → validation/unsupported failure.
- Use `[BindNever]`/redaction conventions where applicable and never structured-log the request.

The interface is intentionally provider-like, but current semantics are explicitly test-only. A real payment integration is rejected for current scope.

## 12. Email architecture

### 12.1 Components

- `IEmailComposer`: produces subject, text body, and encoded/sanitized HTML from a typed model.
- `IEmailTransport`: low-level delivery contract.
- `SmtpEmailTransport`: MailKit implementation using `SmtpOptions`.
- `DevelopmentFileEmailTransport`: writes collision-safe `.eml` or HTML/text artifacts under `App_Data/Emails` using `Path.Combine` and atomic create.
- `IEmailService` / `ResilientEmailService`: orchestration, safe logging, environment fallback, typed `EmailDeliveryResult`; it never makes the calling business action fail.

### 12.2 Environment selection

| Environment/configuration | Behavior |
|---|---|
| Development, SMTP absent | File transport |
| Development, SMTP configured | Try SMTP; on delivery failure save file and log warning |
| Production, valid SMTP | SMTP; on failure return failure and log sanitized error; core action succeeds |
| Production, SMTP absent/invalid | Startup health warning/error log; unavailable transport returns failure; no silent pretend-success and no automatic local PII file |

SMTP host/port/user/from address may be normal configuration; password/secret must use user secrets or environment variables. TLS validation is never disabled. Email templates encode all user/catalog content. Email happens after registration commit or checkout commit; later background queues are optional future work, not required now.

## 13. PDF invoice architecture

`IInvoicePdfService.GenerateAsync(InvoiceModel)` receives a projection composed entirely from order/user/address/item snapshots. QuestPDF produces bytes in memory; `InvoiceNumber` is stored on the order, so repeated generation is data-deterministic even if PDF binary metadata differs. The initial post-commit checkout path attempts generation to satisfy immediate availability and may use bytes as an attachment/link reference. Authorized downloads regenerate on demand.

Chosen regeneration advantages: no stale invoice files, no platform-specific storage management, no public file ACL problem, and history remains reproducible. Rejected stored-file alternative: simpler repeated downloads but introduces cleanup, authorization-at-rest, path, and deployment concerns.

Endpoints fetch owner-scoped/admin-authorized invoice models before generation and return `application/pdf` with a sanitized filename. PDF failures are caught at the boundary, logged with order ID/correlation ID (not address/payment inputs), and return a friendly retry response. The committed order is never rolled back. If exact byte-for-byte legal archival becomes required, immutable stored artifacts and hashes become future scope.

## 14. Image upload architecture

### 14.1 Input paths

Product images are represented by an `ImageKind` plus `ImageLocation`: managed local upload relative path or validated external HTTP(S) URL. Public views resolve through a helper and use a local fallback on missing/error. External URLs permit only absolute `https` by default (`http` allowed only if explicitly approved for Development), have bounded length, and reject credentials, `file:`, `data:`, and script schemes.

### 14.2 Upload validation pipeline

1. Enforce request/form size limits and one file.
2. Normalize claimed extension; allow `.jpg`, `.jpeg`, `.png`, `.webp`; reject SVG.
3. Enforce the configured 5 MiB maximum and nonzero content.
4. Compare allow-listed claimed content type.
5. Read bounded header and validate magic bytes/format structure using a maintained image decoder or strict signature validator chosen during implementation; never rely on one signal.
6. Generate a random filename with server-selected canonical extension.
7. Resolve full destination beneath `wwwroot/uploads/products`; verify it remains under the root.
8. Write with create-new semantics; do not overwrite.
9. After database update succeeds, delete an old managed file only if unreferenced; on database failure, clean up the newly written orphan.

Serving is static content only from a dedicated folder; no execute permission or dynamic processing. Response headers/CSP reduce content-sniffing risk. Admin actions are authorized and anti-forgery protected.

## 15. Administrator-area architecture

`Areas/Admin` has a separate `_AdminLayout`, navigation, view models, and controllers. An area authorization convention plus explicit administrator role attributes protects every route. Admin services project only fields needed by views.

- **Dashboard**: aggregate queries; total customers means users in the Customer role (enabled and disabled), excluding administrator-only accounts. Revenue means `PaymentStatus == Succeeded` regardless of later order cancellation because refunds are out of scope; pending/failed payments are excluded. “Completed or delivered” is displayed as Delivered (label clarified); pending is status Pending; low stock is active product with `0 < stock <= configured threshold` and out-of-stock shown separately or included in a detailed list.
- **Products**: paginated query/edit service, concurrency token hidden field, dedicated create/edit models, image service. Deactivation is normal historical-safe removal. Hard delete only when no order history and after handling cart references.
- **Categories**: paginated list, unique normalized name/slug, restrict deletion when referenced; deactivate instead.
- **Orders**: paginated search by order number/customer email and status; immutable detail projection; status changes only through transition policy with version token.
- **Customers**: paginated safe projection, order link, enabled state actions. Never binds/displays Identity internals. Prevent self/last-admin disable.

All destructive actions are confirmation-page/modal followed by POST—not GET—and return clear conflict feedback.

## 16. UI and accessibility architecture

Use a small custom design layer over Bootstrap variables: color palette meeting contrast, typography scale, spacing tokens, consistent cards/forms/badges, visible focus, and reusable empty/error partials. Razor layouts provide skip link, semantic landmarks, responsive nav, footer, and flash-message region (`role=status/alert` as appropriate).

Product grids use responsive Bootstrap columns and image aspect-ratio handling. Admin tables use responsive wrappers plus mobile-friendly action grouping; essential data is not color-only. Labels remain visible; validation associations use tag helpers; quantity controls work without JavaScript. Toasts/modals may enhance feedback/confirmation, but the server fallback remains complete. Custom 404/access-denied/error views reveal no diagnostics.

Target manual viewports: 1440×900 desktop, 768×1024 tablet, 390×844 mobile. Test current Safari/Chrome on macOS and Edge/Chrome on Windows where available.

## 17. Future AI seam

`IAIService.DraftProductDescriptionAsync(ProductDraftContext, CancellationToken)` returns a typed `AIDraftResult`. `MockAIService` generates deterministic editable placeholder copy locally. `AIServiceOptions` contains provider-neutral flags/model labels but no secret. The optional admin “Draft description” POST is administrator-only, anti-forgery protected, rate/bounds checked, and never auto-saves/overwrites.

A later provider registers another `IAIService` implementation and provider-specific options in composition root; it must add secret management, privacy review, timeouts/retries, output encoding, cost/rate controls, and failure UX. Product/cart/checkout code never depends on the AI interface.

## 18. Configuration, setup, and cross-platform design

### 18.1 Observed planning environment

The pre-plan inspection established this baseline; it is evidence, not a restriction on other supported machines:

| Item | Observed value |
|---|---|
| Workspace | `/Users/ilgaz_uygur/Desktop/ECommerceStore`, isolated local Git root, no remote |
| Host | macOS 26.5.1, Apple Silicon ARM64 (`osx-arm64`) |
| .NET | SDK 8.0.422; ASP.NET Core/.NET runtime 8.0.28 |
| .NET extras | no workloads, global tools, `dotnet-ef`, or `global.json` |
| Editors/tools | VS Code, Cursor, Git, Docker client, Homebrew, Node/npm, SQLite CLI, PostgreSQL client, Python, Make |

The required implementation remains Docker-free and SQLite-based; the presence of Docker/PostgreSQL does not alter the selected stack. Package/tool installation is deferred until explicit implementation approval. A `global.json` pin and repository-local `dotnet-ef` manifest are recommended later for reproducibility.

### 18.2 Configuration groups

Configuration groups planned later:

- `ConnectionStrings:DefaultConnection` — default SQLite under `App_Data`.
- `StoreOptions` — store name, approved currency, shipping fee, low-stock threshold, page size.
- `SmtpOptions` — host, port, TLS mode, username, secret, from address.
- `UploadOptions` — maximum bytes, relative root, formats.
- `AIServiceOptions` — disabled/mock mode only initially.

Unless a value is one of the three approval-required product settings, the implementation defaults are fixed as follows and remain configurable where noted:

| Setting | Default/bound |
|---|---|
| Store name | `ECommerceStore` |
| Public catalog page size | 12 (fixed UI; server maximum 48 if a page-size option is later exposed) |
| Admin/order/customer page size | 20 (server maximum 100) |
| Related products | 4 |
| Search keyword length | 100 characters |
| Cart quantity per product | capped by current stock, with 1,000,000 absolute safety maximum |
| Anonymous cart distinct lines | 100 maximum |
| Anonymous session idle timeout | 30 minutes; ordinary requests renew it |
| Product upload size | 5 MiB maximum |
| Allowed upload dimensions | 1–10,000 pixels per side and at most 40 megapixels after header/decode inspection |
| Checkout processing-stale threshold | 5 minutes for the fake provider only; recovery remains logged and tested |
| Order/invoice random suffix | 16 uppercase hexadecimal characters (64 random bits) |

These are technical safety/usability defaults, not hardcoded secrets. Currency/shipping/tax behavior, the decline card, and the low-stock threshold are approved in §22.

Secrets use `dotnet user-secrets` in Development and environment variables in deployment. Later `.gitignore` excludes databases, email artifacts, local uploads as decided, secrets, logs, build output, IDE files, and OS metadata.

### 18.3 Cross-platform rules

Cross-platform rules:

- Use `Path.Combine`, `Path.GetFullPath`, directory APIs, and forward-slash URL paths; never hardcode `\` or `/Users/...`.
- Create runtime directories at startup with controlled errors; preserve filename casing consistency for case-sensitive macOS/Linux filesystems and Windows.
- Use SQLite, not LocalDB/SQL Server Express/Windows authentication.
- Use UTC persistence and explicit display conversion; no machine-local timestamps in stored data.
- Do not depend on Bash/PowerShell for runtime. README later provides equivalent shell commands where syntax differs.
- Bind development URLs through ASP.NET configuration; trust development certificate instructions per OS.
- Verify QuestPDF and image library native assets on `osx-arm64` and Windows before final package lock.
- Docker is optional future documentation only.

## 19. Errors, observability, and side-effect reliability

Use centralized exception handling outside Development, status-code pages/custom endpoints for 404, and access-denied route. Expected validation/not-found/conflict outcomes are typed results. Unexpected errors get a correlation ID shown to the user and structured server log. Logs use event IDs and identifiers (order ID/number where safe), never card data, password/token/cookie, SMTP secret, Identity internals, or full sensitive form bodies.

Side-effect policy:

| Event | Core commit | Side effect | Failure behavior |
|---|---|---|---|
| Registration | Identity user + role | Welcome email | Account remains; Development fallback; safe log |
| Checkout | Attempt/order/items/stock/cart/payment | Immediate invoice generation and order email | Order remains; retry invoice on download; email fallback/log |
| Product image | Database row and managed file coordinated | Old-file cleanup | New reference remains valid; cleanup failure logged/retriable |

No distributed transaction is claimed. A future durable background outbox is optional if production reliability requirements grow.

## 20. Security decisions and threat controls

| Threat | Control |
|---|---|
| CSRF | Global auto-validation filter for unsafe methods plus form tokens; SameSite cookies |
| XSS | Razor encoding, no untrusted `Html.Raw`, encoded email/PDF text, CSP baseline |
| SQL injection | LINQ/parameterized EF; no concatenated raw SQL; allow-listed sort expressions |
| Broken role auth | Area convention + role attribute + service separation |
| IDOR | Owner ID in order/invoice database predicate; 404 for non-owner |
| Over-posting | Purpose-built input models and explicit mapping |
| File attacks | Multi-signal raster validation, random names, bounded root, no SVG/execution |
| Secret commits | User secrets/environment variables, placeholder config, later `.gitignore`, README checks |
| Session fixation/cart trust | Fresh Identity auth cookie, session cookie has no authorization power, merge-after-auth, clear consumed cart, server price validation |
| Checkout replay | Anti-forgery plus unique user-bound attempt token and idempotent result |
| Stock race | Transaction + conditional decrement + version token + rollback |
| Payment leakage | Transient model, redaction/no logging, sanitized result-only persistence |
| Unsafe redirects | local-URL validation only |
| Error disclosure | environment-specific handlers, correlation IDs, sanitized logs |
| Cookie theft | HTTPS Production, Secure, HttpOnly, SameSite, short/appropriate lifetimes |

Identity defaults are retained unless consciously tightened; password reset/email confirmation flows are not required, but adding them later must use Identity tokens and non-enumerating responses. Data Protection keys are local for development; production deployment persistence is future deployment scope.

## 21. Important decisions and rejected alternatives

| Topic | Chosen design | Rejected/deferrable alternative and reason |
|---|---|---|
| Solution boundaries | One MVC web project + one test project | Multiple domain/infrastructure projects add ceremony now |
| Cart | Session anonymous + DB authenticated | Session-only loses durability; DB anonymous adds identity/cleanup complexity |
| Checkout | Short SQLite transaction, conditional stock updates, attempt token | UI-only button disabling cannot prevent replay/races |
| Concurrency | Integer version + conditional SQL predicates | SQL Server `rowversion` is not portable to SQLite |
| Invoices | Regenerate from immutable snapshots | Stored public/local files add ACL/path/cleanup risk |
| Email | Environment-selected resilient transports | Throwing delivery exceptions through business actions violates reliability |
| Product deletion | Deactivate first; hard-delete only safe data | Cascade delete damages history |
| Numbering | Opaque GUID-derived number + unique retry | Sequential max+1 races and leaks volume |
| Money | `decimal(18,2)`, per-line AwayFromZero | `double` is unsafe; total-only rounding can disagree by line |
| Payment | Deterministic local fake service | Real integration is forbidden and changes compliance/transactions |
| AI | Mock provider seam, optional admin draft | Core recommendations/checkout AI creates external dependency |
| Search | EF/SQLite normalized matching | External search service conflicts with simple local setup |
| Side effects | Synchronous best effort after commit | Durable queue/outbox is reliable but unnecessary for local scope |

## 22. Approved business decisions

1. Currency/shipping/tax behavior: USD, flat 10.00 shipping for nonempty carts, no free threshold, prices treated as tax-inclusive.
2. Fake decline card: `4000 0000 0000 0002`.
3. Low-stock threshold: 5 units.

These values were explicitly approved before implementation. Later configuration changes do not alter the overall architecture; adding tax, multi-currency, or refund behavior is new scope.
