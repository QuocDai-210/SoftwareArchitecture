namespace SwiftRide.Payments.Application;

public sealed class PaymentMethodFactory(IEnumerable<IPaymentMethod> methods)
{
    private readonly IReadOnlyDictionary<PaymentMethod, IPaymentMethod> _methods =
        methods.ToDictionary(x => x.Method);

    public IPaymentMethod Create(PaymentMethod method) =>
        _methods.TryGetValue(method, out var implementation)
            ? implementation
            : throw new RuleException("Phương thức thanh toán không hợp lệ.", ErrorKind.Validation);
}
