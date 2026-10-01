namespace OneRGB.Domain;

/// <summary>
/// Capacidades declarativas (spec §8). A UI monta controles a partir delas, nunca a partir do fabricante.
/// </summary>
public enum Capability
{
    RgbLighting,
    PerLedLighting,
    PersistentLighting,
    FanControl,
    FanCurve,
    PumpCurve,
    TemperatureSensor,
    ClockTelemetry,
    GpuClockControl,
    MemoryClockControl,
    PowerLimitControl,
    VoltageCurve,
    DpiControl,
    PollingRate,
    Macro,
    KeyRemap,
    Actuation,
    RapidTrigger,
    OledDisplay,
    Battery,
    AudioEndpoint,
    AudioRouting,
    Equalizer,
    NoiseSuppression,
    SpatialAudio,
    MicGain,
    PolarPattern,
    Sidetone,
    ChatMix,
    DisplayBrightness,
    DisplayInput,
    MonitorIdentification,
    TouchMapping,
    CameraControl,
    PanTiltZoom,
    DiskHealth,
    FirmwareUpdate,
    DriverUpdate,
}

/// <summary>Classe de segurança de um comando (spec §232). Define confirmação, journal e rollback.</summary>
public enum CommandSafetyClass
{
    /// <summary>Só leitura. Nunca muda estado.</summary>
    ReadOnly,

    /// <summary>Muda estado volátil do dispositivo; some ao desligar.</summary>
    Volatile,

    /// <summary>Grava em memória não volátil do dispositivo (flash/EEPROM). Tem desgaste.</summary>
    Persistent,

    /// <summary>Afeta comportamento térmico/elétrico (fans, bomba, clocks, tensão). Exige watchdog.</summary>
    ThermalOrElectrical,

    /// <summary>Firmware ou driver. Exige verificação de origem e confirmação explícita.</summary>
    FirmwareOrDriver,
}
