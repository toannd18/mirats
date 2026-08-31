using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace aspire_react.Server.Infrastructure.Persistence;

public class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        // [Giai đoạn 0.1] Sau khi tách project: DbContext ở Infrastructure nhưng Migrations vẫn nằm
        // ở aspire-react.Server (convention: dotnet ef migrations add --project aspire-react.Server).
        // Default MigrationsAssembly = assembly chứa DbContext (Infrastructure) → phải chỉ định
        // tường minh, nếu không ef tooling không tìm thấy các migration class.
        optionsBuilder.UseNpgsql("Host=localhost;Database=aspire-react-db;Username=postgres;Password=postgres",
            o => o.MigrationsAssembly("aspire-react.Server"));

        return new AppDbContext(optionsBuilder.Options);
    }
}