using System.Security.Claims;
using CleanRoutine.Domain;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;

namespace CleanRoutine.Services;

public record SignedInUser(string Id, string DisplayName, string Email, string HouseholdId);

/// <summary>Sign-up, sign-in and households. Anyone can sign up: they either start a new household
/// (which gets the starter routine) or join an existing one through its invite link.</summary>
public class AccountService(AccountStore accounts, HouseholdStore households, IPasswordHasher<AppUser> hasher)
{
    public const int MinPasswordLength = 8;
    public const int MaxMembers = 12;

    /// <summary>The household an invite code belongs to, or null.</summary>
    public Task<(string Id, string Name, int Members)?> FindInviteAsync(string? code) => accounts.ReadAsync<(string, string, int)?>(d =>
    {
        if (string.IsNullOrWhiteSpace(code)) return null;
        var h = d.Households.FirstOrDefault(x => string.Equals(x.InviteCode, code.Trim(), StringComparison.OrdinalIgnoreCase));
        return h is null ? null : (h.Id, h.Name, d.Users.Count(u => u.HouseholdId == h.Id));
    });

    public async Task<(SignedInUser? User, string? Error)> RegisterAsync(string name, string email, string password,
        string? inviteCode, string? timeZone)
    {
        email = email.Trim();
        name = name.Trim();
        if (password.Length < MinPasswordLength)
            return (null, $"Use at least {MinPasswordLength} characters for the password.");
        if (name.Length == 0)
            return (null, "Add your first name so the checklist can show who did what.");
        if (name.Length > 40) name = name[..40];
        if (!email.Contains('@') || email.Length > 254)
            return (null, "Check the email address.");

        var hash = hasher.HashPassword(new AppUser(), password);
        return await accounts.WriteAsync<(SignedInUser?, string?)>(d =>
        {
            if (d.Users.Any(u => string.Equals(u.Email, email, StringComparison.OrdinalIgnoreCase)))
                return (null, "There's already an account for that email. Sign in instead.");

            HouseholdInfo? household = null;
            if (!string.IsNullOrWhiteSpace(inviteCode))
            {
                household = d.Households.FirstOrDefault(h =>
                    string.Equals(h.InviteCode, inviteCode.Trim(), StringComparison.OrdinalIgnoreCase));
                if (household is null)
                    return (null, "That invite link has expired. Ask for a new one, or sign up without it to start your own household.");
                if (d.Users.Count(u => u.HouseholdId == household.Id) >= MaxMembers)
                    return (null, "That household is full.");
            }
            else
            {
                household = new HouseholdInfo
                {
                    Name = $"{name}'s home",
                    TimeZone = HouseholdClock.IsValidZone(timeZone) ? timeZone! : "America/New_York",
                };
                d.Households.Add(household);
            }

            var user = new AppUser { Email = email, DisplayName = name, PasswordHash = hash, HouseholdId = household.Id };
            d.Users.Add(user);
            return (ToSignedIn(user), null);
        });
    }

    public async Task<SignedInUser?> ValidateAsync(string email, string password)
    {
        var user = await accounts.ReadAsync(d => d.Users.FirstOrDefault(u =>
            string.Equals(u.Email, email.Trim(), StringComparison.OrdinalIgnoreCase)) is { } u
            ? new AppUser { Id = u.Id, Email = u.Email, DisplayName = u.DisplayName, PasswordHash = u.PasswordHash, HouseholdId = u.HouseholdId }
            : null);
        if (user is null)
        {
            // Spend the same time as a real check so response time doesn't reveal which emails exist.
            hasher.HashPassword(new AppUser(), password);
            return null;
        }

        var result = hasher.VerifyHashedPassword(user, user.PasswordHash, password);
        if (result == PasswordVerificationResult.Failed) return null;
        if (result == PasswordVerificationResult.SuccessRehashNeeded)
            await accounts.WriteAsync(d =>
            {
                var u = d.Users.First(x => x.Id == user.Id);
                u.PasswordHash = hasher.HashPassword(u, password);
            });

        return ToSignedIn(user);
    }

    public Task<SignedInUser?> FindAsync(string userId) =>
        accounts.ReadAsync(d => d.Users.FirstOrDefault(u => u.Id == userId) is { } u ? ToSignedIn(u) : null);

