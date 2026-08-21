using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WorkflowApp.Domain.Entities.Identity;

namespace WorkflowApp.Infrastructure.Persistence.Configurations;

public class RefreshTokenConfig : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> b)
    {
        // Hex-encoded SHA-256 — always 64 characters, fixed length for a tight lookup index.
        b.Property(t => t.TokenHash).HasMaxLength(64).IsFixedLength().IsRequired();
        b.Property(t => t.ReplacedByTokenHash).HasMaxLength(64).IsFixedLength();

        // Every refresh hits this index; the hash is the only lookup key.
        b.HasIndex(t => t.TokenHash).IsUnique().HasDatabaseName("UX_RefreshToken_TokenHash");

        // Supports "revoke everything still live for this user".
        b.HasIndex(t => new { t.UserId, t.RevokedAt });

        b.HasOne(t => t.User).WithMany().HasForeignKey(t => t.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class LoginAttemptConfig : IEntityTypeConfiguration<LoginAttempt>
{
    public void Configure(EntityTypeBuilder<LoginAttempt> b)
    {
        b.Property(a => a.UserNameTried).HasMaxLength(256).IsRequired();
        b.Property(a => a.IpAddress).HasMaxLength(64);
        b.Property(a => a.UserAgent).HasMaxLength(512);
        b.Property(a => a.FailureReason).HasMaxLength(200);

        // Brute-force investigation queries: "attempts for this name, newest first".
        b.HasIndex(a => new { a.UserNameTried, a.CreatedAt });
        b.HasIndex(a => a.CreatedAt);
    }
}
