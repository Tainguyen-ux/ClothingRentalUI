using System.Net.Http.Headers;
using Microsoft.AspNetCore.Http;

namespace ClothingRentalUI.Handlers;

public class AuthTokenHandler : DelegatingHandler
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public AuthTokenHandler(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // Lấy token lưu trong Session (hoặc có thể thay đổi bằng Cookie/Claims tùy ý)
        var token = _httpContextAccessor.HttpContext?.Session.GetString("JWToken");

        if (!string.IsNullOrEmpty(token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return await base.SendAsync(request, cancellationToken);
    }
}
