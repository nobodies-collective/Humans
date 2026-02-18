using Microsoft.AspNetCore.Razor.TagHelpers;

namespace Humans.Web.TagHelpers;

[HtmlTargetElement("script")]
public class NonceTagHelper : TagHelper
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public NonceTagHelper(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext?.Items["CspNonce"] is string nonce)
        {
            output.Attributes.SetAttribute("nonce", nonce);
        }
    }
}
