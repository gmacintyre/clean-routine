using System.Security.Claims;
using CleanRoutine.Domain;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace CleanRoutine.Services;

public record SignedInUser(string Id, string DisplayName, string Email);

/// <summary>Sign-up and sign-in for the household. Only emails listed in Household:AllowedEmails
/// can create an account, and each of those emails can be used once.</summary>
public class AccountService(HouseholdStore store, IOptions<HouseholdOptions> options, IPasswordHasher<AppUser> hasher)
{
    public const int MinPasswordLength = 8;

    public bool IsAllowedEmail(string email) =>
        options.Value.AllowedEmails.Any(a => string.Equals(a.Trim(), email.Trim(), StringComparison.OrdinalIgnoreCase));

    public bool SignUpOpen => options.Value.AllowedEmails.Any(a => !string.IsNullOrWhiteSpace(a));

    public async Task<(SignedInUser? User, string? Error)> RegisterAsync(string name, string email, string password)
    {
        email = email.Trim();
        name = name.Trim();
        if (!IsAllowedEmail(email))
            return (null, "That email isn't on this household's list. Ask whoever set up the app to add it.");
        if (password.Length < MinPasswordLength)
            return (null, $"Use at least {MinPasswordLength} characters for the password.");
        if (name.Length == 0)
            return (null, "Add your first name so the checklist can show who did what.");

        return await store.WriteAsync<(SignedInUser?, string?)>(d =>
        {
            if (d.Users.Any(u => string.Equals(u.Email, email, StringComparison.OrdinalIgnoreCase)))
                return (null, "There's already an account for that email. Sign in instead.");

            var user = new AppUser { Email = email, DisplayName = name };
            user.PasswordHash = hasher.HashPassword(user, password);
            d.Users.Add(user);
            return (new SignedInUser(user.Id, user.DisplayName, user.Email), null);
        });
    }

    public async Task<SignedInUser?> ValidateAsync(string email, string password)
    {
        var user = await store.ReadAsync(d => d.Users.FirstOrDefault(u =>
            string.Equals(u.Email, email.Trim(), StringComparison.OrdinalIgnoreCase)));
        if (user is null)
        {
            // Spend the same time as a real check so response time doesn't reveal which emails exist.
            hasher.HashPassword(new AppUser(), password);
            return null;
        }

        var result = hasher.VerifyHashedPassword(user, user.PasswordHash, password);
        if (result == PasswordVerificationResult.Failed) return null;
        if (result == PasswordVerificationResult.SuccessRehashNeeded)
            await store.WriteAsync(d =>
            {
                var u = d.Users.First(x => x.Id == user.Id);
                u.PasswordHash = hasher.HashPassword(u, password);
            });

        return new SignedInUser(user.Id, user.DisplayName, user.Email);
    }

    public Task<bool> ExistsAsync(string userId) => store.ReadAsync(d => d.Users.Any(u => u.Id == userId));

    public static ClaimsPrincipal ToPrincipal(SignedInUser user) =>
        new(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, user.Id),
            new Claim(ClaimTypes.Name, user.DisplayName),
            new Claim(ClaimTypes.Email, user.Email),
        ], CookieAuthenticationDefaults.AuthenticationScheme));
}

public static class ClaimsExtensions
{
    public static string UserId(this ClaimsPrincipal user) =>
        user.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new InvalidOperationException("Not signed in.");
}
