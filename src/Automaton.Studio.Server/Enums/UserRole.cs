namespace Automaton.Studio.Server.Enums;

public class UserRole
{
    public const string SystemAdministrator = "SysAdmin";
    public const string Administrator = "Admin";
    public const string User = "User";
    public const string Administrators = $"{SystemAdministrator},{Administrator}";

}
