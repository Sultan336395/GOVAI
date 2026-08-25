using GovAI.Domain.Regulatory;
using GovAI.Domain.Sources;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GovAI.Persistence.Configurations;

/// <summary>
/// Faz 2 – RegTech şeması: belge sürümleri, kanıt parçaları ve mevzuat kayıtları.
///
/// Üçü de <b>ortak kataloğa</b> aittir; kiracıya bağlı değildir ve kiracı sorgu filtresi
/// uygulanmaz (bkz. GovAiDbContext.ApplyTenantFilters). Bir müşterinin global belgeyi
/// değiştirerek diğerlerini etkilemesi, servis katmanındaki platform rolü kapısıyla
/// engellenir.
/// </summary>
public sealed class SourceDocumentVersionConfiguration : IEntityTypeConfiguration<SourceDocumentVersion>
{
    public void Configure(EntityTypeBuilder<SourceDocumentVersion> builder)
    {
        builder.ToTable("source_document_versions");
        builder.HasKey(v => v.Id);

        builder.Property(v => v.SourceUrl).HasMaxLength(2000).IsRequired();
        builder.Property(v => v.CanonicalUrl).HasMaxLength(2000).IsRequired();
        builder.Property(v => v.MediaType).HasMaxLength(150).IsRequired();
        builder.Property(v => v.Charset).HasMaxLength(50);
        builder.Property(v => v.RawContentHash).HasMaxLength(64).IsRequired();
        builder.Property(v => v.NormalizedTextHash).HasMaxLength(64);
        builder.Property(v => v.Title).HasMaxLength(1000);
        builder.Property(v => v.Language).HasMaxLength(10);
        builder.Property(v => v.ParseError).HasMaxLength(1000);
        builder.Property(v => v.ParseStatus).HasConversion<int>();

        // Aynı belgenin aynı sürüm numarası iki kez yazılamaz.
        builder.HasIndex(v => new { v.SourceDocumentId, v.VersionNumber })
            .IsUnique()
            .HasDatabaseName("ix_source_document_versions_document_version");

        // Değişiklik tespiti: aynı içerik tekrar geldiğinde yeni sürüm açılmaz.
        builder.HasIndex(v => new { v.SourceDocumentId, v.RawContentHash })
            .HasDatabaseName("ix_source_document_versions_content_hash");

        builder.HasMany(v => v.Chunks)
            .WithOne()
            .HasForeignKey(c => c.DocumentVersionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Metadata.FindNavigation(nameof(SourceDocumentVersion.Chunks))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);
    }
}

public sealed class DocumentEvidenceChunkConfiguration : IEntityTypeConfiguration<DocumentEvidenceChunk>
{
    public void Configure(EntityTypeBuilder<DocumentEvidenceChunk> builder)
    {
        builder.ToTable("document_evidence_chunks");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.Text).IsRequired();
        builder.Property(c => c.TextHash).HasMaxLength(64).IsRequired();
        builder.Property(c => c.SectionTitle).HasMaxLength(500);

        builder.HasIndex(c => new { c.DocumentVersionId, c.SequenceNumber })
            .IsUnique()
            .HasDatabaseName("ix_document_evidence_chunks_sequence");
    }
}

public sealed class RegulatoryChangeConfiguration : IEntityTypeConfiguration<RegulatoryChange>
{
    public void Configure(EntityTypeBuilder<RegulatoryChange> builder)
    {
        builder.ToTable("regulatory_changes");
        builder.HasKey(r => r.Id);
        builder.Ignore(r => r.DomainEvents);

        builder.Property(r => r.Jurisdiction).HasMaxLength(10).IsRequired();
        builder.Property(r => r.Authority).HasMaxLength(300).IsRequired();
        builder.Property(r => r.Title).HasMaxLength(1000).IsRequired();
        builder.Property(r => r.OfficialNumber).HasMaxLength(200);
        builder.Property(r => r.OfficialUrl).HasMaxLength(2000).IsRequired();
        builder.Property(r => r.ContentHash).HasMaxLength(64).IsRequired();
        builder.Property(r => r.QuarantineNote).HasMaxLength(1000);

        builder.Property(r => r.RegulationDomain).HasConversion<int>();
        builder.Property(r => r.ChangeType).HasConversion<int>();
        builder.Property(r => r.Status).HasConversion<int>();
        builder.Property(r => r.QuarantineReason).HasConversion<int>();

        // Aynı belge sürümünden aynı içerik iki kez mevzuat kaydı üretmez.
        builder.HasIndex(r => new { r.DocumentVersionId, r.ContentHash })
            .IsUnique()
            .HasDatabaseName("ix_regulatory_changes_version_content");

        builder.HasIndex(r => new { r.Jurisdiction, r.RegulationDomain, r.PublicationDate })
            .HasDatabaseName("ix_regulatory_changes_lookup");

        builder.HasIndex(r => r.Status).HasDatabaseName("ix_regulatory_changes_status");
    }
}
