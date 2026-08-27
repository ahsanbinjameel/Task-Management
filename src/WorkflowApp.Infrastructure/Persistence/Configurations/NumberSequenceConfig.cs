using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WorkflowApp.Domain.Entities.Common;

namespace WorkflowApp.Infrastructure.Persistence.Configurations;

public class NumberSequenceConfig : IEntityTypeConfiguration<NumberSequence>
{
    public void Configure(EntityTypeBuilder<NumberSequence> b)
    {
        // The name is the key: there is exactly one counter per sequence.
        b.HasKey(s => s.Key);
        b.Property(s => s.Key).HasMaxLength(50);

        // A plain integer token rather than ROWVERSION, so the concurrency guard behaves the same
        // on SQL Server and on the InMemory provider used by tests.
        b.Property(s => s.Version).IsConcurrencyToken();
    }
}
