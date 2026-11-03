import { useState } from "react";
import type { Trip } from "../../domain/models";

export function TripReview({
  trip,
  isRider,
  busy,
  submit,
}: {
  trip: Trip;
  isRider: boolean;
  busy: boolean;
  submit: (rating: number, comment: string) => Promise<void>;
}) {
  const [rating, setRating] = useState(5);
  const [comment, setComment] = useState("");
  if (trip.rating !== null && trip.rating !== undefined)
    return (
      <section className="p-5 border-t border-[#dcebe5]" aria-label="Đánh giá chuyến đi">
        <h2 className="font-bold">Đánh giá: {trip.rating}/5 sao</h2>
        {trip.review && <p className="text-sm mt-2 break-words">{trip.review}</p>}
      </section>
    );
  if (!isRider || !["PaymentPending", "PaymentReview", "Paid"].includes(trip.status)) return null;
  return (
    <section className="p-5 border-t border-[#dcebe5]" aria-label="Đánh giá chuyến đi">
      <h2 className="font-bold">Chuyến đi của bạn thế nào?</h2>
      <form
        className="space-y-3 mt-3"
        onSubmit={(event) => {
          event.preventDefault();
          void submit(rating, comment);
        }}
      >
        <label className="block text-sm">
          Mức đánh giá
          <select
            className="field mt-2"
            value={rating}
            disabled={busy}
            onChange={(event) => setRating(Number(event.target.value))}
          >
            {[5, 4, 3, 2, 1].map((value) => (
              <option key={value} value={value}>
                {value} sao
              </option>
            ))}
          </select>
        </label>
        <label className="block text-sm">
          Nhận xét (không bắt buộc)
          <textarea
            className="field mt-2"
            rows={2}
            maxLength={500}
            value={comment}
            disabled={busy}
            onChange={(event) => setComment(event.target.value)}
          />
        </label>
        <button className="btn w-full" disabled={busy}>
          Gửi đánh giá
        </button>
      </form>
    </section>
  );
}
