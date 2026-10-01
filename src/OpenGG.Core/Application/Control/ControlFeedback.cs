namespace OneRGB.Application.Control;

public enum FeedbackTone
{
    Idle,
    Busy,
    Ok,
    Warning,
    Error,
}

/// <summary>
/// Estado da operação ao lado de cada controle (A27): pendente → aplicando → conferindo → conferido, ou o motivo
/// da falha. O texto sai do <see cref="ControlStatus"/>, então toda tela mostra o mesmo para o mesmo estado.
/// </summary>
public sealed record ControlFeedback(string Text, FeedbackTone Tone)
{
    public static ControlFeedback For(ControlStatus status, IFormatProvider provider)
    {
        ArgumentNullException.ThrowIfNull(status);
        string Show(ControlValue? value) => value?.Display(status.Spec, provider) ?? "—";

        switch (status.State)
        {
            case FeatureState.Disconnected:
                return new("Disconnected", FeedbackTone.Warning);
            case FeatureState.Unavailable:
                return new("Unavailable", FeedbackTone.Warning);
            case FeatureState.Applying:
                return new($"Applying {Show(status.Desired)}…", FeedbackTone.Busy);
            case FeatureState.Verification when status.AwaitingConfirmation:
                return new("Applied: confirm or it will revert", FeedbackTone.Warning);
            case FeatureState.Verification:
                return new("Checking the device…", FeedbackTone.Busy);
            case FeatureState.Recovering:
                return new("Restoring the previous setting…", FeedbackTone.Busy);
            case FeatureState.Error:
                return new(status.Message ?? "Error", FeedbackTone.Error);
        }

        var tolerance = status.Spec.Tolerance;
        if (status.Observed is { } observed && status.Applied is { } applied && !observed.Matches(applied, tolerance))
        {
            // Mudou fora do OneRGB (menu do monitor, app do fabricante): mostra o que o dispositivo tem agora.
            return new($"On device: {Show(observed)}", FeedbackTone.Warning);
        }

        var readOnly = status.ReadOnlyReason is not null || !status.Spec.Writable;
        if (status.Message is { } message && !(readOnly && message == status.ReadOnlyReason))
        {
            return new(message, FeedbackTone.Warning);
        }

        if (status.State == FeatureState.Applied && status.LastWrite is not null)
        {
            if (status.Temporary)
            {
                return new($"Temporary: the profile stores {Show(status.Persistent)}", FeedbackTone.Idle);
            }

            return status.Observed is not null
                ? new("Verified on the device", FeedbackTone.Ok)
                : new("Applied (without readback)", FeedbackTone.Ok);
        }

        return readOnly ? new("Read only", FeedbackTone.Idle) : new(string.Empty, FeedbackTone.Idle);
    }
}
