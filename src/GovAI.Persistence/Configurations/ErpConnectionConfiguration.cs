using GovAI.Domain.Companies;
using GovAI.Domain.Integrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GovAI.Persistence.Configurations;

public sealed class ErpConnectionConfiguration : IEntityTypeConfiguration<ErpConnection>
{
    public void Configure(EntityTypeBuilder<ErpConnection> builder)
    {
        builder.ToTable("erp_connections");
        builder.HasKey(c => c.Id);
        builder.Ignore(c => c.DomainEvents);

        builder.Property(c => c.Vendor).HasConversion<int>();
        builder.Property(c => c.AuthMode).HasConversion<int>();
        builder.Property(c => c.LastRunStatus).HasConversion<int>();
        builder.Property(c => c.BaseUrl).HasMaxLength(1000).IsRequired();
        builder.Property(c => c.LastRunMessage).HasMaxLength(1000);
        builder.Property(c => c.FieldMapJson).HasColumnType("jsonb");

        // Şifreli kimlik. Uzunluk sınırı, AES-GCM paketinin base64 hâline göre bol tutulur.
        builder.Property(c => c.ProtectedSecret).HasMaxLength(2000).IsRequired();

        builder.HasOne<Company>()
            .WithMany()
            .HasForeignKey(c => c.CompanyId)
            .OnDelete(DeleteBehavior.Cascade);

        // Bir firmanın TEK bağlantısı olur: iki bağlantı, aynı profile iki kaynaktan
        // yazmak ve hangisinin kazandığının belirsiz kalması demek olurdu.
        builder.HasIndex(c => c.CompanyId).IsUnique();
    }
}
