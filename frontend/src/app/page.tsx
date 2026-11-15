import { AuthError } from "next-auth";
import { redirect } from "next/navigation";
import { auth, signIn, signOut } from "@/infrastructure/auth/auth";
import RideClient from "@/composition/RideClient";

export default async function Home({
  searchParams,
}: {
  searchParams: Promise<{ login?: string }>;
}) {
  const session = await auth();
  const params = await searchParams;
  if (!session || !session.user.roles?.includes("rider"))
    return (
      <main id="main" className="welcome-page">
        <header className="welcome-header">
          <a className="wordmark" href="/">
            <span className="brand-symbol" aria-hidden="true">
              s
            </span>
            swift<span>ride</span>
          </a>
          <span className="badge">Đi gần hơn. Mỗi ngày.</span>
        </header>
        <div className="welcome-body">
          <section className="welcome-hero">
            <p className="eyebrow">MỖI ĐIỂM ĐẾN, MỘT KHỞI ĐẦU</p>
            <h1>
              Thành phố rộng.
              <br />
              Mình đi <span>cùng nhau.</span>
            </h1>
            <p className="welcome-description">
              Đặt xe dễ dàng. Giá rõ ràng.
              <br />
              An tâm theo dõi từng chặng đường.
            </p>
            <div className="welcome-route" aria-hidden="true">
              <svg viewBox="0 0 460 200" fill="none">
                <path
                  d="M0 45h460M0 120h460M90 0v200M240 0v200M370 0v200"
                  stroke="#d0eade"
                  strokeWidth="20"
                />
                <path
                  d="M65 130h105V65h158v75h70"
                  stroke="#087b66"
                  strokeWidth="6"
                  strokeLinecap="round"
                  strokeLinejoin="round"
                />
                <circle cx="65" cy="130" r="11" fill="white" stroke="#087b66" strokeWidth="5" />
                <circle cx="398" cy="140" r="11" fill="#087b66" stroke="white" strokeWidth="5" />
              </svg>
              <div className="welcome-route-label">
                <span className="status-dot" />
                Từ nơi bạn ở, đến nơi bạn muốn.
              </div>
            </div>
            <div className="welcome-benefits">
              <span>Giá trước chuyến đi</span>
              <span>Thanh toán tiền mặt</span>
              <span>Theo dõi trên bản đồ</span>
            </div>
          </section>
          <section className="welcome-auth card">
            <span className="service-icon" aria-hidden="true">
              S
            </span>
            <p className="eyebrow mt-6">CHÀO MỪNG ĐẾN SWIFTRIDE</p>
            <h2 className="text-3xl font-bold mt-3">
              Chuyến đi bắt đầu
              <br />
              từ đây.
            </h2>
            <p className="muted text-sm leading-relaxed mt-4 mb-7">
              Đăng nhập bằng tài khoản Rider để đặt và theo dõi chuyến đi mô phỏng.
            </p>
            <form
              action={async (formData: FormData) => {
                "use server";
                try {
                  await signIn("credentials", {
                    username: formData.get("username"),
                    password: formData.get("password"),
                    redirectTo: "/",
                  });
                } catch (error) {
                  if (error instanceof AuthError) {
                    redirect(
                      error.type === "CredentialsSignin" ? "/?login=failed" : "/?login=unavailable",
                    );
                  }
                  throw error;
                }
              }}
            >
              <label className="block text-sm font-semibold" htmlFor="username">
                Tên đăng nhập
              </label>
              <input
                className="field mt-2"
                id="username"
                name="username"
                autoComplete="username"
                required
              />
              <label className="mt-4 block text-sm font-semibold" htmlFor="password">
                Mật khẩu
              </label>
              <input
                className="field mt-2"
                id="password"
                name="password"
                type="password"
                autoComplete="current-password"
                required
              />
              {params.login === "failed" && (
                <p className="mt-3 text-sm text-red-700" role="alert">
                  Không thể đăng nhập. Hãy kiểm tra tên đăng nhập, mật khẩu và sử dụng tài khoản
                  Rider.
                </p>
              )}
              {params.login === "unavailable" && (
                <p className="mt-3 text-sm text-red-700" role="alert">
                  Dịch vụ đăng nhập tạm thời chưa sẵn sàng. Vui lòng thử lại.
                </p>
              )}
              <button className="btn w-full" type="submit">
                Đăng nhập <span aria-hidden="true">↗</span>
              </button>
            </form>
            <div className="mt-7 pt-5 border-t border-emerald-100 text-xs leading-relaxed muted">
              Trang này dành cho tài khoản Rider được cấp sẵn trong môi trường mô phỏng.
            </div>
          </section>
        </div>
        <footer className="welcome-footer">
          <span>SWIFTRIDE · ĐI GẦN HƠN</span>
          <span>Bản đồ thực tế · Xe di chuyển giả lập</span>
        </footer>
      </main>
    );
  return (
    <RideClient
      mapKey={process.env.GEOAPIFY_MAP_KEY ?? ""}
      user={{ name: session.user.name ?? "Bạn", roles: session.user.roles, id: session.user.id }}
      logout={
        <form
          action={async () => {
            "use server";
            await signOut({ redirectTo: "/" });
          }}
        >
          <button type="submit" className="text-sm underline underline-offset-4 min-h-11">
            Đăng xuất
          </button>
        </form>
      }
    />
  );
}
