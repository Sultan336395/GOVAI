using GovAI.Domain.Companies;
using GovAI.Domain.Integrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GovAI.Persistence.Configurations;

/// <summary>
/// Süreç olay günlüğü. Tablo <b>yalnızca büyür</b>: olay gerçekleşmiştir, sonradan
/// değişmez.
/// </summary>
public sealed class ErpProcessEventConfiguration : IEntityTypeConfiguration<ErpProcessEvent>
{
    public void Configure(EntityTypeBuilder<ErpProcessEvent> builder)
    {
        builder.ToTable("erp_process_events");
        builder.HasKey(e => e.Id);

        // Hesaplanmış özellik; sütun olarak tutulmaz, tekilleştirme sorgu tarafında yapılır.
        builder.Ignore(e => e.DeduplicationKey);

        builder.Property(e => e.CaseId).HasMaxLength(200).IsRequired();
        builder.Property(e => e.Activity).HasMaxLength(300).IsRequired();
        builder.Property(e => e.Resource).HasMaxLength(200);
        builder.Property(e => e.Department).HasMaxLength(200);
        builder.Property(e => e.ExternalId).HasMaxLength(200);

        builder.HasOne<Company>()
            .WithMany()
            .HasForeignKey(e => e.CompanyId)
            .OnDelete(DeleteBehavior.Cascade);

        // ERP kimliği varsa mükerrer kayıt veritabanı düzeyinde engellenir. Filtreli
        // indeks: kimliği olmayan olaylar bu kısıta girmez, onların tekilliği
        // vaka+faaliyet+zaman üçlüsüyle korunur.
        builder.HasIndex(e => new { e.CompanyId, e.ExternalId })
            .IsUnique()
            .HasFilter("external_id IS NOT NULL");

        builder.HasIndex(e => new { e.CompanyId, e.CaseId, e.Activity, e.OccurredAt });

        // Zaman aralığı sorguları (son turdan beri gelenler) için.
        builder.HasIndex(e => new { e.TenantId, e.CompanyId, e.OccurredAt });
    }
}
