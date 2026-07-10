# ECommerceStore Implementation Tasks

## How to use this checklist

- Do not mark any task complete during planning.
- Implement in phase order from `IMPLEMENTATION_PLAN.md`.
- Each `T-*` identifier is stable and is referenced by the canonical traceability matrix in `REQUIREMENTS.md`.
- A task is complete only after its stated behavior is implemented, reviewed, and verified by the mapped `TEST_PLAN.md` test(s).
- Optional tasks are explicitly labeled and may remain open without blocking required acceptance.
- Build/test checkpoint tasks apply to the repository root and must not hide warnings or failing tests.

## Phase 0 — Planning approval gate

- [x] **T-GOV-001** Verify the repository root contains exactly the six named primary planning Markdown files (plus Git metadata), and each is sufficient without conversation context.
- [x] **T-GOV-002** Verify planning created no solution/project/code/test/config/migration/database/README/`.gitignore`, installed no package/tool/workload, and configured no remote/push.
- [x] **T-GOV-003** Complete the line-by-line traceability/security/platform review, record approved product settings, and obtain explicit user approval before implementation.

### Phase 0 Definition of Done

- [x] All three approval-required settings are resolved consistently in all documents.
- [x] Every requirement ID maps to an existing task and test ID.
- [x] Explicit implementation approval is recorded; no implementation work preceded it.

## Phase 1 — Solution foundation

- [ ] **T-TECH-001** Create a `net8.0` ASP.NET Core MVC web project using EF Core SQLite, Identity roles, Bootstrap 5, and no SPA framework.
- [ ] **T-TECH-002** Add only approved .NET 8-compatible MailKit, QuestPDF, xUnit, and integration-test packages; document/apply the QuestPDF license setting.
- [ ] **T-TECH-003** Establish a Docker-free root workflow for restore, local EF migration, run, and test.
- [ ] **T-ARCH-001** Create the single layered web project and test project/folders defined in `ARCHITECTURE.md`, with dependency direction reviewed.
- [ ] **T-APP-001** Add one final integrated-application acceptance checkpoint proving the completed system is locally runnable and not a static/partial demo.
- [x] Add nullable analysis, async controller/service conventions, option binding, and environment-specific middleware.
- [x] Create `.gitignore` only now (after approval) for secrets, build/database/email/log/IDE/OS artifacts and the approved upload policy.
- [x] Add configuration placeholders with no credential/API/password values.
- [x] Add custom error route/view placeholders and a basic shared layout; defer visual completion.
- [x] Run `dotnet restore`, Debug/Release builds, and initial xUnit smoke test from repository root.

### Phase 1 Definition of Done

- [x] Fresh root restore/build/test succeeds on the development Mac.
- [x] App starts without Docker or an external database server.
- [x] No secret/runtime artifact is tracked; package/tool versions are reproducible.

## Phase 2 — Database, Identity, migrations, and seed

- [ ] **T-DATA-001** Create the nine planned application entities plus complete Identity integration, using no separate invoice entity.
- [ ] Create individual fluent configurations for `ApplicationUser`, `Category`, `Product`, `Cart`, `CartItem`, `CheckoutAttempt`, `Order`, `OrderItem`, and `PaymentRecord`.
- [ ] **T-DATA-002** Implement all lengths, required/optional fields, timestamps, checks, FKs, indexes, unique constraints, and audit-field generation specified in `DATABASE_DESIGN.md`.
- [ ] **T-DATA-003** Implement one checked `decimal(18,2)`-semantic ↔ SQLite integer-minor-unit converter/comparer and shared AwayFromZero line rounding.
- [ ] Reject monetary input with negative values, excessive scale, or range overflow before persistence.
- [ ] **T-DATA-004** Configure integer concurrency tokens and implement a reusable stale-write conflict pattern compatible with SQLite.
- [ ] Implement parameterized conditional product stock decrement with affected-row verification.
- [ ] **T-DATA-005** Implement immutable order/address/item snapshots and collision-retried opaque order/invoice number generators.
- [ ] **T-DATA-006** Configure every delete behavior and add service guards for category/product/user/history safety.
- [ ] Configure Identity unique normalized email, Customer/Administrator roles, password policy, cookie settings, and enabled-user validation hook.
- [ ] **T-SEED-001** Implement idempotent roles/categories/12+ product Development seed, required featured/price/stock cases, and Development-only administrator credentials.
- [ ] Ensure Production never creates `Admin123!` and requires a secure explicit bootstrap approach if an administrator is needed.
- [ ] Create and inspect the initial migration only after model tests pass.
- [ ] Verify migration apply/reapply path and `PRAGMA foreign_keys` on a fresh SQLite database.
- [ ] Add real-SQLite tests for unique indexes, checks, FK/delete rules, money conversion/querying, concurrency, and seed idempotency.

