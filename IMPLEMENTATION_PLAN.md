# ECommerceStore Implementation Plan

## 1. Use of this plan

No phase below may start until the user explicitly approves all six reviewed planning documents and the three product settings in `REQUIREMENTS.md` §11. Commands shown here are future validation instructions, not commands run during planning. Implement phases in order; a phase advances only when its Definition of Done (DoD) passes. Use small local Git commits at checkpoints, with no remote or push unless separately authorized.

Risk labels:

- **HIGH RISK — deeper reasoning required**: transaction/concurrency/security/data integrity; pause for design re-check and adversarial tests.
- **MEDIUM RISK**: cross-cutting behavior or platform/library integration.
- **STANDARD**: conventional implementation with normal review.

Every phase begins with `git status --short --branch` to detect unrelated work and ends with a cleanly understood diff. Never use destructive Git cleanup on user work.

## Phase 0 — Approval and implementation baseline

**Risk:** STANDARD  
**Objective:** convert the approved plan into an immutable implementation baseline without coding early.  
**Dependencies:** explicit user approval and answers to currency/shipping, decline-card, and low-stock decisions.  
**Expected later files/components:** none beyond approved planning updates.

**Steps**

1. Record approved decisions in all six documents and remove “proposed/awaiting” ambiguity.
2. Recheck local SDK (`dotnet --info`), repository root, and scoped Git status.
3. Confirm .NET 8 support requirements and select compatible exact package/tool versions from primary documentation.
4. Agree whether implementation uses a feature branch; do not configure a remote.

**Security checks:** ensure no secret values are introduced; verify repository top-level remains `ECommerceStore`.  
**Build check:** none; no projects yet.  
**Test check:** rerun planning traceability audit.  
**Manual validation:** user confirms approved values and scope.  
**Git checkpoint recommendation:** planning approval commit only if the user separately authorizes committing.  
**DoD:** decisions are final, requirement IDs unchanged or traceability updated, tool/package choices documented, and authorization to code is explicit.

## Phase 1 — Solution foundation

**Risk:** STANDARD  
**Objective:** create the smallest conventional .NET 8 MVC solution and reproducible tool/configuration skeleton.  
**Dependencies:** Phase 0.  
**Expected later files/components:** `ECommerceStore.sln`, `src/ECommerceStore.Web`, `tests/ECommerceStore.Tests`, local tool manifest, `.gitignore`, base configuration placeholders, initial layouts/error pages.

**Steps**

1. Create the .NET 8 solution, MVC project with no alternate auth scaffold, xUnit project, and test-to-web reference.
2. Add only approved NuGet packages; use a repository-local `dotnet-ef` tool manifest, not a global install.
3. Create folder structure from `ARCHITECTURE.md` §3 without empty speculative classes.
4. Add `.gitignore` for build output, IDE/OS files, secrets, database files, `App_Data/Emails`, logs, and chosen upload policy.
5. Add strongly typed options placeholders with no credentials; set development SQLite path via platform-safe relative construction.
6. Configure common MVC, HTTPS, static files, environment-specific exception handling, and Bootstrap assets using an approved delivery method that works offline after restore/build.

**Security checks:** no secrets; HTTPS redirection/secure Production cookie defaults; generic Production error page; no environment-specific absolute paths.  
**Build checks:** `dotnet restore ECommerceStore.sln`; `dotnet build ECommerceStore.sln --no-restore`; warnings reviewed, not blindly suppressed.  
**Test checks:** `dotnet test ECommerceStore.sln --no-build` passes the template smoke test.  
**Manual validation:** `dotnet run --project src/ECommerceStore.Web`; home and error routes render on localhost.  
**Git checkpoint recommendation:** local commit “Create .NET 8 solution foundation.”  
**DoD:** clean restore/build/test from repository root, no Docker/external database, structure matches architecture, and secret/runtime artifacts are ignored.

## Phase 2 — Data model, exact money, Identity, migration, and seed

