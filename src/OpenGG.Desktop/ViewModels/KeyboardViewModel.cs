using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Media;
using System.Windows.Threading;
using OneRGB.Application.Control;
using OneRGB.Application.Devices.Input;
using OneRGB.Application.Devices.Keyboard;
using OneRGB.Application.Presentation;
using OpenGG.Desktop.Mvvm;
using OpenGG.Desktop.Controls;
using Microsoft.Win32;

namespace OpenGG.Desktop.ViewModels;

public enum KeyboardTool { Selection, RapidTrigger, Protection }

/// <summary>Uma tecla no mapa: posição, o que ela tem de próprio e o que a prévia está mostrando.</summary>
public sealed class KeyViewModel(KeyCap cap, double unit) : ObservableObject
{
    private bool _selected;
    private string _actuationText = string.Empty;
    private bool _custom;
    private bool _rapid;
    private bool _protected;
    private string? _marker;
    private bool _active;

    public KeyCap Cap { get; } = cap;

    public string Id => Cap.Id;

    public string Label => Cap.Label;

    public double Left => Cap.X * unit;

    public double Top => Cap.Y * unit;

    public double Width => (Cap.Width * unit) - 4;

    public double Height => unit - 4;

    public bool IsSelected
    {
        get => _selected;
        set => Set(ref _selected, value);
    }

    public string ActuationText
    {
        get => _actuationText;
        private set => Set(ref _actuationText, value);
    }

    /// <summary>Atuação diferente da padrão.</summary>
    public bool HasCustom
    {
        get => _custom;
        private set => Set(ref _custom, value);
    }

    public bool RapidTrigger
    {
        get => _rapid;
        private set => Set(ref _rapid, value);
    }
    public bool Protected { get => _protected; private set => Set(ref _protected, value); }

    /// <summary>"2" (2-em-1), "R" (remapeada), "M" (só na Meta) ou nada.</summary>
    public string? Marker
    {
        get => _marker;
        private set => Set(ref _marker, value);
    }

    /// <summary>A prévia está com a saída desta tecla pressionada.</summary>
    public bool IsActive
    {
        get => _active;
        set => Set(ref _active, value);
    }

    internal void Show(KeyboardConfig config)
    {
        var device = config.Device;
        if (config.Actuation.TryGetValue(Id, out var mm))
        {
            ActuationText = mm.ToString("0.0", CultureInfo.InvariantCulture);
            HasCustom = Math.Abs(mm - device.DefaultActuation) > 1e-9;
        }
        else
        {
            ActuationText = string.Empty;
            HasCustom = false;
        }

        RapidTrigger = config.RapidTriggerEnabled && config.RapidTrigger.GetValueOrDefault(Id)?.Enabled == true;
        Protected = config.Protection.Enabled && config.Protection.Keys.Contains(Id, StringComparer.Ordinal);
        Marker = config.Dual.ContainsKey(Id) ? "2" : config.Remap.ContainsKey(Id) ? "R" : config.Meta.ContainsKey(Id) ? "M" : null;
    }
}

/// <summary>Um par do Rapid Tap (SOCD).</summary>
public sealed class RapidTapRow : ObservableObject
{
    private readonly KeyboardViewModel _owner;
    private string _first;
    private string _second;
    private RapidTapMode _mode;
    private bool _enabled;

    public RapidTapRow(KeyboardViewModel owner, RapidTapPair pair)
    {
        _owner = owner;
        _first = pair.First;
        _second = pair.Second;
        _mode = pair.Mode;
        _enabled = pair.Enabled;
    }

    public IReadOnlyList<ChoiceOption> KeyChoices => _owner.TapKeys;

    public IReadOnlyList<ChoiceOption> ModeChoices => KeyboardViewModel.TapModes;

    public string First
    {
        get => _first;
        set
        {
            if (value is not null && Set(ref _first, value))
            {
                _owner.PairsChanged();
            }
        }
    }

    public string Second
    {
        get => _second;
        set
        {
            if (value is not null && Set(ref _second, value))
            {
                _owner.PairsChanged();
            }
        }
    }

    public string Mode
    {
        get => _mode.ToString();
        set
        {
            if (Enum.TryParse<RapidTapMode>(value, out var mode) && mode != _mode)
            {
                _mode = mode;
                OnPropertyChanged();
                _owner.PairsChanged();
            }
        }
    }

    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (Set(ref _enabled, value))
            {
                _owner.PairsChanged();
            }
        }
    }

    public RapidTapPair ToPair() => new(_first, _second, _mode, _enabled);
}

/// <summary>Uma macro guardada (§539): nome e passos como texto, validados antes de salvar.</summary>
public sealed class MacroRow : ObservableObject
{
    private readonly KeyboardViewModel _owner;
    private string _name;
    private string _steps;
    private string? _error;
    private string _summary = string.Empty;

    public MacroRow(KeyboardViewModel owner, Macro macro)
    {
        _owner = owner;
        Id = macro.Id;
        _name = macro.Name;
        _steps = MacroText.Format(macro.Steps);
        Check();
    }

    public string Id { get; }

    public string Name
    {
        get => _name;
        set
        {
            if (Set(ref _name, value ?? string.Empty))
            {
                Check();
                _owner.MacrosEdited();
            }
        }
    }

    public string Steps
    {
        get => _steps;
        set
        {
            if (Set(ref _steps, value ?? string.Empty))
            {
                Check();
                _owner.MacrosEdited();
            }
        }
    }