### Phase 2 Definition of Done

- [ ] Model and generated migration match `DATABASE_DESIGN.md` field by field.
- [ ] Exact money sort/filter/sum/round-trip tests pass without floating point.
- [ ] Stale edits and last-unit conditional updates behave deterministically.
- [ ] Development seed meets every count/coverage rule; Production seed is credential-safe.

## Phase 3 — Storefront and product details

- [ ] **T-STORE-001** Implement the home, featured/latest, listing, category, search-entry, responsive navigation/cart-count placeholder, footer, and shared feedback/empty-state shell.
- [ ] **T-PROD-001** Implement product create/read persistence and public active/category visibility rules for every specified product field.
- [ ] **T-PROD-002** Implement product detail projection/view with fallback image, price/discount, stock, category, quantity selector, add placeholder, and bounded same-category related products.
- [ ] Add unique slug generation/validation and active-category checks to public queries.
- [ ] Add active-only home queries with deterministic featured/latest ordering.
- [ ] Add storefront 404 and missing-image behavior.
- [ ] Add service/web tests for public visibility, detail content, slugs, related products, and empty catalog.
- [ ] Run build/test and manually inspect home/list/category/detail at initial mobile/desktop widths.

### Phase 3 Definition of Done

- [ ] Public pages expose no inactive product/category.
- [ ] Every product detail requirement renders from a view model, not an entity.
- [ ] Empty and missing states are friendly and safe.

## Phase 4 — Search, filter, sort, and pagination

- [ ] **T-QUERY-001** Implement one normalized query supporting keyword, category, minimum price, maximum price, and in-stock filters together.
- [ ] **T-QUERY-002** Implement all five allow-listed sorts, stable secondary ordering, bounded pagination, and query-parameter retention.
- [ ] Reject/normalize invalid min/max, page, page size, category, and sort values with friendly behavior.
- [ ] Verify effective-price filtering/sorting translates to exact SQLite integer columns.
- [ ] Add combined-filter/sort/page matrix tests and stable page-boundary tests.
- [ ] Manually bookmark/reload filtered URLs and confirm visible form selections match the URL.

### Phase 4 Definition of Done

- [ ] Every requested filter and sort works alone and in combination.
- [ ] Pagination never drops relevant state and invalid query values are safe.
- [ ] Generated SQL uses bounded parameterized predicates and deterministic ordering.

## Phase 5 — Shopping carts and merge

- [ ] **T-CART-001** Implement POST-only add/remove/increment/decrement/update/clear, total-unit count, and server-calculated subtotal/shipping/discount/grand total.
- [ ] Implement a pure cart calculator with list/effective line rounding identical to persisted order math.
- [ ] **T-CART-002** Implement versioned session JSON containing only bounded product IDs/quantities, with safe corrupt/expired repair.
- [ ] **T-CART-003** Implement one persisted cart per authenticated user and transactional anonymous-to-user merge after login/registration.
- [ ] Merge duplicate lines by sum then stock/per-line cap; report dropped inactive/missing/out-of-stock items.
- [ ] Clear anonymous session data only after database merge commit and prevent routine duplicate merge consumption.
- [ ] **T-CART-004** Enforce product ID, active state, stock, positive quantity, per-line cap, and trusted price/discount validation on every mutation and checkout read.
- [ ] Implement lightweight navigation cart-count view component for both cart stores.
- [ ] Add anti-forgery to every mutation and ensure request models contain no price/total/user fields.
- [ ] Add unit/integration/web tests for calculations, all mutations/invalid input, corrupt session, merge/rollback/idempotence, and concurrency.
- [ ] Complete anonymous add → authentication → merge → update → clear manual workflow.

### Phase 5 Definition of Done

- [ ] Anonymous cart survives navigation and successful auth merge without trusting session prices.
- [ ] Existing and anonymous lines merge without exceeding stock or double-consuming session data.
- [ ] All cart totals match `DATA-003`, and invalid operations leave consistent state.

## Phase 6 — Accounts and welcome email

