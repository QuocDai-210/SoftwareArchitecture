# Postman guide

Import `SwiftRide.postman_collection.json` and `SwiftRide.local.postman_environment.json` from
`postman/`. Select the `SwiftRide Local` environment and replace the two password placeholders using
`RIDER_PASSWORD` and `DRIVER_PASSWORD` from the local `.env`. Do not save real passwords in a shared
export or commit them.

Base URL: `http://localhost:8080`.

## Collection Runner

Run only the folder **7 - Sequential ride runner**. Set the collection variable `paymentMethod`
to `Wallet` or `Cod` and select an idle Rider in the environment. The folder logs in the matched
Driver automatically and polls quote, offer, acceptance, arrival, and payment with a one-second
interval and a 60-second limit per step. Keep `SIMULATION_AUTO_ACCEPT_DRIVERS=false` for this runner.
Do not run all folders at once: folders 1-6 are a manual request toolbox and contain alternate flows.

## Rider and Driver sequence

1. Send **Login rider**. The response script stores `riderToken` and makes it the active bearer token.
2. Request a map quote, then **Create COD trip** or **Create wallet trip**. The collection stores the
   trip ID and derives the corresponding seeded Driver username from the matched Driver ID.
3. Repeat **Read trip** until the trip has a live quote. Send **Confirm quote** using the current
   version and quote ID.
4. Send **Login driver** with the derived `driverUsername`, then repeat **Driver offers** until the
   trip appears. Accept the offer and send the ride steps in order. Each response updates the trip
   version for the next command.
5. For COD, confirm cash received after dropoff. For Wallet, Payment settles the hold and credits the
   Driver wallet automatically. Log in as Rider again before Rider-only wallet requests.
6. Top up, request a simulated withdrawal, wait briefly for the worker, then refresh withdrawal
   history and wallet balance.

Use each request's generated `Idempotency-Key`; reuse it only when retrying that same request body.
The collection does not expose internal service routes. Stale versions, ownership violations, and
insufficient balances should fail without applying the operation.

## Automated API smoke test

With the stack running, execute these commands sequentially from the repository root:

```powershell
py -3 scripts/check-simulation.py --rider rider098 --method Wallet
py -3 scripts/check-simulation.py --rider rider097 --method Cod
```

These tests create retained demo trips, use the assigned Driver's seeded account, wait for simulated
arrival, and verify the final Rider and Driver balances. Wallet trips spend the quoted demo amount;
COD must not credit the Driver wallet. Each polling step times out after 60 seconds. Use an idle
Rider with sufficient balance; no database or volume is reset. This smoke test supplements Postman;
it is not evidence that concurrency or broker recovery scenarios have passed.

For automatic acceptance in a demonstration, set `SIMULATION_AUTO_ACCEPT_DRIVERS=true` in `.env`
and recreate Trip with `docker compose up -d trip`. The worker accepts live offers using the same
reservation confirmation workflow. Keep it `false` for manual Driver accept/reject tests and the
smoke test above. Movement still follows the simulation API; automatic acceptance does not skip
pickup/dropoff arrival checks.

An explicit insufficient-funds fixture can be exercised with:

```powershell
py -3 scripts/check-simulation.py --rider rider096 --scenario insufficient
```

This withdraws the fixture's entire available demo balance, verifies withdrawal replay uses the same
request, then confirms a wallet quote. The test checks that no Driver invitation occurs, no funds
remain held, and the trip can be cancelled. The fixture intentionally remains at zero balance;
use the internal top-up API to fund it again. Do not use a Rider with an active trip or held funds.

To verify release and reacquisition across a Driver rejection, run:

```powershell
py -3 scripts/check-simulation.py --rider rider091 --method Wallet --scenario rematch
```

This checks the first hold is returned, the rejected Driver is excluded, a new quote creates a
new hold, and the replacement Driver completes the trip. Holds are identified by trip and quote;
late messages for the previous quote cannot release the new hold. An additive migration records
closed holds, including release-before-hold cases, without changing wallet balances or deleting data.

Fault/restart checks (run sequentially when no one else is using the local demo):