    public string? Error
    {
        get => _error;
        private set => Set(ref _error, value);
    }

    public string Summary
    {
        get => _summary;
        private set => Set(ref _summary, value);
    }

    public Macro? ToMacro() =>
        MacroText.TryParse(_steps, out var steps, out _) ? new Macro { Id = Id, Name = _name.Trim(), Steps = steps } : null;

    private void Check()
    {
        if (!MacroText.TryParse(_steps, out var steps, out var error))
        {
            Error = error;
            Summary = string.Empty;
            return;
        }

        var macro = new Macro { Id = Id, Name = _name.Trim(), Steps = steps };
        Error = Macros.Validate(macro) is [var first, ..] ? first : null;
        Summary = string.Create(CultureInfo.GetCultureInfo("en-US"), $"{steps.Count} step(s) · {Macros.Duration(macro).TotalMilliseconds:0} ms");
    }
}

/// <summary>
/// Teclado analógico (A5, A6.6): atuação e Rapid Trigger por tecla, Rapid Tap, 2-em-1, remapeamento, camada Meta,
/// Protection Mode, presets e macros, sobre o modelo do Apex Pro TKL Gen 3. A prévia usa o
/// <see cref="AnalogKeySimulator"/>. Atuação, Rapid Trigger e OLED vão pelo HID; os perfis internos têm gravação explícita.
/// </summary>
public sealed class KeyboardViewModel : DeviceEditorViewModel
{
    public const double Unit = 46;
    private static readonly CultureInfo En = CultureInfo.GetCultureInfo("en-US");
    private KeyboardConfig _config;
    private KeyboardTool _tool;
    private bool _selectionSync;
    private double _selectedProtection = 20;
    private string _toolsTab = "Presets";
    private OledFrame? _uploadedOled;
    private OledFrame? _oledFrame;
    private string? _oledFileName;
    private readonly DispatcherTimer _oledUpdate;
    private bool _oledSending, _oledAgain;
    private string? _onboardError;
    private AnalogKeySimulator _simulator;
    private double _selectedActuation;
    private bool _selectedRapid;
    private double _selectedPress;
    private double _selectedRelease;
    private string _protectedKeys = string.Empty;
    private string? _protectedError;
    private double _previewDepth;
    private string _previewText = "Drag the depth slider to preview selected key output.";
    private ImageSource? _oled;
    private bool _oledOnKeyboard;
    private string? _oledMessage;
    private bool _directOled;
    private bool _macrosDirty;
    private string? _macroStatus;
    private int _onboardSlot = 2;
    private bool _onboardBusy;
    private byte[]? _onboardProfile;
    private KeyboardConfig? _onboardBaseline;
    private string _onboardStatus = "Read an onboard profile before editing. Save to keyboard stores it permanently.";

    public KeyboardViewModel(AppHost host, MainViewModel main, Dispatcher dispatcher)
        : base(host, main, dispatcher, AnalogKeyboardDescriptor.ApexProTklGen3.DeviceId, AnalogKeyboardDescriptor.ApexProTklGen3.Name, "Keyboard")
    {
        _config = KeyboardConfig.Defaults(Device);
        _simulator = new AnalogKeySimulator(_config);
        _selectedActuation = Device.DefaultActuation;
        _selectedPress = _selectedRelease = Device.DefaultSensitivity;
        _oledUpdate = new DispatcherTimer(TimeSpan.FromMilliseconds(200), DispatcherPriority.Background,
            async (_, _) => { _oledUpdate!.Stop(); await PushOledAsync(); }, dispatcher);
        _oledUpdate.Stop();
        foreach (var cap in Device.Layout)
        {
            Keys.Add(new KeyViewModel(cap, Unit));
        }

        TapKeys = [.. Device.With(KeyCapabilities.RapidTapParticipant).Select(k => new ChoiceOption(k.Code, k.Label))];
        foreach (var macro in host.Settings.Macros)
        {
            MacroRows.Add(new MacroRow(this, macro));
        }

        ToggleKeyCommand = new RelayCommand(p =>
        {
            if (p is KeyViewModel k)
            {
                PickKey(k);
            }
        });
        SelectWasdCommand = new RelayCommand(() => Select(k => KeyboardLayout.Wasd.Contains(k.Id)));
        SelectAllCommand = new RelayCommand(() => Select(_ => true));
        ClearSelectionCommand = new RelayCommand(() => Select(_ => false));
        ApplyActuationCommand = new RelayCommand(() => Change(c => c.SetActuation(SelectedCodes, _selectedActuation)), () => SelectedCount > 0);
        ResetActuationCommand = new RelayCommand(() => Change(c => c.SetActuation(SelectedCodes, Device.DefaultActuation)), () => SelectedCount > 0);
        ApplyRapidCommand = new RelayCommand(() => Change(c => c.SetRapidTrigger(SelectedCodes, _selectedRapid, _selectedPress, _selectedRelease)), () => SelectedCount > 0);
        ApplyPresetCommand = new RelayCommand(p =>
        {
            if (p is KeyboardPreset preset)
            {
                Change(preset.Apply);
                Status = $"Preset \"{preset.Name}\" applied.";
            }
        });
        AddPairCommand = new RelayCommand(AddPair, () => RapidTap.Count < Device.MaximumRapidTapPairs && TapKeys.Count >= 2);
        RemovePairCommand = new RelayCommand(p =>
        {
            if (p is RapidTapRow row)
            {
                RapidTap.Remove(row);
                PairsChanged();
            }
        });
        NewMacroCommand = new RelayCommand(NewMacro);
        RemoveMacroCommand = new RelayCommand(p =>
        {
            if (p is MacroRow row)
            {
                MacroRows.Remove(row);
                MacrosEdited();
            }
        });
        SaveMacrosCommand = new RelayCommand(SaveMacros, () => _macrosDirty && MacroRows.All(m => m.Error is null));
        ReadOnboardCommand = new RelayCommand(async () => await ReadOnboardAsync().ConfigureAwait(true), () => !_onboardBusy && !Busy);
        SaveOnboardCommand = new RelayCommand(async () => await SaveOnboardAsync().ConfigureAwait(true),
            () => !_onboardBusy && !Busy && _onboardProfile is not null && !HasErrors && Host.ApexKeyboard.LoadedSlot == OnboardSlot);
        SetToolCommand = new RelayCommand(p =>
        {
            if (p is KeyboardTool tool) { Tool = Tool == tool ? KeyboardTool.Selection : tool; }
        });
        SetToolsTabCommand = new RelayCommand(p => { if (p is "Presets" or "Rapid Tap" or "Macros") ToolsTab = (string)p; });
        OpenOledFileCommand = new RelayCommand(OpenOledFile);
        UseStatusOledCommand = new RelayCommand(() => { _uploadedOled = null; OledFileName = null; UpdateOledPreview(); });
        Load();
    }

