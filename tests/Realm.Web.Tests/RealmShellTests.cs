using System.Globalization;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Realm.Demo;
using Realm.Domain;
using Realm.Web.Components.Shell;
using Realm.Web.Hosting;
using Xunit;

namespace Realm.Web.Tests;

/// <summary>
/// RealmShell reads the page URL once, in OnInitialized, and creates the circuit's session from it (03 sections 2.1 and 3.1, R-083).
/// Each test calls <see cref="Arrange"/> first: it registers the mode, the options and a factory that records what it is asked, and sets the
/// page URL, all before the first render and before the service provider is built.
/// </summary>
public sealed class RealmShellTests : ComponentTestBase
{
    // The six Demo-only parameters and week, with a value each (demo=1 is added where a test needs it).
    private const string Everything = "?now=2026-10-01T08:00:00-05:00&style=day&sheet=80&layout=panel&variant=all-sources,no-fix&week=2";

    [Fact]
    public void InDemoMode_ParsesTheSevenParameters()
    {
        var factory = Arrange(RealmMode.Demo, Everything + "&demo=1");

        var shell = RenderShell();

        var demo = Assert.Single(factory.Calls);
        Assert.NotNull(demo);
        Assert.Equal(new DateTimeOffset(2026, 10, 1, 13, 0, 0, TimeSpan.Zero), demo.Now);
        Assert.Equal(new[] { "all-sources", "no-fix" }, demo.Variants);
        Assert.Equal(new DemoUiOverrides(Style: "day", Sheet: "80", Layout: "panel", Week: 2), shell.Overrides);
    }

    [Theory]
    [InlineData("night")]
    [InlineData("day")]
    [InlineData("streets")]
    [InlineData("satellite")]
    [InlineData("demo-offline")]
    public void InDemoMode_AcceptsEachMapStyle(string style)
    {
        Arrange(RealmMode.Demo, "?style=" + style);

        Assert.Equal(style, RenderShell().Overrides.Style);
    }

    [Theory]
    [InlineData("peek")]
    [InlineData("80")]
    public void InDemoMode_AcceptsBothSheetStates(string sheet)
    {
        Arrange(RealmMode.Demo, "?sheet=" + sheet);

        Assert.Equal(sheet, RenderShell().Overrides.Sheet);
    }

    [Theory]
    [InlineData("auto")]
    [InlineData("sheet")]
    [InlineData("panel")]
    public void InDemoMode_AcceptsEachLayoutOverride(string layout)
    {
        Arrange(RealmMode.Demo, "?layout=" + layout);

        Assert.Equal(layout, RenderShell().Overrides.Layout);
    }

    [Theory]
    [InlineData(RealmMode.Demo, 0)]
    [InlineData(RealmMode.Demo, 3)]
    [InlineData(RealmMode.Live, 0)]
    [InlineData(RealmMode.Live, 3)]
    public void Week_ZeroToThree_IsHonouredInEveryMode(RealmMode mode, int week)
    {
        Arrange(mode, "?week=" + week);

        Assert.Equal(week, RenderShell().Overrides.Week);
    }

    [Theory]
    [InlineData("style=neon")]
    [InlineData("style=Night")]
    [InlineData("style=")]
    [InlineData("sheet=half")]
    [InlineData("sheet=full")]
    [InlineData("sheet=100")]
    [InlineData("layout=wide")]
    [InlineData("week=4")]
    [InlineData("week=-1")]
    [InlineData("week=two")]
    [InlineData("week=1.5")]
    [InlineData("week=")]
    [InlineData("now=yesterday")]
    [InlineData("now=2026-09-30")]
    [InlineData("now=2026-09-30T21:25:00")]
    [InlineData("now=2026-13-45T00:00:00Z")]
    [InlineData("now=")]
    public void InDemoMode_IgnoresAnInvalidValue(string query)
    {
        var factory = Arrange(RealmMode.Demo, "?" + query);

        var shell = RenderShell();

        Assert.Equal(DemoUiOverrides.None, shell.Overrides);
        Assert.Null(Assert.Single(factory.Calls)?.Now);
    }

    [Theory]
    [InlineData("now=2026-09-30T21:25:00-05:00", "2026-10-01T02:25:00Z")]
    [InlineData("now=2026-10-01T08:00:00Z", "2026-10-01T08:00:00Z")]
    [InlineData("now=2026-10-01T08:00-05:00", "2026-10-01T13:00:00Z")]
    [InlineData("now=2026-10-01T08:00:00.250%2B05:30", "2026-10-01T02:30:00.250Z")]
    public void InDemoMode_ReadsNowAsIsoWithAnOffset(string query, string expectedUtc)
    {
        var factory = Arrange(RealmMode.Demo, "?" + query);

        RenderShell();

        var expected = DateTimeOffset.Parse(expectedUtc, CultureInfo.InvariantCulture);
        Assert.Equal(expected, Assert.Single(factory.Calls)?.Now);
    }

