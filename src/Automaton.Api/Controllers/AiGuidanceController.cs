using Automaton.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Automaton.Api.Controllers;

[ApiController]
[Route("api/ai-guidance")]
public sealed class AiGuidanceController : ControllerBase
{
    private readonly IAiGuidanceRuntimeService _runtimeService;

    public AiGuidanceController(IAiGuidanceRuntimeService runtimeService)
    {
        _runtimeService = runtimeService;
    }

    [HttpGet("export")]
    [Authorize(Policy = "AllowApiKey")]
    public async Task<ActionResult<AiGuidanceExportPackResponse>> Export(CancellationToken cancellationToken)
    {
        var pack = await _runtimeService.BuildExportPackAsync(cancellationToken);
        return Ok(new AiGuidanceExportPackResponse
        {
            Files = pack.Files.Select(file => new AiGuidanceExportFileResponse
            {
                OutputPath = file.TargetPath,
                Content = file.Content
            }).ToList()
        });
    }
}

public sealed class AiGuidanceExportPackResponse
{
    public List<AiGuidanceExportFileResponse> Files { get; set; } = [];
}

public sealed class AiGuidanceExportFileResponse
{
    public string OutputPath { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
}
