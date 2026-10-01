namespace Realm.Infrastructure.Options;

/// <summary>The shape of an add-on option value (02 section 3.1): what the Supervisor writes into <c>options.json</c> for the key.</summary>
public enum OptionKind
{
    /// <summary>A JSON string.</summary>
    Text,

    /// <summary>A JSON whole number.</summary>
    Integer,

    /// <summary>A JSON number.</summary>
    Number,

    /// <summary>A JSON true or false.</summary>
    Flag,

    /// <summary>A JSON array of strings; bound as <c>Path:0</c>, <c>Path:1</c> and so on.</summary>
    TextList,

    /// <summary>A JSON array of flat objects; bound as <c>Path:0:PascalCasedKey</c> (02 section 3.4).</summary>
    ObjectList,
}
