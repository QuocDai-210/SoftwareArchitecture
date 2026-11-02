"use client";
import { useEffect, useState } from "react";
import type { RideService } from "../../application/RideService";

export function AccountPanel({
  service,
  run,
  busy,
  initialName,
}: {
  service: RideService;
  run: (action: () => Promise<void>) => Promise<void>;
  busy: boolean;
  initialName: string;
}) {
  const [name, setName] = useState(initialName);
  const [phone, setPhone] = useState("");
  const [message, setMessage] = useState("");
  const [loaded, setLoaded] = useState(false);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [attempt, setAttempt] = useState(0);
  useEffect(() => {
    let active = true;
    setLoading(true);
    setLoaded(false);
    setError("");
    service
      .profile()
      .then((profile) => {
        if (active) {
          setName(profile.name || initialName);
          setPhone(profile.phone);
          setLoaded(true);
        }
      })
      .catch((e) => {
        if (active) setError(e.message);
      })
      .finally(() => {
        if (active) setLoading(false);
      });
    return () => {
      active = false;
    };
  }, [service, initialName, attempt]);
  return (
    <section id="profile" className="card p-6 mt-6">
      <h2 className="font-bold text-lg mb-4">Hồ sơ cá nhân</h2>
      {loading && (
        <p role="status" className="text-sm mb-4">
          Đang tải hồ sơ…
        </p>
      )}
      {error && (
        <div role="alert" className="app-alert mb-4">
          <p>{error}</p>
          <button className="underline" disabled={loading} onClick={() => setAttempt(attempt + 1)}>
            Thử lại
          </button>
        </div>
      )}
      <form
        className="grid sm:grid-cols-[1fr_1fr_auto] gap-4 items-end"
        onSubmit={(event) => {
          event.preventDefault();
          void run(async () => {
            await service.saveProfile(name.trim(), phone.trim());
            setMessage("Đã lưu hồ sơ.");
          });
        }}
      >
        <label className="text-sm">
          Họ và tên
          <input
            className="field mt-2"
            disabled={busy || !loaded}
            required
            maxLength={100}
            value={name}
            onChange={(e) => {
              setName(e.target.value);
              setMessage("");
            }}
          />
        </label>
        <label className="text-sm">
          Số điện thoại
          <input
            className="field mt-2"
            disabled={busy || !loaded}
            required
            minLength={9}
            maxLength={15}
            type="tel"
            value={phone}
            onChange={(e) => {
              setPhone(e.target.value);
              setMessage("");
            }}
          />
        </label>
        <button className="btn" disabled={busy || !loaded}>
          Lưu hồ sơ
        </button>
      </form>
      {message && (
        <p role="status" className="text-sm mt-3">
          {message}
        </p>
      )}
    </section>
  );
}
