namespace Automaton.Api.Middleware;

public class ExceptionResponseModel
{
    public int ResponseCode { get; set; }
    public string? ResponseMessage { get; set; }
}
