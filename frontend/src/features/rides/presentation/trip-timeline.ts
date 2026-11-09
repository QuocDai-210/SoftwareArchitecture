export type TripTimelineEvent = { status: string; at: string; reason: string | null };
export type TripTimelineState = "complete" | "current" | "upcoming";
export type TripTimelineItem = {
  title: string;
  description: string;
  at: string | null;
  state: TripTimelineState;
};

export function matchingProgress(status: string, rejections: number) {
  if (status !== "Requested" || rejections <= 0) return null;
  return `Có ${rejections}/3 lượt mời đã bị từ chối/hết hạn; đang tìm tài xế khác.`;
}

const steps = [
  {
    title: "Đặt chuyến",
    completedBy: ["Requested"],
    phase: ["Requested", "AwaitingRiderConfirmation", "WalletHoldPending", "ReservingDriver"],
  },
  {
    title: "Tài xế nhận chuyến",
    completedBy: ["DriverAccepted"],
    phase: ["AwaitingDriverAcceptance", "AcceptingDriver", "DriverAccepted"],
  },
  { title: "Tài xế đang đến", completedBy: ["EnRoute"], phase: ["EnRoute"] },
  { title: "Đã đón khách", completedBy: ["PickedUp"], phase: ["PickedUp"] },
  { title: "Đã trả khách", completedBy: ["DroppedOff"], phase: ["DroppedOff"] },
  {
    title: "Thanh toán",
    completedBy: ["PaymentPending", "PaymentReview", "Paid"],
    phase: ["PaymentPending", "PaymentReview", "Paid"],
  },
];

const statusStep = new Map(
  steps.flatMap((step, index) => step.phase.map((status) => [status, index])),
);
const terminalStatuses = new Set(["Cancelled", "Failed"]);

function lastEvent(history: readonly TripTimelineEvent[], statuses: readonly string[]) {
  for (let index = history.length - 1; index >= 0; index--) {
    if (statuses.includes(history[index].status)) return history[index];
  }
  return null;
}

function currentDescription(status: string, index: number) {
  if (index === 0) {
    if (status === "AwaitingRiderConfirmation") return "Chờ bạn xác nhận giá";
    if (status === "WalletHoldPending") return "Đang giữ tiền trong ví";
    if (status === "ReservingDriver") return "Đang giữ chỗ tài xế";
    return "Đang tìm tài xế";
  }
  if (index === 1) {
    if (status === "AwaitingDriverAcceptance") return "Đang chờ tài xế nhận";
    if (status === "AcceptingDriver") return "Đang xác nhận tài xế";
    return "Tài xế đã nhận chuyến";
  }
  if (status === "PaymentPending") return "Chờ xác nhận đã thu tiền mặt";
  if (status === "PaymentReview") return "Thanh toán cần Ops xử lý";
  if (status === "Paid") return "Đã xác nhận thanh toán";
  return "";
}

export function tripTimeline(
  status: string,
  createdAt: string,
  history: readonly TripTimelineEvent[],
): TripTimelineItem[] {
  const terminal = terminalStatuses.has(status);
  const currentIndex = statusStep.get(status) ?? 0;
  const observed = history.map((event) => event.status);
  if (!observed.includes(status)) observed.push(status);

  if (terminal) {
    const lastCompleted = Math.max(
      0,
      ...steps.map((step, index) =>
        observed.some((value) => step.completedBy.includes(value)) ? index : -1,
      ),
    );
    const items: TripTimelineItem[] = steps
      .slice(0, lastCompleted + 1)
      .map((step) => ({
        title: step.title,
        description: "",
        at:
          lastEvent(history, step.completedBy)?.at ??
          (step.completedBy.includes("Requested") ? createdAt : null),
        state: "complete" as const,
      }));
    const event = lastEvent(history, [status]);
    items.push({
      title: status === "Cancelled" ? "Chuyến đã hủy" : "Không tìm được xe",
      description: event?.reason ?? "",
      at: event?.at ?? null,
      state: "current",
    });
    return items;
  }

  return steps.map((step, index) => {
    const current = index === currentIndex;
    const completed = index < currentIndex || (status === "Paid" && index === currentIndex);
    const event = lastEvent(history, current ? step.phase : step.completedBy);
    return {
      title: step.title,
      description: current ? currentDescription(status, index) : "",
      at: event?.at ?? (index === 0 ? createdAt : null),
      state: completed ? "complete" : current ? "current" : "upcoming",
    };
  });
}
