# ECommerceStore Test Plan

## 1. Purpose and release rule

This plan verifies functional correctness, data integrity, authorization/ownership, privacy, failure behavior, accessibility, responsive design, and cross-platform operation. A required test is not replaced by a manual claim when it can be automated. Release acceptance requires all required `TC-*` groups and the final checklist to pass; optional groups may remain open only when their associated optional feature is not implemented.

Each `TC-<requirement>` is the traceable test group referenced by `REQUIREMENTS.md`. Implementations may add stable suffixes (for example `TC-CART-003-A`) without removing the group ID.

## 2. Layering strategy

| Layer | Best suited for | Required infrastructure |
|---|---|---|
| Unit | rounding/totals, query normalization, status transitions, fake-payment rules, number/slug policy, email/PDF model composition, mock AI | no ASP.NET host/database; deterministic clock/data |
| Service/database integration | EF mapping, converter/query behavior, unique/FK/check/delete constraints, cart merge, checkout transaction, conditional stock, concurrency, seed | real SQLite; temporary file for concurrency, shared open in-memory connection for selected fast tests; foreign keys on |
| ASP.NET Core integration | routing, model binding, anti-forgery, cookies/session, Identity, roles, owner scoping, areas, error behavior, redirects, downloads | `WebApplicationFactory`, Testing configuration, real SQLite, HTTP clients/cookies/tokens |
| Adapter tests | SMTP/file fallback, PDF bytes/content, upload validation/storage/cleanup | temporary directories; fake SMTP/transport; controlled sample files; no external network |
| Static/security review | packages/configuration, forbidden fields/log calls, environment branches, exact docs/traceability | repository searches plus human review; never treated as sole auth control |
| Manual browser | visual polish, keyboard UX, PDF appearance, responsive layouts, whole workflows | supported browsers on actual macOS and Windows |

Do not use EF Core InMemory to claim relational, transaction, precision, FK, delete, or concurrency correctness. Each test owns isolated data and time. Parallelize pure unit tests; serialize tests sharing SQLite files/directories or explicitly isolate them.

## 3. Test environments and data

### 3.1 Automated host

- Environment name: `Testing`, never `Development` or `Production` masquerading silently.
- Fresh schema created by applying migrations; selected migration tests invoke the same explicit migration path as users.
- Deterministic seed/test builders create Administrator, customer A, customer B, active/featured/discounted/low-stock/out-of-stock/inactive products, two categories, carts, and orders.
- Fake clock and number generator are injectable. Generated values remain unique per test.
- Fake email/PDF/image/storage failure adapters are registered only in the test host; startup asserts they cannot activate in Production.

### 3.2 Sensitive test data

Use only documented dummy cards. Never print request bodies or card fields in failure output. Test logs and temporary databases are scanned for full success/decline PAN, CVV, password, SMTP secret, security stamp, and authentication cookie. Temporary artifacts are deleted after tests unless intentionally retained as sanitized failure evidence.

### 3.3 Concurrency harness

Use a temporary SQLite file, two independent service scopes/`DbContext` connections, a barrier to align operations, and a bounded service retry policy. Timeouts detect deadlocks. Assert durable end state by opening a third fresh context—not by trusting either racing context's tracked state.

## 4. Traceable test catalog — governance, technology, and architecture

