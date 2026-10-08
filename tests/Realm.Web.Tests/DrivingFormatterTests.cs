using Realm.Demo;
using Realm.Domain;
using Realm.Web.Formatting;
using Xunit;

namespace Realm.Web.Tests;

/// <summary>
/// The strings and numbers of the Driving screen (D76: a file of its own). <see cref="DrivingFormatter"/> builds the week chips, the range line, the driver card lines
/// and the events pill; <see cref="StatNameFormatter"/> builds the names, sentences and accessible names of the six headline items. Every expected string is the one
/// of 01 sections 6 and 8.8 and 10.3, and the figures behind the sentences come from the Demo cast, so the formatters are checked against the same report the screen
/// shows. They are pure: no clock, and the zone is always passed in.
/// </summary>
public sealed class DrivingFormatterTests
{
    private static readonly TimeZoneInfo Chicago = TimeZoneInfo.FindSystemTimeZoneById("America/Chicago");

    // ---- the week --------------------------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(null, 0)]
    [InlineData("", 0)]
    [InlineData("0", 0)]
    [InlineData("1", 1)]
    [InlineData("2", 2)]
    [InlineData("3", 3)]
    [InlineData("4", 0)]
    [InlineData("-1", 0)]
    [InlineData("+1", 0)]
    [InlineData("1.5", 0)]
    [InlineData(" 1", 0)]
    [InlineData("two", 0)]
    public void ParseWeek_AcceptsOnlyPlainDigitsFromZeroToThree(string? value, int expected)
    {
        Assert.Equal(expected, DrivingFormatter.ParseWeek(value));
    }

    [Fact]
    public async Task WeekChips_AreThisWeek_LastWeek_AndTwoDateRanges_WithAnEnDash()
    {
        await using var session = Demo();
        var chips = WeekMath.Chips(session.Time.GetUtcNow(), session.Current.WeekStart, session.Zone);

        // The separator is an en dash with spaces by design (R-005): the reference screenshots use a hyphen and nobody should "fix" this.
        Assert.Equal(["This week", "Last week", "Sep 14 – Sep 20", "Sep 7 – Sep 13"], chips.Select(DrivingFormatter.ChipLabel));
        Assert.Contains('–', DrivingFormatter.ChipLabel(chips[2]));
        Assert.DoesNotContain('-', DrivingFormatter.ChipLabel(chips[2]));
        Assert.Equal(
            [
                "This week, September 28 to October 4.",
                "Last week, September 21 to September 27.",
                "September 14 to September 20.",
                "September 7 to September 13.",
            ],
            chips.Select(DrivingFormatter.ChipAccessibleName));
    }

    [Fact]
    public void RangeText_SpanningTwoYears_CarriesTheYearOnBothEnds()
    {
        var start = new DateTimeOffset(2025, 12, 29, 0, 0, 0, TimeSpan.FromHours(-6));
        var end = new DateTimeOffset(2026, 1, 4, 23, 59, 59, TimeSpan.FromHours(-6));

        Assert.Equal("Dec 29, 2025 – Jan 4, 2026", DrivingFormatter.RangeText(start, end));
        Assert.Equal("Sep 28 – Oct 4", DrivingFormatter.RangeText(new DateTimeOffset(2026, 9, 28, 0, 0, 0, TimeSpan.FromHours(-5)), new DateTimeOffset(2026, 10, 4, 23, 59, 59, TimeSpan.FromHours(-5))));
    }

    [Fact]
    public async Task RangeLine_ReadsSoFar_ThePlainRange_ARecordedFromSuffix_AndTheEmptySentence()
    {
        var current = await Report(0);
        var last = await Report(1);
        var partial = await Report(1, "fresh-install");
        var noRecord = await Report(2, "fresh-install");

        Assert.Equal("Sep 28 – Oct 4 · so far", DrivingFormatter.RangeLine(current, Chicago));
        Assert.Equal("Sep 21 – Sep 27", DrivingFormatter.RangeLine(last, Chicago));
        Assert.Equal("Sep 21 – Sep 27 · recorded from Wed", DrivingFormatter.RangeLine(partial, Chicago));
        Assert.Equal("The scribes have no record of this week.", DrivingFormatter.RangeLine(noRecord, Chicago));
    }

    // ---- numbers ---------------------------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(0, "0")]
    [InlineData(64, "64")]
    [InlineData(1250, "1,250")]
    public void Count_GroupsThousands(int value, string expected)
    {
        Assert.Equal(expected, DrivingFormatter.Count(value));
    }

    [Theory]
    [InlineData(42.91584, 96)]
    [InlineData(24.1, 54)]
    [InlineData(35.7632, 80)]
    [InlineData(0, 0)]
    public void Mph_ConvertsMetresPerSecondWithTheExactFactor(double metresPerSecond, int expected)
    {
        Assert.Equal(expected, DrivingFormatter.Mph(metresPerSecond));
    }

    [Fact]
    public void SpeedText_IsADashWhenThereIsNoSpeed_NeverZero()
    {
        Assert.Equal("—", DrivingFormatter.SpeedText(null));
        Assert.Equal("96 mph", DrivingFormatter.SpeedText(42.91584));
    }

    [Theory]
    [InlineData(94.4, "94.4")]
    [InlineData(202.6, "202.6")]
    [InlineData(366.0, "366.0")]
    [InlineData(118.2, "118.2")]
    [InlineData(0.04, "0.0")]
    [InlineData(1234.56, "1,234.6")]
    public void DriverMiles_AreOneDecimalAtMost(double miles, string expected)
    {
        Assert.Equal(expected, DrivingFormatter.DriverMiles(miles * 1609.344));
    }

    [Theory]
    [InlineData(781.2, "781")]
    [InlineData(842.6, "843")]
    [InlineData(1257.2, "1,257")]
    [InlineData(0, "0")]
    public void TotalMiles_AreWholeMilesWithAThousandsSeparator(double miles, string expected)
    {
        Assert.Equal(expected, DrivingFormatter.TotalMiles(miles * 1609.344));
    }

    [Theory]
    [InlineData(1, "drive")]
    [InlineData(0, "drives")]
    [InlineData(2, "drives")]
    public void Drives_IsSingularAtOne(int count, string expected)
    {
        Assert.Equal(expected, DrivingFormatter.Drives(count));
    }

    [Theory]
    [InlineData("Alden", "A")]
    [InlineData("  briar", "B")]
    [InlineData("élodie", "É")]
    [InlineData("", "?")]
    [InlineData("   ", "?")]
    [InlineData(null, "?")]
    public void Initial_IsTheFirstLetterInCapitals_OrAQuestionMark(string? name, string expected)
    {
        Assert.Equal(expected, DrivingFormatter.Initial(name));
    }

