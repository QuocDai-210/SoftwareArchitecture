# Simulated wallet funding

Wallet top-up is an internal simulation. It does not create a checkout URL and never contacts a bank
or payment gateway. The API writes the wallet credit, balanced journal, and wallet activity in one
Payment DB transaction.

- `GET /wallets/me`: balances in VND (`available`, `held`, `pendingEarnings`).
- `POST /wallets/topups`: Rider-only simulated top-up, 10,000?5,000,000 VND.
- `GET /wallets/topups?page=1`: top-up history.
- `GET /wallets/history?page=1`: all wallet ledger activity.

Send `Idempotency-Key` with every top-up. Replaying the same key and amount returns the original
activity. A different amount for the same key is rejected. The default local fixtures create 100
Rider wallets with opening balances and balanced opening journals. No credentials for external
payment providers are required.
