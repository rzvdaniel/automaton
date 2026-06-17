namespace Automaton.Api.Application.Commands
{
    public class RefreshAccessTokenCommand
    {
        public string? Token { get; set; }

        public RefreshAccessTokenCommand()
        {
        }

        public RefreshAccessTokenCommand(string token)
        {
            Token = token;
        }
    }
}
