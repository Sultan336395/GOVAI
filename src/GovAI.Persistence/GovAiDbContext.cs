using GovAI.Domain.Analysis;
using GovAI.Application.Abstractions.Services;
using GovAI.Domain.Assessments;
using GovAI.Domain.Auditing;
using GovAI.Domain.Common;
using GovAI.Domain.Companies;
using GovAI.Domain.Identity;
using GovAI.Domain.Maintenance;
using GovAI.Domain.Notifications;
using GovAI.Domain.Opportunities;
using GovAI.Domain.Regulatory;
using GovAI.Domain.Sources;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore;

namespace GovAI.Persistence;

/// <summary>
/// PostgreSQL veri bağlamı. Şema adı <c>govai</c>'dir; böylece aynı veritabanında
/// başka uygulamalarla çakışma olmadan barınabilir.
/// </summary>
public class GovAiDbContext(
    DbContextOptions<GovAiDbContext> options,
    IDateTimeProvider? clock = null,
    ICurrentUser? currentUser = null) : DbContext(options)
{
    public const string Schema = "govai";

    /// <summary>
    /// Bu bağlamın hizmet verdiği kiracı. Bağlam oluşturulurken bir kez çözülür;
    /// istek ortasında değişemez.
    ///
    /// Kiracı çözülemezse <see cref="Guid.Empty"/> kalır ve sorgu filtreleri
    /// <b>hiçbir satırla eşleşmez</b>. Bu bilinçlidir: kimliği belirsiz bir bağlam
    /// veri göremez ("bilgi yoksa reddet"). Filtreyi devre dışı bırakan bir yol yoktur;
    /// <c>IgnoreQueryFilters()</c> kod tabanında kullanılmaz.
    ///
    /// Sistem işlemleri (açılış seed'i) yalnızca kiracıya bağlı olmayan tablolara
    /// (<see cref="Tenants"/>, <see cref="Sources"/>, <see cref="Opportunities"/>)
    /// okuma yapar; yazma işlemleri sorgu filtresinden etkilenmez.
    /// </summary>
    private readonly Guid _tenantId = currentUser?.TenantId ?? Guid.Empty;

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<Company> Companies => Set<Company>();
    public DbSet<CompanyGroup> CompanyGroups => Set<CompanyGroup>();
    public DbSet<UserCompany> UserCompanies => Set<UserCompany>();
    public DbSet<CompanyInvitation> CompanyInvitations => Set<CompanyInvitation>();

    public DbSet<PlatformActivation> PlatformActivations => Set<PlatformActivation>();
    public DbSet<CompanyVerificationRequest> CompanyVerificationRequests => Set<CompanyVerificationRequest>();
    public DbSet<Source> Sources => Set<Source>();
    public DbSet<SourceDocument> SourceDocuments => Set<SourceDocument>();
    public DbSet<SourceDocumentVersion> SourceDocumentVersions => Set<SourceDocumentVersion>();
    public DbSet<DocumentEvidenceChunk> DocumentEvidenceChunks => Set<DocumentEvidenceChunk>();
    public DbSet<RegulatoryChange> RegulatoryChanges => Set<RegulatoryChange>();
    public DbSet<Opportunity> Opportunities => Set<Opportunity>();
    public DbSet<EligibilityAssessment> Assessments => Set<EligibilityAssessment>();

    public DbSet<AnalysisRun> AnalysisRuns => Set<AnalysisRun>();

    public DbSet<OpportunityRuleEvidence> OpportunityRuleEvidence => Set<OpportunityRuleEvidence>();
    public DbSet<MaintenanceRun> MaintenanceRuns => Set<MaintenanceRun>();
    public DbSet<ScenarioSimulation> ScenarioSimulations => Set<ScenarioSimulation>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<AuditLogEntry> AuditLog => Set<AuditLogEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(GovAiDbContext).Assembly);

        ApplyDomainGeneratedKeys(modelBuilder);
        ApplyTenantFilters(modelBuilder);

        base.OnModelCreating(modelBuilder);
    }

    /// <summary>
    /// <see cref="Entity.Id"/> her zaman domain tarafından üretilir (<c>Guid.CreateVersion7()</c>);
    /// veritabanı hiçbir anahtarı üretmez. EF'in Guid anahtarlar için varsayılan kabulü ise
    /// "ekleme sırasında üretilir"dir ve bu, sessiz bir veri kaybına yol açar:
    ///
    /// İzlenen bir kök nesnenin koleksiyonuna yeni bir alt kayıt eklendiğinde EF, anahtarın
    /// dolu olmasına bakıp kaydı <c>Added</c> değil <c>Modified</c> sayar; INSERT hiç
    /// üretilmez, var olmayan satıra UPDATE gider ve istek
    /// <c>DbUpdateConcurrencyException</c> ile 500 döner. Firma profilindeki ilk konum,
    /// NACE kodu veya belge eklenirken tetiklenir.
    ///
    /// Anahtarın üretimini modelde doğru bildirmek sorunu kaynağında kapatır.
    /// Koruyan test: <c>Faz1-G. CompanyManager profil alanlarını düzenleyebilir</c>.
    /// </summary>
    private static void ApplyDomainGeneratedKeys(ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (!typeof(Entity).IsAssignableFrom(entityType.ClrType))
            {
                continue;
            }

            var key = entityType.FindPrimaryKey();
            if (key is { Properties: [{ ClrType: var clrType } property] } && clrType == typeof(Guid))
            {
                property.ValueGenerated = ValueGenerated.Never;
            }
        }
    }

    /// <summary>
    /// Kiracıya ait varlıklara zorunlu kiracı sınırı uygular. Savunmanın ilk katmanıdır;
    /// servis katmanındaki yetki kontrolü (CompanyAccessGuard) ikinci katmandır ve
    /// bu filtre var diye kaldırılmaz.
    ///
    /// <b>Bilinçli olarak kapsam dışı:</b> Sources, SourceDocuments, Opportunities,
    /// OpportunityRules, OpportunityDocuments. Bunlar resmî çağrı kataloğudur ve
    /// tasarım gereği tüm kiracılar tarafından ortak kullanılır (bkz. docs/data-model.md).
    ///
    /// Yumuşak silme filtresi de burada birleştirilir: <c>HasQueryFilter</c> aynı varlık
    /// için ikinci kez çağrıldığında öncekini <b>değiştirir</b>, eklemez.
    /// </summary>
    private void ApplyTenantFilters(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Company>()
            .HasQueryFilter(c => !c.IsDeleted && c.TenantId == _tenantId);

        modelBuilder.Entity<AppUser>()
            .HasQueryFilter(u => !u.IsDeleted && u.TenantId == _tenantId);

        modelBuilder.Entity<CompanyGroup>()
            .HasQueryFilter(g => !g.IsDeleted && g.TenantId == _tenantId);

        modelBuilder.Entity<UserCompany>()
            .HasQueryFilter(uc => !uc.IsDeleted && uc.TenantId == _tenantId);

        modelBuilder.Entity<CompanyInvitation>()
            .HasQueryFilter(i => i.TenantId == _tenantId);

        modelBuilder.Entity<CompanyVerificationRequest>()
            .HasQueryFilter(r => r.TenantId == _tenantId);

        modelBuilder.Entity<EligibilityAssessment>()
            .HasQueryFilter(a => a.TenantId == _tenantId);

        modelBuilder.Entity<ScenarioSimulation>()
            .HasQueryFilter(s => s.TenantId == _tenantId);

        modelBuilder.Entity<AnalysisRun>()
            .HasQueryFilter(r => r.TenantId == _tenantId);

        modelBuilder.Entity<Notification>()
            .HasQueryFilter(n => n.TenantId == _tenantId);

        // AuditLogEntry.TenantId nullable'dır (kiracı belirlenemeden oluşan kayıtlar için).
        // Kiracısı olmayan kayıtlar hiçbir kiracıya gösterilmez.
        modelBuilder.Entity<AuditLogEntry>()
            .HasQueryFilter(a => a.TenantId == _tenantId);
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        ApplyAuditInformation();
        return base.SaveChangesAsync(cancellationToken);
    }

    public override int SaveChanges()
    {
        ApplyAuditInformation();
        return base.SaveChanges();
    }

    /// <summary>
    /// Oluşturma/güncelleme damgalarını ve soft-delete işaretlerini merkezî olarak uygular;
    /// böylece her serviste tekrar edilmez ve unutulamaz.
    /// </summary>
    private void ApplyAuditInformation()
    {
        var now = clock?.UtcNow ?? DateTimeOffset.UtcNow;
        var actor = currentUser?.Email ?? currentUser?.UserId?.ToString() ?? "system";

        foreach (var entry in ChangeTracker.Entries<IAuditable>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Entity.CreatedAt = entry.Entity.CreatedAt == default ? now : entry.Entity.CreatedAt;
                    entry.Entity.CreatedBy ??= actor;
                    break;

                case EntityState.Modified:
                    entry.Entity.UpdatedAt = now;
                    entry.Entity.UpdatedBy = actor;
                    break;
            }
        }

        // Silme istekleri fiziksel silme yerine pasifleştirmeye çevrilir (KVKK ve izlenebilirlik).
        foreach (var entry in ChangeTracker.Entries<ISoftDeletable>())
        {
            if (entry.State != EntityState.Deleted)
            {
                continue;
            }

            entry.State = EntityState.Modified;
            entry.Entity.IsDeleted = true;
            entry.Entity.DeletedAt = now;
        }
    }
}
