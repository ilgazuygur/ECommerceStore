# ECommerceStore Requirements

## 1. Purpose and authority

This document restates the supplied functional and technical specification as testable requirements. Together with `ARCHITECTURE.md`, `DATABASE_DESIGN.md`, `IMPLEMENTATION_PLAN.md`, `TASKS.md`, and `TEST_PLAN.md`, it is the implementation source of truth. If implementation discovers an ambiguity, update and re-approve these documents before changing behavior.

Requirement priority is:

- **Required**: must be present for acceptance.
- **Optional**: explicitly non-blocking improvement; it must not delay or destabilize required work.
- **Current-phase constraint**: governs planning and the approval gate rather than the eventual runtime.

Requirement classification:

- **Functional**: `APP`, `AUTH`, `STORE`, `PROD`, `QUERY`, `CART`, `CHECK`, `PAY`, `ORDER`, `PDF`, `ADMIN`, `UPLOAD`, `EMAIL`, `AI`, and `SEED` IDs.
- **Data/integrity**: `DATA` IDs.
- **Non-functional**: `TECH`, `ARCH`, `UI`, `PLAT`, and `SEC` IDs.
- **Verification/documentation**: `TEST` and `DOC` IDs.
- **Planning governance**: `GOV` IDs.

All IDs are numbered acceptance requirements even when grouped by domain rather than repeated as one long numeric list.

## 2. Current-phase governance

### GOV-001 — Exactly six planning artifacts (current-phase constraint)

The planning phase shall create exactly these primary documents: `REQUIREMENTS.md`, `ARCHITECTURE.md`, `DATABASE_DESIGN.md`, `IMPLEMENTATION_PLAN.md`, `TASKS.md`, and `TEST_PLAN.md`.

**Acceptance criteria**

1. All six files exist at the repository root and are internally consistent.
2. No `README.md` is created during planning.
3. The six documents are sufficient for a different coding agent to implement without relying on this conversation.

### GOV-002 — No implementation during planning (current-phase constraint)

Planning shall not create application code, a solution, project files, controllers, models, views, services, tests, configuration, migrations, database files, or `.gitignore`; install packages, tools, or workloads; or configure GitHub/remotes/pushes.

**Acceptance criteria**: until explicit approval, the repository contains only Git metadata and the six planning documents, and no install or implementation command has been run.

### GOV-003 — Reviewed approval gate (current-phase constraint)

Implementation shall begin only after a line-by-line specification review, traceability audit, contradiction/security/compatibility correction, and explicit user approval.

**Acceptance criteria**: the final planning response summarizes the architecture and phases, identifies five highest risks and genuine approval decisions, confirms prohibited actions were not taken, and waits for approval.

## 3. Required functional requirements

### APP-001 — Complete locally runnable store

The final product shall be a real, complete, locally runnable e-commerce application named **ECommerceStore**, not a static UI or partial demo. It shall provide customer accounts and roles, catalog administration and browsing, carts, dummy checkout, order history, invoices, emails, a complete administrator area, responsive UI, and an AI-ready but AI-independent design.

**Acceptance criteria**: all required feature groups in this document work together in a fresh local installation and the final acceptance workflow in `TEST_PLAN.md` passes.

### TECH-001 — Core technology stack

The application shall use C#, .NET 8, ASP.NET Core 8 MVC, Entity Framework Core 8, SQLite by default, ASP.NET Core Identity, Bootstrap 5, and only necessary JavaScript.

**Acceptance criteria**: project target frameworks are `net8.0`; the primary executable is server-rendered MVC; data access uses EF Core with SQLite; authentication uses Identity; the base UI uses Bootstrap 5; no JavaScript SPA framework is required.

### TECH-002 — Supporting libraries and tests

The later implementation shall use an SMTP-compatible library such as MailKit, a free .NET PDF library such as QuestPDF, and xUnit.

**Acceptance criteria**: approved compatible package versions are centrally managed or explicitly documented; SMTP email, PDF generation, and xUnit tests function on both supported operating systems; applicable QuestPDF license configuration is documented.

### TECH-003 — Simple, Docker-free normal setup

Docker shall not be required. A normal finished setup should be no more complex than `dotnet restore`, `dotnet ef database update`, and `dotnet run` (with project/solution arguments documented where necessary).

**Acceptance criteria**: a clean machine with the documented .NET 8 SDK can restore, migrate, run, and test without Docker or an external database server.

### AUTH-001 — Registration and validation

Customers shall register with first name, last name, email, password, password confirmation, and optional phone number. Server-side validation and friendly field-level/summary messages are required.

**Acceptance criteria**: valid unique input creates a customer account; missing/invalid/over-length input, mismatched passwords, weak passwords, or duplicate normalized email is rejected without losing safe form input; password fields are never repopulated or logged.

### AUTH-002 — Login, logout, and password protection

Customers shall log in and log out using ASP.NET Core Identity, whose password hasher stores no plaintext passwords.

**Acceptance criteria**: valid enabled users can log in; invalid credentials receive a non-enumerating friendly error; logout invalidates the local authentication session; stored Identity data contains only Identity-managed hashes and internals.

### AUTH-003 — Roles, authorization, and access denial

The application shall support `Customer` and `Administrator` roles, protect restricted actions server-side, and provide friendly access-denied behavior.

