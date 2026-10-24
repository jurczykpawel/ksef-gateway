using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace KSeFGateway.Api.Tests.Integration;

internal static class MetadataQuery
{
    internal static async Task<List<string?>> ReadAsync(Func<Task<HttpResponseMessage>> request,
        Func<TimeSpan, Task>? delay = null, Func<DateTimeOffset>? utcNow = null)
    {
        using var response = await RequestAsync(request, delay, utcNow);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("data").GetProperty("invoices").EnumerateArray()
            .Select(i => i.GetProperty("invoiceNumber").GetString()).ToList();
    }

    internal static async Task<HttpResponseMessage> RequestAsync(Func<Task<HttpResponseMessage>> request,
        Func<TimeSpan, Task>? delay = null, Func<DateTimeOffset>? utcNow = null)
    {
        delay ??= d => Task.Delay(d);
        utcNow ??= () => DateTimeOffset.UtcNow;
        var waited = TimeSpan.Zero;
        for (var attempt = 0; ; attempt++)
        {
            var response = await request();
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                var now = utcNow();
                var retry = response.Headers.RetryAfter?.Delta
                    ?? (response.Headers.RetryAfter?.Date - now);
                response.Dispose();
                if (retry is null || retry <= TimeSpan.Zero || retry > TimeSpan.FromSeconds(15)
                    || waited + retry > TimeSpan.FromSeconds(15) || attempt >= 2)
                    throw new InvalidOperationException(retry is null
                        ? "limit KSeF TEST wyczerpany, brak Retry-After; sprawdź status przed ponowieniem"
                        : $"limit KSeF TEST wyczerpany, spróbuj po {now + retry:HH:mm} UTC");
                await delay(retry.Value);
                waited += retry.Value;
                continue;
            }
            return response;
        }
    }
}
