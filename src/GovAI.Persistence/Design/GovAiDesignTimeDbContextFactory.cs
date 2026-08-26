using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace GovAI.Persistence.Design;

/// <summary>
/// <c>dotnet ef</c> komutlarının kullandığı context üreticisi.
///
/// <para>
/// Bu sınıf var olduğu sürece EF Core <b>başlangıç projesinin yapılandırmasına
/// bakmaz</b>; hedefi yalnızca buradan alır. Kural <see cref="EfMigrationTarget"/>
/// içindedir: bağlantı açık bir ortam değişkeniyle verilmek zorundadır ve 5180'in
/// veritabanına gitmek ayrı bir onay ister.
/// </para>
///
/// <para>
/// Uygulamanın çalışma zamanı bundan etkilenmez; API kendi bağlantısını
/// <see cref="DependencyInjection.AddPersistence"/> ile alır.
/// </para>
/// </summary>
public sealed class GovAiDesignTimeDbContextFactory : IDesignTimeDbContextFactory<GovAiDbContext>
{
    public GovAiDbContext CreateDbContext(string[] args)
    {
        var target = EfMigrationTarget.Resolve(Environment.GetEnvironmentVariable);

        // Yalnızca sunucu/port/veritabanı yazılır. Kullanıcı adı ve parola YAZILMAZ.
        Console.Error.WriteLine($"[GOVAI] EF hedefi: {target.Description}");

        var options = new DbContextOptionsBuilder<GovAiDbContext>()
            .UseNpgsql(target.ConnectionString, npgsql =>
            {
                npgsql.MigrationsHistoryTable("__ef_migrations_history", GovAiDbContext.Schema);
            })
            .UseSnakeCaseNamingConvention()
            .Options;

        return new GovAiDbContext(options);
    }
}
