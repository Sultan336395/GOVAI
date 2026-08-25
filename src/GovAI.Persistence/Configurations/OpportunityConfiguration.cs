using GovAI.Domain.Opportunities;
using GovAI.Domain.Sources;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GovAI.Persistence.Configurations;

public sealed class OpportunityConfiguration : IEntityTypeConfiguration<Opportunity>
{
    public void Configure(EntityTypeBuilder<Opportunity> builder)
    {
        builder.ToTable("opportunities");
        builder.HasKey(o => o.Id);
        builder.Ignore(o => o.DomainEvents);

        builder.Property(o => o.Title).HasMaxLength(600).IsRequired();
        builder.Property(o => o.Publisher).HasMaxLength(300).IsRequired();
        builder.Property(o => o.Summary).HasMaxLength(4000);
        builder.Property(o => o.SourceUrl).HasMaxLength(1000);
        builder.Property(o => o.SourceType).HasConversion<int>();
        builder.Property(o => o.SupportCategory).HasConversion<int>();
        builder.Property(o => o.RuleExtractionConfidence).HasPrecision(5, 4);

        builder.OwnsOne(o => o.Budget, budget =>
        {
            budget.Property(b => b.MinAmount).HasColumnName("budget_min").HasPrecision(20, 2);
            budget.Property(b => b.MaxAmount).HasColumnName("budget_max").HasPrecision(20, 2);
            budget.Property(b => b.Currency).HasColumnName("budget_currency").HasMaxLength(3);
            budget.Property(b => b.SupportRate).HasColumnName("support_rate").HasPrecision(5, 4);
        });

        builder.HasMany(o => o.Rules)
            .WithOne()
            .HasForeignKey(r => r.OpportunityId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(o => o.DocumentChecklist)
            .WithOne()
            .HasForeignKey(d => d.OpportunityId)
            .OnDelete(DeleteBehavior.Cascade);

        CompanyConfiguration.UseBackingFields(builder, "Rules", "DocumentChecklist");

        builder.HasOne<Source>()
            .WithMany()
            .HasForeignKey(o => o.SourceId)
            .OnDelete(DeleteBehavior.Restrict);

        // Fırsat listesi ekranının ana sıralama ve filtreleme kolonları.
        builder.HasIndex(o => o.Deadline);
        builder.HasIndex(o => o.PublishedAt);
        builder.HasIndex(o => o.SupportCategory);
        builder.HasIndex(o => o.SourceDocumentId).IsUnique().HasFilter("source_document_id IS NOT NULL");

        builder.HasQueryFilter(o => !o.IsDeleted);
    }
}

public sealed class OpportunityRuleConfiguration : IEntityTypeConfiguration<OpportunityRule>
{
    public void Configure(EntityTypeBuilder<OpportunityRule> builder)
    {
        builder.ToTable("opportunity_rules");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.Field).HasMaxLength(120).IsRequired();
        builder.Property(r => r.Value).HasMaxLength(2000).IsRequired();
        builder.Property(r => r.HumanReadable).HasMaxLength(1000).IsRequired();
        builder.Property(r => r.SourceExcerpt).HasMaxLength(4000);
        builder.Property(r => r.Operator).HasConversion<int>();
        builder.Property(r => r.Dimension).HasConversion<int>();
        builder.Property(r => r.Severity).HasConversion<int>();
        builder.Property(r => r.Confidence).HasPrecision(5, 4);

        builder.HasIndex(r => r.OpportunityId);
        builder.HasIndex(r => r.Dimension);
    }
}

public sealed class DocumentRequirementConfiguration : IEntityTypeConfiguration<DocumentRequirement>
{
    public void Configure(EntityTypeBuilder<DocumentRequirement> builder)
    {
        builder.ToTable("opportunity_documents");
        builder.HasKey(d => d.Id);

        builder.Property(d => d.Code).HasMaxLength(60).IsRequired();
        builder.Property(d => d.Name).HasMaxLength(300).IsRequired();
        builder.Property(d => d.IssuingAuthority).HasMaxLength(300);
        builder.Property(d => d.Notes).HasMaxLength(2000);

        builder.HasIndex(d => d.OpportunityId);
    }
}

