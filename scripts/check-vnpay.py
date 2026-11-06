#!/usr/bin/env python3
"""Local sandbox IPN integration checks; never sends a payment to VNPAY."""

from concurrent.futures import ThreadPoolExecutor
import argparse
import hashlib
import hmac
import json
from pathlib import Path
import secrets
import subprocess
from urllib.parse import urlencode
from urllib.request import urlopen
from uuid import uuid4

ROOT = Path(__file__).resolve().parents[1]


def sql(statement):
    result = subprocess.run(
        [
            "docker",
            "compose",
            "exec",
            "-T",
            "postgres",
            "psql",
            "-X",
            "-U",
            "payment_app",
            "-d",
            "PayDB",
            "-qAt",
            "-v",
            "ON_ERROR_STOP=1",
        ],
        cwd=ROOT,
        input=statement,
        text=True,
        encoding="utf-8",
        capture_output=True,
    )
    if result.returncode:
        raise RuntimeError("Sandbox fixture database operation failed.")
    return result.stdout.strip()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--ipn-url", default="http://localhost:3000/api/vnpay/ipn")
    args = parser.parse_args()
    env = dict(
        line.split("=", 1)
        for line in (ROOT / ".env").read_text(encoding="utf-8").splitlines()
        if line and not line.startswith("#") and "=" in line
    )
    secret = env["VNPAY_HASH_SECRET"].strip().strip('"').strip("'")
    merchant = env["VNPAY_TMN_CODE"].strip().strip('"').strip("'")
    reference = uuid4().hex
    owner = "sandbox-check-" + reference
    transaction = str(100000000000000 + secrets.randbelow(900000000000000))
    fields = {
        "vnp_TmnCode": merchant,
        "vnp_TxnRef": reference,
        "vnp_Amount": "1000000",
        "vnp_TransactionNo": transaction,
        "vnp_TransactionStatus": "00",
        "vnp_ResponseCode": "00",
    }

    def notify(values, *, corrupt=False, duplicate=False):
        query = urlencode(sorted(values.items()))
        signature = hmac.new(secret.encode(), query.encode(), hashlib.sha512).hexdigest()
        if corrupt:
            signature = "0" * 128
        query += "&vnp_SecureHash=" + signature
        if duplicate:
            query += "&vnp_Amount=1000000"
        with urlopen(args.ipn_url + "?" + query, timeout=25) as response:
            body = json.load(response)
        assert set(body) == {"RspCode", "Message"}, "Wrong IPN acknowledgement casing"
        return body["RspCode"]

    sql(
        f"""INSERT INTO "SandboxPayments"
        ("Reference", "OwnerId", "RequestKey", "Amount", "CreatedAt", "ExpiresAt",
         "State", "NextCheckAt", "CheckCount")
        VALUES ('{reference}', '{owner}', '{reference}', 10000, now(), now() + interval '15 minutes',
                'Pending', now() + interval '1 day', 0);"""
    )
    try:
        assert notify(fields, corrupt=True) == "97", "Accepted bad signature"
        assert notify(fields, duplicate=True) == "97", "Accepted duplicate query key"
        assert notify({**fields, "vnp_TmnCode": "WRONG"}) == "97", "Accepted wrong merchant"
        assert notify({**fields, "vnp_TxnRef": uuid4().hex}) == "01", "Accepted missing order"
        assert notify({**fields, "vnp_Amount": "2000000"}) == "04", "Accepted wrong amount"
        with ThreadPoolExecutor(max_workers=8) as pool:
            responses = list(pool.map(lambda _: notify(fields), range(8)))
        assert responses.count("00") == 1 and responses.count("02") == 7, responses
        assert (
            sql(
                f"""SELECT "State" || '|' || "ProviderTransaction"
                       FROM "SandboxPayments" WHERE "Reference" = '{reference}';"""
            )
            == ("Succeeded|" + transaction)
        )
        assert sql(f"""SELECT count(*) FROM "Wallets" WHERE "RiderId" = '{owner}';""") == "0"
        assert (
            sql(
                f"""SELECT "LastIpnAt" IS NOT NULL FROM "SandboxPayments"
                       WHERE "Reference" = '{reference}';"""
            )
            == "t"
        )
        assert notify({**fields, "vnp_TransactionStatus": "02", "vnp_ResponseCode": "24"}) == "02"
        assert (
            sql(
                f"""SELECT "State" FROM "SandboxPayments"
                       WHERE "Reference" = '{reference}';"""
            )
            == "Succeeded"
        )
    finally:
        # Delete only this script's isolated sandbox fixture, never business or ledger data.
        sql(
            f"""DELETE FROM "SandboxPayments"
                WHERE "Reference" = '{reference}' AND "OwnerId" = '{owner}';"""
        )
    print("Sandbox IPN passed: signature, merchant, amount, missing order, duplicate fields,")
    print("8 concurrent callbacks, persisted IPN, no wallet credit. Fixtures removed.")


if __name__ == "__main__":
    main()