    public IReadOnlyList<int> OnboardSlots { get; } = [1, 2, 3, 4, 5];
    public override string WriteTitle => Host.ApexKeyboard.WriteEnabled ? "Live and onboard profile" : "Read the onboard profile before editing";
    public override string WriteText => Host.ApexKeyboard.WriteDisabledReason
        ?? "Actuation and Rapid Trigger apply live. Save to keyboard stores the onboard profile with backup and verification.";
    public RelayCommand ReadOnboardCommand { get; }
    public RelayCommand SaveOnboardCommand { get; }
    public string OnboardStatus { get => _onboardStatus; private set => Set(ref _onboardStatus, value); }
    public bool OnboardIdle => !_onboardBusy;
    public string? OnboardError { get => _onboardError; private set => Set(ref _onboardError, value); }
    public int OnboardSlot
    {
        get => _onboardSlot;
        set
        {
            if (value is < 1 or > 5 || !Set(ref _onboardSlot, value)) { return; }
            _onboardProfile = null;
            _onboardBaseline = null;
            OnboardStatus = "Click Read and activate to load this onboard profile.";
        }
    }

    private async Task ReadOnboardAsync()
    {
        _onboardBusy = true;
        OnboardError = null;
        OnPropertyChanged(nameof(OnboardIdle));
        OnboardStatus = "Reading and activating the onboard profile…";
        try
        {
            var profile = await Host.ApexKeyboard.ReadAndLoadProfileAsync(OnboardSlot, CancellationToken.None).ConfigureAwait(true);
            Host.Controls.InvalidateApplied(Device.ActuationResource);
            _onboardProfile = profile;
            _onboardBaseline = ApexProfile.ReadConfig(profile);
            var values = _onboardBaseline.ToValues();
            ImportValues(key => values.GetValueOrDefault(key));
            OnPropertyChanged(nameof(WriteTitle));
            OnPropertyChanged(nameof(WriteText));
            OnboardStatus = $"Profile {OnboardSlot}: {ApexProfile.Name(profile)}. CRC verified; ready to edit.";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _onboardProfile = null;
            _onboardBaseline = null;
            OnboardStatus = ex.Message;
            OnboardError = ex.Message;
        }
        finally
        {
            _onboardBusy = false;
            OnPropertyChanged(nameof(OnboardIdle));
            System.Windows.Input.CommandManager.InvalidateRequerySuggested();
        }
    }

