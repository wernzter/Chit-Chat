using System.Buffers;
using System.Runtime.InteropServices;

namespace ChitChat.Platform.Windows;

// Native UIAutomationClient.h interface slots. The desktop target uses the Windows-supplied
// client and has no dependency on WPF or a second desktop runtime.
internal static class AutomationTextClient
{
    internal const int MaximumText = 16_384;

    private static readonly Guid ClientClass = new("E22AD333-B25F-460C-83D0-0581107395C9");
    private static readonly Guid ClientInterface = new("34723AFF-0C9D-49D0-9896-7AB52DF8CD8A");
    private static readonly Guid TextInterface = new("32EBA289-3583-42C9-9C59-3B6D9A1E9B6A");
    private static readonly Guid Text2Interface = new("506A921A-FCC9-409F-B23B-37EB74106872");
    private static readonly char[] EmptyCharacters = new char[MaximumText + 1];
    private const uint ProviderBudget = 60;
    private const int IsPasswordProperty = 30019;
    private const int ValueIsReadOnlyProperty = 30046;
    private const int IsReadOnlyAttribute = 40015;
    private const int EditType = 50004;
    private const int DocumentType = 50030;
    private const int TextPattern = 10014;
    private const int TextPattern2 = 10024;
    private const ushort BooleanVariant = 11;
    private const int Start = 0;
    private const int End = 1;

    internal delegate T? ReadSnapshot<T>(ReadOnlySpan<char> text, int start, int end, int[] identity) where T : class;

    internal sealed record Snapshot<T>(int[] Identity, T? Value) where T : class;

    internal static Snapshot<T>? Read<T>(uint owner, int[]? expectedIdentity, Func<bool> safe,
                                         ReadSnapshot<T> read) where T : class =>
        WithFocusedElement(owner, safe, (client, element, identity) =>
        {
            if (expectedIdentity is not null && !identity.AsSpan().SequenceEqual(expectedIdentity)) return null;
            var valueReadOnly = ReadBooleanProperty(element, ValueIsReadOnlyProperty);
            if (valueReadOnly == true) return null;
            var editable = valueReadOnly == false;
            var value = ReadSelection(element, safe, read, identity, ref editable);
            if (!safe() || !editable) return null;

            using var current = GetObject(client, 8);
            if (current is null ||
                Slot<CompareObjects>(client, 3)(client.Pointer, element.Pointer, current.Pointer, out var same) < 0 ||
                same == 0 || ReadBooleanProperty(current, IsPasswordProperty) != false ||
                ReadBooleanProperty(current, ValueIsReadOnlyProperty) == true || !safe()) return null;

            return new Snapshot<T>(identity, value);
        });

    private static T? WithFocusedElement<T>(uint owner, Func<bool> safe, Func<ComPointer, ComPointer, int[], T?> access)
        where T : class
    {
        if (!safe() || InitializeApartment(0, 0) < 0) return null;
        try
        {
            using var client = Take(CreateInstance(in ClientClass, 0, 1, in ClientInterface, out var native), native);
            if (client is null || SetTimeout(client, 61) < 0 || SetTimeout(client, 63) < 0 || !safe()) return null;
            using var element = GetObject(client, 8);
            return element is not null && TryIdentify(element, owner, safe, out var identity)
                ? access(client, element, identity) : null;
        }
        finally
        {
            UninitializeApartment();
        }
    }

    private static T? ReadSelection<T>(ComPointer element, Func<bool> safe, ReadSnapshot<T> read, int[] identity,
                                       ref bool editable) where T : class
    {
        if (!safe()) return null;
        using var text2 = FindTextPattern(element, TextPattern2, in Text2Interface);
        using var text1 = text2 is null && safe() ? FindTextPattern(element, TextPattern, in TextInterface) : null;
        if ((text2 ?? text1) is not { } text || !safe()) return null;

        using var selection = FindSelection(text, text2 is not null, safe);
        if (selection is null || !safe()) return null;
        var rangeReadOnly = ReadBooleanAttribute(selection, IsReadOnlyAttribute);
        if (rangeReadOnly == true) editable = false;
        else if (rangeReadOnly == false) editable = true;
        if (!editable) return null;

        using var document = GetObject(text, 7);
        if (document is null || !safe() ||
            !CompareEndpoint(selection, Start, document, Start, out var left) || left < 0 ||
            !CompareEndpoint(selection, End, document, End, out var right) || right > 0)
            return null;

        using var prefix = GetObject(document, 3);
        if (prefix is null || !safe() ||
            Slot<ChangeEndpoint>(prefix, 15)(prefix.Pointer, End, selection.Pointer, Start) < 0) return null;

        using var body = ReadText(document, safe);
        if (body is null) return null;
        using var leading = ReadText(prefix, safe);
        if (leading is null) return null;
        using var selected = ReadText(selection, safe);
        if (selected is null ||
            !body.Text.StartsWith(leading.Text, StringComparison.Ordinal) ||
            !body.Text[leading.Length..].StartsWith(selected.Text, StringComparison.Ordinal) || !safe())
            return null;

        using var currentSelection = FindSelection(text, text2 is not null, safe);
        if (currentSelection is null || !safe() ||
            !CompareEndpoint(selection, Start, currentSelection, Start, out var startChange) || startChange != 0 ||
            !CompareEndpoint(selection, End, currentSelection, End, out var endChange) || endChange != 0 || !safe())
            return null;

        if (ReadBooleanAttribute(currentSelection, IsReadOnlyAttribute) == true)
        {
            editable = false;
            return null;
        }

        return read(body.Text, leading.Length, leading.Length + selected.Length, identity);
    }

