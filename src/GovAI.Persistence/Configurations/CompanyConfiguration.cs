using GovAI.Domain.Companies;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GovAI.Persistence.Configurations;

public sealed class CompanyConfiguration : IEntityTypeConfiguration<Company>
{
    public void Configure(EntityTypeBuilder<Company> builder)
    {
        builder.ToTable("companies");
        builder.HasKey(c => c.Id);
        builder.Ignore(c => c.DomainEvents);
        builder.Ignore(c => c.Size);
        builder.Ignore(c => c.PrimaryNaceCode);

        builder.Property(c => c.LegalName).HasMaxLength(400).IsRequired();
        builder.Property(c => c.TaxNumber).HasMaxLength(20).IsRequired();
        builder.Property(c => c.LegalType).HasConversion<int>();
        builder.Property(c => c.ProfileVersion).IsConcurrencyToken();

        // Aynı kiracıda aynı vergi numarası iki kez kayıtlı olamaz.
        builder.HasIndex(c => new { c.TenantId, c.TaxNumber }).IsUnique();
        builder.HasIndex(c => c.TenantId);

        builder.OwnsOne(c => c.Workforce, workforce =>
        {
            workforce.Property(w => w.EmployeeCount).HasColumnName("employee_count");
            workforce.Property(w => w.WomenEmployeeCount).HasColumnName("women_employee_count");
            workforce.Property(w => w.YoungEmployeeCount).HasColumnName("young_employee_count");
            workforce.Property(w => w.RAndDEmployeeCount).HasColumnName("rnd_employee_count");
            workforce.Property(w => w.DisabledEmployeeCount).HasColumnName("disabled_employee_count");
            workforce.Ignore(w => w.WomenEmployeeRate);
            workforce.Ignore(w => w.YoungEmployeeRate);
            workforce.Ignore(w => w.RAndDEmployeeRate);
        });

        builder.OwnsOne(c => c.Financials, financials =>
        {
            financials.Property(f => f.AnnualRevenue).HasColumnName("annual_revenue").HasPrecision(20, 2);
            financials.Property(f => f.BalanceSize).HasColumnName("balance_size").HasPrecision(20, 2);
            financials.Property(f => f.Equity).HasColumnName("equity").HasPrecision(20, 2);
            financials.Property(f => f.ExportRevenue).HasColumnName("export_revenue").HasPrecision(20, 2);
            financials.Property(f => f.Currency).HasColumnName("currency").HasMaxLength(3);
            financials.Property(f => f.FiscalYear).HasColumnName("fiscal_year");
            financials.Ignore(f => f.ExportRatio);
            financials.Ignore(f => f.HasNegativeEquity);
        });

        builder.HasMany(c => c.NaceCodes)
            .WithOne()
            .HasForeignKey(n => n.CompanyId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(c => c.Locations)
            .WithOne()
            .HasForeignKey(l => l.CompanyId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(c => c.Certificates)
            .WithOne()
            .HasForeignKey(c => c.CompanyId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(c => c.ActiveInvestments)
            .WithOne()
            .HasForeignKey(i => i.CompanyId)
            .OnDelete(DeleteBehavior.Cascade);

        UseBackingFields(builder, "NaceCodes", "Locations", "Certificates", "ActiveInvestments");

        // Sorgu filtresi GovAiDbContext.ApplyTenantFilters içinde tanımlıdır
        // (yumuşak silme + kiracı sınırı birlikte).
    }

    internal static void UseBackingFields<T>(EntityTypeBuilder<T> builder, params string[] navigations) where T : class
    {
        foreach (var navigation in navigations)
        {
            builder.Metadata.FindNavigation(navigation)!.SetPropertyAccessMode(PropertyAccessMode.Field);
        }
    }
}

public sealed class CompanyNaceCodeConfiguration : IEntityTypeConfiguration<CompanyNaceCode>
{
    public void Configure(EntityTypeBuilder<CompanyNaceCode> builder)
    {
        builder.ToTable("company_nace_codes");
        builder.HasKey(n => n.Id);
        builder.Property(n => n.Code).HasMaxLength(12).IsRequired();
        builder.Property(n => n.Description).HasMaxLength(500);
        builder.HasIndex(n => new { n.CompanyId, n.Code }).IsUnique();
    }
}

public sealed class CompanyLocationConfiguration : IEntityTypeConfiguration<CompanyLocation>
{
    public void Configure(EntityTypeBuilder<CompanyLocation> builder)
    {
        builder.ToTable("company_locations");
        builder.HasKey(l => l.Id);
        builder.Property(l => l.City).HasMaxLength(100).IsRequired();
        builder.Property(l => l.District).HasMaxLength(100);
        builder.Property(l => l.Nuts2Code).HasMaxLength(10);
        builder.HasIndex(l => l.CompanyId);
        builder.HasIndex(l => l.Nuts2Code);
    }
}

public sealed class CompanyCertificateConfiguration : IEntityTypeConfiguration<CompanyCertificate>
{
    public void Configure(EntityTypeBuilder<CompanyCertificate> builder)
    {
        builder.ToTable("company_certificates");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Code).HasMaxLength(60).IsRequired();
        builder.Property(c => c.Name).HasMaxLength(300).IsRequired();
        builder.Property(c => c.DocumentUri).HasMaxLength(1000);
        builder.HasIndex(c => new { c.CompanyId, c.Code });
    }
}

public sealed class CompanyInvestmentConfiguration : IEntityTypeConfiguration<CompanyInvestment>
{
    public void Configure(EntityTypeBuilder<CompanyInvestment> builder)
    {
        builder.ToTable("company_investments");
        builder.HasKey(i => i.Id);
        builder.Property(i => i.Title).HasMaxLength(400).IsRequired();
        builder.Property(i => i.RelatedCategory).HasConversion<int>();
        builder.Property(i => i.PlannedBudget).HasPrecision(20, 2);
        builder.HasIndex(i => i.CompanyId);
    }
}
