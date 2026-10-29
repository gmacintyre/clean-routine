using Microsoft.AspNetCore.Authentication;
using System.Security.Cryptography;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using CleanRoutine.Domain;
using CleanRoutine.Push;
using CleanRoutine.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

if (args.Contains("generate-vapid-keys"))
{
    var (publicKey, privateKey) = WebPushCrypto.GenerateVapidKeys();
    Console.WriteLine("Add these as app settings (keep the private key secret):");
    Console.WriteLine($"Push__PublicKey={publicKey}");
    Console.WriteLine($"Push__PrivateKey={privateKey}");
    return;
}

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<HouseholdOptions>(builder.Configuration.GetSection("Household"));
builder.Services.Configure<StorageOptions>(builder.Configuration.GetSection("Storage"));
builder.Services.Configure<PushOptions>(builder.Configuration.GetSection("Push"));

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<HouseholdClock>();
builder.Services.AddSingleton<HouseholdStore>();
builder.Services.AddSingleton<DayPlanner>();
builder.Services.AddSingleton<IPasswordHasher<AppUser>, PasswordHasher<AppUser>>();
builder.Services.AddSingleton<AccountService>();
builder.Services.AddHttpClient("webpush", c => c.Timeout = TimeSpan.FromSeconds(20));
builder.Services.AddSingleton<WebPushSender>();
builder.Services.Configure<ReminderOptions>(builder.Configuration.GetSection("Reminders"));
builder.Services.AddSingleton<DailyReminderService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<DailyReminderService>());

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.LoginPath = "/Account/Login";
        o.LogoutPath = "/Account/Logout";
        o.Cookie.Name = "clean.auth";
        o.Cookie.HttpOnly = true;
        o.Cookie.SameSite = SameSiteMode.Lax;
        o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        // Stay signed in on your phones; any visit within 180 days extends it.
        o.ExpireTimeSpan = TimeSpan.FromDays(180);
        o.SlidingExpiration = true;
        o.Events.OnValidatePrincipal = async ctx =>
        {
            var id = ctx.Principal?.UserId();
            var accounts = ctx.HttpContext.RequestServices.GetRequiredService<AccountService>();
            if (id is null || !await accounts.ExistsAsync(id))
            {
                ctx.RejectPrincipal();
                await ctx.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            }
        };
    });

// Every page needs a signed-in user unless it opts out (the sign-in pages do).
builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy("signin", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(5) }));
    o.AddFixedWindowLimiter("wake", x =>
    {
        x.PermitLimit = 20;
        x.Window = TimeSpan.FromMinutes(10);
    });
});

builder.Services.AddRazorPages(o =>
{
    o.Conventions.AllowAnonymousToFolder("/Account");
    o.Conventions.AllowAnonymousToPage("/Error");
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseStaticFiles();
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapRazorPages();

// Wake-up link for the Free plan: a scheduled GitHub Actions job calls this, which starts the app if
// it's asleep and sends today's reminder if it's due. Calling it more often than needed is harmless.
app.MapPost("/api/reminders/run", async (HttpRequest request, IOptions<ReminderOptions> options,
        DailyReminderService reminders, CancellationToken ct) =>
    {
        var expected = options.Value.TriggerKey;
        if (string.IsNullOrWhiteSpace(expected)) return Results.NotFound();

        var given = request.Headers["X-Reminder-Key"].ToString();
        var match = CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(given)), SHA256.HashData(Encoding.UTF8.GetBytes(expected)));
        if (!match) return Results.Unauthorized();

        return Results.Ok(new { status = await reminders.RunIfDueAsync(ct) });
    })
    .AllowAnonymous()
    .DisableAntiforgery()
    .RequireRateLimiting("wake");

app.Run();