**Risk:** **HIGH RISK — deeper reasoning required**  
**Objective:** establish the complete SQLite schema, Identity roles/users, exact money conversion, concurrency model, and environment-safe seed data before features depend on it.  
**Dependencies:** Phase 1; approved currency/threshold.  
**Expected later files/components:** entities/configurations, `ApplicationDbContext`, money converter/comparer, Identity setup, initializer, initial migration, data integration tests.

**Steps**

1. Implement every field/relationship/constraint/index in `DATABASE_DESIGN.md`; do not expose entities as input models.
2. Implement and unit-test decimal↔minor-unit conversion, scale/range validation, and shared rounding/calculation primitives.
3. Configure `ApplicationUser`, unique normalized email, Customer/Administrator roles, enabled-user principal validation, password policy, and safe cookie settings.
4. Implement idempotent role/category/product seed and Development-only administrator rules.
5. Create the initial migration only after model tests pass; inspect generated migration and model snapshot line by line.
6. Apply migration to a fresh temporary/local development database; verify foreign keys and indexes via SQLite metadata.

**Security checks:** Identity tables retain framework protections; no development admin in Production; delete behaviors protect history; no payment secret fields; user inputs cannot set audit/concurrency fields.  
**Build checks:** clean restore/build; compile with nullable enabled; no migration warnings.  
**Test checks:** model metadata, money round-trip/bounds/order/filter/aggregate, FK/delete behavior, unique constraints, version conflicts, seed idempotency, Production admin absence. Use real SQLite.  
**Manual validation:** migrate an empty file twice, inspect seeded counts/characteristics, log in as Development admin, and prove predictable admin is absent under Production configuration.  
**Git checkpoint recommendation:** local commit “Add SQLite data and Identity foundation.”  
**DoD:** fresh/repeat migrations work; schema matches design; exact money and concurrency tests pass; required seed exists only in correct environment.

## Phase 3 — Storefront shell and catalog details

**Risk:** STANDARD  
**Objective:** deliver public home/list/category/detail navigation with active-only products and coherent view models.  
**Dependencies:** Phase 2.  
**Expected later files/components:** home/catalog controllers, catalog service/query projections, storefront view models/views, nav/footer/cart-count placeholder, fallback image, CSS design tokens.

**Steps**

1. Implement active-only home featured/latest projections and public catalog/category/detail endpoints.
2. Add slug normalization and product detail projection with effective price/discount/stock/category.
3. Add bounded related-product query.
4. Build shared responsive layout, search entry, footer, empty states, feedback partials, and image resolver/fallback.
5. Add unit/service/web tests before adding visual polish beyond baseline.

**Security checks:** output encoded; no entity binding; inactive product/category not disclosed by public detail; external image URL scheme allow-list.  
**Build checks:** build solution; Razor compilation warnings resolved.  
**Test checks:** home sections, active visibility, slug lookup, detail fields, related-product constraints, 404/missing image.  
**Manual validation:** browse seeded home/category/detail on desktop and mobile; verify out-of-stock action disabled but server remains authoritative later.  
**Git checkpoint recommendation:** local commit “Add storefront catalog pages.”  
**DoD:** all `STORE-001`, `PROD-001`, and `PROD-002` acceptance criteria that do not depend on cart pass.

## Phase 4 — Search, filtering, sorting, and pagination

**Risk:** MEDIUM RISK  
**Objective:** implement one composable and bounded product query with stable URLs.  
**Dependencies:** Phase 3 and exact money converter from Phase 2.  
**Expected later files/components:** filter/sort model, paged result, query normalization, query-string UI, query integration tests.

**Steps**

1. Define allow-listed sort values and bounded query/page parameters.
2. Compose active/category/keyword/min/max/in-stock predicates and effective-price expression.
3. Add each requested sort plus deterministic `Id` tiebreaker, count, and `Skip/Take` projection.
4. Preserve normalized filters in pagination/sort links and search form.
5. Measure generated SQL/query plans for seeded data and add indexes only when justified by planned queries.

