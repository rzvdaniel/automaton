using Common.Authentication;
using MediatR;

namespace Automaton.Studio.Server.Core.Commands
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
