namespace ChitChat.Abstractions;

/// <summary>Optional observation of the span inserted by a focused text injector.</summary>
public interface ITextEditObserver
{
    /// <summary>Whether this target supports observation and still has usable insertion evidence.</summary>
    bool CanObserveEdits { get; }

    /// <summary>Reads only the matched insertion span while the original target still has focus.</summary>
    /// <param name="inserted">The text the injector delivered.</param>
    /// <param name="edited">The current span, or null while the insertion remains unconfirmed.</param>
    /// <returns>False when observation must end.</returns>
    bool TryReadEditedText(string inserted, out string? edited);
}
