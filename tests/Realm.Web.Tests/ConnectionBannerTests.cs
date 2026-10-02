using Bunit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Realm.Demo;
using Realm.Domain;
using Realm.Web.Components.Shell;
using Realm.Web.Hosting;
using Xunit;

namespace Realm.Web.Tests;

/// <summary>
/// The two persistent banners and the recovery toast of 01 sections 8.6 and 9 (cases 2 and 4), read from the circuit's session. Each test renders the banner
/// inside the real <c>RealmShell</c>, which cascades the session, with a session whose connection states the test sets and whose <c>Changed</c> it raises, so
/// every one of the sixteen combinations of two connection states is a row. Home Assistant wins when both are down; a banner shows only while a connection is
/// Unavailable (the first 15 s of an outage read Reconnecting and show none); the toast follows only a Home Assistant banner that was shown.
/// </summary>
public sealed class ConnectionBannerTests : ComponentTestBase
{
    private const string HomeAssistantText = "The royal messengers can't reach Home Assistant. Retrying…";
    private const string Life360Text = "Life360 isn't answering. Showing what Home Assistant knows.";

    [Theory(DisplayName = "[AC-49a] Every combination of the Home Assistant and Life360 connection states shows the banner of 01 section 8.6")]
    [InlineData(ConnectionState.Connected, ConnectionState.Connected, null)]
    [InlineData(ConnectionState.Connected, ConnectionState.Reconnecting, null)]
    [InlineData(ConnectionState.Connected, ConnectionState.Unavailable, "banner-life360")]
    [InlineData(ConnectionState.Connected, ConnectionState.NotConnected, null)]
    [InlineData(ConnectionState.Reconnecting, ConnectionState.Connected, null)]
    [InlineData(ConnectionState.Reconnecting, ConnectionState.Reconnecting, null)]
    [InlineData(ConnectionState.Reconnecting, ConnectionState.Unavailable, "banner-life360")]
    [InlineData(ConnectionState.Reconnecting, ConnectionState.NotConnected, null)]
    [InlineData(ConnectionState.Unavailable, ConnectionState.Connected, "banner-ha")]
    [InlineData(ConnectionState.Unavailable, ConnectionState.Reconnecting, "banner-ha")]
    [InlineData(ConnectionState.Unavailable, ConnectionState.Unavailable, "banner-ha")]
    [InlineData(ConnectionState.Unavailable, ConnectionState.NotConnected, "banner-ha")]
    [InlineData(ConnectionState.NotConnected, ConnectionState.Connected, null)]
    [InlineData(ConnectionState.NotConnected, ConnectionState.Reconnecting, null)]
    [InlineData(ConnectionState.NotConnected, ConnectionState.Unavailable, "banner-life360")]
    [InlineData(ConnectionState.NotConnected, ConnectionState.NotConnected, null)]
    public void EveryCombinationOfStates_ShowsTheBannerOfSection86(ConnectionState homeAssistant, ConnectionState life360, string? expected)
    {
        Arrange(homeAssistant, life360);

        var cut = RenderBanner();

        var shown = expected is null ? Array.Empty<string>() : new[] { expected };
        cut.WaitForAssertion(() => Assert.Equal(shown, BannersIn(cut)));
    }

    [Theory(DisplayName = "[AC-49a] The banners carry the copy of 01 section 8.6 and are polite status regions")]
    [InlineData(ConnectionState.Unavailable, ConnectionState.Connected, "banner-ha", HomeAssistantText)]
    [InlineData(ConnectionState.Connected, ConnectionState.Unavailable, "banner-life360", Life360Text)]
    public void TheBanners_CarryTheCopyOfSection86(ConnectionState homeAssistant, ConnectionState life360, string testId, string text)
    {
        Arrange(homeAssistant, life360);

        var cut = RenderBanner();

        cut.WaitForAssertion(() =>
        {
            var banner = cut.Find($"[data-testid='{testId}']");
            Assert.Contains(text, banner.TextContent, StringComparison.Ordinal);
            Assert.Equal("status", banner.GetAttribute("role"));
        });
    }

