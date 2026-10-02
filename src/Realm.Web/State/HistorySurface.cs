namespace Realm.Web.State;

/// <summary>Which page the browser history belongs to while a <see cref="HistorySync"/> is attached (03 section 3.7).</summary>
public enum HistorySurface
{
    /// <summary>The Location page: the depth is the sheet's, <see cref="RealmUiState.Depth"/>.</summary>
    Location,

    /// <summary>The Driving root: one sentinel entry the page pushes on mount, plus one per open popup. Back from the sentinel goes to Location (01 section 2.2).</summary>
    Driving,
}