| Test ID | Requirement | Preconditions | Steps | Expected result |
|---|---|---|---|---|
| **TC-GOV-001** | GOV-001 | Planning handoff | List repository-root planning files; inspect cross-links/content | Exactly six primary planning files exist and are self-contained |
| **TC-GOV-002** | GOV-002 | Planning handoff | Inspect files, Git diff/status, installed-project artifacts/remotes | No prohibited planning-phase artifact/action exists |
| **TC-GOV-003** | GOV-003 | Six drafts complete | Compare spec line by line; compare IDs/tasks/tests; inspect final response | Review is complete, problems corrected, and approval gate remains closed |
| **TC-APP-001** | APP-001 | Feature-complete clean install | Run final 24-step workflow from fresh database | Integrated locally runnable store completes all required flows |
| **TC-TECH-001** | TECH-001 | Solution exists | Inspect target/framework references; build/run; view UI assets | Required .NET 8 MVC/EF SQLite/Identity/Bootstrap stack is used; JS is non-SPA/minimal |
| **TC-TECH-002** | TECH-002 | Packages restored | Exercise SMTP adapter, PDF adapter, xUnit; inspect package compatibility/license | Approved libraries work on both OSes and license choice is documented |
| **TC-TECH-003** | TECH-003 | Clean machine/copy | Execute documented restore, local-tool restore, migrate, run without Docker | Store runs locally with no container/external database dependency |
| **TC-ARCH-001** | ARCH-001 | Feature implementation | Inspect project/dependency layout and representative controllers/services | Two-project layered structure is followed; controllers are thin; async I/O has no blocking calls |
| **TC-OPT-001** | OPT-001 | Optional Docker docs exist, if chosen | Follow normal path without Docker; inspect wording | Docker remains optional and Docker-free workflow remains complete |
| **TC-OPT-002** | OPT-002 | Any future integration proposed | Review change scope/threat model/configuration | No real provider/storage swap occurs without separate approved design |

## 5. Traceable test catalog — accounts, storefront, queries, and cart

| Test ID | Requirement | Preconditions | Steps | Expected result |
|---|---|---|---|---|
| **TC-AUTH-001** | AUTH-001 | Fresh app; no matching user | Submit valid registration; parameterize missing/overlength/invalid email/weak/mismatch/duplicate cases | Valid creates one Customer; invalid creates none and shows friendly server errors without repopulating password |
| **TC-AUTH-002** | AUTH-002 | Enabled user | Login valid/invalid; inspect stored user; logout then revisit protected route | Valid cookie works, invalid message does not enumerate, no plaintext password, logout session is rejected |
| **TC-AUTH-003** | AUTH-003 | Anonymous, customer, admin clients | Request protected customer/admin endpoints and access-denied path | Challenge/deny/allow matrix matches roles independently of hidden navigation |
| **TC-AUTH-004** | AUTH-004 | Logged-in customer; admin client | Admin disables customer; customer requests next page/logs in; re-enable and retry; inspect admin HTML | Active session/new login rejected while disabled, restored after enable, no Identity internals rendered |
| **TC-STORE-001** | STORE-001 | Mixed seeded catalog | Visit home/list/category through nav at empty and populated states | All required shell/pages/sections/count/footer/feedback exist; only public data appears |
| **TC-PROD-001** | PROD-001 | Admin and mixed products | Persist/edit every field; attempt duplicate slug/invalid price-stock; query public inactive | Constraints work, timestamps update, inactive excluded, effective price correct |
| **TC-PROD-002** | PROD-002 | Active discounted product plus related/out-of-stock | Open detail; inspect all fields/related; attempt add unavailable | Required details render; related set bounded/valid; unavailable add is blocked server-side |
| **TC-QUERY-001** | QUERY-001 | Catalog spans categories/prices/stock/text/case and literal `%`/`_` characters | Apply each filter then combinations; include min>max/long/wildcard keyword | Combined active-only results are correct with documented ASCII-insensitive literal search; invalid values safe; empty state friendly |
| **TC-QUERY-002** | QUERY-002 | Enough products for 3 pages/ties | Run each sort across pages; change sort/page; reload query URL; supply invalid sort/page | Ordering/tiebreaker stable, filters retained in URL/UI, invalid values default/bound safely |
| **TC-CART-001** | CART-001 | Active normal/discount products; configured shipping | Exercise every mutation and compare calculated/presented totals at each state | Count is total units; list subtotal/discount/shipping/grand total follow exact formula; mutations are POST-only |
| **TC-CART-002** | CART-002 | Anonymous HTTP client/session | Add/navigate/restart request; tamper/corrupt/expire session payload | Valid IDs/quantities persist in session; corrupt/unbounded state safely repairs; no trusted price stored |
| **TC-CART-003** | CART-003 | User DB cart + anonymous overlapping/non-overlapping/inactive lines | Login/register; race/repeat merge hook; inject commit failure | Valid lines merge once, duplicates sum/cap, unavailable warned/dropped, session clears only after commit |
| **TC-CART-004** | CART-004 | Products invalid/inactive/out-of-stock/limited/negative-price DB fixture attempt | Craft zero/negative/huge quantity and invalid ID requests; mutate product between display/POST | Requests reject/repair atomically; quantity never exceeds stock; inactive/invalid/negative data never orders |

