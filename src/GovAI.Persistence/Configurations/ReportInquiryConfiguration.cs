using GovAI.Domain.Companies;
using GovAI.Domain.Reporting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GovAI.Persistence.Configurations;

public sealed class ReportInquiryConfiguration : IEntityTypeConfiguration<ReportInquiry>
{
    public void Configure(EntityTypeBuilder<ReportInquiry> builder)
    {
        builder.ToTable("report_inquiries");
        builder.HasKey(i => i.Id);
        builder.Ignore(i => i.DomainEvents);

        builder.Property(i => i.Kind).HasConversion<int>();
        builder.Property(i => i.QuestionKey).HasMaxLength(120).IsRequired();
        builder.Property(i => i.QuestionText).HasMaxLength(500).IsRequired();
        builder.Property(i => i.AnswerText).HasMaxLength(8000).IsRequired();
        builder.Property(i => i.AskedBy).HasMaxLength(320).IsRequired();

        builder.HasOne<Company>()
            .WithMany()
            .HasForeignKey(i => i.CompanyId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<WeeklyReport>()
            .WithMany()
            .HasForeignKey(i => i.WeeklyReportId)
            .OnDelete(DeleteBehavior.Cascade);

        // Aynı raporda aynı soru iki kez sorulamaz: ikinci kayıt hem hakkı boşa harcar
        // hem sayacı bozar.
        builder.HasIndex(i => new { i.WeeklyReportId, i.QuestionKey }).IsUnique();
    }
}