**Acceptance criteria**: anonymous requests are challenged where appropriate; authenticated non-admin users receive access denied for admin resources; authorization is enforced independently of navigation visibility.

### AUTH-004 — Administrative account disable/re-enable

Administrators shall disable and re-enable customer accounts without exposing Identity internals.

**Acceptance criteria**: a disabled user cannot establish a new session; existing sessions are rejected on subsequent validation; re-enable restores login ability; the admin UI never exposes password hashes, security stamps, tokens, or equivalent fields.

### STORE-001 — Storefront shell and discovery pages

The customer storefront shall include a home page, featured and latest product sections, product listing, product detail, category pages, search bar, responsive navigation with cart count, footer, and coherent success/failure feedback.

**Acceptance criteria**: each page is reachable through normal navigation, displays only public active catalog data, handles empty data gracefully, and retains a usable navigation/footer at desktop, tablet, and mobile widths.

### PROD-001 — Product data and visibility

Each product shall have an ID, name, unique URL-friendly slug, short description, full description, normal price, optional discount price, stock quantity, category, image URL or local path, active flag, featured flag, creation time, and last-update time.

**Acceptance criteria**: constraints in `DATABASE_DESIGN.md` are enforced; inactive products are excluded from public discovery and ordering; effective price is the valid discount price when present, otherwise normal price.

### PROD-002 — Product detail experience

Product detail shall display image (or fallback), name, price, discount information, description, stock status, category, quantity selector, add-to-cart action, and related products.

**Acceptance criteria**: related products are active products from the same category excluding the current product; out-of-stock/inactive items cannot be added; displayed pricing agrees with server-side cart pricing.

### QUERY-001 — Combined search and filters

Customers shall filter products by category, minimum price, maximum price, in-stock state, and search keyword.

**Acceptance criteria**: filters are combined, validated, applied server-side to active products, and return a friendly empty state; keyword behavior is documented as case-insensitive matching of product name and descriptions supported by SQLite.

### QUERY-002 — Sorting, pagination, and URLs

Customers shall sort by price ascending, price descending, newest, name A–Z, or name Z–A. Search, filters, sort, and pagination shall work together, with relevant state retained in query parameters where practical.

**Acceptance criteria**: results use a deterministic secondary ordering; invalid query values fall back safely; changing page or sort does not silently discard active filters; generated links contain only allow-listed query values.

### CART-001 — Cart operations and totals

The cart shall add, remove, increment, decrement, update, and clear items; show cart-item count, merchandise subtotal, shipping fee, discount amount, and grand total.

**Acceptance criteria**: all mutations use POST plus anti-forgery; count means total units (not distinct lines); totals use the monetary/rounding rules in `DATA-003`; all values are recalculated from trusted server data.

### CART-002 — Anonymous session cart

Anonymous customers shall have a session-backed cart.

**Acceptance criteria**: the cart survives ordinary navigation in the same browser session; session stores only product IDs and quantities, not trusted prices; corrupt/expired session data fails safely to an empty or repaired cart.

### CART-003 — Persisted authenticated cart and merge

Authenticated carts shall be persisted in SQLite. On login or registration, an anonymous cart shall be safely merged into the user's database cart without loss; duplicate product quantities are summed then capped to current stock.

**Acceptance criteria**: merge is transactional, idempotent for a consumed anonymous cart, retains valid existing lines, drops unavailable/inactive lines with feedback, caps quantities to stock, and clears the anonymous session cart only after database success.

### CART-004 — Cart input and catalog validation

The server shall reject or safely repair zero/negative quantities, quantities above stock, invalid product IDs, inactive products, out-of-stock products, and negative monetary data.

**Acceptance criteria**: crafted requests cannot bypass validation; failures do not partially corrupt the cart; product price and discount validity are checked independently of submitted form values.

### CHECK-001 — Authenticated checkout form

Checkout shall require authentication and collect shipping address, city, postal code, country, phone number, an order summary, and dummy card data.

**Acceptance criteria**: anonymous users are redirected to login with a safe local return URL; required address/contact fields have documented limits and server validation; summary is recalculated on POST; no payment secret is redisplayed after failure.

### PAY-001 — Deterministic fake payment

Payment shall be entirely fake and provider-independent. Card `4242 4242 4242 4242`, any future expiration, and any three-digit CVV shall succeed. A documented second test card shall deterministically fail so failure behavior can be tested. No real bank/provider call is permitted.

**Acceptance criteria**: the fake service returns deterministic sanitized results; invalid/expired/malformed inputs fail; payment failure creates no order, changes no stock, and leaves the cart available.

### PAY-002 — Payment-data privacy

The application shall never persist or log a full card number or CVV. Only non-sensitive payment result metadata may be stored.

**Acceptance criteria**: controller/view models keep card input request-scoped; logs, exceptions, database, emails, and analytics contain at most card brand/test-provider result, a generated reference, and last four digits if needed; model state containing secrets is cleared before rendering.

### ORDER-001 — Successful checkout outcome

After successful dummy payment, the application shall create an order and all items, snapshot product names and prices, generate unique order and invoice numbers, record separate payment and order statuses, reduce stock, clear the authenticated cart, attempt invoice generation, attempt order email, and redirect to confirmation.