**Security checks:** no dynamic string-based order expression; bound keyword/page size; encode reflected query state.  
**Build checks:** normal clean build.  
**Test checks:** every filter and sort alone/together, invalid ranges/values, URL retention, stable page boundaries, SQLite decimal-price ordering.  
**Manual validation:** execute manual workflow step 5 and bookmark/reload representative query URLs.  
**Git checkpoint recommendation:** local commit “Add catalog query pipeline.”  
**DoD:** `QUERY-001` and `QUERY-002` pass with correct combined results and friendly empty states.

## Phase 5 — Anonymous/authenticated cart and merge

**Risk:** **HIGH RISK — deeper reasoning required**  
**Objective:** provide trusted calculations, full mutations, session cart, database cart, and loss-safe merge.  
**Dependencies:** Phases 2–4; approved shipping value.  
**Expected later files/components:** cart DTOs/view models/calculator, session serializer/store, database cart service, merge service, controller/views/view component, auth event integration.

**Steps**

1. Implement pure cart calculator using list/paid prices, per-line rounding, discount, shipping, and grand total.
2. Implement versioned/bounded anonymous session store with repair behavior.
3. Implement authenticated database cart mutations and unique-line/concurrency handling.
4. Use a resolver/facade to select cart by authentication state without duplicating business rules.
5. Implement login/registration merge exactly as `ARCHITECTURE.md` §9.4; clear session only post-commit.
6. Add cart views, POST-only mutations, item-count component, and warnings for changed availability.

**Security checks:** anti-forgery; ignore posted prices/totals/user IDs; quantity/product/activity/stock checks; safe session cookie and corrupt JSON handling.  
**Build checks:** clean build and no sync-over-async.  
**Test checks:** all arithmetic boundaries; zero/negative/over-stock/invalid/inactive; mutations; corrupt session; duplicate merge; merge rollback; existing+anonymous cap; multi-tab concurrency.  
**Manual validation:** add anonymously, navigate, login/register, verify no loss; edit/clear/count/totals; tamper quantity/product/price requests.  
**Git checkpoint recommendation:** local commit “Add resilient hybrid cart.”  
**DoD:** every `CART-*` criterion passes; session cannot be trusted for prices; failed merge preserves recoverable anonymous state.

## Phase 6 — Registration completion and resilient email

**Risk:** MEDIUM RISK  
**Objective:** complete registration/login/logout/disable checks and welcome email without coupling account success to SMTP.  
**Dependencies:** Identity Phase 2, cart merge Phase 5.  
**Expected later files/components:** account input models/controllers/views, email contracts/composer/transports/options, principal validator, email tests.

**Steps**

1. Implement registration plus Customer-role assignment in one short Identity database transaction, followed by login/logout, local return URLs, and friendly non-enumerating validation.
2. Integrate cart merge after authentication success.
3. Implement typed email messages/composer, file transport, MailKit SMTP transport, resilient selection/fallback.
4. Attempt welcome email after account success and log typed result safely.
5. Implement admin-compatible enabled-user validation now; UI actions arrive later.

**Security checks:** password never logged/repopulated; no public role binding; SMTP TLS verification; secret from environment/user secrets; safe HTML encoding/file names/paths.  
**Build checks:** build with environment option validation.  
**Test checks:** registration validation/duplicate; role-assignment rollback; login/logout/disable; safe return URL; file fallback; SMTP failure; registration remains; email encoding/no secrets.  
**Manual validation:** register with SMTP absent, inspect `App_Data/Emails`, login/logout, and confirm safe messages.  
**Git checkpoint recommendation:** local commit “Add Identity account flows and resilient email.”  
**DoD:** all `AUTH-*` and `EMAIL-001` criteria pass; registration/cart merge survive email failure.

## Phase 7 — Fake payment and atomic checkout

