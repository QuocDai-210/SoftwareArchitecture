import { auth, signOut } from "@/infrastructure/auth/auth";
import { RideConsole } from "@/features/rides/presentation/RideConsole";

export default async function Home({
  searchParams,
}: {
  searchParams: Promise<{ login?: string }>;
}) {
  const session = await auth();
  const role = session?.user.roles.includes("driver") ? "driver" : "rider";
  return (
    <main id="main" className={session ? "dispatch-shell" : "mx-auto max-w-6xl px-5 py-6 sm:px-10"}>
      <header className="flex items-center justify-between gap-3">
        <span className="wordmark">
          Swift<span>Ride</span>
        </span>
        {session && (
          <details className="dispatch-account">
            <summary aria-label="Tài khoản">{session.user.name ?? role}</summary>
            <form
              action={async () => {
                "use server";
                await signOut({ redirectTo: "/" });
              }}
            >
              <button className="btn btn-secondary" type="submit">
                Đăng xuất
              </button>
            </form>
          </details>
        )}
      </header>
      {session ? (
        <RideConsole role={role} />
      ) : (
        <section className="card mx-auto mt-16 max-w-lg p-8 sm:p-12">
          <h1 className="text-3xl font-bold">Đặt xe và nhận chuyến</h1>
          <p className="muted my-5">Đăng nhập bằng tài khoản hành khách hoặc tài xế của bạn.</p>
          {(await searchParams).login === "failed" && (
            <p role="alert" className="mb-4 text-red-800">
              Phiên đăng nhập chưa hoàn tất. Vui lòng thử lại.
            </p>
          )}
          <a className="btn" href="/login">
            Đăng nhập
          </a>
        </section>
      )}
    </main>
  );
}
