using GovAI.Domain.Companies;
using GovAI.Domain.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GovAI.Persistence.Configurations;

public sealed class NotificationRecipientConfiguration
    : IEntityTypeConfiguration<NotificationRecipient>
{
    public void Configure(EntityTypeBuilder<NotificationRecipient> builder)
    {
        builder.ToTable("notification_recipients");
        builder.HasKey(r => r.Id);
        builder.Ignore(r => r.DomainEvents);

        builder.Property(r => r.Email).HasMaxLength(320).IsRequired();
        builder.Property(r => r.FullName).HasMaxLength(200);
        builder.Property(r => r.Role).HasMaxLength(200);
        builder.Property(r => r.ExternalId).HasMaxLength(200);
        builder.Property(r => r.Source).HasConversion<int>();

        builder.HasOne<Company>()
            .WithMany()
            .HasForeignKey(r => r.CompanyId)
            .OnDelete(DeleteBehavior.Cascade);

        // Aynı kişi bir şirkette iki kez tanımlanamaz; tanımlanabilseydi her bildirimi
        // iki kez alırdı. Adres normalleştirilmiş saklandığı için büyük/küçük harf
        // farkı bu kısıtı atlatamaz.
        builder.HasIndex(r => new { r.CompanyId, r.Email }).IsUnique();

        builder.HasIndex(r => new { r.TenantId, r.CompanyId, r.IsActive });
    }
}
