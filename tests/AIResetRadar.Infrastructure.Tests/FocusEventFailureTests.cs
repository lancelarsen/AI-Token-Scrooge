using System.Runtime.InteropServices;
using AIResetRadar.App.Platforms.Windows;
using Xunit;

namespace AIResetRadar.Infrastructure.Tests;

public class FocusEventFailureTests
{
    [Theory]
    [InlineData("GotFocus", "NewFocusedElement")]
    [InlineData("LostFocus", "OldFocusedElement")]
    public void RecoversOnlyObservedFocusLookupFailure(string transition, string element)
    {
        var stack = $"at ABI.Microsoft.UI.Xaml.Input.IFocusManager{transition}EventArgsMethods.get_{element}(IObjectReference obj)\nat Microsoft.Maui.Handlers.ViewHandler.FocusManager_{transition}(Object sender, EventArgs e)";
        Assert.True(FocusEventFailure.IsKnown(new FocusException(stack, unchecked((int)0x80004005))));
        Assert.False(FocusEventFailure.IsKnown(new FocusException(stack, unchecked((int)0x80070005))));
        Assert.False(FocusEventFailure.IsKnown(new FocusException(stack.Replace("Microsoft.Maui.Handlers.ViewHandler", "AIResetRadar.App.Other"), unchecked((int)0x80004005))));
        Assert.False(FocusEventFailure.IsKnown(new FocusException(stack.Replace("get_" + element, "OtherOperation"), unchecked((int)0x80004005))));
        Assert.False(FocusEventFailure.IsKnown(new InvalidOperationException(stack)));
        Assert.False(FocusEventFailure.IsKnown(new COMException("Unrelated failure", unchecked((int)0x80004005))));
    }

    sealed class FocusException(string stack, int hresult) : COMException("Synthetic test exception", hresult)
    {
        public override string StackTrace => stack;
    }
}
