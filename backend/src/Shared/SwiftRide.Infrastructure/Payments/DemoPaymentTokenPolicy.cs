using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using SwiftRide.Application.Payments;
using SwiftRide.SharedKernel;

namespace SwiftRide.Infrastructure;

// ponytail: local mock tokens only; replace this adapter alongside the real payment provider.
public sealed class DemoPaymentTokenPolicy(IHostEnvironment environment, IConfiguration configuration) : IPaymentTokenPolicy
{
    public void Validate(PaymentMethod method, string? token)
    {
        if (method != PaymentMethod.Card) return;
        if (configuration["Payments:Provider"] != "Test")
        {
            if (!string.IsNullOrEmpty(token)) throw new RuleException("VNPAY không nhận token thẻ từ trình duyệt.", ErrorKind.Validation);
            return;
        }
        if (
            token
            is not (
                "demo-success"
                or "demo-decline"
                or "demo-transient"
                or "demo-unknown"
                or "demo-compensate"
                or "demo-unavailable"
            )
        )
            throw new RuleException("Chọn token thanh toán demo hợp lệ.", ErrorKind.Validation);
        if (!environment.IsDevelopment() && token != "demo-success")
            throw new RuleException(
                "Kịch bản lỗi chỉ dùng trong Development.",
                ErrorKind.Validation
            );
    }
}
