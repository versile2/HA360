namespace Realm.Domain;

/// <summary>The phone sensors that phone-use detection reads (the signals table of 02 section 7.2; activity is not used).</summary>
public enum PhoneSignalKind
{
    /// <summary>binary_sensor.&lt;phone&gt;_interactive: the screen is on.</summary>
    Screen,

    /// <summary>binary_sensor.&lt;phone&gt;_device_locked.</summary>
    Locked,

    /// <summary>binary_sensor.&lt;phone&gt;_android_auto: the phone is connected to the car.</summary>
    AndroidAuto,
}
