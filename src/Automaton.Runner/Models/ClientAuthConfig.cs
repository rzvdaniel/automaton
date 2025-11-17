namespace Automaton.Runner.Console.Models;

public class ClientAuthConfig
{
    public string LoginUserUrl { get; set; } = "api/account/loginuser";
    public string RefreshAccessTokenUrl { get; set; } = "api/token/refreshaccesstoken";
    public int RefreshTokenExpirationMinutesCheck { get; set; } = 2;
}
