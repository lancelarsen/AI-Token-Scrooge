using System.Runtime.InteropServices;

namespace AIResetRadar.App.Platforms.Windows;

internal static class FocusEventFailure
{
    // MAUI 10.0.120 reads these WinUI event arguments before updating IsFocused.
    // A failed focus-element lookup is advisory, not a failed application operation.
    // Keep this workaround restricted to the observed E_FAIL and getter/callback pair.
    internal static bool IsKnown(Exception error)
    {
        if (error is not COMException || error.HResult != unchecked((int)0x80004005)) return false;
        var stack = error.StackTrace ?? "";
        return Matches("GotFocus", "NewFocusedElement") || Matches("LostFocus", "OldFocusedElement");

        bool Matches(string transition, string element) =>
            stack.Contains($"ABI.Microsoft.UI.Xaml.Input.IFocusManager{transition}EventArgsMethods.get_{element}(", StringComparison.Ordinal) &&
            stack.Contains($"Microsoft.Maui.Handlers.ViewHandler.FocusManager_{transition}(", StringComparison.Ordinal);
    }
}