**Risk:** **HIGH RISK — deeper reasoning required**  
**Objective:** create idempotent, privacy-safe, transactionally consistent checkout with no real payment integration.  
**Dependencies:** Phases 2, 5, 6; approved decline card.  
**Expected later files/components:** checkout view models/controller/views, fake payment service, checkout service, attempt/order/payment creation, transaction/concurrency tests.

**Steps**

1. Implement transient card input validation and deterministic sanitized fake service.
2. Implement checkout GET summary/token and authenticated POST with anti-forgery/local redirects.
3. Implement attempt claim, authoritative cart/product reload and total calculation.
4. Implement failed-payment commit path with sanitized metadata and unchanged stock/cart.
5. Implement deterministic conditional stock claims, snapshots, unique numbers, order/items/payment, cart clear, attempt success in one transaction.
6. Handle unique collision, duplicate token states, concurrency/SQLite busy outcomes, and stale fake-attempt recovery.
7. Redirect through owner-scoped confirmation; do not yet depend on email/PDF success.

**Security checks:** explicitly review logs/model state/database for PAN/CVV/expiry; no posted total/price/user/status; anti-replay token user-bound; stock predicate parameterized.  
**Build checks:** clean build plus migration/model unchanged unless reviewed.  
**Test checks:** success/decline/invalid input; transaction rollback at each injected save/stock failure; last-unit race; multi-line rollback; double-click/concurrent duplicate; repeated successful token; stale processing; sensitive-data scan.  
**Manual validation:** success card, decline card, refresh/back/double-submit, tampered totals, stock change between GET/POST, concurrent browsers.  
**Git checkpoint recommendation:** local commit “Add atomic idempotent fake checkout.”  
**DoD:** at most one balanced order per token; no oversell/sensitive persistence; all core state is atomic; failure leaves an actionable cart.

## Phase 8 — Customer orders and confirmation

**Risk:** **HIGH RISK — ownership/IDOR**  
**Objective:** expose immutable owner-scoped order confirmation, paginated history, and details.  
**Dependencies:** Phase 7.  
**Expected later files/components:** order query service, customer order controller/views/view models, ownership web tests.

**Steps**

1. Project order list/detail only from snapshots with `(resource, currentUserId)` predicates.
2. Add confirmation endpoint safe to refresh and owner-scoped.
3. Add My Orders pagination, statuses, item count, details/invoice placeholders.
4. Standardize non-owner/nonexistent response as 404 and log only safe audit context.

**Security checks:** no broad `Find(id)` followed by UI-only check; no user ID from route/form; no catalog dependency for history.  
**Build checks:** clean build/Razor compile.  
**Test checks:** own list/detail/confirmation, another user's ID/number, anonymous challenge, snapshot immutability after catalog/profile changes.  
**Manual validation:** manual steps 12–15, including crafted URL.  
**Git checkpoint recommendation:** local commit “Add owner-scoped order history.”  
**DoD:** `ORDER-003` and `ORDER-004` pass; IDOR tests prove service/query ownership.

## Phase 9 — PDF invoices and order confirmation email

**Risk:** **HIGH RISK — authorization and post-commit failure**  
**Objective:** generate professional invoices from snapshots, enforce download ownership, and send complete order email without invalidating checkout.  
**Dependencies:** Phases 6–8.  
**Expected later files/components:** invoice model/service/template, invoice download endpoint, order email composer, post-commit orchestrator/result UI, PDF/email tests.

**Steps**

1. Configure QuestPDF license appropriate to project use and verify platform support.
2. Build invoice projection and professional layout containing every required field.
3. Attempt generation after checkout commit; generate again on authorized download; never save publicly.
4. Add links to confirmation/list/detail through one owner-scoped endpoint.
5. Compose/send order email post-commit with order summary/view/invoice information.
6. Add friendly retry state for PDF failure and safe logs/fallback for email failure.