    // ---- the driver card -------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task DriverLines_OfTheDefaultFixture_AreTheOnesOfAc37()
    {
        var report = await Report(0);

        Assert.Equal(
            [
                "22 drives • 94.4 miles",
                "18 drives • 202.6 miles",
                "14 drives • 366.0 miles",
                "10 drives • 118.2 miles",
            ],
            report.Drivers.Select(d => DrivingFormatter.DriverLine(d)));
        Assert.Contains('•', DrivingFormatter.DriverLine(report.Drivers[0]));
        Assert.DoesNotContain('·', DrivingFormatter.DriverLine(report.Drivers[0]));
        Assert.Equal(["king", "jester", "cryptid", "queen"], report.Drivers.Select(d => d.MemberId));
    }

    [Fact]
    public void DriverLine_HasOneDriveInTheSingular_AndTheTwoEmptyStates()
    {
        Assert.Equal("1 drive • 0.5 miles", DrivingFormatter.DriverLine(Driver(drives: 1, miles: 0.5)));
        Assert.Equal("No drives this week · resting in the castle", DrivingFormatter.DriverLine(Driver(drives: 0, miles: 0)));
        Assert.Equal("No record of this week", DrivingFormatter.DriverLine(Driver(covered: false, drives: null, miles: null)));
    }

    [Fact]
    public async Task Pill_OfTheDefaultFixture_ListsTheTrackedTypesOnly_WithoutAnAsterisk()
    {
        var report = await Report(0);

        // Alden is the phone-capable driver: two types. The other three have speeding alone, and rapid acceleration and hard braking are absent for everyone.
        Assert.Equal(
            ["6 speeding · 60 phone", "38 speeding events", "10 speeding events", "2 speeding events"],
            report.Drivers.Select(d => DrivingFormatter.Pill(d)!.Text));
        Assert.All(report.Drivers, d => Assert.Null(DrivingFormatter.Pill(d)!.Tooltip));
        Assert.All(report.Drivers, d => Assert.DoesNotContain('*', DrivingFormatter.Pill(d)!.Text));
    }

    [Fact]
    public async Task Pill_OfAllSources_IsTheDataLayersSum_WithThreeOrMoreTypes()
    {
        var report = await Report(0, "all-sources");

        Assert.Equal(["69 events", "167 events", "58 events", "35 events"], report.Drivers.Select(d => DrivingFormatter.Pill(d)!.Text));
    }

    [Fact]
    public async Task Pill_WhenPhoneIsUnavailable_IsSpeedingAlone_WithoutAnAsterisk()
    {
        var report = await Report(0, "phone-unavailable");

        Assert.Equal("6 speeding events", DrivingFormatter.Pill(report.Drivers[0])!.Text);
        Assert.Null(DrivingFormatter.Pill(report.Drivers[0])!.Tooltip);
    }

    [Fact]
    public void Pill_OneType_IsSingularAtOne()
    {
        Assert.Equal("1 speeding event", DrivingFormatter.Pill(Driver(speeding: 1))!.Text);
        Assert.Equal("38 speeding events", DrivingFormatter.Pill(Driver(speeding: 38))!.Text);
        Assert.Equal(PillTone.Events, DrivingFormatter.Pill(Driver(speeding: 1))!.Tone);
    }

    [Fact]
    public void Pill_TwoTypes_ListsBothCounts_AndAZeroIsARealZero()
    {
        Assert.Equal("6 speeding · 60 phone", DrivingFormatter.Pill(Driver(speeding: 6, phone: 60))!.Text);
        Assert.Equal("6 speeding · 0 phone", DrivingFormatter.Pill(Driver(speeding: 6, phone: 0))!.Text);
        Assert.Equal("0 speeding · 5 phone", DrivingFormatter.Pill(Driver(speeding: 0, phone: 5))!.Text);
    }

    [Fact]
    public void Pill_ThreeOrMoreTypes_ReadsTheEventsTotalOfTheDataLayer()
    {
        // The sum is the data layer's: a total the formatter would compute (38 + 115 + 11 + 3 = 167) is not what it prints when the layer says otherwise.
        var summary = Driver(speeding: 38, phone: 115, accel: 11, braking: 3, eventsTotal: 170);

        Assert.Equal("170 events", DrivingFormatter.Pill(summary)!.Text);
    }

    [Fact]
    public void Pill_AllZero_IsTheAllClear()
    {
        var pill = DrivingFormatter.Pill(Driver(speeding: 0))!;

        Assert.Equal(PillTone.Clear, pill.Tone);
        Assert.Equal("No events", pill.Text);
        Assert.Null(pill.Tooltip);
    }

    [Fact]
    public void Pill_WithNoCount_ReadsEventsDash_AndCarriesTheUnavailableTooltip()
    {
        var pill = DrivingFormatter.Pill(Driver(speeding: null, eventsTotal: null))!;

        Assert.Equal(PillTone.Unknown, pill.Tone);
        Assert.Equal("Events: —", pill.Text);
        Assert.Equal("The Realm hasn't recorded this yet", pill.Tooltip);
    }

    [Theory]
    [InlineData(1, null, "38* speeding events")]
    [InlineData(3, 60, "38* speeding · 60 phone")]
    public void Pill_AsteriskFollowsTheSpeedingCount_WhenSomeTripsWereTooSparse(int coarseTrips, int? phone, string expected)
    {
        var pill = DrivingFormatter.Pill(Driver(speeding: 38, phone: phone, coarseTrips: coarseTrips))!;

        Assert.Equal(expected, pill.Text);
        Assert.Equal("Some drives were too sparse to measure speed", pill.Tooltip);
    }

    [Fact]
    public void Pill_AsteriskOnThreeOrMoreTypes_FollowsTheSum()
    {
        var pill = DrivingFormatter.Pill(Driver(speeding: 38, phone: 115, accel: 11, braking: 3, coarseTrips: 2))!;

        Assert.Equal("167* events", pill.Text);
    }

    [Fact]
    public void Pill_NeverGetsAnAsterisk_ForANullCountOfOneKind()
    {
        // A driver who is not phone-capable has no phone count; rapid acceleration and hard braking have no source. None of that is "partial" (R-110, O-9).
        var notPhoneCapable = Driver(speeding: 38, phone: null, phoneCapable: false, coarseTrips: 0);
        var phoneCapableWithoutAReading = Driver(speeding: 6, phone: null, phoneCapable: true, coarseTrips: 0);

        Assert.Equal("38 speeding events", DrivingFormatter.Pill(notPhoneCapable)!.Text);
        Assert.Equal("6 speeding events", DrivingFormatter.Pill(phoneCapableWithoutAReading)!.Text);
        Assert.Null(DrivingFormatter.Pill(notPhoneCapable)!.Tooltip);
        Assert.Null(DrivingFormatter.Pill(phoneCapableWithoutAReading)!.Tooltip);
    }