- [ ] **T-AUTH-001** Implement registration input/view with first/last/email/password/confirmation/optional phone, dedicated validation, friendly Identity errors, and duplicate-email handling.
- [ ] **T-AUTH-002** Implement non-enumerating login, local-return handling, logout POST, Identity password hashing, and authentication-session behavior.
- [ ] **T-AUTH-003** Seed/assign Customer and Administrator roles, configure challenge/access denied, and enforce all restricted actions server-side.
- [ ] **T-AUTH-004** Enforce `IsEnabled` during sign-in/cookie validation and prepare safe disable/re-enable service behavior without Identity-internal projection.
- [ ] Integrate cart merge only after successful login/registration and retain safe merge feedback.
- [ ] **T-EMAIL-001** Implement welcome email composition and resilient SMTP-or-Development-file delivery after registration success.
- [ ] Implement `SmtpOptions` validation, TLS, environment/user-secret credential lookup, atomic safe fallback filenames, and sanitized delivery logs.
- [ ] Verify a role-assignment failure rolls back the registration transaction and leaves no incorrectly successful user.
- [ ] Add account, safe redirect, disabled session, email file/failure/encoding, and registration-survival tests.
- [ ] Manually register with SMTP absent and inspect the generated local email.

### Phase 6 Definition of Done

- [ ] Registration/login/logout and enabled-state behavior pass without exposing credentials/Identity internals.
- [ ] Welcome email success/failure cannot change account success.
- [ ] Development fallback is safe and Production never silently writes local PII mail.

## Phase 7 — Checkout, fake payment, transaction, and stock

- [ ] **T-CHECK-001** Implement authenticated checkout GET/POST with address/city/postal/country/phone, trusted summary, transient dummy card fields, and friendly server validation.
- [ ] Generate a cryptographically random checkout token and bind lookup to the current user; retain anti-forgery as a separate control.
- [ ] **T-PAY-001** Implement deterministic fake success/decline/invalid-expiry/CVV behavior with no network call.
- [ ] **T-PAY-002** Keep PAN/CVV/expiry request-scoped; persist/log only allow-listed result metadata and clear secret model state before redisplay.
- [ ] **T-ORDER-001** Implement successful order/items/snapshots/numbers/status/payment/stock/cart-clear/confirmation orchestration.
- [ ] **T-ORDER-002** Implement one transaction containing attempt claim, trusted recalculation, conditional stock claims, order aggregate, sanitized payment result, cart clear, and attempt completion.
- [ ] Handle fake decline by committing only sanitized failure state while preserving stock/cart.
- [ ] Handle duplicate succeeded/failed/processing tokens and bounded stale fake-attempt recovery without creating a second order.
- [ ] Handle order/invoice-number unique collisions with bounded regeneration inside safe retry scope.
- [ ] Add injected rollback tests at every transaction boundary and verify no partial order/stock/cart mutation.
- [ ] Add deterministic two-context/two-request tests for last-unit race and duplicate checkout.
- [ ] Add database/log/model-state scans proving no prohibited card data.
- [ ] Manually try success/decline, double-click, refresh/back, tampered totals, and stock change between GET/POST.

### Phase 7 Definition of Done

- [ ] One user token produces at most one order and confirmation refresh is safe.
- [ ] Concurrent checkout never oversells and multi-line failure rolls back every decrement.
- [ ] Payment failure creates no order and leaves cart/stock unchanged.
- [ ] No PAN/CVV/expiry is persisted or logged.

## Phase 8 — Customer orders and ownership

- [ ] **T-ORDER-003** Implement owner-only paginated My Orders and details with every specified list/detail field sourced from snapshots.
- [ ] **T-ORDER-004** Implement service/query methods that require both resource identifier and current user ID for confirmation, details, and invoice models.
- [ ] Return consistent 404 for missing/non-owned customer order resources; do not reveal existence.
- [ ] Ensure later profile/product/category/price/deletion changes do not alter displayed history.
- [ ] Add customer A/customer B/anonymous tests against route IDs and public order numbers.
- [ ] Complete My Orders/detail/unauthorized URL manual steps.

### Phase 8 Definition of Done

- [ ] Every order list/detail field is present and immutable.
- [ ] Ownership is proven at service/query and HTTP layers, not just hidden UI.

## Phase 9 — PDF and order email

- [ ] **T-PDF-001** Implement QuestPDF invoice layout containing all required store/customer/address/order/item/financial/payment fields.
- [ ] Expose one owner-scoped PDF endpoint linked from confirmation, list, and details, with sanitized filename/content type.
- [ ] **T-PDF-002** Regenerate from immutable snapshots in memory, attempt after commit, retry on download, and handle generator failure without invalidating the order.
- [ ] **T-EMAIL-002** Implement reusable email abstraction/composer/transports and post-commit order email with required summary/view/invoice information.
- [ ] Ensure duplicate confirmation/token retrieval does not unintentionally resend email repeatedly.
- [ ] Add valid PDF signature/nonempty/content tests and visual PDF fixture review.
- [ ] Add owner/non-owner invoice tests before generator invocation.
- [ ] Add email fallback/SMTP failure tests proving successful order remains committed.
- [ ] Manually open the invoice in a PDF viewer and inspect fallback order email.

