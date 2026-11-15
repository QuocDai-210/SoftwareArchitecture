# Scope implementation status

The scope document [`chuc-nang-va-luong-hoat-dong.md`](../chuc-nang-va-luong-hoat-dong.md) is the
source of truth. This status replaces the earlier assessment against the obsolete VNPAY/Ops version.

| Area | Implemented direction | Verify |
|---|---|---|
| Authentication | Local seeded login, rider/driver roles, Rider-only web | Postman login and Driver web denial |
| Seed data | Additive 100 Rider and 1,000 Driver seed in HCMC | Startup logs and Matching records |
| Trips | Quote, versioned confirmation, reservations, Driver API, route simulation | COD and Wallet Postman sequences |
| Payments | Simulated COD, wallet ledger, top-up and automatic withdrawal worker | Idempotency, insufficient funds, duplicate messages |
| External services | Geoapify maps only | No active VNPAY/Keycloak routes or credentials |
| Operations UI | Removed from active UI/API scope | Confirm no Ops account, route, or approval step |
| Documentation | Windows setup and Postman collection/environment | Follow README from a fresh clone |

Historical payment migrations are retained so existing database volumes are not destructively changed.
They are not active payment integrations. New clean databases run the same migration chain before
using the current wallet/COD API.
