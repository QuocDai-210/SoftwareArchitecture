using System.Diagnostics;
using System.Net;
using Microsoft.EntityFrameworkCore;

namespace SwiftRide.Infrastructure;

public sealed class InternalRetryHandler : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken ct
    )
    {
        var body = request.Content is null ? null : await request.Content.ReadAsByteArrayAsync(ct);
        for (var attempt = 0; ; attempt++)
        {
            using var copy = new HttpRequestMessage(request.Method, request.RequestUri);
            foreach (var h in request.Headers)
                copy.Headers.TryAddWithoutValidation(h.Key, h.Value);
            if (body is not null)
            {
                copy.Content = new ByteArrayContent(body);
                foreach (var h in request.Content!.Headers)
                    copy.Content.Headers.TryAddWithoutValidation(h.Key, h.Value);
            }
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(TimeSpan.FromSeconds(5));
                var response = await base.SendAsync(copy, timeout.Token);
                if (
                    attempt == 3
                    || (
                        (int)response.StatusCode < 500
                        && response.StatusCode != HttpStatusCode.RequestTimeout
                        && response.StatusCode != HttpStatusCode.TooManyRequests
                    )
                )
                    return response;
                response.Dispose();
            }
            catch (HttpRequestException) when (attempt < 3) { }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested && attempt < 3) { }
            await Task.Delay(
                TimeSpan.FromMilliseconds(200 * Math.Pow(2, attempt) + Random.Shared.Next(100)),
                ct
            );
        }
    }
}
