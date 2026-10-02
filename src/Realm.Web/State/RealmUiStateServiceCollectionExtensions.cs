using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Realm.Web.State;

/// <summary>Registers the per-circuit UI state (03 section 3.6).</summary>
public static class RealmUiStateServiceCollectionExtensions
{
    /// <summary>
    /// Adds <see cref="RealmUiState"/> and <see cref="HistorySync"/> as scoped services (one per circuit, which is the Blazor Server meaning of scoped), so the selection, the
    /// sheet and the overlays outlive the page component and survive a trip to Driving (R2-03), and the history switch <see cref="HistoryOptions"/>.
    /// </summary>
    /// <param name="services">The collection.</param>
    /// <param name="configuration">Where <c>Realm:Ui:HistoryTokens</c> is read; null leaves the tokens on.</param>
    public static IServiceCollection AddRealmUiState(this IServiceCollection services, IConfiguration? configuration = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton(HistoryOptions.From(configuration));
        services.TryAddScoped<RealmUiState>();
        services.TryAddScoped<HistorySync>();
        return services;
    }
}