### 5.1 Required cart calculation vectors

Use table-driven unit cases including empty cart, one normal line, one discounted line, multiple quantities, multiple lines, zero shipping for empty, configured shipping for nonempty, midpoint rounding vectors, maximum valid amounts, overflow, and invalid negative/excess-scale input. For every vector assert:

```text
Subtotal - Discount + Shipping = GrandTotal
sum(OrderItem.LineTotal) = Subtotal - Discount
```

No expected result may be calculated by the same production function under test.

## 6. Traceable test catalog — checkout, payment, orders, and data

| Test ID | Requirement | Preconditions | Steps | Expected result |
|---|---|---|---|---|
| **TC-CHECK-001** | CHECK-001 | Anonymous and logged-in users with cart | GET/POST checkout; omit/overlength address fields; tamper summary; inspect failed response | Anonymous challenged with local return; fields validate; POST recomputes summary; card secrets not redisplayed |
| **TC-PAY-001** | PAY-001 | Fake service, fixed clock | Submit success card/future expiry/3-digit CVV; decline card; expired/malformed/other numbers | Deterministic sanitized success/decline/validation; no network; decline creates no order/stock change |
| **TC-PAY-002** | PAY-002 | Logging/database capture enabled | Complete/fail checkout; inspect DB, structured logs, response HTML/model state, email/PDF | No full PAN/CVV/expiry anywhere; only approved reference/result/brand/last4 metadata exists |
| **TC-ORDER-001** | ORDER-001 | Authenticated cart with valid stock | Successful checkout, then reload all rows and confirmation | One balanced order/all items/snapshots/unique numbers/status/payment, stock reduced, cart empty, side effects attempted |
| **TC-ORDER-002** | ORDER-002 | Failure injectors; competing clients | Inject at each write/stock line; race last unit; submit same token concurrently/repeatedly; test decline | Atomic rollback, no oversell, at most one order/token, repeat returns prior result, decline preserves cart/stock |
| **TC-ORDER-003** | ORDER-003 | Customer with several snapshot orders | View paginated list/detail; edit/delete source catalog/profile | Every requested field/action appears; newest order; historical output unchanged |
| **TC-ORDER-004** | ORDER-004 | Customer A order; customer B/anonymous/admin clients | Request details/confirmation/invoice by ID/number as each identity; call service directly with B | A/admin-authorized path succeeds; B gets non-disclosing 404; anonymous challenged; service itself scopes owner |
| **TC-DATA-001** | DATA-001 | Migrated fresh database | Inspect EF model/SQLite schema and create each entity relationship | Every planned entity/table/key/relation exists; no invoice table/card-secret column |
| **TC-DATA-002** | DATA-002 | Migrated database | Test null/length/check/unique/FK/index/timestamp constraints and inspect migration | App and DB enforce specified validation; required indexes/UTC audit values exist |
| **TC-DATA-003** | DATA-003 | Money converter/calculator and SQLite rows | Round-trip boundaries; filter/sort/sum; line midpoint calculations; reject scale/negative/overflow | Exact two-decimal results use AwayFromZero; persisted/displayed totals match; no float conversion |
| **TC-DATA-004** | DATA-004 | Two contexts and low stock | Save stale catalog/order edits; execute simultaneous last-unit claims; fail later line | Stale edit conflicts; one stock success max; version increments; rollback restores earlier decrement |
| **TC-DATA-005** | DATA-005 | Completed order | Change user/address/product/category/prices; force number collision | Snapshots/PDF stay unchanged; unique collision retries; number remains opaque/nonsequential |
| **TC-DATA-006** | DATA-006 | Referenced category/product/user/order/cart plus unused product | Attempt each delete, unused-product hard delete, and an out-of-band product removal fixture | Historical/category/user deletes are blocked/deactivated; only unused product hard-deletes normally; nullable historical FK/snapshots remain defensive |
| **TC-SEED-001** | SEED-001 | Fresh Development and Production databases | Run initializer twice in each environment; inspect roles/admin/categories/products/traits | Idempotent counts; 12+ varied products/featured/low/out; dev admin only; no predictable Production admin |

