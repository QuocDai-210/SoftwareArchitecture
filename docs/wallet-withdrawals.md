# Simulated wallet withdrawal

Rider and Driver accounts can request a simulated withdrawal. No bank account data is collected and
no money leaves the system. An internal worker completes or rejects pending requests automatically;
there is no Ops approval step.

- `POST /wallets/withdrawals` with `{ "amount": 10000 }` and an `Idempotency-Key`.
- `GET /wallets/withdrawals?page=1` shows the account owner's requests.
- `GET /wallets/me` shows available, held, and pending earnings.

Amounts must be 10,000?5,000,000 VND and cannot exceed available balance. Creating a request moves
funds from Available to Held. The default worker outcome is `Completed`; set
`WALLET_WITHDRAWAL_OUTCOME=Rejected` in local `.env` to exercise rejection and fund release. Restart
the Payment service after changing configuration. The worker uses database transactions and a state
check so duplicate processing cannot spend the same request twice.