**Acceptance criteria**: the resulting order is internally balanced, inventory and cart changes agree with its lines, post-commit side-effect failures cannot undo it, and confirmation is safe to refresh.

### ORDER-002 — Atomic, concurrent, idempotent checkout

Order creation, item creation, stock reduction, cart clearing, payment-result persistence, and checkout-attempt completion shall be atomic in one database transaction. Concurrent purchases shall not oversell, and duplicate submissions shall be idempotent.

**Acceptance criteria**: conditional stock updates cause the entire transaction to roll back if any line lacks stock; a unique user-bound checkout token permits at most one order; completed repeats return the existing result; failed payment leaves stock/cart unchanged; processing/error states have a documented recovery path.

### ORDER-003 — Customer order history and details

`My Orders` shall list order number/date, total, payment status, order status, item count, details action, and invoice action. Details shall show item snapshots, quantities, unit prices, line totals, subtotal, shipping, discount, grand total, address snapshot, statuses, and invoice link.

**Acceptance criteria**: only authenticated owner's orders are returned, ordered newest first with pagination; historical display remains unchanged after catalog/user edits.

### ORDER-004 — Order and invoice ownership

A customer shall never access another customer's order, confirmation, or invoice by changing a route, query, or form value. Ownership shall be enforced in the service/query layer.

**Acceptance criteria**: all customer order/invoice lookups require both resource identifier and current user ID; non-owners receive 404 (preferred to avoid disclosure) or a consistently documented denial; UI hiding is not relied upon.

### PDF-001 — Professional invoice contents and entry points

Every successful order shall have a professional PDF invoice containing store name, invoice/order numbers, customer name/email, address, date, product names, quantities, unit prices, line totals, subtotal, shipping, discount, grand total, and payment status. It shall be downloadable from confirmation, order list, and details.

**Acceptance criteria**: output begins with a valid PDF signature, is non-empty/readable, uses immutable snapshots, and all three entry points resolve to the same invoice data.

### PDF-002 — Deterministic invoice and graceful failure

Invoices shall be deterministically regenerated from immutable order snapshots rather than stored as public files. Generation occurs after the main transaction and on authorized download.

**Acceptance criteria**: no invoice exists under a publicly guessable static path; generation failure is logged without sensitive details, does not invalidate an order, yields friendly retry guidance, and a later download can retry.

### ADMIN-001 — Protected, separated administrator area

The administrator area shall use ASP.NET Core Areas, administrator-role authorization, and a separate layout or clearly separate navigation.

**Acceptance criteria**: every admin controller/page is protected centrally and, where appropriate, explicitly; normal customers and anonymous users cannot invoke admin endpoints even with crafted requests.

### ADMIN-002 — Dashboard metrics

The admin dashboard shall show total/active products, total customers, total/pending/completed-or-delivered orders, revenue from successfully paid orders, and low-stock products.

**Acceptance criteria**: metric definitions are documented and computed server-side; revenue includes every order whose payment status is successful (including a later-cancelled order because refunds are out of scope) and excludes pending/failed payment; low-stock threshold is configurable.

### ADMIN-003 — Product management

Administrators shall list/search/filter/create/edit products; change stock/prices/discounts/featured/active state/category; safely deactivate or delete; and choose an uploaded local image or validated HTTP(S) image URL.

**Acceptance criteria**: admin input uses dedicated view models and server validation; historical order snapshots survive changes/deletion; unsafe deletion falls back to deactivation with an explanation; list operations paginate.

### UPLOAD-001 — Secure product images

Uploads shall be constrained by extension, declared MIME type, independently detected file signature, size, generated safe name, and safe storage location; client filename/MIME alone shall never be trusted.

**Acceptance criteria**: only allow-listed raster formats (JPEG, PNG, WebP) up to the configured limit are accepted; SVG/executable/polyglot/invalid signatures and traversal names are rejected; files are stored under a dedicated product upload directory using random names; replacement/deletion is safe; missing images use a fallback.

### ADMIN-004 — Category management

Administrators shall list, create, edit, activate, deactivate, and delete categories only when safe.

**Acceptance criteria**: category name/slug uniqueness is enforced; deletion is blocked when referenced by products, with deactivation offered; no product can reference a missing category.

### ADMIN-005 — Order administration and status transitions

Administrators shall list all orders, search by order number/customer email, filter by order status, view details, and update status. Order statuses are Pending, Paid, Processing, Shipped, Delivered, and Cancelled; payment status is separate.

**Acceptance criteria**: transitions follow the state table in `ARCHITECTURE.md`; invalid or stale transitions are rejected; list operations paginate; no status edit changes immutable financial/address/item snapshots.

### ADMIN-006 — Customer administration

Administrators shall list/search customers, view safe basic information and a customer's orders, and disable/re-enable accounts.

**Acceptance criteria**: results are paginated; only allow-listed profile fields and aggregate/order data are projected; no Identity security internals appear in views, logs, or exports.

### EMAIL-001 — Welcome email with non-blocking fallback

After registration, the application shall attempt a welcome email. Email failure shall not roll back registration. In Development, absent or failed SMTP shall save generated content under `App_Data/Emails`.

**Acceptance criteria**: successful registration returns success regardless of delivery outcome; SMTP credentials come only from environment variables/user secrets; fallback files contain intended content but no secrets; failures are logged safely.

### EMAIL-002 — Reusable order-email architecture