### 6.1 High-risk checkout cases

#### TC-ORDER-002-A — Multi-line rollback after partial stock claim

- **Requirement:** ORDER-002, DATA-004, SEC-005
- **Preconditions:** product A stock 5, product B stock 0, cart requests A×2 and B×1; deterministic ProductId ordering makes A update first.
- **Steps:** submit valid success-card checkout; wait for result; read with a fresh context.
- **Expected:** no order/items/success payment; A stock remains 5, B remains 0; cart remains; the in-transaction attempt is absent because the entire core transaction rolled back; friendly stock message.

#### TC-ORDER-002-B — Last-unit concurrent purchase

- **Requirement:** ORDER-002, DATA-004, SEC-005
- **Preconditions:** product stock 1; customer A and B each cart quantity 1 with distinct tokens; two service scopes blocked at a barrier.
- **Steps:** release both checkout operations; apply bounded lock retries; inspect durable data.
- **Expected:** exactly one order/item and one decrement; final stock 0, never negative; loser retains cart and receives stock/conflict response; no unhandled SQLite lock leak.

#### TC-ORDER-002-C — Duplicate token replay

- **Requirement:** ORDER-002, SEC-005
- **Preconditions:** one user/cart/token; two HTTP clients or requests sharing auth/session and token.
- **Steps:** submit simultaneously, then submit again after completion and refresh confirmation.
- **Expected:** exactly one attempt/order/payment success and one stock decrement; all completed repeats resolve to that same owner order; no duplicate email trigger from simple confirmation retrieval.

#### TC-ORDER-002-D — Failure injection matrix

Inject before attempt insert, after attempt claim, after fake payment, after each stock update, before/after order insert, item insert, payment insert, cart clear, attempt completion, save, commit, PDF, and email. Before commit, expect full core rollback except deliberately committed fake-decline metadata. After commit, expect order/stock/cart durability and only side-effect degradation.

## 7. Traceable test catalog — PDF, email, AI, and admin

