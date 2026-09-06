using GovAI.Domain.Assessments;
using GovAI.Domain.Auditing;
using GovAI.Domain.Companies;
using GovAI.Domain.Identity;
using GovAI.Domain.Notifications;
using GovAI.Domain.Opportunities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GovAI.Persistence.Configurations;

public sealed class EligibilityAssessmentConfiguration : IEntityTypeConfiguration<EligibilityAssessment>
{
    public void Configure(EntityTypeBuilder<EligibilityAssessment> builder)
    {
        builder.ToTable("eligibility_assessments");
        builder.HasKey(a => a.Id);
        builder.Ignore(a => a.DomainEvents);

        builder.Property(a => a.Verdict).HasConversion<int>();
        builder.Property(a => a.SectorFit).HasConversion<int>();
        builder.Property(a => a.FinalScore).HasPrecision(6, 2);
        builder.Property(a => a.Confidence).HasPrecision(5, 4);
        builder.Property(a => a.DetailJson).HasColumnType("jsonb").IsRequired();
        builder.Property(a => a.ExecutiveSummary).HasMaxLength(8000);
        builder.Property(a => a.SummaryModel).HasMaxLength(120);

        builder.HasOne<Company>()
            .WithMany()
            .HasForeignKey(a => a.CompanyId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Opportunity>()
            .WithMany()
            .HasForeignKey(a => a.OpportunityId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(a => a.Dimensions)
            .WithOne()
            .HasForeignKey(d => d.EligibilityAssessmentId)
            .OnDelete(DeleteBehavior.Cascade);

        CompanyConfiguration.UseBackingFields(builder, "Dimensions");

        // Dashboard sorgusu: "bu firmanın güncel değerlendirmeleri, önce sektör uyumuna,
        // sonra skora göre". Sektör kolonu indeksin içinde olmazsa sıralama her sayfada
        // tüm satırları taramak zorunda kalır.
        builder.HasIndex(a => new { a.CompanyId, a.IsLatest, a.SectorFit, a.FinalScore });
        builder.HasIndex(a => new { a.CompanyId, a.IsLatest, a.FinalScore });
        builder.HasIndex(a => new { a.CompanyId, a.OpportunityId, a.IsLatest });
        builder.HasIndex(a => a.EvaluatedAt);
    }
}

public sealed class AssessmentDimensionConfiguration : IEntityTypeConfiguration<AssessmentDimension>
{
    public void Configure(EntityTypeBuilder<AssessmentDimension> builder)
    {
        builder.ToTable("assessment_dimensions");
        builder.HasKey(d => d.Id);

        builder.Property(d => d.Dimension).HasConversion<int>();
        builder.Property(d => d.Value).HasPrecision(6, 4);
        builder.Property(d => d.Weight).HasPrecision(6, 4);
        builder.Property(d => d.Contribution).HasPrecision(6, 4);
        builder.Property(d => d.Rationale).HasMaxLength(2000);

        builder.HasIndex(d => d.EligibilityAssessmentId);
    }
}

public sealed class ScenarioSimulationConfiguration : IEntityTypeConfiguration<ScenarioSimulation>
{
    public void Configure(EntityTypeBuilder<ScenarioSimulation> builder)
    {
        builder.ToTable("scenario_simulations");
        builder.HasKey(s => s.Id);
        builder.Ignore(s => s.DomainEvents);
        builder.Ignore(s => s.ScoreDelta);
        builder.Ignore(s => s.EligibleCountDelta);

        builder.Property(s => s.Name).HasMaxLength(200).IsRequired();
        builder.Property(s => s.ChangesJson).HasColumnType("jsonb").IsRequired();
        builder.Property(s => s.BaselineAverageScore).HasPrecision(6, 2);
        builder.Property(s => s.SimulatedAverageScore).HasPrecision(6, 2);

        builder.HasMany(s => s.Impacts)
            .WithOne()
            .HasForeignKey(i => i.ScenarioSimulationId)
            .OnDelete(DeleteBehavior.Cascade);

        CompanyConfiguration.UseBackingFields(builder, "Impacts");

        builder.HasIndex(s => s.CompanyId);
    }
}

public sealed class ScenarioImpactConfiguration : IEntityTypeConfiguration<ScenarioImpact>
{
    public void Configure(EntityTypeBuilder<ScenarioImpact> builder)
    {
        builder.ToTable("scenario_impacts");
        builder.HasKey(i => i.Id);
        builder.Ignore(i => i.Delta);
        builder.Ignore(i => i.BecameEligible);

        builder.Property(i => i.OpportunityTitle).HasMaxLength(600);
        builder.Property(i => i.BaselineScore).HasPrecision(6, 2);
        builder.Property(i => i.SimulatedScore).HasPrecision(6, 2);
        builder.Property(i => i.BaselineVerdict).HasConversion<int>();
        builder.Property(i => i.SimulatedVerdict).HasConversion<int>();

        builder.HasIndex(i => i.ScenarioSimulationId);
    }
}

public sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.ToTable("notifications");
        builder.HasKey(n => n.Id);
        builder.Ignore(n => n.DomainEvents);
        builder.Ignore(n => n.IsRead);

        builder.Property(n => n.Title).HasMaxLength(400).IsRequired();
        builder.Property(n => n.Body).HasMaxLength(4000);
        builder.Property(n => n.DeduplicationKey).HasMaxLength(300).IsRequired();
        builder.Property(n => n.DeliveryError).HasMaxLength(2000);
        builder.Property(n => n.Kind).HasConversion<int>();
        builder.Property(n => n.Channel).HasConversion<int>();

        // Aynı uyarı iki kez üretilemez.
        builder.HasIndex(n => n.DeduplicationKey).IsUnique();
        builder.HasIndex(n => new { n.TenantId, n.CompanyId, n.CreatedAt });
        builder.HasIndex(n => n.SentAt);
    }
}

