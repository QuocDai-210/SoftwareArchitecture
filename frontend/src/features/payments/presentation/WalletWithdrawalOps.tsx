"use client";

import { useCallback, useEffect, useState } from "react";
import type {
  WalletPage,
  WalletWithdrawal,
  WalletWithdrawalOpsGateway,
} from "../application/WalletGateway";

const money = (amount: number) => `${amount.toLocaleString("vi-VN")} đ`;
const date = (value: string) => new Date(value).toLocaleString("vi-VN");

export function WalletWithdrawalOps({ gateway }: { gateway: WalletWithdrawalOpsGateway }) {
  const [page, setPage] = useState(1);
  const [requests, setRequests] = useState<WalletPage<WalletWithdrawal> | null>(null);
  const [reason, setReason] = useState("");
  const [busyId, setBusyId] = useState("");
  const [error, setError] = useState("");
  const [refresh, setRefresh] = useState(0);
  const [loading, setLoading] = useState(true);

  const load = useCallback(async () => {
    setRequests(await gateway.pending(page));
  }, [gateway, page]);

  useEffect(() => {
    let active = true;
    let timer: ReturnType<typeof setTimeout>;
    setLoading(true);
    load()
      .then(() => {
        if (active) setError("");
      })
      .catch((e) => {
        if (active) setError(e instanceof Error ? e.message : "Không thể tải yêu cầu.");
      })
      .finally(() => {
        if (active) {
          setLoading(false);
          timer = setTimeout(() => setRefresh((value) => value + 1), 15000);
        }
      });
    return () => {
      active = false;
      clearTimeout(timer);
    };
  }, [load, refresh]);

  async function decide(id: string, approve: boolean) {
    if (!approve && !reason.trim()) {
      setError("Nhập lý do trước khi từ chối yêu cầu.");
      return;
    }
    setBusyId(id);
    setError("");
    try {
      await gateway.decide(id, approve, reason.trim());
      setReason("");
      await load();
    } catch (e) {
      setError(e instanceof Error ? e.message : "Không thể xử lý yêu cầu.");
    } finally {
      setBusyId("");
    }
  }

  return (
    <section className="space-y-5" aria-label="Duyệt rút tiền">
      <div>
        <p className="badge">OPS · Ví mô phỏng</p>
        <h1 className="text-3xl font-bold mt-3">Duyệt yêu cầu rút tiền</h1>
        <p className="muted mt-2">
          Duyệt sẽ ghi nhận chi mô phỏng và trừ khoản đang giữ. Từ chối sẽ trả tiền về ví.
        </p>
      </div>
      {error && (
        <p role="alert" className="app-alert">
          {error}
        </p>
      )}
      <div className="card p-6 space-y-4">
        <div className="flex items-center justify-between gap-3">
          <h2 className="text-xl font-bold">Đang chờ xử lý</h2>
          <button
            className="btn btn-secondary"
            disabled={loading}
            onClick={() => setRefresh((value) => value + 1)}
          >
            Làm mới
          </button>
        </div>
        {loading && <p role="status">Đang tải yêu cầu…</p>}
        {!loading && requests?.items.length === 0 && (
          <p className="muted">Không có yêu cầu rút tiền đang chờ.</p>
        )}
        {requests?.items.map((item) => (
          <article key={item.id} className="border-t border-emerald-50 py-5 space-y-3">
            <div className="flex flex-wrap justify-between gap-3">
              <div>
                <p className="text-xl font-bold">{money(item.amount)}</p>
                <p className="muted mt-1">Tài khoản: {item.ownerId}</p>
                <p className="mt-2">Mã yêu cầu: {item.id}</p>
                <small className="muted">Gửi lúc {date(item.requestedAt)}</small>
              </div>
              <span className="badge">Đang giữ tiền</span>
            </div>
            <label className="block font-semibold" htmlFor={`withdraw-reason-${item.id}`}>
              Lý do từ chối (bắt buộc khi từ chối)
            </label>
            <input
              id={`withdraw-reason-${item.id}`}
              maxLength={200}
              value={reason}
              disabled={busyId !== ""}
              onChange={(event) => setReason(event.target.value)}
              placeholder="Tối đa 200 ký tự"
            />
            <div className="flex flex-wrap gap-3">
              <button
                className="btn"
                disabled={busyId !== ""}
                onClick={() => void decide(item.id, true)}
              >
                {busyId === item.id ? "Đang xử lý…" : "Duyệt & mô phỏng chi"}
              </button>
              <button
                className="btn btn-secondary"
                disabled={busyId !== "" || !reason.trim()}
                onClick={() => void decide(item.id, false)}
              >
                Từ chối & trả tiền về ví
              </button>
            </div>
          </article>
        ))}
        <div className="flex items-center justify-between gap-3">
          <button
            className="btn btn-secondary"
            disabled={loading || page <= 1}
            onClick={() => setPage((value) => value - 1)}
          >
            Trang trước
          </button>
          <span className="muted text-sm">
            Trang {page} · {requests?.total ?? 0} yêu cầu chờ
          </span>
          <button
            className="btn btn-secondary"
            disabled={loading || page * 10 >= (requests?.total ?? 0)}
            onClick={() => setPage((value) => value + 1)}
          >
            Trang sau
          </button>
        </div>
      </div>
    </section>
  );
}