**Security checks:** owner predicate before PDF generation; encoded/bounded text; sanitized filename; no path-based invoice lookup; no payment secrets/address in logs.  
**Build checks:** clean build on host architecture.  
**Test checks:** PDF signature/nonempty/content extraction, three entry points, cross-user denial, generator exception, fallback file, SMTP failure, successful order remains.  
**Manual validation:** open/download PDF in native viewer; inspect required content/layout; simulate service failures.  
**Git checkpoint recommendation:** local commit “Add secure invoice and order notifications.”  
**DoD:** every successful order has a stable invoice number and retryable invoice; all `PDF-*`/`EMAIL-002` criteria pass.

## Phase 10 — Admin dashboard, catalog, orders, and customers

**Risk:** **HIGH RISK — authorization and historical integrity**  
**Objective:** deliver the complete role-protected admin area with safe projections, lifecycle actions, and status policy.  
**Dependencies:** Phases 2–9.  
**Expected later files/components:** Admin area/layout/controllers/view models/views/services; dashboard queries; product/category/order/customer CRUD/status operations.

**Steps**

1. Add Admin area authorization convention, explicit role guards, separate layout/navigation, and access-denied handling.
2. Implement dashboard definitions/aggregates with configurable low-stock threshold.
3. Implement paginated product list/search/filter/create/edit/deactivate and safe hard-delete decision; image URL only until Phase 11 upload.
4. Implement paginated category management with unique slug/name and restrict/deactivate behavior.
5. Implement admin order list/search/filter/details and concurrency-checked transition table.
6. Implement safe customer list/search/profile/orders and disable/re-enable; prevent self/last-admin disable.
7. Add confirmation POST patterns and conflict feedback for all destructive/state actions.

**Security checks:** admin role on every route/action; dedicated allow-list view models; no Identity internals; anti-forgery; immutable order data; stale version handling.  
**Build checks:** clean build; area view discovery works.  
**Test checks:** anonymous/customer/admin matrix for every controller; dashboard definitions; CRUD validation/unique conflict; delete restrictions; transition table/stale conflict; disable session rejection/no internals.  
**Manual validation:** manual steps 16, 17, 19–23; inspect mobile admin tables.  
**Git checkpoint recommendation:** local commit “Add protected administrator area.”  
**DoD:** all `ADMIN-*` criteria pass; ordinary customer cannot reach any admin operation; historical output unchanged after admin catalog edits.

## Phase 11 — Secure local image upload

**Risk:** **HIGH RISK — untrusted file handling**  
**Objective:** add local raster uploads and safe replacement/cleanup without trusting client metadata.  
**Dependencies:** Admin products Phase 10 and storefront fallback Phase 3.  
**Expected later files/components:** image options/service/signature decoder, upload inputs, managed directory, cleanup/error handling, attack tests.

**Steps**

1. Choose the minimal maintained decoder/signature strategy compatible with both OSes; record rationale/package.
2. Implement request/file count, extension, size, claimed MIME, decoded signature/type, dimension (if needed), and path-root checks.
3. Generate random canonical filenames and create-new writes.
4. Coordinate new file, product update, old-file cleanup, and failure compensation.
5. Add safe local/external/none choice and missing-image fallback.

**Security checks:** reject SVG, executable/mixed content, traversal, double extension, spoofed MIME, oversized/decompression-bomb inputs; uploads not executable; CSP/nosniff considered.  
**Build checks:** build for current RID without missing native assets.  
**Test checks:** every allowed type; mismatch/spoof/truncation/oversize/empty/traversal/double-extension/polyglot; random naming; write/database/delete failure; missing file.  
**Manual validation:** manual invalid-image step 18 plus valid upload/replacement on macOS and later Windows.  
**Git checkpoint recommendation:** local commit “Add secure product image uploads.”  
**DoD:** `UPLOAD-001` passes adversarial tests and no failed operation leaves an unsafe reference or public executable file.

## Phase 12 — Provider-independent AI seam