    private static bool TryIdentify(ComPointer element, uint owner, Func<bool> safe, out int[] identity)
    {
        identity = [];
        if (!safe() ||
            !ReadInteger(element, 20, out var process) || process <= 0 || (uint)process != owner ||
            !ReadInteger(element, 21, out var type) || type is not (EditType or DocumentType) ||
            !ReadInteger(element, 26, out var focused) || focused == 0 ||
            !ReadInteger(element, 28, out var enabled) || enabled == 0 ||
            ReadBooleanProperty(element, IsPasswordProperty) != false || !safe())
            return false;

        var status = Slot<OutReference>(element, 4)(element.Pointer, out var array);
        if (status < 0 || array == 0) return false;
        try
        {
            if (ArrayDimensions(array) != 1 ||
                GetLowerBound(array, 1, out var lower) < 0 || GetUpperBound(array, 1, out var upper) < 0)
                return false;
            var count = (long)upper - lower + 1;
            if (count is <= 0 or > 64 || ArrayElementSize(array) != sizeof(int) || AccessArray(array, out var data) < 0)
                return false;
            try
            {
                if (data == 0) return false;
                identity = new int[(int)count];
                Marshal.Copy(data, identity, 0, identity.Length);
                return true;
            }
            finally
            {
                _ = UnaccessArray(array);
            }
        }
        finally
        {
            _ = DestroyArray(array);
        }
    }

    private static bool? ReadBooleanProperty(ComPointer element, int id)
    {
        // Ignore defaults so the reserved NotSupported value cannot be mistaken for false.
        var result = Slot<ReadProperty>(element, 11)(element.Pointer, id, 1, out var property);
        try
        {
            return result >= 0 ? property.BooleanValue : null;
        }
        finally
        {
            _ = ClearVariant(ref property);
        }
    }

    private static bool? ReadBooleanAttribute(ComPointer range, int attribute)
    {
        var result = Slot<ReadAttribute>(range, 9)(range.Pointer, attribute, out var property);
        try
        {
            return result >= 0 ? property.BooleanValue : null;
        }
        finally
        {
            _ = ClearVariant(ref property);
        }
    }

    private static ComPointer? FindTextPattern(ComPointer element, int id, in Guid pattern) =>
        Take(Slot<ReadPattern>(element, 14)(element.Pointer, id, in pattern, out var pointer), pointer);

    private static ComPointer? FindSelection(ComPointer text, bool hasCaretPattern, Func<bool> safe)
    {
        using var selections = GetObject(text, 5);
        if (selections is not null && ReadInteger(selections, 3, out var count))
        {
            if (count == 1)
                return Take(Slot<IndexReference>(selections, 4)(selections.Pointer, 0, out var selected), selected);
            if (count != 0) return null;
        }

        if (!hasCaretPattern || !safe()) return null;
        var result = Slot<CaretReference>(text, 10)(text.Pointer, out var active, out var caret);
        var range = Take(result, caret);
        if (active != 0) return range;
        range?.Dispose();
        return null;
    }

    private static TextBuffer? ReadText(ComPointer range, Func<bool> safe)
    {
        if (!safe() || Slot<IndexReference>(range, 12)(range.Pointer, MaximumText + 1, out var data) < 0)
            return null;
        var count = StringLength(data);
        try
        {
            if (count > MaximumText || !safe()) return null;
            return new TextBuffer(data, (int)count);
        }
        finally
        {
            if (count is > 0 and <= MaximumText + 1) Marshal.Copy(EmptyCharacters, 0, data, (int)count);
            FreeString(data);
        }
    }

    private static bool CompareEndpoint(ComPointer range, int endpoint, ComPointer other, int otherEndpoint, out int order) =>
        Slot<CompareRange>(range, 5)(range.Pointer, endpoint, other.Pointer, otherEndpoint, out order) >= 0;

