using GovAI.Domain.Assessments;
using GovAI.Domain.Calibration;
using GovAI.Domain.Companies;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GovAI.Persistence.Configurations;

public sealed class ExpertVerdictConfiguration : IEntityTypeConfiguration<ExpertVerdict>
{
    public void Configure(EntityTypeBuilder<ExpertVerdict> builder)
    {
        builder.ToTable("expert_verdicts");
        builder.HasKey(v => v.Id);
        builder.Ignore(v => v.DomainEvents);

        // Hesaplanan alanlar kolon DEĞİLDİR: karar ile türevini ayrı saklamak, ikisinin
        // birbirinden ayrı düşmesine ve raporun kendi verisiyle çelişmesine yol açar.
        builder.Ignore(v => v.Agrees);
        builder.Ignore(v => v.IsFalsePositive);
        builder.Ignore(v => v.IsFalseNegative);

        builder.Property(v => v.SystemVerdict).HasConversion<int>();
        builder.Property(v => v.ExpertOpinion).HasConversion<int>();
        builder.Property(v => v.DisagreementReason).HasConversion<int>();
        builder.Property(v => v.SystemScore).HasPrecision(6, 2);
        builder.Property(v => v.Note).HasMaxLength(2000);
        builder.Property(v => v.RecordedBy).HasMaxLength(320).IsRequired();

        builder.HasOne<Company>()
            .WithMany()
            .HasForeignKey(v => v.CompanyId)
            .OnDelete(DeleteBehavior.Cascade);

        // Bir değerlendirme için tek uzman kaydı: aynı vaka kalibrasyon sayımına iki kez
        // giremez. Değerlendirme silinirse kayıt da anlamını yitirir.
        builder.HasOne<EligibilityAssessment>()
            .WithMany()
            .HasForeignKey(v => v.AssessmentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(v => v.AssessmentId).IsUnique();
        builder.HasIndex(v => new { v.CompanyId, v.RecordedAt });
    }
}
