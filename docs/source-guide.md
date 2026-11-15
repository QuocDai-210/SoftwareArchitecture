# Source guide

| Concern | Location |
|---|---|
| Authoritative requirements | `chuc-nang-va-luong-hoat-dong.md` |
| Windows setup and accounts | `README.md`, `scripts/setup.py`, `scripts/manage.ps1` |
| Postman workflows | `postman/SwiftRide.postman_collection.json` |
| Gateway routes and authentication | `backend/src/ApiGateway` |
| Local login, seed accounts, Trip workflow | `backend/src/TripService` |
| Driver seed, matching, pricing and route simulation | `backend/src/MatchingService` |
| COD, wallets, ledger, withdrawals | `backend/src/PaymentService` |
| Contracts and internal service JWT | `backend/src/Shared` |
| Rider web and backend proxy | `frontend/src` |
| Local databases, broker, service limits | `docker-compose.yml`, `deploy/postgres`, `deploy/mongo` |

The database migration history includes old provider-related tables. Keep that history to preserve
existing volumes. There is no current VNPAY provider, IPN endpoint, Keycloak realm, Ops dashboard,
public registration flow, or Driver web client.

`PaymentService/Infrastructure/Persistence/HistoricalPaymentRows.cs` contains persistence-only
shapes for those historical tables. Their existing entity names are preserved for EF migration
compatibility; they are not part of the current Domain or provider workflows.
