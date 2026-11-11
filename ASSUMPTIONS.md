# Assumptions

## One active application per academic year

A student is expected to enroll in one class or shift for the year. The default blocks another application in that year while a student has a reserved seat, confirmed admission, or waitlist entry. After a cancellation or expiry, they may apply again. A different academic year is independent. The API supports `STUDENT_HOLD_SCOPE=class` for the per-class behavior. The UI implements the default academic-year policy.

## Opt-in waitlist

A full class still returns `409 SEAT_FULL` on the standard application endpoint. A parent can explicitly join the waitlist when at least one seat is reserved and could become free. The queue is capped at the number of currently reserved seats because only those seats might become available. The oldest eligible entry is promoted first. `PENDING` is an extra status exposed in hold details and student history so the parent can see and cancel a queued application.

## API validation

Student-specific writes and student history require the 17-digit X-Student-Id header. Hold details and class reads work without it. Invalid input returns 400, unknown records 404, a different owner 403, and capacity or state conflicts 409. A successful cancellation returns 204. An expired or cancelled application remains visible in history.

## Class management

A shift or section is part of the class name and has its own seat pool. Names must be unique within an academic year after normalizing case and spaces. Admins can adjust seats within occupancy limits, free or block a seat when cancelling an application, and remove classes without active applications. Removal preserves history.

## Expiration and live updates

PostgreSQL's UTC clock decides expiry. The browser shows a countdown and the sweeper releases expired holds. The browser receives seat changes through Server-Sent Events (SSE) over HTTP. PostgreSQL LISTEN/NOTIFY tells every API process to publish a fresh snapshot after a committed change.

## Two API processes and one database

Both API processes connect to the same PostgreSQL database. During an application, a transaction takes a PostgreSQL advisory lock keyed by the student number and then locks the class row with `SELECT ... FOR UPDATE`. It checks the student's active applications and remaining capacity after acquiring those locks, then writes the hold before committing. A request in another API process waits on the same database locks and sees the committed result when it checks eligibility. Confirmation, cancellation, and waitlist promotion also coordinate through class-row locks. The expiry sweep uses a conditional update on expired holds, and capacity checks disregard expired reservations using the same database clock. The migration service uses a separate advisory lock to serialize schema startup.

The automated two-process check runs two independently started Kestrel containers against a separate test database. It checks seat capacity under concurrent requests and the session-wide student rule across both processes. This test exercises those races. The transaction boundaries and database locks are what enforce the limits during normal operation.