```powershell
py -3 scripts/check-simulation.py --rider rider089 --scenario broker-recovery
py -3 scripts/check-simulation.py --rider rider088 --scenario restart
```

`broker-recovery` explicitly stops/restarts only this project's RabbitMQ, checks the hold request
stays pending during the outage, then cancels after recovery and checks the wallet is restored.
`restart` pauses the simulated pickup route, restarts only Matching, checks progress is retained,
then resumes the route and completes payment. No volume is removed. Normal scenarios do not
start or stop services.

## Concurrency and permission checks

```powershell
py -3 scripts/check-wallet-concurrency.py --rider rider094
```

The script verifies invalid credentials/tokens, Rider access to Driver/internal routes, Driver
access to Rider top-ups, four simultaneous replays of a top-up and a withdrawal, and conflicting
request bodies under the same key. It creates real simulated journal/history entries, but a
successful run leaves the selected Rider's available balance unchanged. Use the default
`WALLET_WITHDRAWAL_OUTCOME=Completed` and an idle Rider.

## Deterministic COD failure fixtures

Set `PAYMENT_SIMULATION_OUTCOME=Declined` or `Timeout` in local `.env`, then recreate Payment
with `docker compose up -d payment`. Complete a COD trip manually: it must enter `PaymentReview`
without a cash journal or a `Paid` state. Timeout uses the bounded broker retry policy before faulting.
Restore `PAYMENT_SIMULATION_OUTCOME=Pending`, recreate Payment, then use the assigned Driver token
to call `POST /trips/{id}/retry-payment` with the latest `expectedVersion`. Wait for a pending
payment, then confirm simulated cash collection. A repeated old payment attempt must not reset
the declined result; a new authorized retry can resume it. These fixtures are local configuration,
not request parameters accepted from Riders.

For the failure phase, `py -3 scripts/check-simulation.py --rider rider092 --method Cod
--scenario payment-review` runs a trip up to `PaymentReview` and prints its ID and assigned Driver.
Use the requests in **8 - Recovery toolbox** after restoring the normal fixture. This intentionally
leaves the Driver busy until the authorized retry and cash confirmation finish.

## Zero-fare wallet regression

After building the current images, run the following with the API stack available. The temporary
pricing override applies a 100% promotion; the `finally` block restores the original configuration.
The first check completes a free Wallet trip; the second rejects four offers and verifies compensation.
Both checks assert the quoted fare is zero and balances remain unchanged.

```powershell
$previousPromotion = $env:PRICING_PROMOTION
try {
    $env:PRICING_PROMOTION = '0'
    docker compose up --no-build -d --no-deps --wait matching
    if ($LASTEXITCODE -ne 0) { throw 'Matching startup failed' }
    py -3 scripts/check-simulation.py --rider rider096 --expected-amount 0
    if ($LASTEXITCODE -ne 0) { throw 'Free ride failed' }
    py -3 scripts/check-simulation.py --rider rider078 --scenario rematch --rejections 4 --expected-amount 0
    if ($LASTEXITCODE -ne 0) { throw 'Free ride compensation failed' }
} finally {
    $env:PRICING_PROMOTION = $previousPromotion
    docker compose up --no-build -d --no-deps --wait matching
}
```

Zero-value trip holds and settlements retain their quote identity, journal and wallet history.
Top-ups and withdrawals still require 10,000–5,000,000 VND. The additive migration preserves existing
history and permits zero-value activities only for trip holds, releases, settlements and earnings.

## Persisted simulation recovery

`SimulationRecoveryTests` runs against a temporary MongoDB container limited to 384 MB. It saves
arrival progress without writing the Driver position, creates a new repository/workflow instance,
and verifies that recovery writes the final position without advancing progress again. It also
checks paused simulations, older documents and stale acknowledgements. The container has no project
volume and is stopped after the test. Run the built test separately from builds:

```powershell
dotnet test backend/tests/SwiftRide.Checks -c Release --no-build --no-restore --filter 'FullyQualifiedName~SimulationRecoveryTests'
```
