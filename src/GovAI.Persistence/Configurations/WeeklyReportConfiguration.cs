using GovAI.Domain.Companies;
using GovAI.Domain.Reporting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GovAI.Persistence.Configurations;

public sealed class WeeklyReportConfiguration : IEntityTypeConfiguration<WeeklyReport>
{
    public void Configure(EntityTypeBuilder<WeeklyReport> builder)
    {
        builder.ToTable("weekly_reports");
        builder.HasKey(r => r.Id);
        builder.Ignore(r => r.DomainEvents);

        builder.Property(r => r.CompanyName).HasMaxLength(300).IsRequired();
        builder.Property(r => r.Trigger).HasConversion<int>();
        builder.Property(r => r.ContentJson).HasColumnType("jsonb").IsRequired();

        builder.HasOne<Company>()
            .WithMany()
            .HasForeignKey(r => r.CompanyId)
            .OnDelete(DeleteBehavior.Cascade);

        // Bir firmanın bir haftası için tek kayıt. Otomatik üretimle elle üretim aynı
        // haftada çakıştığında ikinci satır açılmaz, mevcut kayıt güncellenir.
        builder.HasIndex(r => new { r.CompanyId, r.PeriodStart }).IsUnique();

        // Geçmiş listesi bu sırayla okunur.
        builder.HasIndex(r => new { r.CompanyId, r.GeneratedAt });
    }
}
