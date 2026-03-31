namespace Humans.Web.Models;

public class SitePoliciesViewModel
{
    /// <summary>
    /// Privacy policy content by language code (e.g., "es" → markdown, "en" → markdown).
    /// </summary>
    public Dictionary<string, string> PrivacyPolicyContent { get; set; } = new(StringComparer.Ordinal);

    /// <summary>
    /// DPO email address from configuration.
    /// </summary>
    public string DpoEmail { get; set; } = string.Empty;

    /// <summary>
    /// General support email for app-related inquiries.
    /// </summary>
    public string SupportEmail { get; set; } = string.Empty;
}
