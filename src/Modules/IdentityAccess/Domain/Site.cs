namespace Cmms.Modules.IdentityAccess.Domain;

public sealed class Site
{
    private Site()
    {
    }

    public Site(string code, string name, string timeZone)
    {
        Id = Guid.CreateVersion7();
        Code = code;
        Name = name;
        TimeZone = timeZone;
        CreatedAtUtc = DateTimeOffset.UtcNow;
        IsActive = true;
        RowVersion = 1;
    }

    public Guid Id { get; private set; }

    public string Code { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public string TimeZone { get; private set; } = string.Empty;

    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public long RowVersion { get; private set; }
}
