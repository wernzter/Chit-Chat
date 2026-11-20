using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ChitChat.Platform.Windows;

internal interface IFocusedEditObservation
{
    bool IsAvailable { get; }
    bool Prepare(string inserted);
    void Complete(bool delivered);
    bool TryRead(string inserted, out string? edited);
}

internal sealed class AutomationEditObservation(uint owner, Func<bool> hasFocus, bool watchEdits) : IFocusedEditObservation
{
    private static readonly TimeSpan SnapshotBudget = TimeSpan.FromMilliseconds(400);
    private static readonly TimeSpan ConfirmationWindow = TimeSpan.FromSeconds(2);
    private static int _busy;

    private readonly Task<AutomationTextClient.Snapshot<EditorAnchor>?>? _initial = StartProbe(hasFocus,
        safe => AutomationTextClient.Read(owner, expectedIdentity: null, safe,
            (text, start, end, _) => new EditorAnchor(text, start, end)));
    private Evidence? _baseline;
    private long? _deliveredAt;
    private bool _available = true;

    public bool IsAvailable => watchEdits && _available;

    public bool Prepare(string inserted)
    {
        _baseline = null;
        _deliveredAt = null;
        var initial = WaitFor(_initial);
        if (initial is null) return End();

        var check = Snapshot<DeliveryEvidence>(initial.Identity, (text, start, end, identity) =>
        {
            var unchanged = initial.Value?.Matches(text, start, end) ?? true;
            var proof = unchanged && watchEdits && inserted.Length <= 4096
                ? InsertedSpanEvidence.Capture(text, start, end, inserted) : null;
            return new DeliveryEvidence(unchanged, proof is not null ? new Evidence(identity, proof) : null);
        });
        // If the provider supplied a readable anchor, it must match. Stable editable controls
        // that only expose ValuePattern can still accept ordinary single-line Unicode input.
        if (check is null || check.Value?.Unchanged == false || (initial.Value is not null && check.Value is null))
            return End();

        _baseline = check.Value?.Observation;
        if (_baseline is null) End();
        return true;
    }

    public void Complete(bool delivered)
    {
        if (!delivered || _baseline is null)
        {
            End();
            return;
        }
        _deliveredAt = Stopwatch.GetTimestamp();
    }

    public bool TryRead(string inserted, out string? edited)
    {
        edited = null;
        if (_baseline is not { } baseline || _deliveredAt is not { } deliveredAt ||
            (!baseline.Proof.IsConfirmed && Stopwatch.GetElapsedTime(deliveredAt) >= ConfirmationWindow))
            return End();

        var check = Snapshot<EditedSpan>(baseline.Identity, (text, start, end, _) =>
            baseline.Proof.Read(text, start, end, inserted, out var span) ? new EditedSpan(span) : null);
        if (check?.Value is not { } result) return End();

        edited = result.Text;
        return true;
    }

    private AutomationTextClient.Snapshot<T>? Snapshot<T>(int[] identity, AutomationTextClient.ReadSnapshot<T> read)
        where T : class =>
        WaitFor(StartProbe(hasFocus, safe => AutomationTextClient.Read(owner, identity, safe, read)));

    private static Task<T?>? StartProbe<T>(Func<bool> hasFocus, Func<Func<bool>, T?> run) where T : class
    {
        if (!hasFocus() || Interlocked.CompareExchange(ref _busy, 1, 0) != 0) return null;
        var started = Stopwatch.GetTimestamp();
        return Task.Run(() =>
        {
            try
            {
                return run(() => Stopwatch.GetElapsedTime(started) < SnapshotBudget && hasFocus());
            }
            catch (COMException)
            {
                return null;
            }
            finally
            {
                Volatile.Write(ref _busy, 0);
            }
        });
    }

    private static T? WaitFor<T>(Task<T?>? worker) where T : class
    {
        if (worker is null) return null;

        // Do not stall delivery or polling on a provider that fails to honor the COM timeout.
        // The global lease stays held until that call exits, so later attempts cannot pile up.
        try
        {
            return worker.WaitAsync(SnapshotBudget).GetAwaiter().GetResult();
        }
        catch (TimeoutException)
        {
            return null;
        }
    }

    private bool End()
    {
        _available = false;
        _baseline = null;
        _deliveredAt = null;
        return false;
    }

    private sealed record Evidence(int[] Identity, InsertedSpanEvidence Proof);
    private sealed record DeliveryEvidence(bool Unchanged, Evidence? Observation);
    private sealed record EditedSpan(string? Text);
}