    [Theory]
    [InlineData("variant=all-sources", "all-sources")]
    [InlineData("variant=all-sources,phone-unavailable", "all-sources|phone-unavailable")]
    [InlineData("variant=phone-unavailable,all-sources", "phone-unavailable|all-sources")]
    [InlineData("variant=all-sources%2C%20no-fix", "all-sources|no-fix")]
    [InlineData("variant=,all-sources,,", "all-sources")]
    [InlineData("variant=life360-down&variant=no-fix", "life360-down|no-fix")]
    [InlineData("variant=nonsense,no-fix", "nonsense|no-fix")]
    [InlineData("variant=", "")]
    [InlineData("demo=1", "")]
    public void InDemoMode_SplitsVariantOnCommasInOrder(string query, string expected)
    {
        var factory = Arrange(RealmMode.Demo, "?" + query);

        RenderShell();

        var demo = Assert.Single(factory.Calls);
        Assert.NotNull(demo);
        Assert.Equal(expected, string.Join('|', demo.Variants));
    }

    [Fact]
    public void InDemoMode_WithNoParameters_AsksForAPlainDemoSession()
    {
        var factory = Arrange(RealmMode.Demo, string.Empty);

        var shell = RenderShell();

        var demo = Assert.Single(factory.Calls);
        Assert.NotNull(demo);
        Assert.Null(demo.Now);
        Assert.Empty(demo.Variants);
        Assert.Equal(DemoUiOverrides.None, shell.Overrides);
    }

    [Fact]
    public void InLiveMode_IgnoresTheSixDemoOnlyParameters_AndHonoursWeek()
    {
        var factory = Arrange(RealmMode.Live, Everything + "&demo=1");

        var shell = RenderShell();

        Assert.Null(Assert.Single(factory.Calls));
        Assert.Equal(new DemoUiOverrides(Week: 2), shell.Overrides);
    }

    [Theory]
    [InlineData("true", "demo=1", true)]
    [InlineData("false", "demo=1", false)]
    [InlineData(null, "demo=1", false)]
    [InlineData("maybe", "demo=1", false)]
    [InlineData("true", "", false)]
    [InlineData("true", "demo=0", false)]
    [InlineData("true", "demo=true", false)]
    public void InLiveMode_ADemoSessionNeedsDemoOneAndTheOption(string? allowDemoParam, string demoParameter, bool expectDemoSession)
    {
        var factory = Arrange(RealmMode.Live, "?" + demoParameter + "&variant=no-fix&style=day&week=1", allowDemoParam);

        var shell = RenderShell();

        var demo = Assert.Single(factory.Calls);
        Assert.Equal(expectDemoSession, demo is not null);
        Assert.Equal(expectDemoSession ? "no-fix" : null, demo?.Variants.SingleOrDefault());
        Assert.Equal(expectDemoSession ? "day" : null, shell.Overrides.Style);
        Assert.Equal(1, shell.Overrides.Week);
    }

    [Fact]
    public void TheParametersReachTheDemoSession()
    {
        Arrange(RealmMode.Demo, "?now=2026-10-01T08:00:00-05:00&variant=no-fix");

        var session = RenderShell().Session;

        Assert.Equal(new DateTimeOffset(2026, 10, 1, 13, 0, 0, TimeSpan.Zero), session.Time.GetUtcNow());
        var cryptid = Assert.Single(session.Current.Members, member => member.Id == DemoCast.Cryptid.Id);
        Assert.Equal(Freshness.NoFix, cryptid.Freshness);
    }

    [Fact]
    public void TheSessionIsCreatedOncePerRenderTree_NotPerRender()
    {
        var factory = Arrange(RealmMode.Demo, Everything);
        var cut = RenderWithProviders<RealmShell>(parameters => parameters.AddChildContent<Probe>());

        cut.Render();
        cut.Render();

        Assert.Single(factory.Calls);
        Assert.Same(Assert.Single(factory.Sessions), cut.FindComponent<Probe>().Instance.Session);
    }

    [Fact]
    public void NavigatingInTheApp_NeitherRecreatesTheSessionNorReadsTheUrlAgain()
    {
        var factory = Arrange(RealmMode.Demo, Everything);
        var shell = RenderShell();

        Services.GetRequiredService<NavigationManager>().NavigateTo("driving?week=3&variant=life360-down");

        Assert.Single(factory.Calls);
        Assert.Equal(2, shell.Overrides.Week);
        Assert.Same(factory.Sessions[0], shell.Session);
    }

