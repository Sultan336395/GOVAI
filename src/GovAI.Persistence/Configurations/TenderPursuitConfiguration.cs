using GovAI.Domain.Companies;
using GovAI.Domain.Opportunities;
using GovAI.Domain.Tenders;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GovAI.Persistence.Configurations;

public sealed class TenderPursuitConfiguration : IEntityTypeConfiguration<TenderPursuit>
{
    public void Configure(EntityTypeBuilder<TenderPursuit> builder)
    {
        builder.ToTable("tender_pursuits");
        builder.HasKey(p => p.Id);
        builder.Ignore(p => p.DomainEvents);
        builder.Ignore(p => p.IsClosed);

        builder.Property(p => p.Status).HasConversion<int>();
        builder.Property(p => p.Outcome).HasConversion<int>();
        builder.Property(p => p.Note).HasMaxLength(2000);
        builder.Property(p => p.Owner).HasMaxLength(200);

        builder.HasOne<Company>()
            .WithMany()
            .HasForeignKey(p => p.CompanyId)
            .OnDelete(DeleteBehavior.Cascade);

        // Çağrı silinirse takip de düşer: dayanağı olmayan bir takip ekranda başlıksız
        // bir satır olarak kalırdı.
        builder.HasOne<Opportunity>()
            .WithMany()
            .HasForeignKey(p => p.OpportunityId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(p => p.Events)
            .WithOne()
            .HasForeignKey(e => e.TenderPursuitId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Metadata
            .FindNavigation(nameof(TenderPursuit.Events))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        // Bir firma aynı ihaleyi iki kez takibe alamaz; ikinci kayıt hangisinin geçerli
        // olduğunu belirsizleştirirdi.
        builder.HasIndex(p => new { p.CompanyId, p.OpportunityId }).IsUnique();

        builder.HasIndex(p => new { p.CompanyId, p.Status });
    }
}

public sealed class TenderPursuitEventConfiguration : IEntityTypeConfiguration<TenderPursuitEvent>
{
    public void Configure(EntityTypeBuilder<TenderPursuitEvent> builder)
    {
        builder.ToTable("tender_pursuit_events");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.FromStatus).HasConversion<int>();
        builder.Property(e => e.ToStatus).HasConversion<int>();
        builder.Property(e => e.Outcome).HasConversion<int>();
        builder.Property(e => e.Note).HasMaxLength(2000);
        builder.Property(e => e.By).HasMaxLength(320).IsRequired();

        builder.HasIndex(e => new { e.TenderPursuitId, e.At });
    }
}
