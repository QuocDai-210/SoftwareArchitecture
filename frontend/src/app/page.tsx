import { auth, signIn, signOut } from "@/infrastructure/auth/auth";
import RideClient from "@/composition/RideClient";

export default async function Home() {
  const session = await auth();
  if (!session)
    return (
      <main id="main" className="min-h-screen grid lg:grid-cols-2">
        <section className="bg-[#164b40] p-8 sm:p-16 text-white flex flex-col justify-between gap-16">
          <div className="text-2xl font-black tracking-tight">
            swift<span className="text-[#b4d59a]">ride</span>
            <span className="text-xs ml-3 font-normal tracking-widest">ĐI GẦN HƠN</span>
          </div>
          <div>
            <p className="text-[#c5ddbb] text-sm uppercase tracking-[.2em] mb-5">
              Thành phố của bạn. Nhịp đi của bạn.
            </p>
            <h1 className="text-4xl sm:text-6xl font-bold leading-tight max-w-lg">
              Một chuyến đi.
              <br />
              Trọn vẹn từng bước.
            </h1>
            <p className="text-[#d0ded5] mt-6 max-w-md leading-relaxed">
              Đặt xe, xác nhận giá và theo dõi tài xế. Mọi trạng thái đều rõ ràng.
            </p>
          </div>
          <p className="text-sm text-[#c5d6cc]">SWIFTRIDE · ĐỒ ÁN KIẾN TRÚC PHẦN MỀM</p>
        </section>
        <section className="p-8 sm:p-16 flex flex-col justify-center max-w-xl mx-auto w-full">
          <p className="text-sm text-[#557466] mb-3">CHÀO MỪNG ĐẾN SWIFTRIDE</p>
          <h2 className="text-3xl font-bold mb-3">Bắt đầu hành trình</h2>
          <p className="text-[#60756b] leading-relaxed mb-8">
            User đặt xe, Driver nhận chuyến, Ops quản trị hệ thống. Tài xế được Ops xét duyệt và cấp
            tài khoản.
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
            className="mt-4"
            action={async () => {
              "use server";
              await signIn("keycloak", { redirectTo: "/" }, { prompt: "create" });
            }}
          >
            <button className="btn btn-secondary w-full" type="submit">
              Đăng ký tài khoản User
            </button>
          </form>
          <div className="card p-5 mt-8 text-sm leading-relaxed">
            <strong>Thông tin sử dụng</strong>
            <p className="mt-2 text-[#60756b]">
              Bản đồ dùng đường đi thực tế. Xe di chuyển giả lập để phục vụ đồ án. Đặt điểm đón,
              điểm đến và theo dõi từng mốc đón/trả khách trên bản đồ.
            </p>
          </div>
        </section>
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