    private async Task SaveOnboardAsync()
    {
        if (_onboardProfile is null || _onboardBaseline is null) { return; }
        _onboardBusy = true;
        OnboardError = null;
        OnPropertyChanged(nameof(OnboardIdle));
        OnboardStatus = "Writing to the keyboard and receiver…";
        try
        {
            var profile = ApexProfile.Patch(_onboardProfile, _onboardBaseline, _config);
            if (profile.AsSpan().SequenceEqual(_onboardProfile))
            {
                OnboardStatus = "The onboard profile already contains these settings; no write is needed.";
                return;
            }
            var backup = await Host.ApexKeyboard.SaveProfileAsync(OnboardSlot, _onboardProfile, profile, CancellationToken.None).ConfigureAwait(true);
            Host.Controls.InvalidateApplied(Device.ActuationResource);
            _onboardProfile = profile;
            _onboardBaseline = ApexProfile.ReadConfig(profile);
            OnboardStatus = $"Profile {OnboardSlot} written and read back byte for byte. Backup: {backup}";
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { OnboardStatus = ex.Message; OnboardError = ex.Message; }
        finally
        {
            _onboardBusy = false;
            OnPropertyChanged(nameof(OnboardIdle));
            System.Windows.Input.CommandManager.InvalidateRequerySuggested();
        }
    }

    public static IReadOnlyList<ChoiceOption> TapModes { get; } =
        [.. Enum.GetValues<RapidTapMode>().Select(m => new ChoiceOption(m.ToString(), RapidTapPair.ModeLabel(m)))];

    public static IReadOnlyList<ChoiceOption> ProtectionActivations { get; } =
    [
        new(nameof(ProtectionActivation.WhileProtectedKeyHeld), "While a protected key is held"),
        new(nameof(ProtectionActivation.Always), "Always while the profile is active"),
    ];

    public AnalogKeyboardDescriptor Device { get; } = AnalogKeyboardDescriptor.ApexProTklGen3;

    public IReadOnlyList<ChoiceOption> ProtectionModes => ProtectionActivations;

    public string Name => Device.Name;

    public string Source => Device.Source;

    public ObservableCollection<KeyViewModel> Keys { get; } = [];

    public double BoardWidth => (Keys.Max(k => k.Cap.X + k.Cap.Width) * Unit) + 4;

    public double BoardHeight => (Keys.Max(k => k.Cap.Y) + 1) * Unit + 4;

    public IReadOnlyList<ChoiceOption> TapKeys { get; }

    public IReadOnlyList<KeyboardPreset> Presets => KeyboardConfig.Presets;

    public ObservableCollection<RapidTapRow> RapidTap { get; } = [];

    public ObservableCollection<MacroRow> MacroRows { get; } = [];

    public double ActuationMinimum => Device.Actuation.Minimum;

    public double ActuationMaximum => Device.Actuation.Maximum;

    public bool RapidTriggerOnKeyboard
    {
        get => _config.RapidTriggerEnabled;
        set
        {
            if (_config.RapidTriggerEnabled == value)
            {
                return;
            }

            _config.RapidTriggerEnabled = value;
            OnPropertyChanged();
            Edited();
        }
    }

    public double ActuationStep => Device.Actuation.Step;

    public double SensitivityMinimum => Device.Sensitivity.Minimum;

    public double SensitivityMaximum => Device.Sensitivity.Maximum;

    public bool SeparateSensitivities => Device.SeparateSensitivities;

    public string RangeText => $"Actuation {Device.Millimeters(Device.Actuation.Minimum, En)}–{Device.Millimeters(Device.Actuation.Maximum, En)} · default {Device.Millimeters(Device.DefaultActuation, En)} · one Rapid Trigger value per key · up to {Device.MaximumRapidTapPairs} Rapid Tap pairs";

    public RelayCommand ToggleKeyCommand { get; }

    public RelayCommand SelectWasdCommand { get; }

    public RelayCommand SelectAllCommand { get; }

    public RelayCommand ClearSelectionCommand { get; }

    public RelayCommand SetToolCommand { get; }
    public RelayCommand SetToolsTabCommand { get; }
    public RelayCommand OpenOledFileCommand { get; }
    public RelayCommand UseStatusOledCommand { get; }
    public string ToolsTab { get => _toolsTab; set { if (value is "Presets" or "Rapid Tap" or "Macros") Set(ref _toolsTab, value); } }
    public KeyboardTool Tool
    {
        get => _tool;
        private set { if (Set(ref _tool, value)) { RaiseTools(); } }
    }
    public bool IsRapidTool => Tool == KeyboardTool.RapidTrigger;
    public bool IsProtectionTool => Tool == KeyboardTool.Protection;
    public bool RapidSensitivityEnabled => IsRapidTool || Keys.Any(k => k.IsSelected && k.RapidTrigger);
    public bool ProtectionSensitivityEnabled => IsProtectionTool || Keys.Any(k => k.IsSelected && k.Protected);
    public double SelectedProtection
    {
        get => _selectedProtection;
        set
        {
            if (!Set(ref _selectedProtection, Math.Round(Math.Clamp(value, 0, 255))) || _selectionSync) return;
            var codes = SelectedCodes.Where(c => _config.Protection.Enabled && _config.Protection.Keys.Contains(c)).ToArray();
            if (codes.Length > 0) Change(c => { foreach (var code in codes) c.ProtectionSensitivities[code] = _selectedProtection; });
        }
    }

    public RelayCommand ApplyActuationCommand { get; }

    public RelayCommand ResetActuationCommand { get; }

    public RelayCommand ApplyRapidCommand { get; }

    public RelayCommand ApplyPresetCommand { get; }

    public RelayCommand AddPairCommand { get; }

    public RelayCommand RemovePairCommand { get; }

    public RelayCommand NewMacroCommand { get; }

    public RelayCommand RemoveMacroCommand { get; }

    public RelayCommand SaveMacrosCommand { get; }

    public int SelectedCount => Keys.Count(k => k.IsSelected);

    public bool HasSelection => SelectedCount > 0;

    public string SelectionAccent => IsRapidTool ? "#F59E0B" : IsProtectionTool ? "#60A5FA" : "#F4F4F6";
    public string SelectionText => Tool switch
    {
        KeyboardTool.RapidTrigger => $"Click keys to enable Rapid Trigger{(SelectedCount > 0 ? $" · {SelectedCount} selected" : string.Empty)}",
        KeyboardTool.Protection => $"Click keys to enable Protection Mode{(SelectedCount > 0 ? $" · {SelectedCount} selected" : string.Empty)}",
        _ => SelectedCount switch
    {
        0 => "Click keys to select them",
        1 => $"1 key selected ({Keys.First(k => k.IsSelected).Label})",
        var n => $"{n} keys selected",
    },
    };

    public int CustomCount => Keys.Count(k => k.HasCustom);

    public double SelectedActuation
    {
        get => _selectedActuation;
        set { if (Set(ref _selectedActuation, Device.Actuation.Snap(value)) && !_selectionSync && HasSelection) Change(c => c.SetActuation(SelectedCodes, _selectedActuation)); }
    }

    public bool SelectedRapid
    {
        get => _selectedRapid;
        set => Set(ref _selectedRapid, value);
    }

    public double SelectedPress
    {
        get => _selectedPress;
        set
        {
            if (!Set(ref _selectedPress, Device.Sensitivity.Snap(value)) || _selectionSync) return;
            var codes = SelectedCodes.Where(c => _config.RapidTriggerEnabled && _config.RapidTrigger.GetValueOrDefault(c)?.Enabled == true).ToArray();
            if (codes.Length > 0) Change(c => c.SetRapidTrigger(codes, true, _selectedPress, _selectedPress));
        }
    }

    public double SelectedRelease
    {
        get => _selectedRelease;
        set => Set(ref _selectedRelease, Device.Sensitivity.Snap(value));
    }

    /// <summary>A tecla em edição individual (remapear, Meta, 2-em-1): só com exatamente uma selecionada.</summary>
    public KeySwitch? Focus => SelectedCount == 1 ? Device.Key(Keys.First(k => k.IsSelected).Id) : null;

    public bool HasFocus => Focus is not null;

    public string FocusTitle => Focus is { } key ? $"Key {key.Label}" : "Select one key to edit its action, dual action or Meta layer.";

    public bool CanRemap => Focus?.Has(KeyCapabilities.Remappable) == true;

    public bool CanMeta => Device.HasMetaLayer && Focus?.Has(KeyCapabilities.MetaLayerAssignable) == true;

    public bool CanDual => Focus?.Has(KeyCapabilities.DualAction) == true;

    /// <summary>Atribuições para a tecla em foco: as prontas, as macros guardadas, cada tecla do layout e a atual.</summary>
    public IReadOnlyList<ChoiceOption> ActionChoices { get; private set; } = [];

    public string RemapAction
    {
        get => Focus is { } key ? _config.ActionOf(key.Code).Encode() : InputAction.Default.Encode();
        set => SetAction(_config.Remap, value);
    }

    public string MetaAction
    {
        get => Focus is { } key ? (_config.Meta.GetValueOrDefault(key.Code) ?? InputAction.Default).Encode() : InputAction.Default.Encode();
        set => SetAction(_config.Meta, value);
    }

    public bool DualEnabled
    {
        get => Focus is { } key && _config.Dual.ContainsKey(key.Code);
        set
        {
            if (Focus is not { } key || value == DualEnabled)
            {
                return;
            }

            if (value)
            {
                _config.Dual[key.Code] = new DualAction(Device.Actuation.Maximum - 0.4, InputAction.Keys("ShiftLeft"));
            }
            else
            {
                _config.Dual.Remove(key.Code);
            }

            FocusChanged();
            Edited();
        }
    }

    public double DualDeep
    {
        get => Focus is { } key && _config.Dual.TryGetValue(key.Code, out var dual) ? dual.DeepMm : Device.Actuation.Maximum - 0.4;
        set => UpdateDual(d => d with { DeepMm = Device.Actuation.Snap(value) });
    }

    public string DualSecond
    {
        get => Focus is { } key && _config.Dual.TryGetValue(key.Code, out var dual) ? dual.Second.Encode() : InputAction.Default.Encode();
        set
        {
            if (InputAction.TryParse(value, out var action, out _))
            {
                UpdateDual(d => d with { Second = action });
            }
        }
    }

    public bool DualKeepFirst
    {
        get => Focus is { } key && _config.Dual.TryGetValue(key.Code, out var dual) && dual.KeepFirst;
        set => UpdateDual(d => d with { KeepFirst = value });
    }

    public bool ProtectionEnabled
    {
        get => _config.Protection.Enabled;
        set
        {
            if (value != _config.Protection.Enabled)
            {
                _config.Protection = _config.Protection with { Enabled = value };
                Edited();
            }
        }
    }

    /// <summary>Teclas protegidas (códigos ou rótulos, separados por vírgula).</summary>
    public string ProtectedKeys
    {
        get => _protectedKeys;
        set
        {
            if (!Set(ref _protectedKeys, value ?? string.Empty))
            {
                return;
            }

            var codes = KeyboardConfig.SplitCodes(_protectedKeys).Select(c => KeyboardLayout.Resolve(c) ?? c).ToList();
            _protectedError = KeyboardRules.CheckProtectedKeys(Device, string.Join(',', codes));
            if (_protectedError is null)
            {
                _config.Protection = _config.Protection with { Keys = codes };
            }

            Edited();
        }
    }

    public double ProtectionReduction
    {
        get => _config.Protection.ReductionMm;
        set
        {
            var mm = Device.ProtectionReduction.Snap(value);
            if (Math.Abs(mm - _config.Protection.ReductionMm) > 1e-9)
            {
                _config.Protection = _config.Protection with { ReductionMm = mm };
                OnPropertyChanged();
                Edited();
            }
        }
    }

    public double ReductionMinimum => Device.ProtectionReduction.Minimum;

    public double ReductionMaximum => Device.ProtectionReduction.Maximum;

    public string ProtectionActivationMode
    {
        get => _config.Protection.Activation.ToString();
        set
        {
            if (Enum.TryParse<ProtectionActivation>(value, out var mode) && mode != _config.Protection.Activation)
            {
                _config.Protection = _config.Protection with { Activation = mode };
                Edited();
            }
        }
    }

    public string AffectedText => KeyboardRules.AffectedKeys(_config) is { Count: > 0 } keys
        ? $"Hardens {string.Join(", ", keys.Select(k => Device.Key(k)?.Label ?? k))}"
        : "No neighboring keys affected.";

    /// <summary>Preview depth (mm) aplicada às teclas selecionadas.</summary>
    public double PreviewDepth
    {
        get => _previewDepth;
        set
        {
            if (Set(ref _previewDepth, Math.Round(Math.Clamp(value, 0, Device.Actuation.Maximum), 2)))
            {
                StepPreview();
            }
        }
    }

    public string PreviewText
    {
        get => _previewText;
        private set => Set(ref _previewText, value);
    }

    public bool HasOled => Device.Oled is not null;

    public ImageSource? OledPreview
    {
        get => _oled;
        private set => Set(ref _oled, value);
    }

    /// <summary>Prévia temporária na OLED: HID direto ou ponte GameSense; desligar devolve a imagem de fundo.</summary>
    public bool OledOnKeyboard
    {
        get => _oledOnKeyboard;
        set
        {
            if (Set(ref _oledOnKeyboard, value))
            {
                _ = value ? PushOledAsync() : StopOledAsync();
            }
        }
    }

    public string? OledMessage
    {
        get => _oledMessage;
        private set => Set(ref _oledMessage, value);
    }
    public string? OledFileName { get => _oledFileName; private set => Set(ref _oledFileName, value); }

    public string? MacroStatus
    {
        get => _macroStatus;
        private set => Set(ref _macroStatus, value);
    }

    private IReadOnlyList<string> SelectedCodes => [.. Keys.Where(k => k.IsSelected).Select(k => k.Id)];

    internal void PairsChanged()
    {
        _config.RapidTapPairs.Clear();
        _config.RapidTapPairs.AddRange(RapidTap.Select(r => r.ToPair()));
        Edited();
    }

    internal void MacrosEdited()
    {
        _macrosDirty = true;
        MacroStatus = null;
        System.Windows.Input.CommandManager.InvalidateRequerySuggested();
    }

    protected override void Fill(Func<ControlKey, ControlValue?> valueOf)
    {
        _config = KeyboardConfig.From(Device, valueOf);
        RapidTap.Clear();
        foreach (var pair in _config.RapidTapPairs)
        {
            RapidTap.Add(new RapidTapRow(this, pair));
        }

        ProtectedKeys = string.Join(", ", _config.Protection.Keys.Select(c => Device.Key(c)?.Label ?? c));
        _protectedError = null;
        OnPropertyChanged(nameof(RapidTriggerOnKeyboard));
        OnPropertyChanged(nameof(ProtectionEnabled));
        OnPropertyChanged(nameof(ProtectionReduction));
        OnPropertyChanged(nameof(ProtectionActivationMode));
        SelectionChanged();
    }

    protected override IReadOnlyDictionary<ControlKey, ControlValue> Values() => _config.ToValues();

    protected override IEnumerable<ConfigIssue> Validate()
    {
        var issues = KeyboardRules.Validate(_config, Context())
            .Select(c => new ConfigIssue(c.Message, c.Severity == ConflictSeverity.Error)).ToList();
        if (_protectedError is not null)
        {
            issues.Insert(0, new ConfigIssue($"Protected keys: {_protectedError}", IsError: true));
        }

        return issues;
    }

    protected override void Validated()
    {
        OnPropertyChanged(nameof(RapidTriggerOnKeyboard));
        foreach (var key in Keys)
        {
            key.Show(_config);
        }

        OnPropertyChanged(nameof(CustomCount));
        OnPropertyChanged(nameof(AffectedText));
        RaiseTools();
        _simulator = new AnalogKeySimulator(_config);
        StepPreview();
    }

    private async Task PushOledAsync()
    {
        if (_oledSending) { _oledAgain = true; return; }
        _oledSending = true;
        try
        {
            do
            {
                _oledAgain = false;
                if (!OledOnKeyboard || _oledFrame is null) return;
                await Host.ApexKeyboard.SendOledAsync(_oledFrame.ToRowMajorMsbFirst(), CancellationToken.None).ConfigureAwait(true);
                _directOled = true;
                OledMessage = OledFileName is null ? "OLED synchronized." : OledFileName + " · 128 × 40";
            } while (_oledAgain);
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { OledMessage = ex.Message; }
        finally { _oledSending = false; }
    }

    private async Task StopOledAsync()
    {
        _oledUpdate.Stop();
        // A send already in flight must be followed by the reset, even before its ACK arrives.
        if (!_directOled && !_oledSending) return;
        try
        {
            await Host.ApexKeyboard.ResetOledAsync(CancellationToken.None).ConfigureAwait(true);
            _directOled = false;
            OledMessage = "OLED background restored.";
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { OledMessage = ex.Message; }
    }

    private InputActionContext Context()
    {
        var ids = MacroRows.Select(m => m.Id).ToHashSet(StringComparer.Ordinal);
        return new InputActionContext { MacroExists = ids.Contains, AllowLayer = Device.HasMetaLayer };
    }

    private void Change(Action<KeyboardConfig> change)
    {
        change(_config);
        FocusChanged();
        Edited();
    }

    private void Select(Func<KeyViewModel, bool> predicate)
    {
        if (Tool != KeyboardTool.Selection)
        {
            var targets = Keys.Where(predicate).ToArray();
            foreach (var key in Keys) key.IsSelected = false;
            PaintKeys(targets.Length == 0 ? Keys : targets, targets.Length > 0);
            return;
        }
        foreach (var k in Keys)
        {
            k.IsSelected = predicate(k);
        }

        SelectionChanged();
    }

    private void SelectionChanged()
    {
        _selectionSync = true;
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(SelectionText));
        if (Keys.FirstOrDefault(k => k.IsSelected && _config.Actuation.ContainsKey(k.Id)) is { } first)
        {
            SelectedActuation = _config.Actuation.GetValueOrDefault(first.Id, Device.DefaultActuation);
            var protectedKey = Keys.FirstOrDefault(k => k.IsSelected && k.Protected) ?? first;
            SelectedProtection = _config.ProtectionSensitivities.GetValueOrDefault(protectedKey.Id, 20);
            var rapidKey = Keys.FirstOrDefault(k => k.IsSelected && k.RapidTrigger) ?? first;
            if (_config.RapidTrigger.TryGetValue(rapidKey.Id, out var rt))
            {
                SelectedRapid = rt.Enabled;
                SelectedPress = rt.PressMm;
                SelectedRelease = rt.ReleaseMm;
            }
        }
        _selectionSync = false;
        RaiseTools();

        FocusChanged();
        _previewDepth = 0;
        OnPropertyChanged(nameof(PreviewDepth));
        _simulator = new AnalogKeySimulator(_config);
        StepPreview();
        System.Windows.Input.CommandManager.InvalidateRequerySuggested();
    }

    private void FocusChanged()
    {
        var choices = InputActions.Common(new InputActionContext { AllowLayer = Device.HasMetaLayer }).ToList();
        choices.AddRange(MacroRows.Select(m => new ChoiceOption(InputAction.RunMacro(m.Id).Encode(), $"Macro {m.Name}")));
        choices.AddRange(Device.Layout.Select(k => new ChoiceOption(InputAction.Keys(k.Id).Encode(), $"Key {k.Label}")));
        if (Focus is { } key)
        {
            foreach (var current in new[] { _config.ActionOf(key.Code), _config.Meta.GetValueOrDefault(key.Code), _config.Dual.GetValueOrDefault(key.Code)?.Second })
            {
                if (current is not null && choices.All(c => c.Value != current.Encode()))
                {
                    choices.Add(new ChoiceOption(current.Encode(), current.Describe()));
                }
            }
        }

        ActionChoices = choices;
        foreach (var name in new[]
        {
            nameof(ActionChoices), nameof(Focus), nameof(HasFocus), nameof(FocusTitle), nameof(CanRemap), nameof(CanMeta), nameof(CanDual),
            nameof(RemapAction), nameof(MetaAction), nameof(DualEnabled), nameof(DualDeep), nameof(DualSecond), nameof(DualKeepFirst),
        })
        {
            OnPropertyChanged(name);
        }
    }

    private void SetAction(Dictionary<string, InputAction> map, string? value)
    {
        if (Focus is not { } key || !InputAction.TryParse(value, out var action, out _))
        {
            return;
        }

        var current = map.GetValueOrDefault(key.Code) ?? InputAction.Default;
        if (current == action)
        {
            return;
        }

        if (action.Kind == InputActionKind.Default)
        {
            map.Remove(key.Code);
        }
        else
        {
            map[key.Code] = action;
        }

        FocusChanged();
        Edited();
    }

    private void UpdateDual(Func<DualAction, DualAction> change)
    {
        if (Focus is not { } key || !_config.Dual.TryGetValue(key.Code, out var dual))
        {
            return;
        }

        var next = change(dual);
        if (next != dual)
        {
            _config.Dual[key.Code] = next;
            FocusChanged();
            Edited();
        }
    }

    private void AddPair()
    {
        var used = RapidTap.SelectMany(r => new[] { r.First, r.Second }).ToHashSet(StringComparer.Ordinal);
        var free = TapKeys.Select(k => k.Value).Where(k => !used.Contains(k)).ToList();
        var pair = free.Contains("KeyA") && free.Contains("KeyD") ? new RapidTapPair("KeyA", "KeyD")
            : free.Count >= 2 ? new RapidTapPair(free[0], free[1])
            : new RapidTapPair(TapKeys[0].Value, TapKeys[1].Value);
        RapidTap.Add(new RapidTapRow(this, pair));
        PairsChanged();
    }

    private void StepPreview()
    {
        var selected = SelectedCodes;
        if (selected.Count == 0)
        {
            foreach (var key in Keys) key.IsActive = false;
            PreviewText = "Select keys and drag the depth slider to preview their output.";
            UpdateOledPreview();
            return;
        }

        var depths = selected.ToDictionary(c => c, _ => _previewDepth, StringComparer.Ordinal);
        _simulator.Step(depths);
        var active = _simulator.Active;
        foreach (var key in Keys)
        {
            key.IsActive = active.Contains(key.Id);
        }

        var outputs = active.Select(o => o.EndsWith(AnalogKeySimulator.DeepSuffix, StringComparison.Ordinal)
            ? $"{Device.Key(o[..^AnalogKeySimulator.DeepSuffix.Length])?.Label} (deep: {_config.Dual.GetValueOrDefault(o[..^AnalogKeySimulator.DeepSuffix.Length])?.Second.Describe()})"
            : _config.ActionOf(o) is { Kind: not InputActionKind.Default } remap ? $"{Device.Key(o)?.Label} → {remap.Describe()}" : Device.Key(o)?.Label ?? o).ToList();
        var depth = Device.Millimeters(_previewDepth, En);
        PreviewText = (outputs.Count == 0 ? $"At {depth}: no output." : $"At {depth}: sends {string.Join(", ", outputs)}.")
            + (_simulator.ProtectionActive ? " Protection Mode active." : string.Empty);
        UpdateOledPreview();
    }

    private void RaiseTools()
    {
        foreach (var name in new[] { nameof(IsRapidTool), nameof(IsProtectionTool), nameof(RapidSensitivityEnabled), nameof(ProtectionSensitivityEnabled), nameof(SelectionText), nameof(SelectionAccent) })
            OnPropertyChanged(name);
    }

    private void PickKey(KeyViewModel key)
    {
        if (Tool == KeyboardTool.Selection) { key.IsSelected = !key.IsSelected; SelectionChanged(); return; }
        PaintKeys([key], Tool == KeyboardTool.RapidTrigger ? !key.RapidTrigger : !key.Protected);
    }

    private void PaintKeys(IEnumerable<KeyViewModel> keys, bool enabled)
    {
        var capability = Tool == KeyboardTool.RapidTrigger ? KeyCapabilities.RapidTrigger : KeyCapabilities.ActuationAdjustable;
        var targets = keys.Where(k => Device.Key(k.Id)?.Has(capability) == true).ToArray();
        if (targets.Length == 0) { SelectionChanged(); return; }
        if (Tool == KeyboardTool.RapidTrigger)
        {
            if (!_config.RapidTriggerEnabled)
                foreach (var code in _config.RapidTrigger.Keys) _config.RapidTrigger[code] = _config.RapidTrigger[code] with { Enabled = false };
            _config.SetRapidTrigger(targets.Select(k => k.Id), enabled, _selectedPress, _selectedPress);
            _config.RapidTriggerEnabled = _config.RapidTrigger.Values.Any(r => r.Enabled);
        }
        else
        {
            var codes = _config.Protection.Enabled ? _config.Protection.Keys.ToList() : [];
            foreach (var key in targets)
            {
                codes.Remove(key.Id);
                if (enabled) { codes.Add(key.Id); _config.ProtectionSensitivities[key.Id] = _selectedProtection; }
            }
            _config.Protection = _config.Protection with { Enabled = codes.Count > 0, Keys = codes };
        }
        foreach (var key in targets) key.IsSelected = enabled;
        Edited();
        SelectionChanged();
    }

    private void OpenOledFile()
    {
        var dialog = new OpenFileDialog { Title = "OLED — Open File", CheckFileExists = true,
            Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff|All files|*.*" };
        if (dialog.ShowDialog(System.Windows.Application.Current.MainWindow) != true) return;
        try
        {
            _uploadedOled = OledImage.Load(dialog.FileName);
            OledFileName = System.IO.Path.GetFileName(dialog.FileName);
            UpdateOledPreview();
            OledOnKeyboard = true;
        }
        catch (Exception ex) { OledMessage = $"Could not open the image: {ex.Message}"; }
    }

    private void UpdateOledPreview()
    {
        if (Device.Oled is not { } panel) return;
        _oledFrame = _uploadedOled ?? (HasSelection && PreviewDepth > 0
            ? OledScreens.Lines(panel, ["OpenGG", $"DEPTH {PreviewDepth:0.00} MM", _simulator.Active.Count > 0 ? "ACTIVE: " + string.Join(' ', _simulator.Active.Select(c => Device.Key(c)?.Label ?? c)) : "NO INPUT", $"RT {(_config.RapidTriggerEnabled ? "ON" : "OFF")}  PROT {(_simulator.ProtectionActive ? "ON" : "OFF")}"])
            : OledScreens.Status(panel, OledStatus.From("OpenGG", _config), En));
        OledPreview = OledImage.Render(_oledFrame);
        if (OledOnKeyboard) { _oledUpdate.Stop(); _oledUpdate.Start(); }
    }

    public void StopUpdates() { _oledUpdate.Stop(); _oledOnKeyboard = false; _oledAgain = false; }

    private void NewMacro()
    {
        var n = 1;
        while (MacroRows.Any(m => m.Id == $"macro{n}"))
        {
            n++;
        }

        MacroRows.Add(new MacroRow(this, new Macro { Id = $"macro{n}", Name = $"Macro {n}", Steps = [MacroStep.Tap("KeyE"), MacroStep.Wait(50), MacroStep.Tap("KeyR")] }));
        MacrosEdited();
    }

    private void SaveMacros()
    {
        var macros = MacroRows.Select(m => m.ToMacro()).OfType<Macro>().ToList();
        Host.Update(s => s with { Macros = macros });
        _macrosDirty = false;
        MacroStatus = $"{macros.Count} macro(s) saved.";
        FocusChanged();
        Revalidate();
    }
}
