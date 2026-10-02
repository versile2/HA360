using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Realm.Demo;
using Realm.Domain;
using Realm.TestKit;
using Realm.Web.Hosting;
using Xunit;

namespace Realm.Web.Tests;

/// <summary>
/// <see cref="ViewerResolver"/>, the SSR chain that decides who "me" is (03 section 5.5, 02 section 2.5, R-082, D38): the person link, the <c>me_fallback_member</c> option, the first live
/// member, nobody. The expected members are read from <see cref="DemoCast"/>. The Home Assistant user id goes in and never comes out: not in the result, not in the log.
/// </summary>
public sealed class ViewerResolverTests
{
    private static IConfiguration Configuration(string? fallback = null)
    {
        var settings = new Dictionary<string, string?>();
        if (fallback is not null)
        {
            settings[ViewerResolver.FallbackKey] = fallback;
        }

        return new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
    }

    private static ViewerResolver Resolver(string? fallback = null, IRealmSessionFactory? factory = null, ILogger<ViewerResolver>? logger = null) =>
        new(factory ?? new DemoRealmSessionFactory(), Configuration(fallback), logger ?? NullLogger<ViewerResolver>.Instance);

    [Fact]
    public async Task ThePersonLink_NamesTheMember_OfEachDemoIdentity()
    {
        await using var resolver = Resolver();

        foreach (var member in DemoCast.Members.Where(member => member.PersonUserId is not null))
        {
            var resolution = resolver.Resolve(member.PersonUserId);

            Assert.Equal(new ViewerResolution(member.Id, ViewerSource.Person), resolution);
        }
    }

    [Fact]
    public async Task ThePersonLink_BeatsTheFallbackOption()
    {
        await using var resolver = Resolver(fallback: DemoCast.Jester.Id);

        var resolution = resolver.Resolve(DemoCast.Queen.PersonUserId);

        Assert.Equal(new ViewerResolution(DemoCast.Queen.Id, ViewerSource.Person), resolution);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("a-user-no-person-has")]
    public async Task WithoutAPersonLink_TheFirstLiveMemberInSortOrderIsMe(string? haUserId)
    {
        await using var resolver = Resolver();
        var first = DemoCast.Members.Where(member => member.Kind == MemberKind.Live).OrderBy(member => member.SortOrder).First();

        var resolution = resolver.Resolve(haUserId);

        Assert.Equal(new ViewerResolution(first.Id, ViewerSource.FirstLive), resolution);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("a-user-no-person-has")]
    public async Task WithoutAPersonLink_TheFallbackOptionWins_WhenItNamesAMember(string? haUserId)
    {
        await using var resolver = Resolver(fallback: DemoCast.Cryptid.Id);

        var resolution = resolver.Resolve(haUserId);

        Assert.Equal(new ViewerResolution(DemoCast.Cryptid.Id, ViewerSource.Fallback), resolution);
    }

    [Theory]
    [InlineData("nobody-by-that-id")]
    [InlineData("  ")]
    [InlineData("")]
    public async Task AFallbackThatNamesNoMember_IsNoFallback(string fallback)
    {
        await using var resolver = Resolver(fallback: fallback);

        var resolution = resolver.Resolve(null);

        Assert.Equal(ViewerSource.FirstLive, resolution.Source);
        Assert.Equal(DemoCast.King.Id, resolution.MemberId);
    }

    [Fact]
    public async Task WithNoMembersAtAll_NobodyIsMe()
    {
        await using var resolver = Resolver(factory: new FixedFactory(() => new EmptySession()), fallback: DemoCast.King.Id);

        var resolution = resolver.Resolve(DemoCast.King.PersonUserId);

        Assert.Equal(new ViewerResolution(null, ViewerSource.None), resolution);
    }

    [Fact]
    public async Task TheSessionIsCreatedOnce_FromTheHostsDefaultMode_AndDisposedWithTheResolver()
    {
        var sessions = new List<EmptySession>();
        var factory = new FixedFactory(() =>
        {
            var session = new EmptySession();
            sessions.Add(session);
            return session;
        });
        var resolver = Resolver(factory: factory);
        Assert.Empty(sessions);   // nothing is created until someone asks

        resolver.Resolve("one");
        resolver.Resolve("two");
        Assert.Single(sessions);
        Assert.Null(Assert.Single(factory.Requests));   // the default mode, never a Demo request
        Assert.False(sessions[0].Disposed);

        await resolver.DisposeAsync();
        Assert.True(sessions[0].Disposed);
        await resolver.DisposeAsync();   // twice is safe
    }

    [Fact(DisplayName = "[D38] The log says which member and how, and never the Home Assistant user id")]
    public async Task TheLog_NamesTheMemberAndTheSource_NeverTheUserId()
    {
        var sink = new InMemoryLogSink();
        using var loggers = LoggerFactory.Create(builder => builder.AddProvider(sink).SetMinimumLevel(LogLevel.Debug));
        await using var resolver = Resolver(logger: loggers.CreateLogger<ViewerResolver>());
        var userId = DemoCast.King.PersonUserId;

        resolver.Resolve(userId);
        resolver.Resolve("an-unknown-user-id");

        var messages = sink.Entries.Select(entry => entry.Message).ToList();
        Assert.Equal(["viewer=king via=Person", "viewer=king via=FirstLive"], messages);
        Assert.All(messages, message =>
        {
            Assert.DoesNotContain(userId!, message, StringComparison.Ordinal);   // null-forgiving: the Demo king has a person link
            Assert.DoesNotContain("an-unknown-user-id", message, StringComparison.Ordinal);
        });
        Assert.All(sink.Entries, entry => Assert.Equal(LogLevel.Debug, entry.Level));
    }

    private sealed class FixedFactory(Func<IRealmSession> create) : IRealmSessionFactory
    {
        public List<DemoUrlParams?> Requests { get; } = [];

        public IRealmSession Create(DemoUrlParams? demo)
        {
            Requests.Add(demo);
            return create();
        }
    }

    // A Demo session that has no members and no person links.
    private sealed class EmptySession : IRealmSession
    {
        private readonly IRealmSession _inner = new DemoRealmSessionFactory().Create(null);

        public bool Disposed { get; private set; }

        public RealmSnapshot Current => _inner.Current with { Members = [] };

        public event Action? Changed
        {
            add => _inner.Changed += value;
            remove => _inner.Changed -= value;
        }

        public TimeProvider Time => _inner.Time;

        public TimeZoneInfo Zone => _inner.Zone;

        public ValueTask<WeekReportVm> GetWeekReportAsync(int weekOffset, DayOfWeek weekStart, CancellationToken ct) =>
            _inner.GetWeekReportAsync(weekOffset, weekStart, ct);

        public ValueTask<DriverWeek?> GetDriverWeekAsync(string memberId, int weekOffset, DayOfWeek weekStart, CancellationToken ct) =>
            _inner.GetDriverWeekAsync(memberId, weekOffset, weekStart, ct);

        public string? ResolveMe(string? haUserId) => null;

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return _inner.DisposeAsync();
        }
    }
}
