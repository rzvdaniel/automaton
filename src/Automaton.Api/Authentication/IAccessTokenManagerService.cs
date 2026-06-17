using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;

namespace Automaton.Api.Authentication
{
    public interface IAccessTokenManagerService
    {
        Task<bool> CurrentAccessTokenIsActive();
        Task DeactivateCurrentAccessTokenAsync();
        Task<bool> CurrentAccessTokenIsActive(string token);
        Task DeactivateAccessTokenAsync(string token);
    }
}