    /// <summary>Move a signed-in person into the household an invite belongs to. If they were the last
    /// member of their old household, that household is deleted.</summary>
    public async Task<string?> JoinAsync(string userId, string inviteCode)
    {
        string? left = null;
        var (error, emptied) = await accounts.WriteAsync<(string?, string?)>(d =>
        {
            var user = d.Users.FirstOrDefault(u => u.Id == userId);
            var target = d.Households.FirstOrDefault(h =>
                string.Equals(h.InviteCode, inviteCode.Trim(), StringComparison.OrdinalIgnoreCase));
            if (user is null || target is null) return ("That invite link has expired. Ask for a new one.", null);
            if (user.HouseholdId == target.Id) return (null, null);
            if (d.Users.Count(u => u.HouseholdId == target.Id) >= MaxMembers) return ("That household is full.", null);

            var old = user.HouseholdId;
            var lastMember = d.Users.Count(u => u.HouseholdId == old) == 1;
            if (lastMember && d.Households.FirstOrDefault(h => h.Id == old) is { } oldHousehold && HasLiveSubscription(oldHousehold))
                return ("Your household still has an ad-free subscription. Cancel it under Settings → Ad-free first, so you aren't charged for an empty household.", null);

            user.HouseholdId = target.Id;
            left = old;
            if (!lastMember) return (null, null);
            d.Households.RemoveAll(h => h.Id == old);
            return (null, old);
        });
        if (emptied is not null) await households.DeleteAsync(emptied);
        else if (left is not null) await ForgetPhonesAsync(left, userId);
        return error;
    }

    /// <summary>Delete an account. If it was the household's last member, the household's data goes too.</summary>
    public async Task<string?> DeleteAccountAsync(string userId)
    {
        string? left = null;
        var (error, emptied) = await accounts.WriteAsync<(string?, string?)>(d =>
        {
            var user = d.Users.FirstOrDefault(u => u.Id == userId);
            if (user is null) return (null, null);
            var lastMember = d.Users.Count(u => u.HouseholdId == user.HouseholdId) == 1;
            if (lastMember && d.Households.FirstOrDefault(h => h.Id == user.HouseholdId) is { } h && HasLiveSubscription(h))
                return ("Cancel the ad-free subscription first (Settings → Ad-free → Manage billing), then delete your account.", null);

            d.Users.Remove(user);
            left = user.HouseholdId;
            if (!lastMember) return (null, null);
            d.Households.RemoveAll(x => x.Id == user.HouseholdId);
            return (null, user.HouseholdId);
        });
        if (emptied is not null) await households.DeleteAsync(emptied);
        else if (left is not null) await ForgetPhonesAsync(left, userId);
        return error;
    }

    /// <summary>Stop a household's reminders going to someone who left it.</summary>
    private Task ForgetPhonesAsync(string householdId, string userId) =>
        households.WriteAsync(householdId, d => d.PushSubscriptions.RemoveAll(s => s.UserId == userId));

    public static bool HasLiveSubscription(HouseholdInfo h) =>
        h.StripeSubscriptionId is not null && !h.SubscriptionEnding
        && h.SubscriptionStatus is not ("canceled" or "incomplete_expired" or "unpaid");

    private static SignedInUser ToSignedIn(AppUser u) => new(u.Id, u.DisplayName, u.Email, u.HouseholdId);

    public const string HouseholdClaim = "hid";

    public static ClaimsPrincipal ToPrincipal(SignedInUser user) =>
        new(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, user.Id),
            new Claim(ClaimTypes.Name, user.DisplayName),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim(HouseholdClaim, user.HouseholdId),
        ], CookieAuthenticationDefaults.AuthenticationScheme));
}

public static class ClaimsExtensions
{
    public static string UserId(this ClaimsPrincipal user) =>
        user.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new InvalidOperationException("Not signed in.");

    public static string HouseholdId(this ClaimsPrincipal user) =>
        user.FindFirstValue(AccountService.HouseholdClaim) ?? throw new InvalidOperationException("Not signed in.");
}

/// <summary>The signed-in person's household details, loaded once per request.</summary>
public class CurrentHousehold(IHttpContextAccessor http, AccountStore accounts)
{
    private HouseholdInfo? _info;

    public async Task<HouseholdInfo> GetAsync()
    {
        if (_info is not null) return _info;
        var id = http.HttpContext?.User.HouseholdId() ?? throw new InvalidOperationException("No request.");
        _info = await accounts.GetHouseholdAsync(id) ?? throw new InvalidOperationException("Household not found.");
        return _info;
    }

    /// <summary>True when this request should show ads: signed in and not on the ad-free plan.</summary>
    public async Task<bool> ShowAdsAsync()
    {
        if (http.HttpContext?.User.Identity?.IsAuthenticated != true) return false;
        return !(await GetAsync()).IsAdFree;
    }
}