Reusable `IEmailService`, SMTP, and development-file implementations shall support welcome and order-confirmation messages. The order email shall include customer name, order number, product summary, total, order-viewing information, and invoice information.

**Acceptance criteria**: environment/configuration selects a service as defined in `ARCHITECTURE.md`; order email runs only after transaction commit; SMTP and fallback failure cannot invalidate a successful order; users do not see diagnostics.

### AI-001 — Provider-independent, nonessential AI abstraction

Core behavior shall not depend on AI. A provider-independent `IAIService`, `AIServiceOptions`, and `MockAIService` shall require no external API or real key and expose the future provider seam.

**Acceptance criteria**: disabling/removing AI does not affect required commerce flows; mock output is deterministic and identified as a draft; configuration contains no hardcoded provider secret.

### ARCH-001 — Beginner-readable application structure

The solution shall favor a single layered MVC web project plus a test project, using `Areas/Admin`, `Controllers`, `Data`, `Models`, `ViewModels`, domain-focused `Services` folders, `Views`, and `wwwroot`. Controllers remain small, business rules live in services, and database/I/O APIs are asynchronous where appropriate.

**Acceptance criteria**: dependency direction and responsibilities match `ARCHITECTURE.md`; no microservices or unnecessary enterprise patterns are introduced; comments explain only non-obvious intent.

## 4. Data requirements

### DATA-001 — Complete persistent model

EF Core shall model at least `ApplicationUser`, `Category`, `Product`, persisted `Cart`, `CartItem`, `CheckoutAttempt`, `Order`, `OrderItem`, and `PaymentRecord`; invoice metadata shall be stored on `Order` unless later evidence requires a separate entity.

**Acceptance criteria**: all entities, fields, keys, relationships, optionality, constraints, and delete behaviors are specified in `DATABASE_DESIGN.md` and represented by migrations later.

### DATA-002 — Validation, auditing, constraints, and indexes

The persistent model shall define appropriate required fields, length limits, created/updated timestamps, foreign keys, validation rules, useful indexes, and database unique constraints.

**Acceptance criteria**: application validation is backed by database constraints where SQLite supports them; timestamps are UTC; query-critical and uniqueness indexes listed in `DATABASE_DESIGN.md` exist in the generated migration. The specified Development fixture password is public test data and environment-gated, not an operational secret; real passwords/keys/SMTP credentials are never source-controlled.

### DATA-003 — Money and rounding

All application/domain money shall use C# `decimal` with logical precision/scale `decimal(18,2)` and two-decimal line-level rounding with `MidpointRounding.AwayFromZero`. SQLite shall use the exact integer-minor-unit mapping in `DATABASE_DESIGN.md`; floating-point money is forbidden.

**Acceptance criteria**: merchandise subtotal is the sum of rounded list-price line amounts; discount is the sum of rounded per-line list-versus-effective differences; grand total equals subtotal minus discount plus shipping; persisted totals and displayed totals agree exactly and cannot be negative.

### DATA-004 — Stock and concurrency

Stock shall be a non-negative integer. Product and mutable admin/order records shall use an application-managed integer concurrency token compatible with SQLite. Checkout stock decrements shall additionally use atomic conditional updates.

**Acceptance criteria**: stale admin edits receive a conflict response; two checkouts for the last unit yield one success at most; a failed multi-line claim restores all prior decrements through rollback.

### DATA-005 — Immutable snapshots and unique numbering

Orders shall snapshot customer name/email/address and financial totals; order items shall snapshot product name/slug, list unit price, paid unit price, discount, quantity, and line total. Order and invoice numbers shall be unique, opaque, non-sequential, and generated with collision retry.

**Acceptance criteria**: later product, category, price, address, or user changes do not alter historical output; database unique indexes reject collisions; number generation does not depend on provider-specific sequences.

### DATA-006 — Historical safety and delete behavior

Delete behavior shall protect history and prevent invalid references.

**Acceptance criteria**: users with orders, orders, order items, and payment records are not cascade-deleted; category deletion is restricted while products reference it; product hard deletion is blocked when historical order items reference it and otherwise allowed only when operational references are safely absent/removed; nullable `OrderItem.ProductId` with `SetNull` remains defense in depth while snapshots preserve history; cart cleanup is explicit and transactional.

### SEED-001 — Environment-safe seed data

Development shall seed one administrator (`admin@localstore.test` / `Admin123!`, development-only), several categories, and at least 12 realistic products spanning price ranges, including several featured, one low-stock, and one out-of-stock product.

**Acceptance criteria**: seeding is idempotent; roles are always seeded; predictable credentials are created only in Development and clearly documented; Production requires a secure out-of-band bootstrap configuration and never auto-creates that password.

## 5. UI, accessibility, and platform requirements

### UI-001 — Polished visual system

The application shall look purpose-designed rather than like an untouched Bootstrap template, using consistent typography/spacing, product cards/grid, clear buttons, status badges, alerts/toasts, and confirmation before destructive actions without paid templates/assets.

**Acceptance criteria**: a consistent theme is applied across storefront, account, order, and admin views; destructive operations require a POST confirmation pattern; placeholder imagery is license-safe.

### UI-002 — Responsive and accessible interaction

Navigation, grids, cards, forms, and admin tables shall work at desktop, tablet, and mobile widths. Forms need visible labels; controls shall be keyboard-friendly and use meaningful focus/order/semantics.

