using SwiftRide.SharedKernel;

namespace SwiftRide.Application.Payments;

public interface IPaymentTokenPolicy
{
    void Validate(PaymentMethod method, string? token);
}