| Test ID | Requirement | Preconditions | Steps | Expected result |
|---|---|---|---|---|
| **TC-PDF-001** | PDF-001 | Completed snapshot order | Generate/download from confirmation/list/detail; inspect PDF header, length, extracted/visual fields | Valid readable nonempty PDF includes every required field and consistent values at all entry points |
| **TC-PDF-002** | PDF-002 | Order exists; no stored file; injectable generator failure | Generate twice; inspect storage; force failure then retry; request as non-owner | Uses snapshots/in-memory regeneration; no public file; failure retains order/friendly retry; non-owner blocked before generation |
| **TC-EMAIL-001** | EMAIL-001 | Development SMTP absent/failing and valid registration | Register; inspect fallback and logs; force both transport outcomes | Account succeeds; welcome content file written safely in Development; failure logged sanitized; no secret/diagnostic shown |
| **TC-EMAIL-002** | EMAIL-002 | Completed order; SMTP/file/failure adapters | Compose/send after commit; inspect content; fail SMTP/file; reload order | Required order/customer/product/total/view/invoice info present; fallback rules hold; committed order remains |
| **TC-AI-001** | AI-001 | AI disabled and mock modes | Run all core smoke flows disabled; call mock twice/cancel; inspect configuration/network | Core works without AI; mock deterministic/local/provider-neutral; no API key/call |
| **TC-AI-002** | AI-002 optional | Optional draft endpoint implemented; admin/customer clients | POST draft, edit returned text, navigate without save, test CSRF/role/encoded input | Explicit editable unsaved draft; no overwrite/network; only admin with token allowed |
| **TC-ADMIN-001** | ADMIN-001 | Anonymous/customer/admin | Enumerate every Admin route/action using GET/POST as relevant | Anonymous challenged, customer denied, admin allowed; separate layout/nav; POST still anti-forgery protected |
| **TC-ADMIN-002** | ADMIN-002 | Known fixtures across roles/statuses/payments/stock | Load dashboard and independently query expected metrics | All cards match definitions; Customer-role count excludes admin-only users; successful paid includes later-cancelled orders, pending/failed excludes; threshold applied |
| **TC-ADMIN-003** | ADMIN-003 | Admin; existing historical product order | Exercise list/search/filter/create/edit/stock/price/discount/featured/active/category/url/delete/deactivate | Valid updates persist with version; invalid rejected; unsafe delete deactivates; order snapshot unchanged |
| **TC-UPLOAD-001** | UPLOAD-001 | Admin; temporary upload root; sample corpus | Upload each valid raster and each attack/invalid file; replace; inject file/DB/delete failures | Only bounded correctly detected raster accepted under random contained name; compensation/fallback safe |
| **TC-ADMIN-004** | ADMIN-004 | Used/unused categories | List/create/edit/activate/deactivate; duplicate name/slug; delete used/unused | Unique rules work; used delete blocked with deactivate; no orphan products |
| **TC-ADMIN-005** | ADMIN-005 | Orders in every status/payment combination/email/number | Search/filter/view; parameterize allowed/disallowed/stale transitions, including unpaid→Paid | Results/pagination correct; paid-or-later requires successful payment; status edit never mutates payment; only valid current-version transition changes status/snapshots untouched |
| **TC-ADMIN-006** | ADMIN-006 | Admin/customer datasets | List/search/view/orders/disable/re-enable; inspect response source; attempt self/last-admin disable | Safe fields only; actions work; Identity internals absent; safety guards enforced; disabled session rejected |

### 7.1 PDF validation depth

Automated PDF tests assert `%PDF-`, sensible minimum byte length, no exception, filename/content type, and required text where a maintained parser can be justified. A golden binary comparison is forbidden because metadata/layout engines can vary. Manual visual review checks page breaks, table alignment, long names/addresses, totals, font rendering, and a multi-page order on both OSes.

### 7.2 Upload attack corpus

Include valid JPEG/PNG/WebP; zero-byte; truncated headers; renamed executable; JPEG bytes declared PNG and vice versa; double extensions; `../`/absolute/control-character names; SVG with script; oversize payload; extreme dimensions/decompression bomb fixture; malformed WebP; duplicate random-name scenario; read-only/unavailable root; database failure after write; old-file deletion failure. Do not store genuinely dangerous executable samples outside controlled test bytes.

## 8. Traceable test catalog — UI, platform, security, and documentation

