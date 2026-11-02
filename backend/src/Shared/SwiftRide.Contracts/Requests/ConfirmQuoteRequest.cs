using System.Text.Json.Serialization;

namespace SwiftRide.Contracts;

public sealed record ConfirmQuoteRequest(
    [property: JsonRequired] long ExpectedVersion,
    [property: JsonRequired] Guid QuoteId
);
