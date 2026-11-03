"use client";
import { useEffect, useState } from "react";
import type { Point } from "../../domain/models";
import type { RideService } from "../../application/RideService";

export function AddressField({
  label,
  value,
  onChange,
  service,
  disabled,
  onFocus,
}: {
  label: string;
  value: Point | null;
  onChange: (point: Point | null) => void;
  service: RideService;
  disabled: boolean;
  onFocus?: () => void;
}) {
  const [text, setText] = useState(value?.label ?? "");
  const [results, setResults] = useState<Point[]>([]);
  const [error, setError] = useState("");
  useEffect(() => {
    if (value) {
      setText(value.label);
      setResults([]);
    }
  }, [value]);
  useEffect(() => {
    if (text.trim().length < 3 || text === value?.label || disabled) return;
    let active = true;
    const timer = setTimeout(() => {
      service
        .search(text.trim())
        .then((points) => {
          if (active) {
            setResults(points);
            setError("");
          }
        })
        .catch((e) => {
          if (active) {
            setResults([]);
            setError(e.message);
          }
        });
    }, 400);
    return () => {
      active = false;
      clearTimeout(timer);
    };
  }, [text, value, service, disabled]);
  return (
    <div>
      <label className="block text-sm font-bold">
        {label}
        <input
          className="field mt-2 font-normal"
          value={text}
          maxLength={120}
          disabled={disabled}
          autoComplete="off"
          onFocus={onFocus}
          onChange={(e) => {
            setText(e.target.value);
            setResults([]);
            setError("");
            onChange(null);
          }}
        />
      </label>
      {text !== value?.label && text.length > 0 && (
        <p className="text-xs mt-1">Chọn địa chỉ từ kết quả tìm kiếm.</p>
      )}
      {error && (
        <p role="alert" className="text-sm text-red-800">
          {error}
        </p>
      )}
      {results.length > 0 && (
        <ul className="border rounded-lg divide-y mt-2" aria-label={`Kết quả ${label}`}>
          {results.map((point, i) => (
            <li key={i}>
              <button
                className="text-left w-full p-3 text-sm hover:bg-green-50"
                type="button"
                onClick={() => {
                  onChange(point);
                  setText(point.label);
                  setResults([]);
                }}
              >
                {point.label}
              </button>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}
