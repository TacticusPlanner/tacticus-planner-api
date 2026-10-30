namespace TacticusPlanner.Api.Features.AccountManagement;

/// <summary>
/// The app registration (in the Entra External ID tenant) the API uses to delete users through Microsoft Graph.
/// It needs the <c>User.DeleteRestore.All</c> application permission with admin consent. Leaving
/// <see cref="TenantId"/> or <see cref="ClientId"/> empty keeps the no-op deleter.
/// </summary>
public sealed class ExternalIdentityOptions
{
    public const string SectionName = "ExternalIdentity";

    public string TenantId { get; set; } = string.Empty;

    public string ClientId { get; set; } = string.Empty;

    /// <summary>
    /// Client id of the user-assigned managed identity that the app registration trusts through a federated
    /// identity credential (audience <c>api://AzureADTokenExchange</c>). Used when <see cref="ClientSecret"/> is empty.
    /// </summary>
    public string ManagedIdentityClientId { get; set; } = string.Empty;

    /// <summary>Fallback for hosts without a managed identity, such as local development.</summary>
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>
    /// Also remove the user from the tenant's deleted items, so the email can sign up again at once instead of
    /// after Entra's 30-day soft-delete window.
    /// </summary>
    public bool PermanentlyDelete { get; set; } = true;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(TenantId) && !string.IsNullOrWhiteSpace(ClientId);
}
