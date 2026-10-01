namespace Realm.Domain;

/// <summary>Creates one <see cref="IRealmSession"/> per circuit.</summary>
public interface IRealmSessionFactory
{
    /// <summary>
    /// A non-null <paramref name="demo"/> means this circuit asked for Demo; null means the mode's default.
    /// </summary>
    IRealmSession Create(DemoUrlParams? demo);
}
