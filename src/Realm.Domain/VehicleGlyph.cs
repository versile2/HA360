namespace Realm.Domain;

/// <summary>The glyphs a pin can show instead of a photo or an initial (0.2.1: a person's pin can show one too).</summary>
public enum VehicleGlyph
{
    /// <summary>The truck (the custom pickup silhouette; token <c>glyph:truck</c>).</summary>
    Pickup,

    /// <summary>The car (the default of a tracker).</summary>
    Car,

    /// <summary>A person.</summary>
    Person,

    /// <summary>A pet (a paw).</summary>
    Pet,

    /// <summary>A phone.</summary>
    Phone,

    /// <summary>A tag.</summary>
    Tag,
}
