using System.Buffers;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ChitChat.Platform.Windows;

internal sealed class FocusedEditObservation(nint editor, uint owner, Func<bool> hasFocus, bool watchEdits) : IFocusedEditObservation
{
    private const int MaximumControlLength = 16_384;
    private const int MaximumInsertionLength = 4_096;
    private const int WindowStyle = -16;
    private const int PasswordStyle = 0x20;
    private const int ReadOnlyStyle = 0x800;
    private const int MultilineStyle = 0x4;
    private const uint GetSelection = 0x00B0;
    private const uint GetText = 0x000D;
    private const uint GetTextLength = 0x000E;
    private const uint GetPasswordCharacter = 0x00D2;
    private const uint GetTextLimit = 0x00D5;
    private const uint ReplaceSelection = 0x00C2;
    private const uint AbortOnHangOrExit = 0x0023;
    private const uint MessageBudgetMilliseconds = 40;

    private static readonly TimeSpan ConfirmationWindow = TimeSpan.FromSeconds(2);
    private EditorAnchor? _anchor;
    private InsertedSpanEvidence? _evidence;
    private string? _className;
    private bool _available = true;
    private long? _deliveredAt;

    public bool IsAvailable => watchEdits && _available;

    internal static FocusedEditObservation? ForTarget(nint editor, uint owner, Func<bool> hasFocus, bool watchEdits,
                                                       out bool knownNativeEditor)
    {
        var observer = new FocusedEditObservation(editor, owner, hasFocus, watchEdits);
        var canRead = observer.CheckEditor();
        // An identified native password/read-only control must not fall back to UIA.
        knownNativeEditor = observer._className is not null;
        if (!canRead) return null;
        observer.CaptureAnchor();
        return observer;
    }

    private void CaptureAnchor()
    {
        var buffer = ArrayPool<char>.Shared.Rent(MaximumControlLength + 2);
        try
        {
            if (TrySnapshot(buffer, out var length, out var range))
                _anchor = new EditorAnchor(buffer.AsSpan(0, length), range.Start, range.End);
        }
        finally
        {
            ArrayPool<char>.Shared.Return(buffer, clearArray: true);
        }
    }

    internal bool TryReplaceMultiline(string text)
    {
        if (!CheckEditor() || (GetWindowLong(editor, WindowStyle) & MultilineStyle) == 0 ||
            !TrySend(GetSelection, 0, 0, out var selection) || selection >= uint.MaxValue ||
            !TrySend(GetTextLimit, 0, 0, out var limit) || limit == 0 ||
            !TrySend(GetTextLength, 0, 0, out var length) || length > MaximumControlLength)
            return false;

        var start = selection & 0xffff;
        var end = selection >> 16;
        if (end < start || end > length || length - (end - start) + (nuint)text.Length > limit || !CheckEditor())
            return false;

        var value = Marshal.StringToHGlobalUni(text);
        try
        {
            // This changes the editor directly. It cannot translate a line break to an Enter key.
            return hasFocus() && TrySend(ReplaceSelection, 1, value, out _);
        }
        finally
        {
            Marshal.ZeroFreeGlobalAllocUnicode(value);
        }
    }

    public bool Prepare(string inserted)
    {
        _evidence = null;
        _deliveredAt = null;
        if (_anchor is not { } anchor) return End();

        // Match the original caret and whole control before the injector can send anything.
        var buffer = ArrayPool<char>.Shared.Rent(MaximumControlLength + 2);
        try
        {
            if (!TrySnapshot(buffer, out var length, out var selection)) return End();
            var current = buffer.AsSpan(0, length);
            if (!anchor.Matches(current, selection.Start, selection.End)) return End();
            if (watchEdits && inserted.Length <= MaximumInsertionLength)
                _evidence = InsertedSpanEvidence.Capture(current, selection.Start, selection.End, inserted);
            if (_evidence is null) End();
        }
        finally
        {
            ArrayPool<char>.Shared.Return(buffer, clearArray: true);
        }
        return true;
    }

    public void Complete(bool delivered)
    {
        if (!delivered || _evidence is null)
        {
            End();
            return;
        }
        _deliveredAt = Stopwatch.GetTimestamp();
    }

