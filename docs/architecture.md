# Current architecture

The [assignment](../RideLite-KienTrucPhanMem.pdf) defines the three business services.
The default Postman stack runs the gateway, services, databases and RabbitMQ. The Rider web
is optional. Matching estimates routes locally for the Postman run and uses the configured
map provider in the web demo.

```mermaid
flowchart LR
  Rider[Next.js Rider web] --> Gateway[API Gateway]
  Postman[Postman Rider and Driver] --> Gateway
  Gateway --> Trip[Trip Service]
  Gateway --> Match[Matching Service]
  Gateway --> Pay[Payment Service]
  Trip --> TripDB[(PostgreSQL TripDB)]
  Match --> MatchDB[(MongoDB MatchDB)]
  Pay --> PayDB[(PostgreSQL PayDB)]
  Trip <--> Rabbit[RabbitMQ]
  Pay <--> Rabbit
  Match <--> Rabbit
  Match --> Geo[Geoapify maps only]
```

- Web authentication is local seeded Rider authentication. Driver accounts use API/Postman only.
- The only account roles are `rider` and `driver`; machine-to-machine JWTs are not user roles.
- Trip, Matching, and Payment own separate data stores. There is no SQL Server, Keycloak, or fourth
  business service in the active Compose stack.
- The web demo uses Geoapify. An optional HTTPS OIDC issuer supplies user access tokens. Fares, COD collection, wallet funding, and withdrawals
  are simulations; no bank transfer or payment-provider request is made.
- Old VNPAY/checkout migration records are retained as historical schema/data for existing volumes.
  They have no active route, worker, provider, or frontend flow.

## Trip workflow

`Requested -> AwaitingRiderConfirmation -> ReservingDriver -> AwaitingDriverAcceptance ->
DriverAccepted -> EnRoute -> PickedUp -> PaymentPending -> Paid`.

The trip worker asks Matching for an eligible nearby seeded Driver. Rider wallet confirmation holds
funds before the reservation request. A failed hold never creates an offer. Rejected or expired
offers release both the reservation and wallet hold before rematching. COD does not create a wallet
hold. COD collection is recorded by the assigned Driver after dropoff.

Wallet settlement transfers the Rider hold to a Driver wallet credit in one Payment DB transaction
and writes balanced ledger entries. COD settlement records cash received by the Driver and never
credits the Driver wallet. Withdrawal requests move available funds to held funds; a local worker
settles or rejects them exactly once.

## Local data and migrations

Seed data is additive and deterministic: 100 Rider accounts/profiles and 1,000 Driver records in
Ho Chi Minh City. Startup does not reset existing accounts, trips, wallets, or ledgers. Existing
volumes must be kept; do not remove volumes to re-seed. PostgreSQL TripDB and PayDB share an
instance but have separate database users. MongoDB stores Matching data.
