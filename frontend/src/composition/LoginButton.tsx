"use client";

import { useFormStatus } from "react-dom";

export function LoginButton() {
  const { pending } = useFormStatus();
  return (
    <button className="btn w-full mt-5" type="submit" disabled={pending} aria-busy={pending}>
      {pending ? "Đang đăng nhập…" : "Đăng nhập"}
      {!pending && <span aria-hidden="true">↗</span>}
    </button>
  );
}
