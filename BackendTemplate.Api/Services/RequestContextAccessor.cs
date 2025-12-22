using System.Diagnostics;
using Microsoft.AspNetCore.Http;

namespace BackendTemplate.Api.Services;

public sealed class RequestContextAccessor
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public RequestContextAccessor(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public string GetTraceId()
    {
        var ctx = _httpContextAccessor.HttpContext;
        if (ctx is not null)
            return ctx.TraceIdentifier;

        return Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString("N");
    }

    public int? GetUserId()
    {
        // Placeholder: later map from JWT claims (sub/userId) when auth is added.
        return null;
    }
}