    [Fact]
    public void EachRenderTree_GetsItsOwnSession_AsPrerenderAndTheCircuitDo()
    {
        var factory = Arrange(RealmMode.Demo, Everything);

        var prerender = RenderShell();
        var circuit = RenderShell();

        Assert.Equal(2, factory.Calls.Count);
        Assert.NotSame(prerender.Session, circuit.Session);
    }

    [Fact]
    public void CreatingTheSession_HasNoOtherSideEffect()
    {
        var factory = Arrange(RealmMode.Demo, "?variant=ha-down");

        RenderShell();

        var session = Assert.Single(factory.Sessions);
        Assert.Equal(0, session.MemberAccessCount);   // no snapshot built, no clock read, no Changed subscription (which would start the ha-down timer)
        Assert.Equal(0, session.DisposeCount);
    }

    [Fact]
    public async Task EndingTheTree_DisposesEverySessionOnce()
    {
        var factory = Arrange(RealmMode.Demo, Everything);
        var first = RenderShell();
        RenderShell();

        await DisposeComponentsAsync();
        await ((IAsyncDisposable)first.Instance).DisposeAsync();   // a second disposal does nothing

        Assert.Equal(2, factory.Sessions.Count);
        Assert.All(factory.Sessions, session => Assert.Equal(1, session.DisposeCount));
    }

    // Registers everything RealmShell injects and moves the page to the URL, before the first render builds the service provider.
    private SpyFactory Arrange(RealmMode mode, string relativeUrl, string? allowDemoParam = null)
    {
        var settings = new Dictionary<string, string?>();
        if (allowDemoParam is not null)
        {
            settings["Demo:AllowParam"] = allowDemoParam;
        }

        var factory = new SpyFactory(new DemoRealmSessionFactory());
        Services.AddSingleton(new RuntimeOptions(mode, DetailedErrors: false));
        Services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
        Services.AddSingleton<IRealmSessionFactory>(factory);

        var navigation = Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo(relativeUrl);
        Assert.EndsWith(relativeUrl, navigation.Uri, StringComparison.Ordinal);   // the "ignored" tests must not pass because the URL was never applied
        return factory;
    }

    private ShellView RenderShell()
    {
        var cut = RenderWithProviders<RealmShell>(parameters => parameters.AddChildContent<Probe>());
        return new ShellView(cut.Instance, cut.FindComponent<Probe>().Instance);
    }

    // A rendered shell and what its child sees: the cascaded session and overrides.
    private sealed record ShellView(RealmShell Instance, Probe Child)
    {
        public IRealmSession Session => Child.Session ?? throw new InvalidOperationException("No session was cascaded.");

        public DemoUiOverrides Overrides => Child.Overrides ?? throw new InvalidOperationException("No overrides were cascaded.");
    }

    private sealed class Probe : ComponentBase
    {
        [CascadingParameter]
        public IRealmSession? Session { get; set; }

        [CascadingParameter]
        public DemoUiOverrides? Overrides { get; set; }
    }

    private sealed class SpyFactory(IRealmSessionFactory inner) : IRealmSessionFactory
    {
        public List<DemoUrlParams?> Calls { get; } = [];

        public List<SpySession> Sessions { get; } = [];

        public IRealmSession Create(DemoUrlParams? demo)
        {
            Calls.Add(demo);
            var session = new SpySession(inner.Create(demo));
            Sessions.Add(session);
            return session;
        }
    }

    // Counts every use of the session so a test can tell "created" from "used".
    private sealed class SpySession(IRealmSession inner) : IRealmSession
    {
        public int MemberAccessCount { get; private set; }

        public int DisposeCount { get; private set; }

        public RealmSnapshot Current
        {
            get
            {
                MemberAccessCount++;
                return inner.Current;
            }
        }

        public event Action? Changed
        {
            add
            {
                MemberAccessCount++;
                inner.Changed += value;
            }

            remove => inner.Changed -= value;
        }

        public TimeProvider Time
        {
            get
            {
                MemberAccessCount++;
                return inner.Time;
            }
        }

        public TimeZoneInfo Zone
        {
            get
            {
                MemberAccessCount++;
                return inner.Zone;
            }
        }

        public ValueTask<WeekReportVm> GetWeekReportAsync(int weekOffset, DayOfWeek weekStart, CancellationToken ct)
        {
            MemberAccessCount++;
            return inner.GetWeekReportAsync(weekOffset, weekStart, ct);
        }

        public ValueTask<DriverWeek?> GetDriverWeekAsync(string memberId, int weekOffset, DayOfWeek weekStart, CancellationToken ct)
        {
            MemberAccessCount++;
            return inner.GetDriverWeekAsync(memberId, weekOffset, weekStart, ct);
        }

        public string? ResolveMe(string? haUserId)
        {
            MemberAccessCount++;
            return inner.ResolveMe(haUserId);
        }

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            return inner.DisposeAsync();
        }
    }
}