### Phase 9 Definition of Done

- [ ] All successful orders have stable invoice numbers and authorized retryable PDFs.
- [ ] PDF/email failures are visible in safe logs/UX but never roll back orders.
- [ ] No invoice is stored under a public guessable path.

## Phase 10 — Administrator area

- [ ] **T-ADMIN-001** Create `Areas/Admin` with separate layout/navigation and area-wide plus explicit Administrator role enforcement.
- [ ] **T-ADMIN-002** Implement dashboard counts/revenue/low-stock queries using documented definitions and configured threshold.
- [ ] **T-ADMIN-003** Implement paginated product search/filter/create/edit/stock/price/discount/featured/activate/deactivate/category/image-URL operations with concurrency tokens.
- [ ] Implement product hard-delete eligibility check; otherwise deactivate with explanation while preserving snapshots.
- [ ] **T-ADMIN-004** Implement paginated category create/edit/activate/deactivate and restrict referenced deletion with unique name/slug handling.
- [ ] **T-ADMIN-005** Implement paginated admin order search by number/email, status filter/details, and concurrency-checked transition table.
- [ ] **T-ADMIN-006** Implement paginated safe customer search/profile/orders and disable/re-enable without Identity internals.
- [ ] Prevent current admin self-disable and last-enabled-administrator disable.
- [ ] Make all destructive/state changes confirmation + anti-forgery POST, never GET.
- [ ] Add anonymous/customer/admin authorization matrix for every admin controller/action.
- [ ] Add dashboard, CRUD uniqueness/deletion, transition, concurrent edit, disable-session, and projection tests.
- [ ] Complete manual admin workflow for products/categories/customers/orders and customer route blocking.

### Phase 10 Definition of Done

- [ ] Normal customers cannot execute any admin route/action.
- [ ] All required dashboard/manage/search/filter/status/account operations work and paginate.
- [ ] Historical orders and Identity secrets remain protected.

## Phase 11 — Product image uploads

- [ ] **T-UPLOAD-001** Implement bounded JPEG/PNG/WebP validation using extension, claimed MIME, independent decoded signature/type, size, generated name, and contained storage path.
- [ ] Reject SVG, empty, oversized, malformed/truncated, content mismatch, double-extension, traversal, executable, polyglot, and decompression-bomb candidates.
- [ ] Store with random canonical name/create-new semantics and ensure upload directory serves static non-executable content only.
- [ ] Coordinate new write/database update/old managed file cleanup and compensate failure/orphans safely.
- [ ] Validate external image URL scheme/length/credentials and keep missing-image fallback.
- [ ] Add valid-format and adversarial upload tests plus write/database/delete failure injection.
- [ ] Complete valid upload/replacement and supplied invalid-upload manual step.

### Phase 11 Definition of Done

- [ ] Client filename/MIME is never trusted alone and no path escapes the upload root.
- [ ] Every accepted file is an allow-listed decodable raster with safe name/size.
- [ ] Failure leaves neither unsafe database reference nor executable public content.

## Phase 12 — AI abstraction

- [ ] **T-AI-001** Implement provider-independent `IAIService`, `AIServiceOptions`, deterministic `MockAIService`, and disabled mode with no key/network/core-flow dependency.
- [ ] Document the exact composition-root seam and security/privacy/retry/rate/cost work required for a future provider.
- [ ] **T-AI-002 (optional)** Add administrator-only, anti-forgery-protected “Draft product description” that returns editable unsaved mock text and never overwrites automatically.
- [ ] Add tests proving deterministic mock behavior and all required commerce flows work with AI disabled.

### Phase 12 Definition of Done

- [ ] Required app works unchanged when AI is disabled.
- [ ] No real provider package, key, or call exists.
- [ ] Optional draft, if implemented, is explicit, editable, encoded, bounded, and admin-only.

## Phase 13 — UI, accessibility, errors, and security