    [Fact]
    public void Pill_IsAbsent_ForADriverWithNoDrives_OrWithNoRecord()
    {
        Assert.Null(DrivingFormatter.Pill(Driver(drives: 0, miles: 0, speeding: 0)));
        Assert.Null(DrivingFormatter.Pill(Driver(covered: false, drives: null, miles: null, speeding: null, eventsTotal: null)));
    }

    [Fact]
    public async Task DriverAccessibleName_OfTheDefaultFixture_IsTheOneOfSection10_3()
    {
        var report = await Report(0);
        var cass = report.Drivers.Single(d => d.MemberId == "jester");

        Assert.Equal("Cass: 18 drives, 202.6 miles, 38 speeding events. Double tap for weekly details.", DrivingFormatter.DriverAccessibleName("Cass", cass));
        Assert.Equal(
            "Alden: 22 drives, 94.4 miles, 6 speeding · 60 phone. Double tap for weekly details.",
            DrivingFormatter.DriverAccessibleName("Alden", report.Drivers[0]));
    }

    [Fact]
    public void DriverAccessibleName_SaysTheAsteriskInWords_AndTheEmptyStates()
    {
        Assert.Equal(
            "Cass: 18 drives, 202.6 miles, 38 speeding events, some drives were too sparse to measure speed. Double tap for weekly details.",
            DrivingFormatter.DriverAccessibleName("Cass", Driver(speeding: 38, coarseTrips: 1)));
        Assert.Equal("Cass: no drives this week. Double tap for weekly details.", DrivingFormatter.DriverAccessibleName("Cass", Driver(drives: 0, miles: 0, speeding: 0)));
        Assert.Equal(
            "Cass: no record of this week. Double tap for weekly details.",
            DrivingFormatter.DriverAccessibleName("Cass", Driver(covered: false, drives: null, miles: null, speeding: null, eventsTotal: null)));
    }

    [Fact]
    public void DriverHref_IsRelative_AndCarriesTheWeek()
    {
        Assert.Equal("driving/jester?week=0", DrivingFormatter.DriverHref("jester", 0));
        Assert.Equal("driving/king?week=3", DrivingFormatter.DriverHref("king", 3));
        Assert.Equal("driving/a%20b?week=1", DrivingFormatter.DriverHref("a b", 1));
        Assert.NotEqual('/', DrivingFormatter.DriverHref("jester", 0)[0]);
    }

    [Fact]
    public async Task TheCardsOfTheDefaultFixture_ReadAsInAc36()
    {
        await using var session = Demo();
        var report = await Report(0);
        var alden = session.Current.Members.Single(m => m.Id == "king").DisplayName;

        Assert.Equal("96 mph", DrivingFormatter.TopSpeedValue(report.TopSpeed));
        Assert.Equal("—", DrivingFormatter.TopSpeedValue(null));
        Assert.Equal("Top speed: 96 miles per hour, Alden. Double tap for details.", DrivingFormatter.TopSpeedAccessibleName(report.TopSpeed, alden));
        Assert.Equal("Top speed: not recorded yet. Double tap for details.", DrivingFormatter.TopSpeedAccessibleName(null, null));
        Assert.Equal("64", DrivingFormatter.Count(report.Totals.Drives));
        Assert.Equal("781", DrivingFormatter.TotalMiles(report.Totals.Meters));
        Assert.Equal("Drives: 64. Total miles: 781. Double tap for details.", DrivingFormatter.DrivesAccessibleName(report.Totals));
        Assert.Equal("Drives: not recorded yet. Double tap for details.", DrivingFormatter.DrivesAccessibleName(null));
    }

    // ---- the stat names and sentences (01 sections 6.3 and 6.7) --------------------------------------------------------------------------------

    [Fact]
    public void LabelsTitlesAndLore_AreTheOnesOfTheCopyDeck()
    {
        Assert.Equal(["Speeding", "Phone use", "Rapid accel.", "Hard braking"], StatNameFormatter.ChipKeys.Select(StatNameFormatter.Label));
        Assert.Equal(["Speeding", "Phone use", "Rapid acceleration", "Hard braking", "Top Speed", "Total Drives"], Keys.Select(StatNameFormatter.Title));
        Assert.Equal(
            ["Heralds of haste", "Eyes on the road, good sirs", "The sudden gallop", "Whoa, steed!", "The fastest charge", "Leagues travelled"],
            Keys.Select(StatNameFormatter.Lore));
        Assert.Equal("Top Speed", StatNameFormatter.Label(StatNameFormatter.TopSpeedKey));
        Assert.Equal("Drives", StatNameFormatter.Label(StatNameFormatter.DrivesKey));
    }

    [Fact]
    public void Footnotes_BeginWithTheRealmsOwnRecords_AndNeverNameLife360()
    {
        foreach (var key in Keys)
        {
            var footnote = StatNameFormatter.Footnote(key);

            Assert.StartsWith("Source: The Realm's own records", footnote, StringComparison.Ordinal);
            Assert.DoesNotContain("Life360", footnote, StringComparison.Ordinal);
        }

        Assert.Contains("sampled every ~42 s, so brief bursts are missed", StatNameFormatter.Footnote(EventKeys.Speeding), StringComparison.Ordinal);
        Assert.Contains("default 80 mph", StatNameFormatter.Footnote(EventKeys.Speeding), StringComparison.Ordinal);
        Assert.Equal("Source: The Realm's own records.", StatNameFormatter.Footnote(EventKeys.Accel));
        Assert.Equal("Source: The Realm's own records.", StatNameFormatter.Footnote(EventKeys.Braking));
        Assert.EndsWith("Distances are GPS-estimated (about 2–5 % low on winding roads).", StatNameFormatter.Footnote(StatNameFormatter.DrivesKey), StringComparison.Ordinal);
    }

    [Fact]
    public void TheFixedSentences_AreTheOnesOfSection8_8()
    {
        Assert.Equal("The Realm hasn't recorded this yet", StatNameFormatter.UnavailableTooltip);
        Assert.Equal("The Realm hasn't recorded this yet.", StatNameFormatter.UnavailableSentence);
        Assert.Equal("Screen time while Android Auto is connected isn't counted, so navigation doesn't count as phone use.", StatNameFormatter.AndroidAutoTooltip);
        Assert.Equal("Distances are GPS-estimated (about 2–5 % low on winding roads)", StatNameFormatter.DistanceCaption);
        Assert.Equal("No speed data for this week yet.", StatNameFormatter.NoSpeedSentence);
        Assert.Equal("sampled", StatNameFormatter.SampledCaption);
    }

