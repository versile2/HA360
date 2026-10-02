using System.Text.RegularExpressions;
using Xunit;

namespace Realm.Web.Tests;

/// <summary>
/// What the Location page does with the camera ([X-07], R1-12, 01 section 4.11, 03 section 4.7). The script reports a settled camera only when the recentre state changes, because every
/// report re-renders the page on the server and a pan and a wheel zoom must stay under six frames; the page keeps the position for the way back from Driving by reading it from the script once, as it
/// goes away. A page is not rendered here, so these rows pin the page's source in the manner of <c>SheetDispatchTests</c>; the reads themselves are covered in <c>MapViewTests</c> and
/// <c>MapInteropTests</c>, and the one-report-per-gesture rule of the script in <c>tests/js/cameraReport.test.mjs</c>.
/// </summary>
public sealed class LocationPageCameraTests
{
    private const string DisposeSignature = "public async ValueTask DisposeAsync() {";

    private static string Page() =>
        Regex.Replace(File.ReadAllText(Path.Combine(PayloadContractTests.FindRepositoryRoot(), "src", "Realm.Web", "Pages", "LocationPage.razor")), @"\s+", " ");

    [Fact(DisplayName = "[AC-20] a camera report records the camera and re-renders the page only when the recentre state changed, so the recentre button follows the state")]
    public void ACameraReport_RerendersThePage_OnlyWhenTheRecentreStateChanged()
    {
        var page = Page();

        Assert.Contains("OnCameraChanged=\"OnCameraChangedAsync\"", page, StringComparison.Ordinal);
        Assert.Contains("Ui.RecordCamera(camera, Session.Time.GetUtcNow()); if (camera.Recenter != _recenter) { _recenter = camera.Recenter; StateHasChanged(); }", page, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "[R1-12] the camera is read from the script once, as the page goes away, before the page awaits anything, so the call is queued ahead of the map's dispose")]
    public void TheCamera_IsReadFromTheScriptOnce_AsThePageGoesAway_BeforeItAwaitsAnything()
    {
        var page = Page();

        Assert.Single(Regex.Matches(page, @"\.GetCameraAsync\("));
        var dispose = page[page.IndexOf(DisposeSignature, StringComparison.Ordinal)..];
        var read = dispose.IndexOf("leaving = _map?.GetCameraAsync();", StringComparison.Ordinal);
        Assert.True(read > 0, "DisposeAsync asks the map for its camera.");
        Assert.DoesNotContain("await ", dispose[..read], StringComparison.Ordinal);
        Assert.Contains("if (_started) { await RememberCameraAsync(leaving); }", dispose, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "[R1-12] the script's camera is waited for at most two seconds, a script that is gone is not an error, and the ten minutes are stamped from the moment the page goes away")]
    public void TheWaitForTheCamera_IsBounded_AndTheStampIsTheMomentThePageGoesAway()
    {
        var page = Page();

        Assert.Contains("camera = await leaving.WaitAsync(TimeSpan.FromSeconds(2));", page, StringComparison.Ordinal);
        Assert.Contains("exception is JSException or JSDisconnectedException or OperationCanceledException or TimeoutException", page, StringComparison.Ordinal);
        Assert.Contains("if ((camera ?? Ui.LastCamera) is { } remembered) { Ui.RecordCamera(remembered, Session.Time.GetUtcNow()); }", page, StringComparison.Ordinal);
    }
}