**Acceptance criteria**: checks at 1440×900, 768×1024, and 390×844 have no essential horizontal overflow (responsive tables may intentionally scroll), clipped actions, or inaccessible controls; key workflows complete by keyboard; automated accessibility scan has no serious/critical violations.

### UI-003 — Friendly states and production-safe errors

The application shall provide useful empty states, friendly validation and success/failure feedback, a custom 404 page, custom access-denied page, friendly production error page, and missing-image fallback.

**Acceptance criteria**: expected errors do not expose stack traces, secrets, SQL, paths, or payment input; each listed state is manually reachable and understandable; Development retains developer diagnostics only in Development.

### PLAT-001 — macOS and Windows compatibility

The application shall run on macOS including Apple Silicon and on Windows, without OS-specific database servers, paths, shell assumptions, or Docker.

**Acceptance criteria**: restore/build/migrate/run/test workflows pass on an Apple Silicon Mac and a supported Windows environment using .NET 8; paths use platform APIs; filename casing is consistent; SQLite, PDF, uploads, email fallback, and secrets instructions work on both systems.

## 6. Security and reliability requirements

### SEC-001 — CSRF, validation, and over-posting protection

All state-changing MVC actions shall use anti-forgery protection and server-side validation. Dedicated input view models and explicit mapping shall prevent mass assignment.

**Acceptance criteria**: missing/invalid tokens fail; domain-controlled fields such as role, user ID, price snapshots, totals, statuses, stock claims, and ownership cannot be bound from customer input.

### SEC-002 — Authorization, ownership, and safe redirects

Role and resource authorization shall be server-enforced; IDOR-prone lookups shall be scoped by owner; return URLs shall be accepted only when local.

**Acceptance criteria**: anonymous, cross-user, and non-admin crafted requests fail consistently; unsafe external return URLs are rejected; identifiers alone never grant access.

### SEC-003 — XSS, SQL injection, and upload attacks

Razor output encoding, allow-listed limited rich text behavior, parameterized EF Core queries, and the upload controls in `UPLOAD-001` shall mitigate XSS, SQL injection, traversal, content spoofing, and executable uploads.

**Acceptance criteria**: stored/search inputs render encoded; no raw SQL uses concatenated user input; malicious filename/content/MIME combinations fail and no upload is executable as active content.

### SEC-004 — Secrets, cookies, sessions, and safe logging

Secrets shall come from environment variables or .NET user secrets and shall never be committed. Authentication cookies and session shall use secure, HTTP-only, same-site settings appropriate to Development/Production. Authentication sign-in shall issue a fresh authentication session; the non-authorizing anonymous cart session shall be treated as untrusted and cleared after successful merge. Logs shall exclude credentials, tokens, cookies, card/CVV, sensitive Identity internals, and unnecessary personal data.

**Acceptance criteria**: configuration templates contain placeholders only; Production requires HTTPS and secure cookies; possession/fixation of an anonymous session ID grants no authenticated authority or trusted cart values; consumed cart data is cleared after login merge; a logging review finds no prohibited data.

### SEC-005 — Checkout replay, payment privacy, and stock races

Checkout shall combine anti-forgery, a user-bound single-use token, server-recomputed totals, fake-payment data minimization, transactional writes, and conditional stock updates.

**Acceptance criteria**: replay/concurrent double-submit produces at most one order; tampered totals/prices/user/cart IDs are ignored or rejected; overselling and sensitive payment persistence/logging tests pass.

### SEC-006 — Graceful failures and correct environments

Email, PDF, missing images, invalid input, concurrency conflicts, and unexpected exceptions shall be handled without corrupting core state or revealing diagnostics. Development and Production behavior shall be explicitly separated.

**Acceptance criteria**: post-commit side-effect failures retain successful registration/order state; database failures roll back atomic work; production error responses are generic with a correlation ID; detailed diagnostics remain server-side and sanitized.

## 7. Testing and documentation requirements

### TEST-001 — Required automated business tests

Automated coverage shall include cart subtotals/totals; invalid/over-stock quantities; anonymous merge; fake payment success/failure; order totals; transaction rollback; stock reduction and concurrency; duplicate checkout; cross-customer order/invoice denial; admin authorization; PDF validity; email fallback/non-blocking failures; image validation; and inactive-product ordering denial.

**Acceptance criteria**: every named behavior has at least one deterministic automated test identified in `TEST_PLAN.md` and all pass together.

### TEST-002 — Layer-appropriate test suite

Tests shall be divided appropriately among unit, service/database integration, ASP.NET Core integration, authorization/ownership, PDF/email/upload, and manual browser tests.

**Acceptance criteria**: pure calculations have fast unit tests; EF transaction/concurrency behavior uses real SQLite rather than EF InMemory; routing/auth/filter behavior uses `WebApplicationFactory`; tests are isolated and repeatable.

### TEST-003 — Complete manual customer/admin workflow

Manual acceptance shall cover all 24 supplied workflow steps from opening the store through responsive layout testing, including email fallback, anonymous-to-authenticated cart, checkout, stock/cart/order/invoice checks, unauthorized access, and admin product/category/customer/order operations.

**Acceptance criteria**: `TEST_PLAN.md` contains the numbered workflow with preconditions, expected outcomes, and evidence expectations; it passes on a release-like local build.

