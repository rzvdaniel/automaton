namespace Automaton.Api.Authentication
{
    public class JsonWebToken
    {
        public string AccessToken { get; set; } = string.Empty;
        public string RefreshToken { get; set; } = string.Empty;
        public long Expires { get; set; }
        public string UserId { get; set; } = string.Empty;
    }
}
