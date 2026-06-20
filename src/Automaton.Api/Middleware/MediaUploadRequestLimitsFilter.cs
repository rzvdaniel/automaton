using Automaton.Api.Config;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;

namespace Automaton.Api.Middleware;

public sealed class MediaUploadRequestLimitsFilter : IAsyncResourceFilter
{
    private readonly MediaStorageOptions options;

    public MediaUploadRequestLimitsFilter(IOptions<MediaStorageOptions> options)
    {
        this.options = options.Value;
    }

    public Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        var maxUploadBytes = options.MaxUploadBytes;

        var maxRequestBodySizeFeature = context.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (maxRequestBodySizeFeature is { IsReadOnly: false })
        {
            maxRequestBodySizeFeature.MaxRequestBodySize = maxUploadBytes;
        }

        var request = context.HttpContext.Request;
        var formOptions = new FormOptions
        {
            MultipartBodyLengthLimit = maxUploadBytes
        };

        context.HttpContext.Features.Set<IFormFeature>(new FormFeature(request, formOptions));

        return next();
    }
}

public sealed class MediaUploadRequestLimitsAttribute : TypeFilterAttribute
{
    public MediaUploadRequestLimitsAttribute()
        : base(typeof(MediaUploadRequestLimitsFilter))
    {
    }
}