**Risk:** STANDARD for required seam; optional demo  
**Objective:** add a no-network mock abstraction without coupling core commerce behavior.  
**Dependencies:** Phase 10 admin product form.  
**Expected later files/components:** `IAIService`, options, deterministic mock; optional admin draft action/UI.

**Steps**

1. Implement provider-neutral interface/options/result and deterministic `MockAIService`.
2. Prove no catalog/cart/checkout service requires AI.
3. If optional AI-002 is approved, add explicit admin-only draft POST that returns editable unsaved text with bounds and anti-forgery.
4. Document future provider registration and security/cost/privacy requirements—no provider key or package now.

**Security checks:** encode mock output; admin authorization; no auto-save/overwrite; no secret setting.  
**Build checks:** build with AI disabled and mock mode.  
**Test checks:** deterministic mock, cancellation/error, core flows with AI disabled; optional endpoint auth/CSRF/encoding.  
**Manual validation:** app works with service disabled; optional draft is editable and requires explicit save.  
**Git checkpoint recommendation:** local commit “Add provider-independent AI seam.”  
**DoD:** `AI-001` passes; AI-002 only if approved; no external API call/dependency exists.

## Phase 13 — UI, accessibility, error, and security polish

**Risk:** MEDIUM RISK  
**Objective:** complete consistent responsive UX and defense-in-depth review across all finished features.  
**Dependencies:** Phases 3–12.  
**Expected later files/components:** final CSS/JS, shared components, error/access-denied/404 views, CSP/security headers as compatible, responsive admin/card/form refinements.

**Steps**

1. Apply consistent visual tokens, card/grid/forms/buttons/badges/feedback/empty states.
2. Ensure visible labels/focus, skip link, keyboard actions, semantic status messages, contrast, and non-color cues.
3. Test responsive nav, storefront grids, checkout/account forms, order/admin tables.
4. Complete exception/status handling and correlation IDs; verify no Production diagnostics.
5. Perform explicit CSRF/XSS/SQL/IDOR/over-post/redirect/cookie/session/log/upload/payment threat review.
6. Minimize necessary JavaScript and provide server fallbacks.

**Security checks:** execute the full §20 architecture threat checklist and inspect rendered source/logs/configuration.  
**Build checks:** release build with warnings reviewed.  
**Test checks:** web security headers/tokens/encoding/redirects/error environments; automated accessibility scan where available.  
**Manual validation:** keyboard-only customer/admin journeys; 1440/768/390 viewports; forced 404/access denied/exception/missing image.  
**Git checkpoint recommendation:** local commit “Polish responsive accessible secure UI.”  
**DoD:** `UI-*` and `SEC-*` criteria pass with no serious/critical accessibility issue or sensitive error/log disclosure.

## Phase 14 — Complete automated suite and failure injection

**Risk:** **HIGH RISK — false confidence if environment is unrealistic**  
**Objective:** fill all test catalog entries and prove failure/concurrency behavior using appropriate layers.  
**Dependencies:** implementation feature-complete through Phase 13.  
**Expected later files/components:** unit/integration/web/security fixtures, SQLite temporary database factory, fake clock/number/email/PDF/failure injectors, coverage report configuration if approved.

**Steps**

1. Cross-check every test in `TEST_PLAN.md` and requirement traceability.
2. Keep pure calculations/policies unit-level; use real SQLite for converters, constraints, transactions, concurrency.
3. Use `WebApplicationFactory` for routing, auth, anti-forgery, ownership, area, error behavior.
4. Add deterministic barriers/two contexts for stock concurrency and simultaneous duplicate submission.
5. Inject failure before/after each checkout boundary, email transport, PDF, file, and database save.
6. Run tests individually, by category, together, repeatedly, and with randomized order where supported.