    public bool TryRead(string inserted, out string? edited)
    {
        edited = null;
        if (_evidence is not { } evidence || _deliveredAt is not { } deliveredAt) return End();
        if (!evidence.IsConfirmed && Stopwatch.GetElapsedTime(deliveredAt) >= ConfirmationWindow)
            return End();

        var buffer = ArrayPool<char>.Shared.Rent(MaximumControlLength + 2);
        try
        {
            if (!TrySnapshot(buffer, out var length, out var selection) ||
                !evidence.Read(buffer.AsSpan(0, length), selection.Start, selection.End, inserted, out edited))
                return End();
            return true;
        }
        finally
        {
            ArrayPool<char>.Shared.Return(buffer, clearArray: true);
        }
    }

    private bool End()
    {
        _available = false;
        _anchor = null;
        _evidence = null;
        _deliveredAt = null;
        return false;
    }

    private bool TrySnapshot(char[] buffer, out int length, out (int Start, int End) selection)
    {
        length = 0;
        selection = default;
        if (!CheckEditor() || !TrySend(GetSelection, 0, 0, out var originalSelection)) return false;

        // WM_GETTEXT is a system message. Windows marshals this buffer to the external process.
        // Never pass a pointer to a RichEdit private message.
        var pinned = GCHandle.Alloc(buffer, GCHandleType.Pinned);
        try
        {
            if (!hasFocus() ||
                !TrySend(GetText, MaximumControlLength + 2, pinned.AddrOfPinnedObject(), out var count) ||
                count > MaximumControlLength)
                return false;
            length = (int)count;
        }
        finally
        {
            pinned.Free();
        }
        if (!CheckEditor() || !TrySend(GetSelection, 0, 0, out var currentSelection) ||
            originalSelection != currentSelection || currentSelection >= uint.MaxValue || !hasFocus())
            return false;

        var start = (int)(currentSelection & 0xffff);
        var end = (int)(currentSelection >> 16);
        if (_className != "Edit")
        {
            // RichEdit counts a paragraph break as one position, even where returned as CRLF.
            start = RichEditOffset(buffer.AsSpan(0, length), start);
            end = RichEditOffset(buffer.AsSpan(0, length), end);
        }
        if (start < 0 || end < start || end > length) return false;
        selection = (start, end);
        return true;
    }

    private bool CheckEditor()
    {
        if (!hasFocus() || owner == (uint)Environment.ProcessId ||
            GetWindowThreadProcessId(editor, out var process) == 0 || process != owner ||
            !IsWindowUnicode(editor) || !IsWindowEnabled(editor))
            return false;

        var name = new char[48];
        var count = GetClassName(editor, name, name.Length);
        if (count == 0 || count >= name.Length - 1) return false;
        var currentClass = new string(name, 0, count);
        if (currentClass is not ("Edit" or "RichEdit20W" or "RichEdit50W" or "RICHEDIT50W" or "RichEditD2DPT") ||
            (_className is not null && !string.Equals(currentClass, _className, StringComparison.Ordinal)))
            return false;

        _className = currentClass;
        var style = GetWindowLong(editor, WindowStyle);
        if (style == 0 || (style & (PasswordStyle | ReadOnlyStyle)) != 0 ||
            !TrySend(GetPasswordCharacter, 0, 0, out var mask) || mask != 0)
            return false;
        return true;
    }

    private static int RichEditOffset(ReadOnlySpan<char> text, int index)
    {
        var offset = 0;
        for (var position = 0; position < index; position++)
        {
            if (offset >= text.Length) return -1;
            if (text[offset] == '\r' && offset + 1 < text.Length && text[offset + 1] == '\n') offset++;
            offset++;
        }
        return offset;
    }

    private bool TrySend(uint message, nuint wParam, nint lParam, out nuint result) =>
        SendMessageTimeout(editor, message, wParam, lParam, AbortOnHangOrExit, MessageBudgetMilliseconds, out result) != 0;

    [DllImport("user32.dll", EntryPoint = "SendMessageTimeoutW", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern nint SendMessageTimeout(nint window, uint message, nuint wParam, nint lParam,
                                                   uint flags, uint milliseconds, out nuint result);

    [DllImport("user32.dll", EntryPoint = "GetClassNameW", ExactSpelling = true, CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int GetClassName(nint window, [Out, MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.U2)] char[] name,
                                           int maximumCount);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int GetWindowLong(nint window, int index);

    [DllImport("user32.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern uint GetWindowThreadProcessId(nint window, out uint processId);

    [DllImport("user32.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowUnicode(nint window);

    [DllImport("user32.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowEnabled(nint window);
}
