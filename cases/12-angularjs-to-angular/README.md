# Case 12: AngularJS to Angular

An order desk written with AngularJS 1.8, whose support ended in December 2021, and the same application written
with Angular 22. There is no upgrade path between the two: different language, different template syntax, different
everything. The application is rewritten, and the question is how to know that users get what they had.

Twelve tests drive a real browser (Chromium, through Playwright). The same test file runs against both applications
and against the same small backend.

| | Before | After |
|---|---|---|
| Framework | AngularJS 1.8.3 | Angular 22 |
| Language, build | JavaScript, no build step | TypeScript, Angular CLI |
| Structure | controllers and templates | standalone components with signals |
| Lists | `filter` and `orderBy` in the template | code in the component |
| Routing | ngRoute, addresses like `/#!/orders/3` | Angular router, `/orders/3` |
| HTTP | `$http` | `HttpClient` |
| Forms | `ng-model`, `form.field.$error` | template-driven forms, `ngModel` |

## Layout

```
before/app/            the AngularJS application: index.html, app.js, two templates
after/src/app/         the Angular application; angularjs-compat.ts holds what AngularJS did implicitly
server/server.js       serves either application and the API both call; data in memory, reset per test
e2e/tests/             the twelve browser tests
```

## Run it

You need Node 22.

```bash
cd before && npm ci
```

```bash
cd after && npm ci && npx ng build
```

```bash
cd e2e && npm ci && npx playwright install chromium && npx playwright test
```

`APP=before` or `APP=after` in front of the last command runs one application only.

## What is verified, and what is not

| Check | Where | Result |
|---|---|---|
| 12 browser tests against the AngularJS application | CI, Chromium | @@BEFORE@@ |
| The same 12 against the Angular application | CI, Chromium | @@AFTER@@ |

Not verified:

- **Other browsers.** Chromium only.
- **How it looks.** The tests read texts, attributes and what reaches the server. No screenshots are compared.
- **A step-by-step migration.** Large AngularJS applications are usually moved piece by piece, with both frameworks
  running in one page (ngUpgrade) for months. This case rewrites a small application in one go and does not show that.
- **What this application does not have:** custom directives with `link` functions, `$scope.$watch` chains,
  `$rootScope` events, third-party AngularJS widgets. Those are where the effort goes in a real application.

## What the first port got wrong

The first Angular version was written the way a developer translates a screen they can see: same routes, same
columns, same form. Against the tests:

| | Tests passing |
|---|---|
| AngularJS application | 12 of 12 |
| First Angular port | 2 of 12 |
| First port, old addresses handled | 6 of 12 |
| Finished version | 12 of 12 |

**Old addresses.** AngularJS addresses look like `/#!/orders/3`. They are in bookmarks, e-mails and wiki pages.
The Angular router uses `/orders/3` and treats everything after `#` as a fragment, so an old address opens the list.
No error, just the wrong page. Five of the twelve tests open an order through such an address, and all five failed.
One line in
[`main.ts`](after/src/main.ts) rewrites the address before the application starts.

The other failures all come from things AngularJS did inside its templates:

```html
<tr ng-repeat="order in vm.orders | filter: vm.search | orderBy: vm.sort : vm.reverse">
```

Angular has no `filter` and no `orderBy` pipe, deliberately. Whoever ports the screen writes that code, and writes
what they think it did.

| What the user does | AngularJS | First Angular port |
|---|---|---|
| Types `BERLIN` into the search box | finds the two orders from Berlin | finds nothing: only the customer name was searched |
| Clicks the Customer column | Anna, bernd, carla, Zoë, Émile | Anna, Zoë, bernd, carla, Émile |
| Clicks the Total column | the order without a total comes last | it comes first |
| Chooses "All" in the status filter | `GET /api/orders` | `GET /api/orders?status=` |
| Opens an order | `Feb 11, 2026 8:00:00 AM` | `Feb 11, 2026, 8:00:00 AM` |

- **`filter` searches every field.** With a text, the AngularJS filter matches an item if any of its values contains
  the text. Users search by city and by status without knowing that this was never designed.
- **`orderBy` ignores letter case and puts empty values last.** A plain `sort` with `>` does neither.
- **`$http` leaves out parameters without a value.** `HttpClient` sends `status=`. This backend treats that as no
  filter. Another one may answer "orders whose status is empty".
- **The `medium` date format differs by a comma.** Harmless for a person, not for an export that someone parses.

All four are in one file, [`angularjs-compat.ts`](after/src/app/angularjs-compat.ts), so that the day somebody
decides the old behaviour is not wanted, there is one place to change and a test that will say so.

## What translated without surprises

- currency and short date formatting, for the browser locale used here
- sanitising of HTML in a note: formatting and links stay, scripts and event handlers go, in both frameworks
- form validation: required, minimum, maximum, the Save button disabled while invalid
- numbers from a number input arrive at the server as numbers
- the XSRF token: both frameworks read the cookie and send the header without any code
- wording of counts ("No orders", "1 order", "5 orders")

## A note on the test setup

The browser in the tests has a German locale and the Berlin time zone, while the application formats in US English
and UTC. That is deliberate: it shows that neither application takes its formats from the browser, and it would
catch a port that starts to.