| Test ID | Requirement | Preconditions | Steps | Expected result |
|---|---|---|---|---|
| **TC-UI-001** | UI-001 | Feature-complete views | Review storefront/account/order/admin visual system and destructive flows | Consistent purpose-designed theme, clear controls/badges/feedback, confirmation POST, no premium dependency |
| **TC-UI-002** | UI-002 | Three viewport/browser matrix | Complete key flows by keyboard at each viewport; inspect labels/focus/semantics/overflow | Usable responsive views, visible focus/labels, no essential clipping, serious/critical accessibility findings zero |
| **TC-UI-003** | UI-003 | Fixtures for empty/invalid/success/fail/404/403/500/missing image | Trigger each in Development and Production | Friendly correct state; Production reveals no diagnostic; missing image stable |
| **TC-PLAT-001** | PLAT-001 | Clean Apple Silicon Mac and Windows environments | Restore tools/packages, migrate, seed, build, test, run; exercise PDF/email/upload/SQLite/paths | Same required workflows pass with no OS-specific service/Docker/path assumptions |
| **TC-SEC-001** | SEC-001 | HTTP client and crafted forms | Omit/break anti-forgery on every unsafe action; add protected fields/invalid model values | Requests rejected or protected fields ignored; no state change; server validation applies |
| **TC-SEC-002** | SEC-002 | Anonymous/customer A/B/admin | Enumerate IDOR/admin routes and external/local return URLs | Role/owner matrix enforced server-side; external redirects rejected; no existence disclosure |
| **TC-SEC-003** | SEC-003 | Malicious text/search/file fixtures | Persist/render script markup; SQL-like search; invoke every sort; upload attack corpus | Razor/email/PDF safely encode; queries parameterized/allow-listed; attacks rejected/non-executable |
| **TC-SEC-004** | SEC-004 | Development/Production configs and captured logs/cookies/session | Inspect tracked files/options; run auth transition; inspect headers/logs/artifacts for banned values | Secrets absent; Production secure cookie/HTTPS; session merge safe; banned sensitive data absent |
| **TC-SEC-005** | SEC-005 | Checkout threat harness | Tamper price/total/user/cart/status; CSRF; replay; duplicate; stock races; inspect payment data | Server recomputes/owns data; CSRF/replay blocked; no oversell/duplicate/leak |
| **TC-SEC-006** | SEC-006 | Failure adapters and both environments | Trigger email/PDF/image/DB/concurrency/unexpected exceptions and missing image | Correct rollback/post-commit behavior, safe correlation/log, generic Production UX, detailed Development only |
| **TC-TEST-001** | TEST-001 | Automated suite complete | Locate/run every named required behavior test and inspect results | Each named test exists, is deterministic, and passes together |
| **TC-TEST-002** | TEST-002 | Test project | Review test placement/fixtures and intentionally break relational behavior | Correct layers detect failures; no EF InMemory claim; suite isolated/repeatable |
| **TC-TEST-003** | TEST-003 | Release-like app/browser/data | Execute manual workflow §11 in order and record evidence | All 24 steps pass without undocumented intervention |
| **TC-TEST-004** | TEST-004 | Platform/viewport/accessibility matrix | Execute §12–14 matrices and record tools/versions/results | Required accessibility/responsive/macOS/Windows cells pass |
| **TC-DOC-001** | DOC-001 | Final README and clean copies | Check every required heading/value; execute exact commands on both OSes | README is complete, truthful, and produces a working setup |

## 9. Authorization and ownership matrix

Run at both service and HTTP layers where applicable.

| Resource/action | Anonymous | Customer owner | Other customer | Administrator |
|---|---|---|---|---|
| Public catalog | Allow | Allow | Allow | Allow |
| Anonymous cart | Allow own session | n/a after merge | n/a | n/a |
| Checkout | Challenge | Allow own cart | Cannot select other cart/user | Allowed as own account only; no bypass needed |
| My Orders/list/detail/confirmation | Challenge | Allow own | 404 | Admin uses separate Admin route/service |
| Customer invoice | Challenge | Allow own | 404 before PDF generation | Admin invoice action may use explicit admin service |
| Admin dashboard/catalog/categories/orders/customers | Challenge | Access denied | Access denied | Allow |
| Disable/re-enable | Challenge | Deny | Deny | Allow except self/last-enabled-admin guard |

Changing navigation visibility is never acceptable evidence for this matrix.

## 10. Failure-scenario matrix

