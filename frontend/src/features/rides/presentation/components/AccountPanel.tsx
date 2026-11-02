"use client";
import { useEffect, useState } from "react";
import type { RideService } from "../../application/RideService";

export function AccountPanel({ service, run, busy }: {
  service: RideService;
  run: (action: () => Promise<void>) => Promise<void>;
  busy: boolean;
}) {
  const [name, setName] = useState("");
  const [phone, setPhone] = useState("");
  const [message, setMessage] = useState("");
  useEffect(() => {
    let active = true;
    service.profile().then(profile => { if (active) { setName(profile.name); setPhone(profile.phone); } })
      .catch(e => { if (active) setMessage(e.message); });
    return () => { active = false; };
  }, [service]);
  return <section id="profile" className="card p-6 mt-6">
    <h2 className="font-bold text-lg mb-4">Hồ sơ cá nhân</h2>
    <form className="grid sm:grid-cols-[1fr_1fr_auto] gap-4 items-end" onSubmit={event => {
      event.preventDefault();
      void run(async () => { await service.saveProfile(name.trim(), phone.trim()); setMessage("Đã lưu hồ sơ."); });
    }}>
      <label className="text-sm">Họ và tên<input className="field mt-2" required maxLength={100} value={name} onChange={e => setName(e.target.value)} /></label>
      <label className="text-sm">Số điện thoại<input className="field mt-2" required minLength={9} maxLength={15} type="tel" value={phone} onChange={e => setPhone(e.target.value)} /></label>
      <button className="btn" disabled={busy}>Lưu hồ sơ</button>
    </form>
    {message && <p role="status" className="text-sm mt-3">{message}</p>}
  </section>;
}
