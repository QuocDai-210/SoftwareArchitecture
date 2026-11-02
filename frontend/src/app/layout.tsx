import type { Metadata } from "next";
import "./globals.css";
export const metadata: Metadata = { title: "SwiftRide · Đi gần hơn", description: "Đồ án đặt xe SwiftRide — Rider và Driver, từ đặt chuyến đến thanh toán." };
export default function RootLayout({ children }: Readonly<{ children: React.ReactNode }>) {
  return <html lang="vi"><body><a className="sr-only focus:not-sr-only focus:fixed focus:top-2 focus:left-2 focus:z-50 focus:bg-white focus:p-3" href="#main">Đến nội dung chính</a>{children}</body></html>;
}
