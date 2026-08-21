using Microsoft.AspNetCore.Identity;
using WorkflowApp.Application.Common.Interfaces;
using WorkflowApp.Domain.Entities.Identity;

using AppPasswordVerification = WorkflowApp.Application.Common.Interfaces.PasswordVerification;

namespace WorkflowApp.Infrastructure.Identity;

/// <summary>
/// Adapts ASP.NET Core Identity's <see cref="PasswordHasher{TUser}"/> to the Application layer's
/// <see cref="IPasswordHasher"/>. We take Identity's hashing (PBKDF2-HMAC-SHA256, 100k iterations,
/// 128-bit salt, versioned format) without adopting the AspNetUsers schema — the project models
/// identity with its own tables.
/// </summary>
public sealed class PasswordHasherAdapter : IPasswordHasher
{
    private readonly PasswordHasher<User> _inner = new();

    public string Hash(string password) => _inner.HashPassword(null!, password);

    public AppPasswordVerification Verify(string passwordHash, string providedPassword)
    {
        if (string.IsNullOrEmpty(passwordHash) || string.IsNullOrEmpty(providedPassword))
            return AppPasswordVerification.Failed;

        // A malformed stored hash must read as a failed verification, not blow up the login path.
        PasswordVerificationResult result;
        try
        {
            result = _inner.VerifyHashedPassword(null!, passwordHash, providedPassword);
        }
        catch (FormatException)
        {
            return AppPasswordVerification.Failed;
        }

        return result switch
        {
            PasswordVerificationResult.Success => AppPasswordVerification.Success,
            PasswordVerificationResult.SuccessRehashNeeded => AppPasswordVerification.SuccessRehashNeeded,
            _ => AppPasswordVerification.Failed
        };
    }
}
