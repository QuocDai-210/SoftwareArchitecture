# Scope implementation status

The [assignment](../RideLite-KienTrucPhanMem.pdf) defines the coursework. The table below
also lists features already present in the [web demo](../chuc-nang-va-luong-hoat-dong.md).

| Area | Implemented direction | Verify |
|---|---|---|
| Authentication | Seeded demo login or configured OIDC issuer, rider/driver roles | Postman login and Driver web denial |
| Seed data | Additive 100 Rider and 1,000 Driver seed in HCMC | Startup logs and Matching records |
| Trips | Quote, versioned confirmation, reservations, Driver API, route simulation | COD and Wallet Postman sequences |
| Payments | Simulated COD, wallet ledger, top-up and automatic withdrawal worker | Idempotency, insufficient funds, duplicate messages |
| External services | Optional Geoapify maps and OIDC issuer | No active VNPAY/Keycloak routes or credentials |
| Operations UI | Removed from active UI/API scope | Confirm no Ops account, route, or approval step |
| Documentation | Windows setup and Postman collection/environment | Follow README from a fresh clone |

Historical payment migrations are retained so existing database volumes are not destructively changed.
They are not active payment integrations. New clean databases run the same migration chain before
using the current wallet/COD API.
