using GovAI.Domain.Companies;
using GovAI.Domain.Integrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GovAI.Persistence.Configurations;

public sealed class ErpServiceIdentityConfiguration : IEntityTypeConfiguration<ErpServiceIdentity>
{
    public void Configure(EntityTypeBuilder<ErpServiceIdentity> builder)
    {
        builder.ToTable("erp_service_identities");
        builder.HasKey(i => i.Id);
        builder.Ignore(i => i.DomainEvents);
        builder.Ignore(i => i.ActiveKeys);

        builder.Property(i => i.ClientId).HasMaxLength(120).IsRequired();
        builder.Property(i => i.DisplayName).HasMaxLength(200).IsRequired();

        // İstemci kimliği KİRACIDAN BAĞIMSIZ tekildir: jeton ucu oturumsuz çalışır ve
        // kaydı yalnızca bu değerle bulur. Kiracı içinde tekil olsaydı iki kiracıdaki
        // aynı değer hangi kaydın aranacağını belirsiz bırakırdı.
        builder.HasIndex(i => i.ClientId).IsUnique();

        builder.HasOne<Company>()
            .WithMany()
            .HasForeignKey(i => i.CompanyId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(i => i.Keys)
            .WithOne()
            .HasForeignKey(k => k.ErpServiceIdentityId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Metadata
            .FindNavigation(nameof(ErpServiceIdentity.Keys))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);
    }
}

public sealed class ErpSigningKeyConfiguration : IEntityTypeConfiguration<ErpSigningKey>
{
    public void Configure(EntityTypeBuilder<ErpSigningKey> builder)
    {
        builder.ToTable("erp_signing_keys");
        builder.HasKey(k => k.Id);
        builder.Ignore(k => k.IsActive);

        builder.Property(k => k.KeyId).HasMaxLength(120).IsRequired();
        builder.Property(k => k.Algorithm).HasConversion<int>();
        builder.Property(k => k.PublicKeyPem).HasMaxLength(4000).IsRequired();

        builder.HasIndex(k => new { k.ErpServiceIdentityId, k.KeyId });
    }
}

public sealed class ErpAssertionUseConfiguration : IEntityTypeConfiguration<ErpAssertionUse>
{
    public void Configure(EntityTypeBuilder<ErpAssertionUse> builder)
    {
        builder.ToTable("erp_assertion_uses");
        builder.HasKey(u => u.Id);

        builder.Property(u => u.ClientId).HasMaxLength(120).IsRequired();
        builder.Property(u => u.TokenId).HasMaxLength(200).IsRequired();

        // Tekrar korumasının TAMAMI bu indekstir. Kaldırılırsa yakalanan bir beyan
        // ömrü boyunca sınırsız kez kullanılabilir.
        builder.HasIndex(u => new { u.ClientId, u.TokenId }).IsUnique();

        builder.HasIndex(u => u.ExpiresAt);
    }
}
