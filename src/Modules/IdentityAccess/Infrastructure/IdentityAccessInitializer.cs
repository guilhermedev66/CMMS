using Cmms.Modules.IdentityAccess.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Cmms.Modules.IdentityAccess.Infrastructure;

public static class IdentityAccessInitializer
{
    public static async Task BootstrapAdminAsync(
        IServiceProvider services,
        IConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        var email = configuration["BootstrapAdmin:Email"];
        var password = configuration["BootstrapAdmin:Password"];

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            return;
        }

        await using var scope = services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var dbContext = scope.ServiceProvider.GetRequiredService<IdentityAccessDbContext>();
        var logger = scope.ServiceProvider
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("IdentityAccessBootstrap");

        var user = await userManager.FindByEmailAsync(email);
        if (user is null)
        {
            user = new ApplicationUser(email, configuration["BootstrapAdmin:DisplayName"] ?? "CMMS Administrator");
            var result = await userManager.CreateAsync(user, password);
            if (!result.Succeeded)
            {
                throw new InvalidOperationException(
                    $"Could not create bootstrap administrator: {string.Join("; ", result.Errors.Select(error => error.Description))}");
            }
        }

        var hasAdminAssignment = await dbContext.CompanyRoleAssignments
            .AnyAsync(
                assignment => assignment.UserId == user.Id && assignment.RoleCode == RoleCode.Admin,
                cancellationToken);

        if (!hasAdminAssignment)
        {
            dbContext.CompanyRoleAssignments.Add(new CompanyRoleAssignment(user.Id));
            await dbContext.SaveChangesAsync(cancellationToken);
            await userManager.UpdateSecurityStampAsync(user);
            logger.LogInformation("Assigned the company-wide Admin role to bootstrap user {UserId}.", user.Id);
        }
    }
}
