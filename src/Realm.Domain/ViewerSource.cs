namespace Realm.Domain;

/// <summary>How the viewer's member was found. For logs and tests only, never for display.</summary>
public enum ViewerSource
{
    Person,
    Fallback,
    FirstLive,
    None,
}