    [Fact(DisplayName = "[AC-49a] Restoring Home Assistant clears its banner and shows the toast once per outage")]
    public void WhenHomeAssistantComesBack_TheBannerGoesAndTheToastShowsOncePerOutage()
    {
        var session = Arrange(ConnectionState.Unavailable, ConnectionState.Connected);
        var cut = RenderBanner();
        cut.WaitForAssertion(() => Assert.Equal(new[] { "banner-ha" }, BannersIn(cut)));
        Assert.Equal(0, ToastCount());

        session.Set(ConnectionState.Connected, ConnectionState.Connected);

        cut.WaitForAssertion(() => Assert.Empty(BannersIn(cut)));
        Assert.Equal(1, ToastCount());

        // A later change with Home Assistant still connected is no recovery.
        session.Set(ConnectionState.Connected, ConnectionState.Reconnecting);
        session.Set(ConnectionState.Unavailable, ConnectionState.Reconnecting);
        cut.WaitForAssertion(() => Assert.Equal(new[] { "banner-ha" }, BannersIn(cut)));
        Assert.Equal(1, ToastCount());

        session.Set(ConnectionState.Connected, ConnectionState.Connected);
        cut.WaitForAssertion(() => Assert.Empty(BannersIn(cut)));
        Assert.Equal(2, ToastCount());
    }

    [Fact(DisplayName = "[AC-49a] An outage that ends within the 15 s window shows no banner and no toast")]
    public void AnOutageThatReadsReconnecting_ShowsNeitherBannerNorToast()
    {
        var session = Arrange(ConnectionState.Connected, ConnectionState.Connected);
        var cut = RenderBanner();

        session.Set(ConnectionState.Reconnecting, ConnectionState.Connected);
        session.Set(ConnectionState.Connected, ConnectionState.Connected);
        session.Set(ConnectionState.Unavailable, ConnectionState.Connected);   // the last change doubles as the point where the earlier ones have been handled

        cut.WaitForAssertion(() => Assert.Equal(new[] { "banner-ha" }, BannersIn(cut)));
        Assert.Equal(0, ToastCount());
    }

    [Fact(DisplayName = "[AC-49a] The Life360 banner comes and goes with the trackers and never shows the toast")]
    public void TheLife360Banner_FollowsTheTrackers_WithNoToast()
    {
        var session = Arrange(ConnectionState.Connected, ConnectionState.Unavailable);
        var cut = RenderBanner();
        cut.WaitForAssertion(() => Assert.Equal(new[] { "banner-life360" }, BannersIn(cut)));

        session.Set(ConnectionState.Connected, ConnectionState.Connected);

        cut.WaitForAssertion(() => Assert.Empty(BannersIn(cut)));
        Assert.Equal(0, ToastCount());
    }

    [Fact(DisplayName = "[AC-49a] When Home Assistant is back but the trackers are still down, the Life360 banner replaces its banner")]
    public void WhenHomeAssistantIsBackAndTheTrackersAreStillDown_TheLife360BannerTakesOver()
    {
        var session = Arrange(ConnectionState.Unavailable, ConnectionState.Unavailable);
        var cut = RenderBanner();
        cut.WaitForAssertion(() => Assert.Equal(new[] { "banner-ha" }, BannersIn(cut)));

        session.Set(ConnectionState.Connected, ConnectionState.Unavailable);

        cut.WaitForAssertion(() => Assert.Equal(new[] { "banner-life360" }, BannersIn(cut)));
        Assert.Equal(1, ToastCount());
    }

