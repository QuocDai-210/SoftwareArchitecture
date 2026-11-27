const { chromium } = require("playwright-core");
const assert = require("node:assert/strict");
process.loadEnvFile(".env");
async function check() {
  const browser = await chromium.launch({
    executablePath: process.env.CHROME_BIN,
    headless: true,
    args: ["--no-sandbox", "--disable-dev-shm-usage", "--no-proxy-server"],
  });
  try {
    const role = async (name, password, mobile) => {
      const context = await browser.newContext({
        viewport: mobile ? { width: 390, height: 844 } : { width: 1280, height: 820 },
        isMobile: mobile,
        hasTouch: mobile,
        locale: "vi-VN",
      });
      const page = await context.newPage();
      page.setDefaultTimeout(35000);
      await page.goto("http://localhost:8976");
      await page.getByRole("link", { name: "Đăng nhập", exact: true }).click();
      await page.locator("#username").fill(name);
      await page.locator("#password").fill(password);
      await page.locator("#kc-login").click();
      await page
        .getByRole("heading", {
          name: mobile ? "Lời mời và hành trình" : "Chuyến đi của bạn",
          exact: true,
        })
        .waitFor();
      assert(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth));
      return page;
    };
    const rider = await role("rider1", process.env.RIDER_PASSWORD, false);
    const driver = await role("driver1", process.env.DRIVER_PASSWORD, true);
    console.log("PASS desktop Rider and mobile Driver sign in via Keycloak");
    const click = (page, name) => page.getByRole("button", { name, exact: true }).click();
    assert.equal(await rider.locator(".leaflet-container").count(), 1);
    assert.equal(await rider.getByRole("textbox", { name: "Điểm đến" }).count(), 0);
    await click(rider, "Lịch sử chuyến");
    await rider.getByRole("heading", { name: "Lịch sử chuyến" }).waitFor();
    await click(rider, "← Trang chính");
    await click(rider, "Bạn muốn đi đâu?");
    await rider.getByRole("textbox", { name: "Điểm đến", exact: true }).fill("Đại học Sài Gòn");
    await rider.getByRole("button", { name: "Đại học Sài Gòn", exact: true }).click();
    await click(rider, "Đặt chuyến");
    await click(rider, "Xác nhận giá");
    await click(driver, "Nhận chuyến");
    await click(driver, "Đang đến đón");
    await click(driver, "Đã đón khách");
    await click(driver, "Đã trả khách");
    await rider.getByText(/Thanh toán · Đã thu/).waitFor();
    const paid = await rider.locator(".badge[role=status]").textContent();
    assert.equal(paid, "Chuyến hoàn tất");
    assert.equal(await driver.getByText(/Thanh toán · Đã thu/).count(), 0);
    await click(rider, "Đặt chuyến mới");
    const map = rider.locator(".leaflet-container");
    await map.click({ position: { x: 60, y: 80 } });
    const destination = rider.getByRole("textbox", { name: "Điểm đến", exact: true });
    assert.match(await destination.inputValue(), /,/);
    await destination.fill("cho ben thanh");
    await rider.getByRole("button", { name: "Chợ Bến Thành", exact: true }).click();
    await click(rider, "Đặt chuyến");
    await click(rider, "Xác nhận giá");

    await click(driver, "Từ chối");
    await rider.getByRole("button", { name: "Xác nhận giá", exact: true }).waitFor();
    await click(rider, "Hủy chuyến");
    await rider.locator(".badge[role=status]").filter({ hasText: "Đã hủy" }).waitFor();
    console.log(
      "PASS map selection, destination search, driver rejection, new quote and rider cancellation",
    );
    await driver.getByLabel("Tài khoản").click();
    await click(driver, "Đăng xuất");
    await driver.getByRole("link", { name: "Đăng nhập" }).waitFor();
    console.log(
      "PASS Wallet quote, offer, drive, dropoff, Paid rider receipt, driver receipt privacy and logout",
    );
  } finally {
    await browser.close();
  }
}
check().catch((e) => {
  console.error(e.message);
  process.exitCode = 1;
});
