import { SandboxRequestError, type SandboxGateway, type SandboxPayment } from "./SandboxGateway";

export const isConfirmed = (payment: SandboxPayment) =>
  payment.state === "Succeeded" || payment.state === "Failed";

// Only the first request asks the provider. Subsequent reads observe IPN/background results.
export function watchSandboxPayment(
  gateway: SandboxGateway,
  reference: string,
  update: (payment: SandboxPayment) => void,
  reportError: (message: string) => void,
  schedule: (action: () => void, delay: number) => () => void,
) {
  let active = true;
  let cancelTimer = () => {};
  let reads = 0;
  async function refresh(first = false) {
    let retry = true;
    try {
      const result = first ? await gateway.reconcile(reference) : await gateway.get(reference);
      if (!active) {
        return;
      }
      update(result);
      reportError("");
      retry = !isConfirmed(result);
    } catch (error) {
      if (!active) {
        return;
      }
      reportError(error instanceof Error ? error.message : "Mất kết nối. Đang thử lại…");
      if (error instanceof SandboxRequestError && [401, 403, 404].includes(error.status)) {
        retry = false;
      }
    }
    if (active && retry) {
      cancelTimer = schedule(() => void refresh(), ++reads < 20 ? 3000 : 10000);
    }
  }
  void refresh(true);
  return () => {
    active = false;
    cancelTimer();
  };
}
