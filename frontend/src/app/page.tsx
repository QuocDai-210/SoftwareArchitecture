import { auth, signIn, signOut } from "@/infrastructure/auth/auth";
import RideClient from "@/composition/RideClient";

export default async function Home() {
  const session = await auth();
  if (!session)
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
              Đăng nhập để đặt xe, nhận chuyến hoặc quản lý hoạt động của bạn.
            </p>
            <form
              action={async () => {
                "use server";
                await signIn("keycloak", { redirectTo: "/" });
              }}
            >
              <button className="btn w-full" type="submit">
                Đăng nhập để tiếp tục <span aria-hidden="true">↗</span>
              </button>
            </form>
            <form
              className="mt-3"
              action={async () => {
                "use server";
                await signIn("keycloak", { redirectTo: "/" }, { prompt: "create" });
              }}
            >
              <button className="btn btn-secondary w-full" type="submit">
                Đăng ký tài khoản User
              </button>
            </form>
            <div className="mt-7 pt-5 border-t border-emerald-100 text-xs leading-relaxed muted">
              <strong className="text-emerald-900">Bạn muốn trở thành tài xế?</strong>
              <p className="mt-1">Liên hệ bộ phận vận hành để gửi hồ sơ và được xét duyệt.</p>
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
