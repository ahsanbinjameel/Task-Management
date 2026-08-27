using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WorkflowApp.Domain.Entities.Requests;
using WorkflowApp.Domain.Entities.Verifications;

namespace WorkflowApp.Infrastructure.Persistence.Configurations;

public class VerificationConfig : IEntityTypeConfiguration<Verification>
{
    public void Configure(EntityTypeBuilder<Verification> b)
    {
        b.HasIndex(v => v.VerificationNumber).IsUnique();
        b.Property(v => v.VerificationNumber).HasMaxLength(30).IsRequired();
        b.Property(v => v.Title).HasMaxLength(300).IsRequired();
        b.Property(v => v.Instructions).HasMaxLength(4000);
        b.Property(v => v.ExpectedBehavior).HasMaxLength(2000);
        b.Property(v => v.TargetName).HasMaxLength(300);
        b.Property(v => v.TargetReference).HasMaxLength(300);
        b.Property(v => v.Findings).HasMaxLength(8000);
        b.Property(v => v.CancellationReason).HasMaxLength(2000);

        b.HasIndex(v => v.Status);

        // "What is on my desk" — the checker's queue, asked on every page load of their screen.
        b.HasIndex(v => new { v.AssignedToUserId, v.Status });

        // "Is anything still being checked for this request?" — asked by triage before it will let
        // a reviewer approve, and by every request detail that shows the verification panel.
        b.HasIndex(v => v.RequestId);

        b.HasOne(v => v.RequestedByUser).WithMany().HasForeignKey(v => v.RequestedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
        b.HasOne(v => v.AssignedToUser).WithMany().HasForeignKey(v => v.AssignedToUserId)
            .OnDelete(DeleteBehavior.Restrict);

        // Restrict, not Cascade: a completed verification is the record of an investigation that
        // was carried out, and it has to survive whatever happens to the request that prompted it.
        b.HasOne(v => v.Request).WithMany().HasForeignKey(v => v.RequestId)
            .OnDelete(DeleteBehavior.Restrict);

        // Modules are retired, never deleted (see SetupService), so this never fires in practice —
        // Restrict states the intent rather than trusting that convention holds forever.
        b.HasOne(v => v.Module).WithMany().HasForeignKey(v => v.ModuleId)
            .OnDelete(DeleteBehavior.Restrict);

        // Named explicitly for the same reason RequestBatch names its own: left to convention EF
        // cannot tell that `Attachment.VerificationId` backs this navigation, and would quietly add
        // a second column beside it.
        b.HasMany(v => v.Attachments).WithOne().HasForeignKey(a => a.VerificationId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasMany(v => v.Activity).WithOne(a => a.Verification).HasForeignKey(a => a.VerificationId)
            .OnDelete(DeleteBehavior.Cascade);

        BaseEntityConventions.ApplyRowVersion(b);
    }
}

public class VerificationActivityConfig : IEntityTypeConfiguration<VerificationActivity>
{
    public void Configure(EntityTypeBuilder<VerificationActivity> b)
    {
        b.Property(a => a.Description).HasMaxLength(1000).IsRequired();

        // Ordered by (OccurredAt, Id) everywhere, per the project-wide rule: two events can share a
        // timestamp, and without the tie-break "the latest" resolves arbitrarily.
        b.HasIndex(a => new { a.VerificationId, a.OccurredAt, a.Id });
    }
}
