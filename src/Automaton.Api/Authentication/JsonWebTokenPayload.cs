using System.Collections.Generic;

namespace Automaton.Api.Authentication
{
    public class JsonWebTokenPayload
    {
        public string? UserName { get; set; }
        public string UserId { get; set; } = string.Empty;
        
        public IReadOnlyList<string> Roles { get; set; } = Array.Empty<string>();
        public long Expires { get; set; }
        
        public string Audience { get; set; } = string.Empty;
        public IDictionary<string, string> Claims { get; set; } = new Dictionary<string, string>();
    }
}
