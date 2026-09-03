using Microsoft.AspNetCore.Identity;

namespace Cmms.Modules.IdentityAccess.Domain;

public sealed class ApplicationUser : IdentityUser<Guid>
{
    private ApplicationUser()
    {
    }

    public ApplicationUser(string email, string displayName)
    {
        Id = Guid.CreateVersion7();
        Email = email;
        UserName = email;
        DisplayName = displayName;
        CreatedAtUtc = DateTimeOffset.UtcNow;
        IsActive = true;
    }

    public string DisplayName { get; private set; } = string.Empty;

    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }
}