    [Fact(DisplayName = "[AC-49a] The banner subscribes once and disposing the tree returns the subscriber count to zero")]
    public async Task TheBanner_SubscribesOnce_AndLetsGoOnDisposal()
    {
        var session = Arrange(ConnectionState.Connected, ConnectionState.Connected);
        RenderBanner();
        Assert.Equal(1, session.Subscribers);

        await DisposeComponentsAsync();

        Assert.Equal(0, session.Subscribers);
        session.Set(ConnectionState.Unavailable, ConnectionState.Unavailable);   // raised after disposal: nobody listens and nothing throws
    }

    // Registers what RealmShell injects, with a factory that hands out the one session the test controls, before the first render builds the service provider.
    private FakeSession Arrange(ConnectionState homeAssistant, ConnectionState life360)
    {
        var session = new FakeSession(homeAssistant, life360);
        Services.AddSingleton(new RuntimeOptions(RealmMode.Live, DetailedErrors: false));
        Services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        Services.AddSingleton<IRealmSessionFactory>(new FixedFactory(session));
        return session;
    }

    private IRenderedComponent<RealmShell> RenderBanner() =>
        RenderWithProviders<RealmShell>(parameters => parameters.AddChildContent<ConnectionBanner>());

    private static List<string> BannersIn(IRenderedComponent<RealmShell> cut) =>
        cut.FindAll("[data-testid^='banner-']").Select(element => element.GetAttribute("data-testid") ?? string.Empty).ToList();

    // The toasts MudBlazor holds for the circuit. No MudSnackbarProvider is rendered, so nothing expires them.
    private int ToastCount() => Services.GetRequiredService<MudBlazor.ISnackbar>().ShownSnackbars.Count();

    private sealed class FixedFactory(IRealmSession session) : IRealmSessionFactory
    {
        public IRealmSession Create(DemoUrlParams? demo) => session;
    }

    // A session whose connection states and Changed event the test controls; the rest (clock, zone, reports) is a Demo session's.
    private sealed class FakeSession : IRealmSession
    {
        private readonly IRealmSession _inner = new DemoRealmSessionFactory().Create(null);
        private Action? _changed;
        private RealmSnapshot _current;

        public FakeSession(ConnectionState homeAssistant, ConnectionState life360)
        {
            _current = _inner.Current with { Connections = Connections(homeAssistant, life360) };
        }

        public int Subscribers => _changed?.GetInvocationList().Length ?? 0;

        public RealmSnapshot Current => _current;

        public event Action? Changed
        {
            add => _changed += value;
            remove => _changed -= value;
        }

        public TimeProvider Time => _inner.Time;

        public TimeZoneInfo Zone => _inner.Zone;

        /// <summary>Changes the two states and raises <c>Changed</c> on the calling thread, as the data source raises it off the UI thread.</summary>
        public void Set(ConnectionState homeAssistant, ConnectionState life360)
        {
            _current = _current with { Connections = Connections(homeAssistant, life360) };
            _changed?.Invoke();
        }

        public ValueTask<WeekReportVm> GetWeekReportAsync(int weekOffset, DayOfWeek weekStart, CancellationToken ct) =>
            _inner.GetWeekReportAsync(weekOffset, weekStart, ct);

        public ValueTask<DriverWeek?> GetDriverWeekAsync(string memberId, int weekOffset, DayOfWeek weekStart, CancellationToken ct) =>
            _inner.GetDriverWeekAsync(memberId, weekOffset, weekStart, ct);

        public string? ResolveMe(string? haUserId) => _inner.ResolveMe(haUserId);

        public ValueTask DisposeAsync() => _inner.DisposeAsync();

        // Exactly four entries, in the order of 02 section 1.8.
        private static IReadOnlyList<ConnectionVm> Connections(ConnectionState homeAssistant, ConnectionState life360) =>
        [
            new ConnectionVm(ConnectionNames.HomeAssistant, homeAssistant, null),
            new ConnectionVm(ConnectionNames.Life360Trackers, life360, null),
            new ConnectionVm(ConnectionNames.FordPass, ConnectionState.Connected, null),
            new ConnectionVm(ConnectionNames.VehiclePlaceholder, ConnectionState.NotConnected, null),
        ];
    }
}
