"use client";

import { HttpSandboxGateway } from "@/features/payments/infrastructure/HttpSandboxGateway";
import { SandboxPanel } from "@/features/payments/presentation/SandboxPanel";

const gateway = new HttpSandboxGateway();

export default function SandboxClient({ reference }: { reference?: string }) {
  return <SandboxPanel gateway={gateway} reference={reference} />;
}
