using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using SwiftRide.Application.Serialization;
using SwiftRide.SharedKernel;

namespace SwiftRide.Infrastructure;

public static class HttpResponseExtensions
{
    public static async Task<T> Read<T>(this HttpResponseMessage response, CancellationToken ct)
    {
        if (!response.IsSuccessStatusCode)
        {
            if (response.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.NotFound)
                throw new RuleException(
                    "Tài nguyên đã đổi trạng thái hoặc không tồn tại.",
                    response.StatusCode == HttpStatusCode.NotFound
                        ? ErrorKind.NotFound
                        : ErrorKind.Conflict
                );
            if (response.StatusCode == HttpStatusCode.BadRequest)
                throw new RuleException("Dữ liệu không hợp lệ.", ErrorKind.Validation);
            response.EnsureSuccessStatusCode();
        }
        return (await response.Content.ReadFromJsonAsync<T>(ApplicationJson.Options, ct))!;
    }
}