### TEST-004 — Accessibility, responsive, and cross-platform verification

The final acceptance suite shall include keyboard/accessibility checks, desktop/tablet/mobile layouts, macOS Apple Silicon, and Windows.

**Acceptance criteria**: the matrix in `TEST_PLAN.md` records OS, SDK/runtime, browser, database migration, build/test results, viewport checks, and known deviations; no required workflow is platform-specific.

### DOC-001 — Final README (later implementation only)

After implementation, `README.md` shall document description, features, stack, prerequisites, exact install/restore/migrate/run/test commands, development admin credentials, fake-payment data, SMTP and fallback behavior, PDF behavior, AI seam, folder structure, environment variables, user-secrets, macOS, Windows, troubleshooting, and known limitations.

**Acceptance criteria**: a fresh-user documentation walkthrough succeeds on macOS and Windows; all commands and stated behavior match the final repository. The README is not created during planning.

## 8. Optional improvements (not acceptance blockers)

### AI-002 — Optional mock description drafting

The admin product form may demonstrate `IAIService` by drafting a product description through `MockAIService`. It must be explicitly user-triggered, return editable draft text, make no external call, and never overwrite saved content automatically.

### OPT-001 — Optional future Docker documentation

Docker/container support may be designed after the normal local workflow is stable. It shall never replace or become a prerequisite for the Docker-free path.

### OPT-002 — Optional later production integrations

A real SMTP service, real AI provider, persistent invoice cache/object storage, or real payment provider may be added only as separately scoped future work with new threat modeling. The current dummy payment must never be presented as production payment processing.

## 9. Explicit scope decisions and non-requirements

- No real payment gateway, bank call, card vault, tax engine, shipment carrier integration, returns/refunds, guest checkout, social login, email-confirmation gate, multi-currency conversion, or AI network call is in current scope.
- Prices are treated as tax-inclusive display prices; no separate tax line is calculated unless scope is re-approved.
- The implementation uses database-persisted authenticated carts and session-only anonymous carts.
- Invoices are regenerated from snapshots; no public invoice files are stored.
- Product deletion is secondary to deactivation; historical integrity takes precedence over physical deletion.
- JavaScript is progressive enhancement for UI behavior only; security and business validation remain server-side.

## 10. Canonical traceability matrix

This is the canonical requirement-to-work-to-verification map. Task IDs refer to `TASKS.md`; test IDs refer to `TEST_PLAN.md`. Optional requirements are mapped but remain non-blocking. Section names are deliberately stable; if renamed, this table must be updated.

