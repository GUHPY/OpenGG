using OneRGB.Application;
using OneRGB.Application.Control;
namespace OneRGB.Hardware.Integrations;
internal sealed class HidAdapter(ControlKey key, ControlSpec spec, Func<bool> available, ControlArbiter arbiter,
    Func<CancellationToken, Task<ControlValue?>> read, Func<ControlValue, CancellationToken, Task>? write, string? locked) : IControlAdapter
{
    public ControlKey Key => key;

    public ControlSpec Spec => spec;

    public bool IsAvailable => available();

    public bool WriteEnabled => write is not null && locked is null;

    public string? WriteDisabledReason => write is null ? null : locked;

    public Task<ControlValue?> ReadAsync(CancellationToken cancellationToken) => read(cancellationToken);

    public Task WriteAsync(ControlValue value, Lease lease, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(lease);
        if (write is null || locked is not null)
        {
            throw new InvalidOperationException($"{spec.Label}: {locked ?? "reported by the device, not adjustable"}");
        }

        return lease.Resource != key.Resource || !arbiter.IsCurrent(lease)
            ? throw new InvalidOperationException("Another writer took control of the device before the write.")
            : write(value, cancellationToken);
    }
}