    [Fact]
    public async Task TheChips_OfTheDefaultFixture_ReadAsInAc34()
    {
        var report = await Report(0);

        Assert.Equal(["56", "60*", "—", "—"], StatNameFormatter.ChipKeys.Select(key => StatNameFormatter.ChipNumber(report.Events[key])));
        Assert.Equal(
            [TrendDirection.Up, TrendDirection.Down, TrendDirection.None, TrendDirection.None],
            StatNameFormatter.ChipKeys.Select(key => StatNameFormatter.Trend(report.Events[key])));
        Assert.Equal(
            [
                "Counted from ~42 s samples; short bursts are missed",
                "Only 1 of 4 drivers shared this",
                "The Realm hasn't recorded this yet",
                "The Realm hasn't recorded this yet",
            ],
            StatNameFormatter.ChipKeys.Select(key => StatNameFormatter.ChipTooltip(report.Events[key])));
    }

    [Fact]
    public async Task TheChips_OfEveryWeek_FollowTheDataLayersTrend()
    {
        // Last week: speeding 63 up, phone 64* down. The oldest week has no earlier week, so no chip has an arrow (AC-34).
        var last = await Report(1);
        var oldest = await Report(3);

        Assert.Equal(("63", TrendDirection.Up), (StatNameFormatter.ChipNumber(last.Events[EventKeys.Speeding]), StatNameFormatter.Trend(last.Events[EventKeys.Speeding])));
        Assert.Equal(("64*", TrendDirection.Down), (StatNameFormatter.ChipNumber(last.Events[EventKeys.Phone]), StatNameFormatter.Trend(last.Events[EventKeys.Phone])));
        Assert.Equal(("44", TrendDirection.None), (StatNameFormatter.ChipNumber(oldest.Events[EventKeys.Speeding]), StatNameFormatter.Trend(oldest.Events[EventKeys.Speeding])));
        Assert.Equal(("61*", TrendDirection.None), (StatNameFormatter.ChipNumber(oldest.Events[EventKeys.Phone]), StatNameFormatter.Trend(oldest.Events[EventKeys.Phone])));
        Assert.All(StatNameFormatter.ChipKeys, key => Assert.Equal(TrendDirection.None, StatNameFormatter.Trend(oldest.Events[key])));
    }

    [Fact]
    public async Task TheChips_OfAllSources_HaveNoAsterisk_AndTheFourArrowsOfAc34()
    {
        var report = await Report(0, "all-sources");

        Assert.Equal(["56", "250", "18", "5"], StatNameFormatter.ChipKeys.Select(key => StatNameFormatter.ChipNumber(report.Events[key])));
        Assert.Equal(
            [TrendDirection.Up, TrendDirection.Down, TrendDirection.Up, TrendDirection.Down],
            StatNameFormatter.ChipKeys.Select(key => StatNameFormatter.Trend(report.Events[key])));
    }

    [Fact]
    public async Task ThePhoneChip_WhenUnavailable_IsADash_WithNoArrow_AndNeverZero()
    {
        var report = await Report(0, "phone-unavailable");
        var phone = report.Events[EventKeys.Phone];

        Assert.Equal("—", StatNameFormatter.ChipNumber(phone));
        Assert.Equal(TrendDirection.None, StatNameFormatter.Trend(phone));
        Assert.Equal("The Realm hasn't recorded this yet", StatNameFormatter.ChipTooltip(phone));
        Assert.Equal("Phone use: not recorded yet. Double tap for details.", StatNameFormatter.ChipAccessibleName(EventKeys.Phone, phone, 0));
        Assert.Equal("The Realm hasn't recorded this yet.", StatNameFormatter.Summary(EventKeys.Phone, phone, 0));
        Assert.Equal("56", StatNameFormatter.ChipNumber(report.Events[EventKeys.Speeding]));
    }

    [Fact]
    public void ChipNumber_NeverTurnsANullIntoZero_AndKeepsARealZero()
    {
        Assert.Equal("—", StatNameFormatter.ChipNumber(null));
        Assert.Equal("—", StatNameFormatter.ChipNumber(Stat(total: null)));
        Assert.Equal("0", StatNameFormatter.ChipNumber(Stat(total: 0)));
        Assert.Equal("1,250", StatNameFormatter.ChipNumber(Stat(total: 1250)));
    }

    [Theory]
    [InlineData(7, TrendDirection.Up)]
    [InlineData(1, TrendDirection.Up)]
    [InlineData(-11, TrendDirection.Down)]
    [InlineData(0, TrendDirection.Flat)]
    [InlineData(null, TrendDirection.None)]
    public void Trend_IsTheSignOfTheDelta(int? delta, TrendDirection expected)
    {
        Assert.Equal(expected, StatNameFormatter.Trend(Stat(total: 56, delta: delta)));
    }

    [Fact]
    public void Trend_HasNoArrow_WithoutAValue()
    {
        Assert.Equal(TrendDirection.None, StatNameFormatter.Trend(null));
        Assert.Equal(TrendDirection.None, StatNameFormatter.Trend(Stat(total: null, delta: 5)));
    }

    [Fact]
    public void TheAsterisk_IsThePartialFlag_OnAValue_OfATypeThatCanHaveOne()
    {
        Assert.Equal("60*", StatNameFormatter.ChipNumber(Stat(total: 60, partial: true, availability: EventAvailability.Some)));
        Assert.Equal("60*", StatNameFormatter.ChipNumber(Stat(total: 60, partial: true, availability: EventAvailability.All)));
        Assert.Equal("60", StatNameFormatter.ChipNumber(Stat(total: 60, partial: false, availability: EventAvailability.Some)));

        // A permanent gap never reads as a glitch (R-110), and a dash never carries an asterisk.
        Assert.Equal("60", StatNameFormatter.ChipNumber(Stat(total: 60, partial: true, availability: EventAvailability.None)));
        Assert.Equal("—", StatNameFormatter.ChipNumber(Stat(total: null, partial: true, availability: EventAvailability.Some)));
    }

    [Fact]
    public void TheTooltip_IsTheUnavailableSentence_ThePartialSentence_OrTheNote()
    {
        var shared = new[] { new EventDriverCount("king", 60, 71), new EventDriverCount("queen", null, null), new EventDriverCount("jester", null, null), new EventDriverCount("cryptid", null, null) };
        const string Note = "Counted from ~42 s samples; short bursts are missed";

        Assert.Equal("The Realm hasn't recorded this yet", StatNameFormatter.ChipTooltip(Stat(total: null)));
        Assert.Equal("The Realm hasn't recorded this yet", StatNameFormatter.ChipTooltip(null));
        Assert.Equal("Only 1 of 4 drivers shared this", StatNameFormatter.ChipTooltip(Stat(total: 60, partial: true, availability: EventAvailability.Some, drivers: shared)));
        Assert.Equal(Note, StatNameFormatter.ChipTooltip(Stat(total: 56, note: Note)));
        Assert.Equal(
            "Only 1 of 4 drivers shared this. " + Note,
            StatNameFormatter.ChipTooltip(Stat(total: 60, partial: true, availability: EventAvailability.Some, note: Note, drivers: shared)));
        Assert.Null(StatNameFormatter.ChipTooltip(Stat(total: 56)));
        Assert.Null(StatNameFormatter.ChipTooltip(Stat(total: 56, note: "  ")));
    }