| Requirement | Task(s) | Test(s) | Architecture/database source |
|---|---|---|---|
| GOV-001 | T-GOV-001 | TC-GOV-001 | `ARCHITECTURE.md` §1 |
| GOV-002 | T-GOV-002 | TC-GOV-002 | `IMPLEMENTATION_PLAN.md` §1 |
| GOV-003 | T-GOV-003 | TC-GOV-003 | `IMPLEMENTATION_PLAN.md` §Final approval gate |
| APP-001 | T-APP-001 | TC-APP-001 | `ARCHITECTURE.md` §2–4 |
| TECH-001 | T-TECH-001 | TC-TECH-001 | `ARCHITECTURE.md` §2 |
| TECH-002 | T-TECH-002 | TC-TECH-002 | `ARCHITECTURE.md` §12–13; §20 |
| TECH-003 | T-TECH-003 | TC-TECH-003 | `ARCHITECTURE.md` §18 |
| AUTH-001 | T-AUTH-001 | TC-AUTH-001 | `ARCHITECTURE.md` §6; `DATABASE_DESIGN.md` §3.1 |
| AUTH-002 | T-AUTH-002 | TC-AUTH-002 | `ARCHITECTURE.md` §6 |
| AUTH-003 | T-AUTH-003 | TC-AUTH-003 | `ARCHITECTURE.md` §6 |
| AUTH-004 | T-AUTH-004 | TC-AUTH-004 | `ARCHITECTURE.md` §6; `DATABASE_DESIGN.md` §3.1 |
| STORE-001 | T-STORE-001 | TC-STORE-001 | `ARCHITECTURE.md` §5; §16 |
| PROD-001 | T-PROD-001 | TC-PROD-001 | `DATABASE_DESIGN.md` §3.3 |
| PROD-002 | T-PROD-002 | TC-PROD-002 | `ARCHITECTURE.md` §5 |
| QUERY-001 | T-QUERY-001 | TC-QUERY-001 | `ARCHITECTURE.md` §8 |
| QUERY-002 | T-QUERY-002 | TC-QUERY-002 | `ARCHITECTURE.md` §8 |
| CART-001 | T-CART-001 | TC-CART-001 | `ARCHITECTURE.md` §9 |
| CART-002 | T-CART-002 | TC-CART-002 | `ARCHITECTURE.md` §9 |
| CART-003 | T-CART-003 | TC-CART-003 | `ARCHITECTURE.md` §9; `DATABASE_DESIGN.md` §3.4–3.5 |
| CART-004 | T-CART-004 | TC-CART-004 | `ARCHITECTURE.md` §9 |
| CHECK-001 | T-CHECK-001 | TC-CHECK-001 | `ARCHITECTURE.md` §10 |
| PAY-001 | T-PAY-001 | TC-PAY-001 | `ARCHITECTURE.md` §11 |
| PAY-002 | T-PAY-002 | TC-PAY-002 | `ARCHITECTURE.md` §11; `DATABASE_DESIGN.md` §3.9 |
| ORDER-001 | T-ORDER-001 | TC-ORDER-001 | `ARCHITECTURE.md` §10; `DATABASE_DESIGN.md` §3.7–3.9 |
| ORDER-002 | T-ORDER-002 | TC-ORDER-002 | `ARCHITECTURE.md` §10; `DATABASE_DESIGN.md` §5 |
| ORDER-003 | T-ORDER-003 | TC-ORDER-003 | `ARCHITECTURE.md` §7; `DATABASE_DESIGN.md` §3.7–3.8 |
| ORDER-004 | T-ORDER-004 | TC-ORDER-004 | `ARCHITECTURE.md` §6.4; §13 |
| PDF-001 | T-PDF-001 | TC-PDF-001 | `ARCHITECTURE.md` §13 |
| PDF-002 | T-PDF-002 | TC-PDF-002 | `ARCHITECTURE.md` §13 |
| ADMIN-001 | T-ADMIN-001 | TC-ADMIN-001 | `ARCHITECTURE.md` §15 |
| ADMIN-002 | T-ADMIN-002 | TC-ADMIN-002 | `ARCHITECTURE.md` §15 |
| ADMIN-003 | T-ADMIN-003 | TC-ADMIN-003 | `ARCHITECTURE.md` §15; `DATABASE_DESIGN.md` §3.3 |
| UPLOAD-001 | T-UPLOAD-001 | TC-UPLOAD-001 | `ARCHITECTURE.md` §14 |
| ADMIN-004 | T-ADMIN-004 | TC-ADMIN-004 | `ARCHITECTURE.md` §15; `DATABASE_DESIGN.md` §3.2 |
| ADMIN-005 | T-ADMIN-005 | TC-ADMIN-005 | `ARCHITECTURE.md` §15; `DATABASE_DESIGN.md` §3.7 |
| ADMIN-006 | T-ADMIN-006 | TC-ADMIN-006 | `ARCHITECTURE.md` §15 |
| EMAIL-001 | T-EMAIL-001 | TC-EMAIL-001 | `ARCHITECTURE.md` §12 |
| EMAIL-002 | T-EMAIL-002 | TC-EMAIL-002 | `ARCHITECTURE.md` §12 |
| AI-001 | T-AI-001 | TC-AI-001 | `ARCHITECTURE.md` §17 |
| ARCH-001 | T-ARCH-001 | TC-ARCH-001 | `ARCHITECTURE.md` §3–4 |
| DATA-001 | T-DATA-001 | TC-DATA-001 | `DATABASE_DESIGN.md` §2–3 |
| DATA-002 | T-DATA-002 | TC-DATA-002 | `DATABASE_DESIGN.md` §3–4 |
| DATA-003 | T-DATA-003 | TC-DATA-003 | `DATABASE_DESIGN.md` §6 |
| DATA-004 | T-DATA-004 | TC-DATA-004 | `DATABASE_DESIGN.md` §5 |
| DATA-005 | T-DATA-005 | TC-DATA-005 | `DATABASE_DESIGN.md` §3.7–3.8; §7 |
| DATA-006 | T-DATA-006 | TC-DATA-006 | `DATABASE_DESIGN.md` §4 |
| SEED-001 | T-SEED-001 | TC-SEED-001 | `DATABASE_DESIGN.md` §8 |
| UI-001 | T-UI-001 | TC-UI-001 | `ARCHITECTURE.md` §16 |
| UI-002 | T-UI-002 | TC-UI-002 | `ARCHITECTURE.md` §16 |
| UI-003 | T-UI-003 | TC-UI-003 | `ARCHITECTURE.md` §16; §19 |
| PLAT-001 | T-PLAT-001 | TC-PLAT-001 | `ARCHITECTURE.md` §18 |
| SEC-001 | T-SEC-001 | TC-SEC-001 | `ARCHITECTURE.md` §20 |
| SEC-002 | T-SEC-002 | TC-SEC-002 | `ARCHITECTURE.md` §6.4; §20 |
| SEC-003 | T-SEC-003 | TC-SEC-003 | `ARCHITECTURE.md` §14; §20 |
| SEC-004 | T-SEC-004 | TC-SEC-004 | `ARCHITECTURE.md` §18; §20 |
| SEC-005 | T-SEC-005 | TC-SEC-005 | `ARCHITECTURE.md` §10–11; §20 |
| SEC-006 | T-SEC-006 | TC-SEC-006 | `ARCHITECTURE.md` §12–13; §19 |
| TEST-001 | T-TEST-001 | TC-TEST-001 | `TEST_PLAN.md` §2–9 |
| TEST-002 | T-TEST-002 | TC-TEST-002 | `TEST_PLAN.md` §2 |
| TEST-003 | T-TEST-003 | TC-TEST-003 | `TEST_PLAN.md` §11 |
| TEST-004 | T-TEST-004 | TC-TEST-004 | `TEST_PLAN.md` §12–14 |
| DOC-001 | T-DOC-001 | TC-DOC-001 | `IMPLEMENTATION_PLAN.md` Phase 16 |
| AI-002 (optional) | T-AI-002 | TC-AI-002 | `ARCHITECTURE.md` §17 |
| OPT-001 (optional) | T-OPT-001 | TC-OPT-001 | `ARCHITECTURE.md` §18 |
| OPT-002 (optional) | T-OPT-002 | TC-OPT-002 | `ARCHITECTURE.md` §21 |