- [ ] **T-UI-001** Apply consistent custom typography/spacing/colors/cards/grid/buttons/badges/alerts/toasts/confirmation across storefront/account/order/admin without paid assets.
- [ ] **T-UI-002** Make navigation, grids, forms, and admin tables usable at 1440×900, 768×1024, and 390×844 with labels, keyboard focus, semantic controls, and non-color status cues.
- [ ] **T-UI-003** Complete friendly empty/validation/success/failure/missing-image states and custom 404/access-denied/Production error pages.
- [ ] **T-SEC-001** Apply/verify anti-forgery on all unsafe actions, server validation, dedicated input models, explicit mapping, and over-post protection.
- [ ] **T-SEC-002** Audit every role/ownership check and return URL; eliminate ID-only access and external redirects.
- [ ] **T-SEC-003** Audit Razor/email/PDF encoding, parameterized EF queries/allow-listed sorts, upload execution/path/content, and CSP/nosniff behavior.
- [ ] **T-SEC-004** Audit secrets/configuration, Production HTTPS/cookies, session/auth transition, and logs for credentials/tokens/card/Identity/PII leakage.
- [ ] **T-SEC-005** Threat-model and retest checkout anti-forgery, replay, total tampering, payment privacy, stock races, and duplicate submissions.
- [ ] **T-SEC-006** Inject email/PDF/image/database/concurrency/unexpected failures and verify safe Production messages, correlation IDs, rollback/commit boundaries, and Development-only diagnostics.
- [ ] Run keyboard-only journeys and an automated accessibility scan; fix serious/critical findings.
- [ ] Minimize JavaScript and verify every core action has a server fallback.

### Phase 13 Definition of Done

- [ ] UI is purpose-designed, responsive, keyboard-usable, and provides every friendly state.
- [ ] Full architecture threat-control table is evidenced by code review/test.
- [ ] Production errors/logs reveal no sensitive or diagnostic detail.

## Phase 14 — Complete test suite

- [ ] **T-TEST-001** Implement every named automated business/security/failure test in `TEST-001`, including repeated concurrency and duplicate checks.
- [ ] **T-TEST-002** Place pure rules in unit tests, SQLite behavior in real-SQLite integration tests, HTTP/auth behavior in `WebApplicationFactory`, and visual/browser checks in manual acceptance.
- [ ] Add isolated temporary/shared-in-memory SQLite fixtures with foreign keys enabled and deterministic cleanup.
- [ ] Add fake clock/number/email/PDF/image/failure injectors without Production registration paths.
- [ ] Run each category, the whole Release suite, randomized order where possible, and concurrency cases at least 20 iterations.
- [ ] Verify tests/logs/fixtures contain no real credentials or prohibited payment data.

### Phase 14 Definition of Done

- [ ] Every required `TC-*` test passes deterministically in Release.
- [ ] No test uses EF InMemory to claim SQLite transaction/concurrency behavior.
- [ ] No unmapped or untested required acceptance criterion remains.

## Phase 15 — Cross-platform and manual acceptance

- [ ] **T-PLAT-001** Run clean restore/local-tool restore/migration/build/test/run plus SQLite/PDF/email/upload/secrets/path checks on Apple Silicon macOS and Windows.
- [ ] **T-TEST-003** Execute and record all 24 manual customer/admin workflow steps with expected evidence.
- [ ] **T-TEST-004** Execute keyboard/accessibility, three-viewport responsive, browser, macOS, and Windows matrices and record versions/results.
- [ ] Resolve case-sensitive file, path-separator, native library, HTTPS, file locking, and environment-command differences in shared code/docs.
- [ ] Rerun last-unit and duplicate concurrency tests on both operating systems.

### Phase 15 Definition of Done

- [ ] Both OS rows include actual—not assumed—restore/migrate/build/test/run evidence.
- [ ] All 24 manual steps pass at required viewports/browsers.
- [ ] No required runtime depends on Docker, LocalDB, Bash, PowerShell, or an external service.

## Phase 16 — Documentation and final acceptance

- [ ] **T-DOC-001** Create the later `README.md` with every required topic and exact verified commands for macOS and Windows.
- [ ] Walk through README from a clean copy on both systems and correct any mismatch.
- [ ] Re-audit requirements/tasks/tests/architecture/database/migration field by field and resolve all contradictions.
- [ ] Perform final secret/runtime-artifact/scoped-Git-status inspection.
- [ ] Run clean Release restore/build/test, fresh migration/seed/run, and final browser acceptance.
- [ ] Present known limitations honestly and obtain final user acceptance before any deployment/remote/push.

### Phase 16 Definition of Done

- [ ] README commands and documented behavior exactly match the repository.
- [ ] Required tasks/tests/acceptance criteria all pass; optional omissions are labeled.
- [ ] No security, integrity, platform, or documentation blocker remains.

## Optional future work (not part of required DoD)

- [ ] **T-OPT-001 (optional)** Document/add Docker only after the Docker-free local path is stable; do not make it required.
- [ ] **T-OPT-002 (optional)** Scope any real SMTP/AI/payment provider or invoice artifact storage as a new design/threat-model/migration effort rather than swapping it silently into current flows.