**Security checks:** test data/log artifacts contain no real secrets or card data; test-only auth hooks cannot activate outside Testing.  
**Build checks:** `dotnet build ECommerceStore.sln -c Release`; optionally treat project warnings as errors after generated/package exceptions are understood.  
**Test checks:** `dotnet test ECommerceStore.sln -c Release --no-build`; run concurrency suite repeatedly for at least 20 iterations without flakes.  
**Manual validation:** inspect representative failures and test artifact cleanup.  
**Git checkpoint recommendation:** local commit “Complete automated acceptance suite.”  
**DoD:** all required test IDs pass deterministically and no requirement lacks task/test coverage.

## Phase 15 — macOS and Windows verification

**Risk:** **HIGH RISK — platform/native/library differences**  
**Objective:** validate the documented clean workflow and all platform-sensitive features on Apple Silicon macOS and Windows.  
**Dependencies:** Phase 14.  
**Expected later files/components:** compatibility evidence/checklist and any portability fixes; no platform forks unless unavoidable and approved.

**Steps**

1. On each OS, start from a clean clone/copy without database/build/runtime artifacts.
2. Confirm supported .NET 8 SDK, restore local tools/packages, apply migration, run seed, build Release, test Release, and run application.
3. Validate SQLite transaction/concurrency, QuestPDF output, image uploads/paths, development email files, user secrets/environment syntax, HTTPS/browser behavior, and case-sensitive static assets.
4. Run essential customer/admin manual workflow in current Safari/Chrome (macOS) and Edge/Chrome (Windows).
5. Record exact OS/architecture/SDK/browser and results; fix shared code rather than creating silent platform divergence.

**Security checks:** secure cookie/HTTPS behavior and file permissions/paths on each OS; no test secret files copied into repository.  
**Build checks:** restore/build Release succeeds from clean state on both.  
**Test checks:** full suite plus repeated concurrency tests on both.  
**Manual validation:** `TEST_PLAN.md` platform matrix and 24-step workflow.  
**Git checkpoint recommendation:** local commit “Verify cross-platform compatibility.”  
**DoD:** `PLAT-001` and `TEST-004` pass on actual Apple Silicon macOS and Windows, or any unavailable environment is explicitly reported as an acceptance blocker—not assumed.

## Phase 16 — README, final audit, and handoff

**Risk:** MEDIUM RISK  
**Objective:** produce accurate operational documentation and perform the final requirement/security/data review.  
**Dependencies:** Phase 15.  
**Expected later files/components:** `README.md`, final evidence/checklists, known-limitations section.

**Steps**

1. Write every README topic in `DOC-001`, including exact root commands and OS-specific user-secrets/environment examples.
2. Execute every documented command from clean state on macOS and Windows; correct documentation or implementation.
3. Re-run line-by-line requirements/traceability audit; map every final task/test result.
4. Review Identity/roles, IDOR, cart merge, transaction boundary, stock/duplicate protection, rounding, uploads, payment privacy, email/PDF failures, delete behavior, SQLite, and both OSes.
5. Run final Release build/test/manual acceptance and inspect scoped Git diff for secrets/runtime artifacts.

**Security checks:** secret scan, dependency vulnerability review using approved tooling, log/database/artifact inspection, Production configuration smoke test.  
**Build checks:** clean `dotnet restore`, `dotnet build -c Release`, migration, run.  
**Test checks:** clean `dotnet test -c Release`; all acceptance evidence recorded.  
**Manual validation:** complete `TEST_PLAN.md` final checklist and documentation walkthrough.  
**Git checkpoint recommendation:** local commit “Complete ECommerceStore acceptance documentation” only after user review.  
**DoD:** README is exact; no unmapped requirement/open required task/failing test/security blocker; fresh setup works on both OSes; known limitations are honest.

## Final approval gate

Implementation is complete only when all required task checkboxes and phase DoDs are satisfied, all required tests pass, cross-platform evidence exists, and the user accepts the result. Optional tasks may remain unchecked only when clearly marked optional and do not impair required behavior. A local commit, remote creation, push, deployment, or external account connection always requires its own authorization when not already explicitly requested.