public sealed class SourceConfiguration : IEntityTypeConfiguration<Source>
{
    public void Configure(EntityTypeBuilder<Source> builder)
    {
        builder.ToTable("sources");
        builder.HasKey(s => s.Id);
        builder.Ignore(s => s.DomainEvents);

        builder.Property(s => s.Name).HasMaxLength(200).IsRequired();
        builder.Property(s => s.BaseUrl).HasMaxLength(1000).IsRequired();
        builder.Property(s => s.CronExpression).HasMaxLength(100).IsRequired();
        builder.Property(s => s.ConfigurationJson).HasColumnType("jsonb");
        builder.Property(s => s.LastRunMessage).HasMaxLength(2000);
        builder.Property(s => s.Type).HasConversion<int>();
        builder.Property(s => s.LastRunStatus).HasConversion<int>();

        // ── Faz 2: RegTech künyesi ──
        builder.Property(s => s.Category).HasConversion<int>();
        builder.Property(s => s.Health).HasConversion<int>();

        builder.OwnsOne(s => s.Profile, profile =>
        {
            profile.Property(x => x.Authority).HasColumnName("authority").HasMaxLength(300);
            profile.Property(x => x.Jurisdiction).HasColumnName("jurisdiction").HasMaxLength(10);
            profile.Property(x => x.OfficialDomain).HasColumnName("official_domain").HasMaxLength(300);
            profile.Property(x => x.Language).HasColumnName("language").HasMaxLength(10);
        });

        builder.OwnsOne(s => s.CrawlPlan, plan =>
        {
            plan.Property(x => x.StartUrl).HasColumnName("start_url").HasMaxLength(2000);
            plan.Property(x => x.ListSelector).HasColumnName("list_selector").HasMaxLength(500);
            plan.Property(x => x.ContentSelector).HasColumnName("content_selector").HasMaxLength(500);
            plan.Property(x => x.UrlPattern).HasColumnName("url_pattern").HasMaxLength(500);
            plan.Property(x => x.MaxPages).HasColumnName("max_pages");
            plan.Property(x => x.AllowedDomains).HasColumnName("allowed_domains").HasMaxLength(1000);
            plan.Property(x => x.DocumentTypes).HasColumnName("document_types").HasMaxLength(500);
        });

        // Tüm alanları null olan sahipli referansı EF "yok" sayar ve sonradan değer
        // atandığında var olmayan satırı güncellemeye çalışır (bkz. docs/data-model.md).
        builder.Navigation(s => s.Profile).IsRequired();
        builder.Navigation(s => s.CrawlPlan).IsRequired();

        builder.HasIndex(s => s.IsEnabled);
        builder.HasIndex(s => new { s.Category, s.Health }).HasDatabaseName("ix_sources_category_health");
    }
}

public sealed class SourceDocumentConfiguration : IEntityTypeConfiguration<SourceDocument>
{
    public void Configure(EntityTypeBuilder<SourceDocument> builder)
    {
        builder.ToTable("source_documents");
        builder.HasKey(d => d.Id);
        builder.Ignore(d => d.DomainEvents);

        builder.Property(d => d.Url).HasMaxLength(1000).IsRequired();
        builder.Property(d => d.Title).HasMaxLength(600);
        builder.Property(d => d.MediaType).HasMaxLength(120);
        builder.Property(d => d.ContentHash).HasMaxLength(64).IsRequired();
        builder.Property(d => d.ProcessingError).HasMaxLength(4000);
        builder.Property(d => d.Status).HasConversion<int>();

        builder.HasOne<Source>()
            .WithMany()
            .HasForeignKey(d => d.SourceId)
            .OnDelete(DeleteBehavior.Cascade);

        // Aynı kaynaktan aynı adres tek kayıt olarak tutulur; sürümler Revision ile izlenir.
        builder.Property(d => d.CanonicalUrl).HasMaxLength(2000);
        builder.Property(d => d.QuarantineReason).HasConversion<int>();
        builder.Property(d => d.QuarantineNote).HasMaxLength(1000);

        builder.HasMany(d => d.Versions)
            .WithOne()
            .HasForeignKey(v => v.SourceDocumentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Metadata.FindNavigation(nameof(GovAI.Domain.Sources.SourceDocument.Versions))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(d => d.QuarantineReason).HasDatabaseName("ix_source_documents_quarantine");

        builder.HasIndex(d => new { d.SourceId, d.Url }).IsUnique();
        builder.HasIndex(d => d.ContentHash);
        builder.HasIndex(d => d.Status);
    }
}
