using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using CleanRoutine.Domain;
using CleanRoutine.Services;
using Microsoft.Extensions.Options;

namespace CleanRoutine.Push;

public record PushMessage(string Title, string Body, string Url = "/", string Tag = "daily");

public record PushResult(int Sent, int Removed, int Failed);

public class WebPushSender(
    IHttpClientFactory http,
    IOptions<PushOptions> options,
    HouseholdStore store,
    TimeProvider time,
    ILogger<WebPushSender> log)
{
    public bool IsConfigured => options.Value.IsConfigured;
    public string? PublicKey => options.Value.PublicKey;

    /// <summary>Send to every device in the household, or only one person's devices when <paramref name="userId"/> is set.
    /// Devices the push service says are gone (404/410) are removed.</summary>
    public async Task<PushResult> SendAsync(string householdId, PushMessage message, string? userId = null,
        CancellationToken ct = default)
    {
        if (!IsConfigured) return new PushResult(0, 0, 0);

        var subs = await store.ReadAsync(householdId, d => d.PushSubscriptions
            .Where(s => userId is null || s.UserId == userId)
            .Select(s => new PushSubscriptionRecord
                { Id = s.Id, Endpoint = s.Endpoint, P256dh = s.P256dh, Auth = s.Auth })
            .ToList());

        var payload = JsonSerializer.SerializeToUtf8Bytes(message, JsonSerializerOptions.Web);
        var client = http.CreateClient("webpush");
        var gone = new List<string>();
        int sent = 0, failed = 0;

        foreach (var sub in subs)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, sub.Endpoint);
                request.Headers.TryAddWithoutValidation("Authorization", WebPushCrypto.VapidAuthorization(
                    sub.Endpoint, options.Value.Subject, options.Value.PublicKey!, options.Value.PrivateKey!,
                    time.GetUtcNow()));
                request.Headers.Add("TTL", "43200");
                request.Headers.Add("Urgency", "normal");
                request.Content = new ByteArrayContent(WebPushCrypto.Encrypt(payload, sub.P256dh, sub.Auth));
                request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                request.Content.Headers.ContentEncoding.Add("aes128gcm");

                using var response = await client.SendAsync(request, ct);
                if (response.IsSuccessStatusCode)
                {
                    sent++;
                }
                else if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
                {
                    gone.Add(sub.Id);
                }
                else
                {
                    failed++;
                    log.LogWarning("Push to {Host} failed: {Status} {Body}", new Uri(sub.Endpoint).Host,
                        (int)response.StatusCode, await response.Content.ReadAsStringAsync(ct));
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failed++;
                log.LogWarning(ex, "Push to a device failed.");
            }
        }

        if (gone.Count > 0)
            await store.WriteAsync(householdId, d => d.PushSubscriptions.RemoveAll(s => gone.Contains(s.Id)));

        return new PushResult(sent, gone.Count, failed);
    }
}