| Failure | Injection point | Required durable outcome | User/log outcome |
|---|---|---|---|
| Registration validation | before Identity | no account | friendly fields; no sensitive log |
| Role assignment | after user creation, before registration commit | account/role transaction rolls back; no incorrectly successful user | friendly retry + sanitized error |
| Welcome SMTP/file | post account | account remains | success UX; fallback or safe warning log |
| Cart merge DB | before commit | prior DB cart unchanged; session preserved | retryable friendly message |
| Fake decline | within attempt transaction | failed sanitized attempt/payment only; cart/stock intact | friendly decline; new token on retry |
| Stock conflict | any product claim | no order; all prior claims rolled back; cart intact | availability message |
| Order DB/save/commit | before commit | no partial order/item/stock/cart success | generic retry/correlation |
| Duplicate checkout | claim/index conflict | exactly one order/decrement | existing confirmation or processing message |
| SQLite busy | concurrent writer | bounded whole-operation retry; no duplicate/partial state | friendly retry if exhausted |
| PDF generation | after commit/download | order unchanged | invoice temporarily unavailable/retry; sanitized log |
| Order email | after commit | order unchanged | confirmation succeeds; fallback/log |
| Upload validation | before write | no file/database change | field-level rejection |
| Upload write | before DB | no product change; partial temp cleaned | friendly retry/log |
| Product DB after new file | before commit | old product reference remains; new orphan cleaned | friendly retry/log |
| Old image cleanup | after DB | new reference remains valid | safe warning/retriable cleanup |
| Unexpected Production exception | any boundary | transaction rules respected | generic page + correlation; no stack/path/SQL |

## 11. Manual end-to-end acceptance workflow

Run in order on a fresh migrated/seeded Development database, first as the customer and later as the Development administrator. Capture screenshots or a concise result log without sensitive fields.

1. **Open the store:** home loads, featured/latest/nav/footer/zero cart count are correct.
2. **Register a customer:** valid required/optional fields create/sign in the Customer.
3. **Verify welcome-email fallback:** with SMTP absent, a readable safe local message exists under `App_Data/Emails` and registration succeeded.
4. **Browse products:** list, category, detail, discount, stock, related, missing-image behavior work.
5. **Search/filter/sort/paginate:** combine keyword/category/min/max/in-stock/sort/page; URL/UI/results agree.
6. **Add anonymously:** in a separate signed-out browser/session add normal and discounted products; cart/count/totals update.
7. **Log in without losing cart:** use an account with an existing overlapping DB line; verify merge/sum/cap/warnings.
8. **Update quantities:** increment/decrement/direct update/remove and invalid/over-stock attempt; totals stay correct.
9. **Complete dummy checkout:** submit address/contact and success card; no real network payment; land on owner confirmation.
10. **Confirm stock reduction:** admin/database-visible stock decreased exactly by ordered quantity.
11. **Confirm cart clearing:** database cart and nav count are empty; refresh does not create another order.
12. **Open My Orders:** new order appears with all list fields and invoice action.
13. **View order details:** snapshot lines/totals/address/status/payment/link agree with confirmation.
14. **Download PDF invoice:** from confirmation, list, and detail; open and visually inspect required content.
15. **Attempt unauthorized order access:** customer B changes ID/number for detail/confirmation/invoice and receives non-disclosing denial.
16. **Log in as administrator:** admin layout/dashboard metrics work; normal customer session cannot reuse admin route.
17. **Create/edit a product:** all fields, category, stock, prices/discount, featured/active and image URL work; stale conflict tested.
18. **Validate invalid image upload:** spoofed/mismatched or oversized upload is rejected; valid raster succeeds/fallback works.
19. **Create/edit categories:** unique behavior, activate/deactivate, used deletion block and unused safe delete work.
20. **View customers:** search/profile/orders show safe fields; disable/re-enable test rejects/restores customer session.
21. **Filter orders:** search number/email and status; details/financial snapshots correct.
22. **Update order status:** allowed transition succeeds; disallowed/stale transition fails clearly; payment unchanged.
23. **Confirm customer blocked from admin:** enumerate admin navigation/direct GET/POST; all denied/challenged appropriately.
24. **Test layouts:** repeat core browse/cart/checkout/order/admin actions at desktop, tablet, and mobile sizes without essential clipping/overflow.

Also manually verify fake decline leaves stock/cart/order unchanged, PDF/email failure after commit leaves order valid, custom 404/access-denied/error states, keyboard-only flow, and Production-safe diagnostics.

## 12. Responsive-design matrix