    [Fact]
    public async Task TheChipAccessibleNames_OfTheDefaultFixture_AreThoseOfAc46()
    {
        var report = await Report(0);
        string Name(string key) => StatNameFormatter.ChipAccessibleName(key, report.Events[key], 0);

        Assert.Equal("Speeding: 56 events this week, up 7 from last week, which is worse. Double tap for details.", Name(EventKeys.Speeding));
        Assert.Equal("Phone use: 60 events this week, from 1 of 4 drivers, down 11 from last week, which is better. Double tap for details.", Name(EventKeys.Phone));
        Assert.Equal("Rapid acceleration: not recorded yet. Double tap for details.", Name(EventKeys.Accel));
        Assert.Equal("Hard braking: not recorded yet. Double tap for details.", Name(EventKeys.Braking));
    }

    [Fact]
    public void TheChipAccessibleName_FollowsTheWeek_TheSingular_AndAFlatTrend()
    {
        Assert.Equal("Speeding: 1 event this week, the same as last week. Double tap for details.", StatNameFormatter.ChipAccessibleName(EventKeys.Speeding, Stat(total: 1, delta: 0), 0));
        Assert.Equal("Speeding: 63 events last week, up 5 from the week before, which is worse. Double tap for details.", StatNameFormatter.ChipAccessibleName(EventKeys.Speeding, Stat(total: 63, delta: 5), 1));
        Assert.Equal("Speeding: 44 events that week. Double tap for details.", StatNameFormatter.ChipAccessibleName(EventKeys.Speeding, Stat(total: 44), 3));
        Assert.Equal("Speeding: not recorded yet. Double tap for details.", StatNameFormatter.ChipAccessibleName(EventKeys.Speeding, null, 0));
    }

    [Fact]
    public async Task ThePopupSentences_OfTheDefaultFixture_AreTheOnesOfSection6_7()
    {
        await using var session = Demo();
        var report = await Report(0);
        var alden = session.Current.Members.Single(m => m.Id == report.TopSpeed!.MemberId).DisplayName;

        Assert.Equal("56 speeding events this week, 7 more than last week.", StatNameFormatter.Summary(EventKeys.Speeding, report.Events[EventKeys.Speeding], 0));
        Assert.Equal("60 phone-use events this week from the 1 driver tracked, 11 fewer than last week.", StatNameFormatter.Summary(EventKeys.Phone, report.Events[EventKeys.Phone], 0));
        Assert.Equal("The Realm hasn't recorded this yet.", StatNameFormatter.Summary(EventKeys.Accel, report.Events[EventKeys.Accel], 0));
        Assert.Equal("The Realm hasn't recorded this yet.", StatNameFormatter.Summary(EventKeys.Braking, report.Events[EventKeys.Braking], 0));
        Assert.Equal("Alden hit 96 mph on Tue, Sep 29.", StatNameFormatter.TopSpeedSummary(report.TopSpeed, alden, session.Zone));
        Assert.Equal("No speed data for this week yet.", StatNameFormatter.TopSpeedSummary(null, null, session.Zone));
        Assert.Equal("64 drives, 781 miles on the road.", StatNameFormatter.DrivesSummary(report.Totals));
    }

    [Fact]
    public async Task ThePopupSentences_OfAllSources_NameTheRapidAccelerationsAndTheHardBraking()
    {
        var report = await Report(0, "all-sources");

        Assert.Equal("250 phone-use events this week, 24 fewer than last week.", StatNameFormatter.Summary(EventKeys.Phone, report.Events[EventKeys.Phone], 0));
        Assert.Equal("18 rapid accelerations this week, 6 more than last week.", StatNameFormatter.Summary(EventKeys.Accel, report.Events[EventKeys.Accel], 0));
        Assert.Equal("5 hard-braking events this week, 2 fewer than last week.", StatNameFormatter.Summary(EventKeys.Braking, report.Events[EventKeys.Braking], 0));
    }

    [Fact]
    public void TheSummary_FollowsThePeriod_TheSingular_TheZero_AndTheMissingComparator()
    {
        Assert.Equal("1 speeding event this week, 1 more than last week.", StatNameFormatter.Summary(EventKeys.Speeding, Stat(total: 1, delta: 1), 0));
        Assert.Equal("1 phone-use event last week, the same as the week before.", StatNameFormatter.Summary(EventKeys.Phone, Stat(total: 1, delta: 0), 1));
        Assert.Equal("1 rapid acceleration that week.", StatNameFormatter.Summary(EventKeys.Accel, Stat(total: 1), 2));
        Assert.Equal("1 hard-braking event that week, 4 fewer than the week before.", StatNameFormatter.Summary(EventKeys.Braking, Stat(total: 1, delta: -4), 3));
        Assert.Equal("No speeding events this week.", StatNameFormatter.Summary(EventKeys.Speeding, Stat(total: 0, delta: -49), 0));
        Assert.Equal("44 speeding events that week.", StatNameFormatter.Summary(EventKeys.Speeding, Stat(total: 44), 3));
    }

    [Fact]
    public void TheDrivesSummary_IsSingularAtOne()
    {
        Assert.Equal("1 drive, 12 miles on the road.", StatNameFormatter.DrivesSummary(new WeekTotals(1, 12 * 1609.344)));
    }

    // ---- the popups (01 sections 6.7 and 6.7.1) --------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("speeding", "56 speeding events this week, 7 more than last week.")]
    [InlineData("phone", "60 phone-use events this week from the 1 driver tracked, 11 fewer than last week.")]
    [InlineData("accel", "The Realm hasn't recorded this yet.")]
    [InlineData("braking", "The Realm hasn't recorded this yet.")]
    [InlineData("topspeed", "Alden hit 96 mph on Tue, Sep 29.")]
    [InlineData("drives", "64 drives, 781 miles on the road.")]
    public async Task PopupSummary_OfTheDefaultFixture_IsTheSentenceOfSection6_7(string key, string expected)
    {
        var report = await Report(0);

        Assert.Equal(expected, StatNameFormatter.PopupSummary(key, report, DemoMembers(), 0, Chicago));
    }