public sealed class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> builder)
    {
        builder.ToTable("tenants");
        builder.HasKey(t => t.Id);
        builder.Ignore(t => t.DomainEvents);

        builder.Property(t => t.Name).HasMaxLength(300).IsRequired();
        builder.Property(t => t.Slug).HasMaxLength(80).IsRequired();
        builder.Property(t => t.Plan).HasMaxLength(60).IsRequired();

        builder.HasIndex(t => t.Slug).IsUnique();
        builder.HasQueryFilter(t => !t.IsDeleted);
    }
}

public sealed class AppUserConfiguration : IEntityTypeConfiguration<AppUser>
{
    public void Configure(EntityTypeBuilder<AppUser> builder)
    {
        builder.ToTable("users");
        builder.HasKey(u => u.Id);
        builder.Ignore(u => u.DomainEvents);

        builder.Property(u => u.Email).HasMaxLength(320).IsRequired();
        builder.Property(u => u.FullName).HasMaxLength(300).IsRequired();
        builder.Property(u => u.PasswordHash).HasMaxLength(400);
        builder.Property(u => u.ExternalSubjectId).HasMaxLength(200);
        builder.Property(u => u.ScopedCompanyIdsJson).HasColumnType("jsonb");
        builder.Property(u => u.Role).HasConversion<int>();

        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(u => u.TenantId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(u => u.Email).IsUnique();
        builder.HasIndex(u => u.TenantId);
        // Sorgu filtresi GovAiDbContext.ApplyTenantFilters içinde tanımlıdır
        // (yumuşak silme + kiracı sınırı birlikte).
    }
}

public sealed class AuditLogEntryConfiguration : IEntityTypeConfiguration<AuditLogEntry>
{
    public void Configure(EntityTypeBuilder<AuditLogEntry> builder)
    {
        builder.ToTable("audit_log");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.Action).HasMaxLength(120).IsRequired();
        builder.Property(a => a.EntityType).HasMaxLength(120);
        builder.Property(a => a.EntityId).HasMaxLength(80);
        builder.Property(a => a.UserId).HasMaxLength(80);
        builder.Property(a => a.UserEmail).HasMaxLength(320);
        builder.Property(a => a.IpAddress).HasMaxLength(64);
        builder.Property(a => a.UserAgent).HasMaxLength(500);
        builder.Property(a => a.CorrelationId).HasMaxLength(80);
        builder.Property(a => a.PayloadJson).HasColumnType("jsonb");

        builder.HasIndex(a => a.OccurredAt);
        builder.HasIndex(a => new { a.EntityType, a.EntityId });
        builder.HasIndex(a => a.UserEmail);
    }
}