| Viewport | Customer pages | Forms/checkout | Orders/invoice links | Admin |
|---|---|---|---|---|
| 1440×900 | 4-ish column grid as design allows; full nav | labels/errors/summary aligned | totals readable | full table/actions usable |
| 768×1024 | reduced columns; nav collapses appropriately | no clipped inputs/buttons | detail stacks sensibly | responsive wrapper/action group |
| 390×844 | single/small grid; touch targets and cart count visible | keyboard/zoom no horizontal page scroll | lines/totals readable | intentional table scroll/cards; actions reachable |

Check portrait and, for tablet/mobile, one landscape pass. Browser zoom at 200% must retain essential content/function. Images preserve aspect ratio and do not cause layout shift beyond reserved space.

## 13. Accessibility checks

- Keyboard-only: skip link, nav/menu, search/filter, product quantity/cart, registration/login, checkout, orders/download, admin CRUD/status/confirmation.
- Visible focus and logical focus order; focus moved/announced appropriately after validation/modal/alert.
- Every input has persistent label, described validation, meaningful autocomplete where safe (never retain CVV), and error summary links.
- Semantic heading hierarchy, landmarks, buttons versus links, table captions/headers, and status badges not color-only.
- Image alternative text; decorative images ignored; fallback meaningful.
- Color contrast meets WCAG 2.1 AA target; target size/useable touch controls.
- Automated scan (for example axe via approved tooling) on representative pages has no serious/critical issues; manual checks remain required.
- PDF visual/readability check; tagged-PDF conformance is not promised unless separately scoped.

## 14. Cross-platform verification matrix

Record actual values; blank/assumed cells fail acceptance.

| Check | macOS Apple Silicon | Windows |
|---|---|---|
| OS version / architecture | record | record |
| .NET 8 SDK/runtime | record/pass | record/pass |
| `dotnet restore` / local tool restore | pass | pass |
| Fresh `dotnet ef database update` | pass | pass |
| Release build/test | pass | pass |
| SQLite foreign keys/money/concurrency | pass | pass |
| `dotnet run` + HTTPS/browser | pass | pass |
| QuestPDF generation/open | pass | pass |
| Development email fallback path | pass | pass |
| JPEG/PNG/WebP upload/path/replace | pass | pass |
| user-secrets/environment instructions | pass | pass |
| Chrome current | pass | pass |
| Safari current / Edge current | Safari pass | Edge pass |
| 24-step essential workflow | pass | pass |

If Windows hardware/VM is unavailable, the plan is not fully accepted as cross-platform; CI compilation alone is useful evidence but does not replace PDF/file/browser verification.

## 15. Final acceptance checklist

- [ ] Exactly the approved required scope is implemented; optional scope is labeled.
- [ ] Fresh Docker-free restore, local tool restore, migration, seed, run, and Release test succeed.
- [ ] Registration/login/logout/roles/disable/re-enable and welcome fallback pass.
- [ ] Storefront/detail/search/filter/sort/pagination and all states pass.
- [ ] Anonymous/authenticated carts, trusted totals, merge, invalid quantity/product/activity handling pass.
- [ ] Fake success/decline and payment-data privacy pass.
- [ ] Checkout is atomic, idempotent, and resists last-unit/multi-line/duplicate races.
- [ ] Order history/details remain immutable and owner-scoped.
- [ ] PDF required content/entry points/ownership/retry and order email failure behavior pass.
- [ ] Complete Admin dashboard/product/category/order/customer behavior and authorization pass.
- [ ] Image attack corpus and storage compensation pass.
- [ ] AI-disabled core and deterministic mock seam pass; no real key/call.
- [ ] CSRF, XSS, SQL, IDOR, over-post, redirect, secret, cookie/session, log, error checks pass.
- [ ] UI polish, keyboard/accessibility, three viewports and friendly state matrix pass.
- [ ] Full automated suite is deterministic and all 24 manual steps pass.
- [ ] Actual macOS Apple Silicon and Windows matrix passes.
- [ ] README later contains every required topic and every exact command was executed.
- [ ] Traceability has no missing requirement/task/test and final Git inspection contains no secret/runtime artifact.
