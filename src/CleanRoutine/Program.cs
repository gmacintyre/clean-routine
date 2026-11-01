using Microsoft.AspNetCore.Authentication;
using System.Security.Cryptography;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using CleanRoutine.Billing;
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
builder.Services.Configure<SiteOptions>(builder.Configuration.GetSection("Site"));
builder.Services.Configure<AdsOptions>(builder.Configuration.GetSection("Ads"));
builder.Services.Configure<BillingOptions>(builder.Configuration.GetSection("Billing"));

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<HouseholdClock>();
builder.Services.AddSingleton<DataPaths>();
builder.Services.AddSingleton<AccountStore>();
builder.Services.AddSingleton<HouseholdStore>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<CurrentHousehold>();
builder.Services.AddSingleton<DayPlanner>();
builder.Services.AddSingleton<IPasswordHasher<AppUser>, PasswordHasher<AppUser>>();
builder.Services.AddSingleton<AccountService>();
builder.Services.AddHttpClient("webpush", c => c.Timeout = TimeSpan.FromSeconds(20));
builder.Services.AddSingleton<WebPushSender>();
builder.Services.AddHttpClient(StripeBilling.HttpClientName, c =>
{
    c.BaseAddress = new Uri("https://api.stripe.com/");
    c.Timeout = TimeSpan.FromSeconds(30);
});
builder.Services.AddSingleton<StripeBilling>();
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
            var id = ctx.Principal?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            var accounts = ctx.HttpContext.RequestServices.GetRequiredService<AccountService>();
            var user = id is null ? null : await accounts.FindAsync(id);
            if (user is null)
            {
                ctx.RejectPrincipal();
                await ctx.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                return;
            }
            // Cookies from before households existed, or after joining another household: refresh the claims.
            if (ctx.Principal!.FindFirst(AccountService.HouseholdClaim)?.Value != user.HouseholdId
                || ctx.Principal.Identity?.Name != user.DisplayName)
            {
                ctx.ReplacePrincipal(AccountService.ToPrincipal(user));
                ctx.ShouldRenew = true;
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
    o.Conventions.AllowAnonymousToPage("/Welcome");
    o.Conventions.AllowAnonymousToPage("/Privacy");
    o.Conventions.AllowAnonymousToPage("/Terms");
    o.Conventions.AllowAnonymousToPage("/Join");
});

var app = builder.Build();

// The first time this version runs, move the original single household into the multi-household layout.
await DataMigration.RunAsync(app.Services);

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

// Visitors who aren't signed in see the welcome page instead of being sent straight to sign-in.
app.Use(async (ctx, next) =>
{
    if (HttpMethods.IsGet(ctx.Request.Method) && ctx.Request.Path == "/" && ctx.User.Identity?.IsAuthenticated != true)
    {
        ctx.Response.Redirect("/Welcome");
        return;
    }
    await next();
});

app.UseAuthorization();

app.MapRazorPages();

// ads.txt tells ad buyers which ad accounts may sell space on this site. AdSense checks for it.
app.MapGet("/ads.txt", (IOptions<AdsOptions> ads) => ads.Value.PublisherId is { Length: > 0 } pub
        ? Results.Text($"google.com, {pub}, DIRECT, f08c47fec0942fa0\n", "text/plain")
        : Results.NotFound())
    .AllowAnonymous();

// Stripe calls this when a checkout finishes and when a subscription changes or ends.
app.MapPost("/api/stripe/webhook", async (HttpRequest request, StripeBilling billing) =>
    {
        using var reader = new StreamReader(request.Body);
        var payload = await reader.ReadToEndAsync();
        return await billing.HandleWebhookAsync(payload, request.Headers["Stripe-Signature"])
            ? Results.Ok()
            : Results.BadRequest();
    })
    .AllowAnonymous()
    .DisableAntiforgery();

// Wake-up link for plans without Always On: a scheduled GitHub Actions job calls this, which starts the
// app if it's asleep and sends any reminders that are due. Calling it more often than needed is harmless.
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
