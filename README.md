# School Admission System

The application reserves school seats on a first-come, first-served basis. A parent can apply with a 17-digit birth registration number, pay the simulated admission fee before the hold expires, cancel an unpaid application, and view application history. Administrators create and manage classes at `/admin`.

## Run locally

Start Docker Desktop, open a terminal in the repository root, and run:

```sh
docker compose up --build
```

- Frontend: http://localhost:3000
- Admin: http://localhost:3000/admin
- API: http://localhost:8080
- API health: http://localhost:8080/health

The database starts empty. Create a class in the admin page to try the application flow. The home page shows class summaries before a student number is saved and application history once a number is saved. **Clear number** returns to the class summaries. Use **Start a new application** to see classes and apply again.

`docker compose down` stops the app and preserves data. `docker compose down --volumes` deletes the local application database.

## Configuration

Set these values in the shell or a local Compose `.env` file before starting the app.

| Variable | Default | Purpose |
|---|---|---|
| `HOLD_DURATION_SECONDS` | `600` | Reservation duration |
| `SWEEP_INTERVAL_MS` | `1000` | Interval for releasing expired reservations |
| `STUDENT_HOLD_SCOPE` | `session` | Server-side application limit. Set to `class` to exercise the per-class API rule |
| `DEFAULT_SESSION` | `2027` | API default academic year when creating a class without a session |
| `ConnectionStrings__Default` | Compose PostgreSQL service | API database connection |
| `BACKEND_URL` | `http://localhost:8080` | API URL reachable from the browser |
| `CORS_ORIGINS` | `http://localhost:3000` | Comma-separated frontend origins |
| `API_PORT`, `FRONTEND_PORT`, `DB_PORT` | `8080`, `3000`, `5432` | Host ports |

If a port is occupied, change the corresponding host port. Update `BACKEND_URL` when changing the API port and `CORS_ORIGINS` when changing the frontend port.

## Run backend tests

```sh
docker compose -p onnorokom-tests -f compose.test.yml up --build --exit-code-from backend-tests
docker compose -p onnorokom-tests -f compose.test.yml down
```

The first command returns the test runner's exit code. The test project uses an isolated, temporary PostgreSQL database and exposes no host ports. Both API containers use that same database. The tests cover concurrent applications, expiry, payment, cancellation, admin operations, input validation, and waitlist promotion.

The two-process test splits 100 simultaneous requests for 10 seats between two independent API containers. It checks that exactly 10 requests succeed and that both containers report 10 held seats. It also submits simultaneous requests for two different classes in one academic year using the same student number and checks that exactly one succeeds. See [ASSUMPTIONS.md](ASSUMPTIONS.md) for the locking mechanism.

## Run frontend tests

Use Node.js 24 and npm from the repository root.

```sh
npm --prefix frontend ci
npm --prefix frontend test
```

You can also run the suite with Docker alone.

```sh
docker build --target test -t onnorokom-frontend-tests ./frontend
docker run --rm onnorokom-frontend-tests
```

The Vitest suite renders the React UI with Testing Library and jsdom. It covers student number validation, the home page switching between class summaries and application history, payment and cancellation controls on the matching class card, the countdown and expiry without a reload, and localized seat conflict feedback. The API and live stream are mocked so the suite runs without a database or backend. The backend integration suite tests the real API and database separately.
