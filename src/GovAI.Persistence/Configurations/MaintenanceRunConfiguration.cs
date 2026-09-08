using GovAI.Domain.Maintenance;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GovAI.Persistence.Configurations;

/// <summary>
/// Bakım çalıştırmalarının şeması (Faz 3).
///
/// <para>
/// <c>DetailJson</c> <c>jsonb</c>'dir: geri alma bu içeriği okur ve ileride "hangi kayda
/// ne yapıldı" sorgusu doğrudan JSON üzerinden çalıştırılabilsin. Metin kolonu bunu
/// imkânsız kılardı.
/// </para>
/// </summary>
public sealed class MaintenanceRunConfiguration : IEntityTypeConfiguration<MaintenanceRun>
{
    public void Configure(EntityTypeBuilder<MaintenanceRun> builder)
    {
        builder.ToTable("maintenance_runs");
        builder.HasKey(r => r.Id);
        builder.Ignore(r => r.CanUndo);

        builder.Property(r => r.Operation).HasConversion<int>();
        builder.Property(r => r.PlanHash).HasMaxLength(64).IsRequired();
        builder.Property(r => r.DetailJson).HasColumnType("jsonb").IsRequired();
        builder.Property(r => r.PerformedBy).HasMaxLength(320);
        builder.Property(r => r.UndoneBy).HasMaxLength(320);

        // İnceleme ekranı son çalıştırmaları listeler; sorgu buradan hızlanır.
        builder.HasIndex(r => r.StartedAt);
        builder.HasIndex(r => new { r.Operation, r.StartedAt });
    }
}