    [Theory]
    [InlineData("phone", "250 phone-use events this week, 24 fewer than last week.")]
    [InlineData("accel", "18 rapid accelerations this week, 6 more than last week.")]
    [InlineData("braking", "5 hard-braking events this week, 2 fewer than last week.")]
    public async Task PopupSummary_OfAllSources_IsTheSentenceOfSection6_7(string key, string expected)
    {
        var report = await Report(0, "all-sources");

        Assert.Equal(expected, StatNameFormatter.PopupSummary(key, report, DemoMembers(), 0, Chicago));
    }

    [Fact]
    public async Task PopupSummary_OfLastWeek_NamesThatWeeksTopSpeed_AndTheWeekBefore()
    {
        var report = await Report(1);

        Assert.Equal("63 speeding events last week, 5 more than the week before.", StatNameFormatter.PopupSummary(EventKeys.Speeding, report, DemoMembers(), 1, Chicago));
        Assert.Equal("Cass hit 92 mph on Fri, Sep 25.", StatNameFormatter.PopupSummary(StatNameFormatter.TopSpeedKey, report, DemoMembers(), 1, Chicago));
    }

    [Fact]
    public async Task PopupSummary_OfAnUnreadableOrUnrecordedWeek_IsTheUnavailableSentence_ForAllSixItems()
    {
        var noRecord = await Report(2, "fresh-install");

        foreach (var key in Keys)
        {
            Assert.Equal("The Realm hasn't recorded this yet.", StatNameFormatter.PopupSummary(key, null, DemoMembers(), 0, Chicago));
            Assert.Equal("The Realm hasn't recorded this yet.", StatNameFormatter.PopupSummary(key, noRecord, DemoMembers(), 2, Chicago));
        }
    }

    [Fact]
    public async Task PopupSummary_ForAnUnknownMember_FallsBackToTheId()
    {
        var report = await Report(0);

        Assert.Equal("king hit 96 mph on Tue, Sep 29.", StatNameFormatter.PopupSummary(StatNameFormatter.TopSpeedKey, report, new Dictionary<string, MemberVm>(), 0, Chicago));
        Assert.Equal("stranger", StatNameFormatter.NameOf(new Dictionary<string, MemberVm>(), "stranger"));
        Assert.Equal("Alden", StatNameFormatter.NameOf(DemoMembers(), "king"));
    }

    [Fact]
    public void ThePopupShape_OnlyDrivesHasTheToggle_AndMilesAndSpeedsHaveTheWidePills()
    {
        Assert.Equal(["drives"], Keys.Where(StatNameFormatter.HasToggle));
        Assert.Equal(["topspeed"], Keys.Where(key => StatNameFormatter.IsWide(key, miles: false)));
        Assert.Equal(["topspeed", "drives"], Keys.Where(key => StatNameFormatter.IsWide(key, miles: true)));
    }

    [Fact]
    public async Task PopupBars_OfTheDefaultFixture_AreInValueOrder_WithTheFractionOfTheLargest()
    {
        var report = await Report(0);

        AssertBars(report, "speeding", "Cass 38 1.000 | Dara 10 0.263 | Alden 6 0.158 | Briar 2 0.053");
        AssertBars(report, "topspeed", "Alden 96 mph 1.000 | Cass 88 mph 0.917 | Dara 84 mph 0.875 | Briar 82 mph 0.854");
        AssertBars(report, "drives", "Alden 22 1.000 | Cass 18 0.818 | Dara 14 0.636 | Briar 10 0.455");
        AssertBars(report, "drives", "Dara 366.0 mi 1.000 | Cass 202.6 mi 0.554 | Briar 118.2 mi 0.323 | Alden 94.4 mi 0.258", miles: true);
    }

    [Fact]
    public async Task PopupBars_ForAValueNobodyRecorded_AreADashWithNoFraction_NeverZero_InDriveOrder()
    {
        var report = await Report(0);

        AssertBars(report, "phone", "Alden 60 1.000 | Cass — null | Dara — null | Briar — null");
        AssertBars(report, "accel", "Alden — null | Cass — null | Dara — null | Briar — null");
        AssertBars(report, "braking", "Alden — null | Cass — null | Dara — null | Briar — null");
        Assert.Equal(
            ["Alden: 60 phone-use events", "Cass: not recorded", "Dara: not recorded", "Briar: not recorded"],
            StatNameFormatter.PopupBars("phone", report, DemoMembers()).Select(bar => bar.Text));
    }

    [Fact]
    public async Task PopupBars_WhenPhoneIsUnavailable_AreADashForEveryDriver()
    {
        var report = await Report(0, "phone-unavailable");

        AssertBars(report, "phone", "Alden — null | Cass — null | Dara — null | Briar — null");
        AssertBars(report, "speeding", "Cass 38 1.000 | Dara 10 0.263 | Alden 6 0.158 | Briar 2 0.053");
    }

    [Fact]
    public async Task PopupBars_OfAllSources_KeepARealZeroAsACountAtTheFloor_AndBreakTiesByDrivesThenName()
    {
        var report = await Report(0, "all-sources");

        // Dara and Briar both have 1 hard-braking event: Dara drove more, so Dara is first. Alden's 0 is a count, below them, not a dash.
        AssertBars(report, "braking", "Cass 3 1.000 | Dara 1 0.333 | Briar 1 0.333 | Alden 0 0.000");
        AssertBars(report, "phone", "Cass 115 1.000 | Alden 60 0.522 | Dara 44 0.383 | Briar 31 0.270");
        AssertBars(report, "accel", "Cass 11 1.000 | Alden 3 0.273 | Dara 3 0.273 | Briar 1 0.091");
        Assert.Equal("Alden: 0 hard-braking events", StatNameFormatter.PopupBars("braking", report, DemoMembers()).Last().Text);
        Assert.Equal("Briar: 1 rapid acceleration", StatNameFormatter.PopupBars("accel", report, DemoMembers()).Last().Text);
    }

    [Fact]
    public async Task PopupBars_OfAWeekNobodyWasRecordedIn_AreAllDashes_ByName()
    {
        var report = await Report(2, "fresh-install");

        foreach (var key in Keys)
        {
            AssertBars(report, key, "Alden — null | Briar — null | Cass — null | Dara — null");
        }

        AssertBars(report, "drives", "Alden — null | Briar — null | Cass — null | Dara — null", miles: true);
    }

    [Fact]
    public async Task PopupBars_WithNoReport_AreEmpty()
    {
        Assert.Empty(StatNameFormatter.PopupBars("speeding", null, DemoMembers()));
        Assert.Equal(4, StatNameFormatter.PopupBars("speeding", await Report(0), DemoMembers()).Count);
    }

    [Fact]
    public async Task PopupBars_TheAccessibleTexts_NameTheUnit()
    {
        var report = await Report(0);

        Assert.Equal("Alden: 96 miles per hour", StatNameFormatter.PopupBars("topspeed", report, DemoMembers())[0].Text);
        Assert.Equal("Alden: 22 drives", StatNameFormatter.PopupBars("drives", report, DemoMembers())[0].Text);
        Assert.Equal("Dara: 366.0 miles", StatNameFormatter.PopupBars("drives", report, DemoMembers(), miles: true)[0].Text);
        Assert.Equal("Cass: 38 speeding events", StatNameFormatter.PopupBars("speeding", report, DemoMembers())[0].Text);
    }

