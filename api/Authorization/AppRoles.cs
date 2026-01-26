namespace api.Authorization;

/// <summary>
/// Role and admin constants. Roles are included in the JWT; Admin is granted only to the configured admin email.
/// </summary>
public static class AppRoles
{
    public const string User = "User";
    public const string Support = "Support";
    public const string Admin = "Admin";

    /// <summary>Only this email is treated as Admin (can delete rounds, manage user roles).</summary>
    public const string AdminEmail = "dmunyard@gmail.com";
}