## 11. Approved product decisions

The user approved these values before implementation:

1. **Currency and shipping rule**: `USD`, tax-inclusive product prices, a configurable flat `10.00` shipping fee, and no free-shipping threshold. Changing these values later is configuration; adding tax/multi-currency logic is new scope.
2. **Failed fake card**: deterministic decline card `4000 0000 0000 0002`; all other structurally valid cards fail as unsupported except the specified success card.
3. **Low-stock threshold**: configurable default `5` units.

All significant design choices are approved. No product decision remains open for the initial implementation.

## 12. Final specification review record

The supplied specification was reviewed from first line to last after all six drafts were created. Each original section maps as follows; every bullet is represented by the cited requirement group and its acceptance criteria.

| Original specification section | Requirement coverage | Review result |
|---|---|---|
| Planning-only prohibitions and outputs | GOV-001–GOV-003 | Exact six-file scope; no implementation artifact/action |
| Project overview and stack | APP-001, TECH-001–TECH-003, PLAT-001, ARCH-001 | Complete local MVC application; Docker-free; both OSes |
| Authentication/users/registration email | AUTH-001–AUTH-004, EMAIL-001, SEC-001–SEC-004 | Fields, roles, hashing, validation, disable, non-blocking safe email |
| Storefront and product fields/detail | STORE-001, PROD-001–PROD-002, UI-001–UI-003 | Every page, field, detail element, state, and responsive shell covered |
| Search/filter/sort/pagination/URL | QUERY-001–QUERY-002 | All filters/sorts combined with stable pagination/query state |
| Anonymous/authenticated cart and merge | CART-001–CART-004, DATA-003, SEC-004 | Full operations/totals; hybrid storage; safe transactional merge/input checks |
| Checkout/dummy payment | CHECK-001, PAY-001–PAY-002, ORDER-001–ORDER-002, SEC-005 | Fake-only cards, privacy, transaction, stock race, idempotency, side effects |
| Customer orders and ownership | ORDER-003–ORDER-004 | Every list/detail field; service/query ownership and IDOR denial |
| PDF invoices | PDF-001–PDF-002 | Every field/entry point; deterministic authorized regeneration/failure |
| Admin dashboard/products/categories/orders/customers | ADMIN-001–ADMIN-006, UPLOAD-001 | Every operation/metric/status; upload safety; Identity-internal exclusion |
| Database/model/precision/seed | DATA-001–DATA-006, SEED-001 | Entities/fields/keys/constraints/indexes/snapshots/concurrency/numbers/seed |
| UI/accessibility | UI-001–UI-003, TEST-004 | Polish, responsive widths, forms/tables, keyboard, custom states/pages |
| Email architecture | EMAIL-001–EMAIL-002 | Reusable transports, two messages, config/fallback/logging/non-blocking behavior |
| AI abstraction | AI-001, AI-002 | Required provider-neutral mock seam; optional editable admin draft |
| Security/reliability | SEC-001–SEC-006 | Every named control/threat and Development/Production distinction covered |
| Suggested structure/engineering style | ARCH-001 | Folder map, thin controllers, services, async I/O, useful comments only |
| Automated/manual testing | TEST-001–TEST-004 | Every named test, layer division, 24 manual steps, platform/viewport matrices |
| Future README | DOC-001 | Every required README topic, created only after implementation |
| Optional/future scope | AI-002, OPT-001–OPT-002 | Clearly non-blocking and isolated from required operation |

Mechanical traceability review result:

- 65 unique requirement headings.
- 65 unique `TASKS.md` task-group IDs.
- 65 unique `TEST_PLAN.md` test-group IDs.
- 65 unique canonical traceability rows.
- No missing, extra, or duplicate IDs in any of those four sets.
- No task checkbox is marked complete during planning.

Contradiction/security/cross-platform corrections made during review:

1. Corrected the logical `decimal(18,2)` maximum and retained exact SQLite integer-minor storage to avoid floating-point/provider precision problems.
2. Replaced a nonportable session-ID-rotation assumption with the correct security boundary: a fresh Identity authentication cookie, no authorization from session, server-trusted merge, and clearing consumed anonymous cart state.
3. Made Identity account creation plus Customer-role assignment one database transaction.
4. Aligned product deletion everywhere: hard-delete only never-ordered/unreferenced products; otherwise deactivate; nullable historical FK remains defense in depth.
5. Aligned dashboard revenue with the specification: all successfully paid orders count; cancelled paid orders remain paid because refunds are not implemented.
6. Distinguished the explicitly public Development administrator fixture from real secrets and prohibited it in Production.
7. Confirmed post-commit email/PDF failures never roll back registration/order, while pre-commit checkout failures roll back all core state.
8. Added actual macOS ARM64 environment evidence and mandatory actual Windows verification rather than assuming compatibility.

The review found no remaining unmapped requirement, unresolved technical contradiction, or pending business decision. The user approved §11 and authorized implementation.
