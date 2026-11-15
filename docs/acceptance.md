# Acceptance checklist

The scope is defined by [`chuc-nang-va-luong-hoat-dong.md`](../chuc-nang-va-luong-hoat-dong.md).
Use the Postman collection in [`postman`](../postman) for API acceptance. Do not use the retired
VNPAY, Keycloak, Driver web, or Ops web scenarios from the former test suite.

## Local readiness

- [ ] `manage.ps1 setup`, `build`, and `up` succeed on Windows with Docker Desktop and Python 3.
- [ ] Web, Gateway, Trip, Matching, Payment, PostgreSQL, MongoDB, and RabbitMQ are healthy.
- [ ] 100 Rider accounts/profiles and 1,000 Driver records exist; rerunning startup adds no duplicates.
- [ ] `rider001` can log into web; a Driver cannot obtain a web session.
- [ ] `driver0001` can log in using `POST /auth/login` and use Driver API routes.

## Postman ride flows

- [ ] Rider can request a quote and create a COD or Wallet trip using an `Idempotency-Key`.
- [ ] Rider confirms the current quote/version; driver offers contain the matching trip.
- [ ] Assigned Driver accepts, starts, picks up, and drops off the Rider; stale versions are rejected.
- [ ] COD remains pending until the assigned Driver confirms cash. COD does not credit a wallet.
- [ ] A Wallet hold happens before the offer. Insufficient funds create no offer and do not make balance
  negative. Completion debits the hold and credits Driver earnings exactly once.
- [ ] Rider cancellation, Driver rejection, reservation expiry, and no-driver failure release holds.
- [ ] Withdrawal moves available funds to held funds. The local worker completes or rejects it without
  an Ops action; rejection returns the held funds.

## Safety and recovery

- [ ] Replaying the same idempotency key and body does not duplicate a trip, top-up, payment, or journal.
- [ ] Reusing a key with a different body is rejected.
- [ ] Rider cannot use Driver routes; Driver cannot act on another Driver's trip or wallet.
- [ ] User JWT cannot call internal service endpoints.
- [ ] Journal entries balance; all wallet state changes have matching history entries.
- [ ] Restarting a service does not reset seeded or operational data.
- [ ] Geoapify keys stay in local `.env`; logs and Git contain no credentials or bearer tokens.

For concurrency cases, send the same wallet and withdrawal command concurrently from Postman Runner
or separate clients and verify the resulting balance, history, and journal in the API responses.
