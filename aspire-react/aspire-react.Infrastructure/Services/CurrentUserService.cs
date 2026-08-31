using System.Security.Claims;
using aspire_react.Server.Domain.Interfaces;
using Microsoft.AspNetCore.Http;

namespace aspire_react.Server.Infrastructure.Services;

public class CurrentUserService : ICurrentUserService
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CurrentUserService(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public Guid GetLocalUserId()
    {
        var claimValue = _httpContextAccessor.HttpContext?.User?.FindFirstValue("local_user_id");
        return Guid.TryParse(claimValue, out var id) ? id : Guid.Empty;
    }
}