    private static bool ReadInteger(ComPointer pointer, int slot, out int value) =>
        Slot<OutInteger>(pointer, slot)(pointer.Pointer, out value) >= 0;

    private static int SetTimeout(ComPointer pointer, int slot) =>
        Slot<WriteUnsigned>(pointer, slot)(pointer.Pointer, ProviderBudget);

    private static ComPointer? GetObject(ComPointer pointer, int slot) =>
        Take(Slot<OutReference>(pointer, slot)(pointer.Pointer, out var value), value);

    private static ComPointer? Take(int result, nint pointer) => result >= 0 && pointer != 0 ? new(pointer) : null;

    private static T Slot<T>(ComPointer owner, int index) where T : Delegate
    {
        var table = Marshal.ReadIntPtr(owner.Pointer);
        return Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(table, index * IntPtr.Size));
    }

    private sealed class ComPointer(nint pointer) : IDisposable
    {
        internal nint Pointer { get; private set; } = pointer;

        public void Dispose()
        {
            if (Pointer == 0) return;
            _ = Slot<ReleaseReference>(this, 2)(Pointer);
            Pointer = 0;
        }
    }

    private sealed class TextBuffer : IDisposable
    {
        private char[]? _buffer;

        internal TextBuffer(nint data, int count)
        {
            _buffer = ArrayPool<char>.Shared.Rent(count);
            Length = count;
            if (count > 0) Marshal.Copy(data, _buffer, 0, count);
        }

        internal int Length { get; }
        internal ReadOnlySpan<char> Text => _buffer.AsSpan(0, Length);

        public void Dispose()
        {
            if (_buffer is null) return;
            ArrayPool<char>.Shared.Return(_buffer, clearArray: true);
            _buffer = null;
        }
    }

    // The VARIANT payload occupies two native words (BRECORD).
    [StructLayout(LayoutKind.Sequential)]
    private struct Variant
    {
        internal ushort Type;
        internal ushort Reserved1;
        internal ushort Reserved2;
        internal ushort Reserved3;
        internal nint Value;
        internal nint Extra;

        internal readonly bool? BooleanValue => Type == BooleanVariant ? (short)Value switch
        {
            0 => false,
            -1 => true,
            _ => null,
        } : null;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int OutReference(nint self, out nint result);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate uint ReleaseReference(nint self);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int OutInteger(nint self, out int result);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int WriteUnsigned(nint self, uint value);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int ReadProperty(nint self, int id, int ignoreDefault, out Variant value);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int ReadAttribute(nint self, int id, out Variant value);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int ReadPattern(nint self, int id, in Guid pattern, out nint result);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int IndexReference(nint self, int position, out nint result);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int CaretReference(nint self, out int active, out nint result);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int CompareObjects(nint self, nint first, nint second, out int same);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int CompareRange(nint self, int endpoint, nint other, int otherEndpoint, out int comparison);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int ChangeEndpoint(nint self, int endpoint, nint other, int otherEndpoint);

    [DllImport("ole32.dll", EntryPoint = "CoInitializeEx", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int InitializeApartment(nint reserved, uint apartment);

    [DllImport("ole32.dll", EntryPoint = "CoUninitialize", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern void UninitializeApartment();

    [DllImport("ole32.dll", EntryPoint = "CoCreateInstance", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int CreateInstance(in Guid clsid, nint outer, uint context, in Guid iface, out nint result);

    [DllImport("oleaut32.dll", EntryPoint = "VariantClear", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int ClearVariant(ref Variant variant);

    [DllImport("oleaut32.dll", EntryPoint = "SysStringLen", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern uint StringLength(nint text);

    [DllImport("oleaut32.dll", EntryPoint = "SysFreeString", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern void FreeString(nint text);

    [DllImport("oleaut32.dll", EntryPoint = "SafeArrayGetDim", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern uint ArrayDimensions(nint array);

    [DllImport("oleaut32.dll", EntryPoint = "SafeArrayGetElemsize", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern uint ArrayElementSize(nint array);

    [DllImport("oleaut32.dll", EntryPoint = "SafeArrayGetLBound", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int GetLowerBound(nint array, uint dimension, out int bound);

    [DllImport("oleaut32.dll", EntryPoint = "SafeArrayGetUBound", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int GetUpperBound(nint array, uint dimension, out int bound);

    [DllImport("oleaut32.dll", EntryPoint = "SafeArrayAccessData", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int AccessArray(nint array, out nint data);

    [DllImport("oleaut32.dll", EntryPoint = "SafeArrayUnaccessData", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int UnaccessArray(nint array);

    [DllImport("oleaut32.dll", EntryPoint = "SafeArrayDestroy", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int DestroyArray(nint array);
}
