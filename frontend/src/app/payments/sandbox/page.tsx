import { auth, signIn } from "@/infrastructure/auth/auth";
import SandboxClient from "@/composition/SandboxClient";

export default async function SandboxPage({
  searchParams,
}: {
  searchParams: Promise<{ reference?: string }>;
}) {
  const params = await searchParams;
  const reference =
    typeof params.reference === "string" && /^[a-f0-9]{32}$/.test(params.reference)
      ? params.reference
      : undefined;
  const session = await auth();
  if (!session) {
    return (
      <main id="main" className="p-8">
        <form
          action={async () => {
            "use server";
            await signIn("keycloak", {
              redirectTo: `/payments/sandbox${reference ? `?reference=${reference}` : ""}`,
            });
          }}
        >
          <button className="btn">Đăng nhập để thử thanh toán</button>
        </form>
      </main>
    );
  }
  return (
    <main id="main" className="p-8">
      <SandboxClient reference={reference} />
    </main>
  );
}