    // ---- the driver week (01 section 6.6) --------------------------------------------------------------------------------------------------

    [Fact]
    public async Task WeekHeading_IsTheChipNameAndTheRange_ForThisWeekAndLastWeek_AndThePlainRangeAfter()
    {
        await using var session = Demo();
        var chips = WeekMath.Chips(session.Time.GetUtcNow(), session.Current.WeekStart, session.Zone);

        Assert.Equal(
            ["This week · Sep 28 – Oct 4", "Last week · Sep 21 – Sep 27", "Sep 14 – Sep 20", "Sep 7 – Sep 13"],
            chips.Select(DrivingFormatter.WeekHeading));
    }

    [Theory]
    [InlineData(0, "driving?week=0")]
    [InlineData(1, "driving?week=1")]
    [InlineData(3, "driving?week=3")]
    public void BackHref_IsRelative_AndCarriesTheWeek(int week, string expected)
    {
        Assert.Equal(expected, DrivingFormatter.BackHref(week));
        Assert.NotEqual('/', DrivingFormatter.BackHref(week)[0]);
    }

    [Fact]
    public async Task TopSpeedOf_IsTheReportsSpeedOfThatDriver_OrNull()
    {
        var report = await Report(0);

        Assert.Equal(96, DrivingFormatter.Mph(DrivingFormatter.TopSpeedOf(report, "king")!.Value));
        Assert.Equal(88, DrivingFormatter.Mph(DrivingFormatter.TopSpeedOf(report, "jester")!.Value));
        Assert.Null(DrivingFormatter.TopSpeedOf(report, "prince"));
        Assert.Null(DrivingFormatter.TopSpeedOf(null, "king"));
        Assert.Null(DrivingFormatter.TopSpeedOf(await Report(2, "fresh-install"), "king"));
    }

    [Fact]
    public async Task TheTiles_OfCass_AreTheDefaultFixtures()
    {
        var cass = (await DriverWeekOf("jester", 0))!.Summary;
        var report = await Report(0);

        Assert.Equal("18", DrivingFormatter.DrivesTile(cass));
        Assert.Equal("202.6", DrivingFormatter.MilesTile(cass));
        Assert.Equal("88 mph", DrivingFormatter.TopSpeedTile(cass, DrivingFormatter.TopSpeedOf(report, "jester")));
        Assert.Equal("38 speeding events", DrivingFormatter.EventsTile(cass));
        Assert.Null(DrivingFormatter.EventsTileTooltip(cass));
        Assert.Equal(["38", "—", "—", "—"], StatNameFormatter.ChipKeys.Select(key => DrivingFormatter.EventCount(cass, key)));
    }

    [Fact]
    public async Task TheTiles_OfAlden_ReadTheTwoCountsInThePill()
    {
        var alden = (await DriverWeekOf("king", 0))!.Summary;

        Assert.Equal("6 speeding · 60 phone", DrivingFormatter.EventsTile(alden));
        Assert.Equal(["6", "60", "—", "—"], StatNameFormatter.ChipKeys.Select(key => DrivingFormatter.EventCount(alden, key)));
    }

    [Fact]
    public void TheTiles_OfADriverWhoWasNotCovered_AreDashes_EvenForASpeed()
    {
        var driver = Driver(covered: false, drives: null, miles: null, speeding: null, eventsTotal: null);

        Assert.Equal("—", DrivingFormatter.DrivesTile(driver));
        Assert.Equal("—", DrivingFormatter.MilesTile(driver));
        Assert.Equal("—", DrivingFormatter.TopSpeedTile(driver, 42.9));
        Assert.Equal("—", DrivingFormatter.EventsTile(driver));
        Assert.Null(DrivingFormatter.EventsTileTooltip(driver));
        Assert.Equal("—", DrivingFormatter.EventCount(driver, "speeding"));
    }

    [Fact]
    public void TheTiles_OfADriveFreeWeek_AreRealZeros_ExceptTheSpeedWhichIsUnknown()
    {
        var driver = Driver(drives: 0, miles: 0, speeding: 0);

        Assert.Equal("0", DrivingFormatter.DrivesTile(driver));
        Assert.Equal("0.0", DrivingFormatter.MilesTile(driver));
        Assert.Equal("—", DrivingFormatter.TopSpeedTile(driver, null));
        Assert.Equal("No events", DrivingFormatter.EventsTile(driver));
        Assert.Equal("0", DrivingFormatter.EventCount(driver, "speeding"));
        Assert.Equal("—", DrivingFormatter.EventCount(driver, "phone"));
    }

    [Fact]
    public void TheEventsTile_IsADash_WhenNoTypeHasACount_AndCarriesTheSparseAsterisk()
    {
        Assert.Equal("—", DrivingFormatter.EventsTile(Driver(speeding: null)));
        Assert.Equal("38* speeding events", DrivingFormatter.EventsTile(Driver(speeding: 38, coarseTrips: 2)));
        Assert.Equal("Some drives were too sparse to measure speed", DrivingFormatter.EventsTileTooltip(Driver(speeding: 38, coarseTrips: 2)));
        Assert.Null(DrivingFormatter.EventsTileTooltip(Driver(speeding: 38)));
    }

    [Fact]
    public void TheEmptyState_IsTheNoRecordSentence_TheQuietSentence_OrNothing()
    {
        Assert.Equal("The scribes have no record of this week.", DrivingFormatter.EmptyWeekText(Driver(covered: false, drives: null, miles: null, speeding: null, eventsTotal: null), 0));
        Assert.Equal("No drives this week. The roads are quiet.", DrivingFormatter.EmptyWeekText(Driver(drives: 0, miles: 0, speeding: 0), 0));
        Assert.Null(DrivingFormatter.EmptyWeekText(Driver(), 18));
    }

    [Fact]
    public void TheCaption_IsForGpsAndMixedDistances_OfADriverWithDrives()
    {
        Assert.True(DrivingFormatter.ShowDriverCaption(Driver(), 18));
        Assert.False(DrivingFormatter.ShowDriverCaption(Driver(), 0));
        Assert.False(DrivingFormatter.ShowDriverCaption(Driver(covered: false, drives: null, miles: null, speeding: null, eventsTotal: null), 0));
        Assert.True(DrivingFormatter.ShowDriverCaption(Driver() with { DistanceBasis = DistanceBasis.Mixed }, 3));
    }

