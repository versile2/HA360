namespace Realm.Domain;

/// <summary>
/// Resolves who "me" is during server-side rendering: the person link, then the fallback option,
/// then the first live member, then nobody.
/// </summary>
public interface IViewerResolver
{
    ViewerResolution Resolve(string? haUserId);
}
