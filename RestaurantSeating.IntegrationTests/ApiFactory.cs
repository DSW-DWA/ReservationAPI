using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RestaurantSeating.Api.Data;

namespace RestaurantSeating.IntegrationTests;

public sealed class ApiFactory(string connectionString, SaveChangesInterceptor? interceptor = null) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Restaurant"] = connectionString,
                ["Database:Initialize"] = "true"
            }));
        if (interceptor is not null)
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<DbContextOptions<RestaurantDbContext>>();
                services.AddDbContext<RestaurantDbContext>(options =>
                    options.UseSqlServer(connectionString).AddInterceptors(interceptor));
            });
    }
}