    [Fact]
    public async Task DriveDays_GroupCassesWeekIntoThreeDays_NewestFirst_WithAGlobalIndex()
    {
        var week = (await DriverWeekOf("jester", 0))!;

        var days = DrivingFormatter.DriveDays(week.Trips, Chicago);

        Assert.Equal(["Wed, Sep 30", "Tue, Sep 29", "Mon, Sep 28"], days.Select(day => day.Label));
        Assert.Equal([6, 4, 8], days.Select(day => day.Rows.Count));
        Assert.Equal(Enumerable.Range(0, 18), days.SelectMany(day => day.Rows).Select(row => row.Index));

        var newest = days[0].Rows[0];
        Assert.Equal("9:01 – 9:06 pm", newest.Time);
        Assert.Equal("Hearth Haven → The Jester's Hall", newest.Route);
        Assert.Equal("1.1 mi · Top 38 mph", newest.Detail);
        Assert.Empty(newest.Events);

        // The chips are the non-zero counts: only speeding is recorded for Cass, and the chips add up to the week's 38.
        var chips = days.SelectMany(day => day.Rows).SelectMany(row => row.Events).ToList();
        Assert.All(chips, chip => Assert.Equal("speeding", chip.Key));
        Assert.Equal(38, chips.Sum(chip => chip.Count));
        Assert.Equal(new DriveEventChip("speeding", 4, "4 speeding events"), days[0].Rows[2].Events.Single());
    }

    [Fact]
    public void DriveDays_UseTheZone_ForTheDayOfAnEveningDrive()
    {
        // 02:30 UTC on Oct 1 is 9:30 pm on Sep 30 in Chicago: the drive belongs to Wednesday there and to Thursday in UTC.
        var start = new DateTimeOffset(2026, 10, 1, 2, 30, 0, TimeSpan.Zero);
        var trip = new DriveVm(start, start.AddMinutes(10), "Home", "Work", 1609.344, 17.8816, new Dictionary<string, int?>());

        Assert.Equal("Wed, Sep 30", DrivingFormatter.DriveDays([trip], Chicago).Single().Label);
        Assert.Equal("Thu, Oct 1", DrivingFormatter.DriveDays([trip], TimeZoneInfo.Utc).Single().Label);
    }

    [Fact]
    public void DriveDays_NameAnUnnamedPlace_AFarAwaySpeed_AndAMissingSpeed()
    {
        var start = new DateTimeOffset(2026, 9, 30, 15, 0, 0, TimeSpan.Zero);
        var events = new Dictionary<string, int?> { ["speeding"] = 1, ["phone"] = 0, ["accel"] = null, ["braking"] = 2 };
        var unknown = new DriveVm(start, start.AddMinutes(20), null, "Work", 8046.72, null, events);
        var fast = new DriveVm(start.AddHours(-3), start.AddHours(-3).AddMinutes(40), "Work", "Home", 40233.6, 38.0, new Dictionary<string, int?>());

        var rows = DrivingFormatter.DriveDays([unknown, fast], Chicago).SelectMany(day => day.Rows).ToList();

        Assert.Equal("Somewhere in the Realm → Work", rows[0].Route);
        Assert.Equal("5.0 mi · Top —", rows[0].Detail);
        Assert.Equal(["1 speeding event", "2 hard-braking events"], rows[0].Events.Select(chip => chip.Text));
        Assert.Equal("Work → Home", rows[1].Route);
        Assert.Equal("25 mi · Top 85 mph", rows[1].Detail);
        Assert.Empty(rows[1].Events);
    }

    [Fact]
    public void DriveDays_OfNoDrives_AreNoDays()
    {
        Assert.Empty(DrivingFormatter.DriveDays([], Chicago));
    }

    // ---- helpers (shared with StatChipTests) -----------------------------------------------------------------------------------------------

    private static readonly string[] Keys =
    [
        EventKeys.Speeding,
        EventKeys.Phone,
        EventKeys.Accel,
        EventKeys.Braking,
        StatNameFormatter.TopSpeedKey,
        StatNameFormatter.DrivesKey,
    ];

    internal static IRealmSession Demo(params string[] variants) =>
        FullCast.Session(new DemoUrlParams(null, variants));

    private static IReadOnlyDictionary<string, MemberVm> DemoMembers() =>
        Demo().Current.Members.ToDictionary(member => member.Id, StringComparer.Ordinal);

    private static async Task<DriverWeek?> DriverWeekOf(string memberId, int week, params string[] variants)
    {
        await using var session = Demo(variants);
        return await session.GetDriverWeekAsync(memberId, week, session.Current.WeekStart, CancellationToken.None);
    }

    // The bars of a popup as one line: "Cass 38 1.000 | Dara 10 0.263": the name, the pill and the fraction (three decimals, "null" for a dash).
    private static void AssertBars(WeekReportVm report, string key, string expected, bool miles = false)
    {
        var bars = StatNameFormatter.PopupBars(key, report, DemoMembers(), miles);
        var actual = string.Join(
            " | ",
            bars.Select(bar => $"{bar.Name} {bar.Pill} {(bar.Fraction is { } fraction ? fraction.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture) : "null")}"));
        Assert.Equal(expected, actual);
    }

    internal static async Task<WeekReportVm> Report(int week, params string[] variants)
    {
        await using var session = Demo(variants);
        return await session.GetWeekReportAsync(week, session.Current.WeekStart, CancellationToken.None);
    }

    internal static EventStat Stat(
        int? total,
        int? delta = null,
        bool partial = false,
        EventAvailability availability = EventAvailability.All,
        string? note = null,
        IReadOnlyList<EventDriverCount>? drivers = null) =>
        new(total, total is { } t && delta is { } d ? t - d : null, delta, StatSource.Derived, availability, partial, note, drivers ?? []);

    // A driver week with the given counts; the sum of the non-null counts is the data layer's EventsTotal unless the test says otherwise.
    internal static DriverSummary Driver(
        int? drives = 18,
        double? miles = 202.6,
        int? speeding = 38,
        int? phone = null,
        int? accel = null,
        int? braking = null,
        int coarseTrips = 0,
        bool phoneCapable = false,
        bool covered = true,
        object? eventsTotal = null)
    {
        var counts = new Dictionary<string, int?>
        {
            [EventKeys.Speeding] = speeding,
            [EventKeys.Phone] = phone,
            [EventKeys.Accel] = accel,
            [EventKeys.Braking] = braking,
        };
        int? total = eventsTotal is int given
            ? given
            : eventsTotal is null && counts.Values.Any(count => count is not null) ? counts.Values.Sum(count => count ?? 0) : null;
        return new DriverSummary(
            "jester",
            drives,
            miles * 1609.344,
            DistanceBasis.Gps,
            coarseTrips,
            phoneCapable,
            counts,
            total,
            coarseTrips > 0,
            covered,
            null);
    }
}
