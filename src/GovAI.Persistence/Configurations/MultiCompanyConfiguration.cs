using GovAI.Domain.Companies;
using GovAI.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GovAI.Persistence.Configurations;

/// <summary>
/// Faz 1 çoklu şirket yapısının eşlemeleri: şirket grubu, kullanıcı-şirket üyeliği,
/// davet ve doğrulama talebi.
/// </summary>
public sealed class CompanyGroupConfiguration : IEntityTypeConfiguration<CompanyGroup>
{
    public void Configure(EntityTypeBuilder<CompanyGroup> builder)
    {
        builder.ToTable("company_groups");
        builder.HasKey(g => g.Id);
        builder.Ignore(g => g.DomainEvents);

        builder.Property(g => g.Name).HasMaxLength(300).IsRequired();
        builder.Property(g => g.Description).HasMaxLength(2000);

        // Aynı kiracıda aynı grup adı iki kez bulunamaz.
        builder.HasIndex(g => new { g.TenantId, g.Name }).IsUnique();
        builder.HasIndex(g => g.TenantId);

        // Sorgu filtresi GovAiDbContext.ApplyTenantFilters içinde tanımlıdır.
    }
}

public sealed class UserCompanyConfiguration : IEntityTypeConfiguration<UserCompany>
{
    public void Configure(EntityTypeBuilder<UserCompany> builder)
    {
        builder.ToTable("user_companies");
        builder.HasKey(uc => uc.Id);
        builder.Ignore(uc => uc.DomainEvents);
        builder.Ignore(uc => uc.GrantsAccess);
        builder.Ignore(uc => uc.CanManageMembers);
        builder.Ignore(uc => uc.CanManageProfile);
        builder.Ignore(uc => uc.CanOperate);

        builder.Property(uc => uc.CompanyRole).HasConversion<int>();

        // Bir kullanıcı aynı şirkete iki kez bağlanamaz. Yumuşak silinmiş kayıtlar da
        // bu kısıta dahildir; yeniden üyelik açmak yerine mevcut kayıt aktifleştirilir.
        builder.HasIndex(uc => new { uc.UserId, uc.CompanyId }).IsUnique();
        builder.HasIndex(uc => new { uc.TenantId, uc.CompanyId });
        builder.HasIndex(uc => uc.UserId);

        // "Bir kullanıcının en fazla bir varsayılan şirketi olabilir" kuralı veritabanı
        // seviyesinde de korunur: yalnızca is_default = true satırları için benzersiz.
        builder.HasIndex(uc => uc.UserId)
            .HasFilter("is_default = true")
            .IsUnique()
            .HasDatabaseName("ix_user_companies_single_default");

        builder.HasOne<AppUser>()
            .WithMany()
            .HasForeignKey(uc => uc.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Company>()
            .WithMany()
            .HasForeignKey(uc => uc.CompanyId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class CompanyInvitationConfiguration : IEntityTypeConfiguration<CompanyInvitation>
{
    public void Configure(EntityTypeBuilder<CompanyInvitation> builder)
    {
        builder.ToTable("company_invitations");
        builder.HasKey(i => i.Id);
        builder.Ignore(i => i.DomainEvents);

        builder.Property(i => i.Email).HasMaxLength(320).IsRequired();
        builder.Property(i => i.CompanyRole).HasConversion<int>();

        // Jetonun kendisi değil, yalnızca SHA-256 özeti saklanır.
        builder.Property(i => i.TokenHash).HasMaxLength(128).IsRequired();
        builder.HasIndex(i => i.TokenHash).IsUnique();

        builder.HasIndex(i => new { i.TenantId, i.CompanyId });

        builder.HasOne<Company>()
            .WithMany()
            .HasForeignKey(i => i.CompanyId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class CompanyVerificationRequestConfiguration : IEntityTypeConfiguration<CompanyVerificationRequest>
{
    public void Configure(EntityTypeBuilder<CompanyVerificationRequest> builder)
    {
        builder.ToTable("company_verification_requests");
        builder.HasKey(r => r.Id);
        builder.Ignore(r => r.DomainEvents);

        builder.Property(r => r.TaxNumber).HasMaxLength(20).IsRequired();
        builder.Property(r => r.RequestedLegalName).HasMaxLength(400).IsRequired();
        builder.Property(r => r.Status).HasConversion<int>();
        builder.Property(r => r.ResolutionNote).HasMaxLength(2000);

        builder.HasIndex(r => new { r.TenantId, r.TaxNumber });
        builder.HasIndex(r => r.Status);

        // Karşı tarafa (diğer kiracıya) ait hiçbir yabancı anahtar bilinçli olarak yok:
        // talebin varlığı bile başka bir kiracının verisini işaret etmemelidir.
    }
}
