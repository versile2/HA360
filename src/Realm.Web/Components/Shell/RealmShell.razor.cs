using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Primitives;
using Realm.Domain;
using Realm.Web.Hosting;

namespace Realm.Web.Components.Shell;

/// <summary>
/// Creates the circuit's <see cref="IRealmSession"/> in <see cref="OnInitialized"/> from <c>NavigationManager.Uri</c> (03 section 2.1, R-083),
/// cascades it with a <see cref="DemoUiOverrides"/>, and disposes it when the circuit's component tree ends.
/// </summary>
/// <remarks>
/// <para>
/// The seven Appendix-B parameters are <c>demo</c>, <c>now</c>, <c>style</c>, <c>sheet</c>, <c>layout</c>, <c>variant</c> and <c>week</c>.
/// <c>week</c> is an ordinary route query, honoured in every mode (R2-008). The other six apply only to a Demo session: Demo mode,
/// or a Live circuit that arrived with <c>demo=1</c> while the option <c>allow_demo_param</c> (<c>Demo:AllowParam</c>) is true. In any
/// other circuit they are ignored. A value outside its set is ignored, never an error.
/// </para>
/// <para>
/// Prerendering runs <see cref="OnInitialized"/> twice (the prerender scope, then the circuit), so it only reads the URL and asks the
/// factory for a session; the factory creates it cheaply and the session builds its data on first use (03 section 3.1). The URL is read
/// once: in-app navigation never recreates the session, and the shell is disposed with the circuit.
/// </para>
/// </remarks>
public sealed partial class RealmShell : IAsyncDisposable
{
    private static readonly string[] StyleIds = ["night", "day", "streets", "satellite", "demo-offline"];
    private static readonly string[] SheetStates = ["peek", "80"];
    private static readonly string[] LayoutModes = ["auto", "sheet", "panel"];

    // ISO 8601 with an explicit offset or Z. Without one the instant would depend on the server's zone, so it is not accepted.
    private static readonly string[] NowFormats =
    [
        "yyyy-MM-dd'T'HH:mm:ssK",
        "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK",
        "yyyy-MM-dd'T'HH:mmK",
    ];

    private IRealmSession? _session;
    private FirstDataWatch? _firstData;
    private DemoUiOverrides _overrides = DemoUiOverrides.None;
    private bool _disposed;

    /// <summary>The page and the controls (<c>MainLayout</c> passes its <c>main</c>, the top controls and the bottom navigation).</summary>
    [Parameter]
    public RenderFragment? ChildContent { get; set; }

    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    [Inject]
    private IRealmSessionFactory SessionFactory { get; set; } = default!;

    [Inject]
    private RuntimeOptions Runtime { get; set; } = default!;

    [Inject]
    private IConfiguration Configuration { get; set; } = default!;

    // Set by OnInitialized, which always runs before the first render.
    private IRealmSession Session => _session ?? throw new InvalidOperationException("The session is created in OnInitialized.");

    private DemoUiOverrides Overrides => _overrides;

    // The circuit's first-data deadlines, on the session's own clock; created beside the session, started on the first render of the circuit.
    private FirstDataWatch FirstData => _firstData ?? throw new InvalidOperationException("The first-data watch is created in OnInitialized.");

    /// <inheritdoc />
    protected override void OnInitialized()
    {
        var query = ParseQuery(Navigation.Uri);

        var demoSession = Runtime.Mode == RealmMode.Demo || (First(query, "demo") == "1" && AllowDemoParam());

        // A non-null value tells the factory that this circuit is a Demo session; null is the mode's default (03 section 2.2).
        var demo = demoSession ? new DemoUrlParams(ParseNow(First(query, "now")), ParseVariants(query)) : null;

        _overrides = new DemoUiOverrides(
            Style: demoSession ? OneOf(First(query, "style"), StyleIds) : null,
            Sheet: demoSession ? OneOf(First(query, "sheet"), SheetStates) : null,
            Layout: demoSession ? OneOf(First(query, "layout"), LayoutModes) : null,
            Week: ParseWeek(First(query, "week")));

        _session = SessionFactory.Create(demo);
        _firstData = new FirstDataWatch(() => Session.Time);
    }

    /// <inheritdoc />
    protected override void OnAfterRender(bool firstRender)
    {
        // Only the circuit renders after: a prerender pass runs no OnAfterRender, so no timer is started on a throw-away session's behalf.
        if (firstRender)
        {
            FirstData.Begin();
        }
    }

    /// <summary>Disposes the circuit's session once; later calls do nothing.</summary>
    public ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return ValueTask.CompletedTask;
        }

        _disposed = true;
        _firstData?.Dispose();
        return _session?.DisposeAsync() ?? ValueTask.CompletedTask;
    }

    // The option allow_demo_param, mapped to Demo:AllowParam by the options loader (02 section 3.4). Absent or unreadable means false.
    private bool AllowDemoParam() => bool.TryParse(Configuration["Demo:AllowParam"], out var allowed) && allowed;

    private static Dictionary<string, StringValues> ParseQuery(string uri) =>
        Uri.TryCreate(uri, UriKind.Absolute, out var parsed) ? QueryHelpers.ParseQuery(parsed.Query) : new();

    // The first occurrence of a single-valued parameter.
    private static string? First(Dictionary<string, StringValues> query, string name) =>
        query.TryGetValue(name, out var values) && values.Count > 0 ? values[0] : null;

    private static string? OneOf(string? value, string[] allowed) => value is not null && allowed.Contains(value) ? value : null;

    // week=0..3 written in plain digits.
    private static int? ParseWeek(string? value) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var week) && week is >= 0 and <= 3 ? week : null;

    private static DateTimeOffset? ParseNow(string? value) =>
        value is not null && HasExplicitOffset(value)
            && DateTimeOffset.TryParseExact(value, NowFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var instant)
            ? instant
            : null;

    private static bool HasExplicitOffset(string value) =>
        value.EndsWith('Z') || (value.Length > 6 && value[^6] is ('+' or '-') && value[^3] == ':');

    // variant=a,b: one or more names, commas inside a value and repeated parameters alike, left to right. The Demo factory ignores names it
    // does not know (02 section 9.5).
    private static string[] ParseVariants(Dictionary<string, StringValues> query) =>
        query.TryGetValue("variant", out var values)
            ? [.. values.SelectMany(value => (value ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))]
            : [];
}
