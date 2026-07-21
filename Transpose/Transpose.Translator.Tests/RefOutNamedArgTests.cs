using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Transpose.Translator.Tests
{
    /// <summary>
    /// Regression test for calls that combine an <c>out</c>/<c>ref</c> argument with named arguments
    /// that skip an optional parameter. The ref/out invocation path emitted arguments in source order
    /// without reordering named args or filling omitted-optional defaults, so a skipped optional
    /// shifted every later argument by one. This is Tesserae's Popover crash: `Tippy.ShowFor(anchor,
    /// content, out hide, …, manualTrigger: true, …)` (no onClickOutside) put `true` into the
    /// onClickOutside slot, so tippy's `props.onClickOutside` was a boolean and its `.apply` threw.
    /// </summary>
    [TestClass]
    public class RefOutNamedArgTests : TranslatorTestBase
    {
        [TestMethod]
        public async Task TestOutParamWithSkippedNamedOptionalAsync()
        {
            await RunTest(
                @"
using System;

public class Program
{
    static void Show(int a, int b, out Action hide,
                     bool arrow = false, Action onHidden = null, Func<bool> onHide = null,
                     Action<int> onClickOutside = null, bool manualTrigger = false, int border = 8)
    {
        hide = () => { };
        Console.WriteLine(
            ""arrow="" + arrow +
            "" onHidden="" + (onHidden == null ? ""null"" : ""fn"") +
            "" onHide="" + (onHide == null ? ""null"" : ""fn"") +
            "" onClickOutside="" + (onClickOutside == null ? ""null"" : ""fn"") +
            "" manual="" + manualTrigger +
            "" border="" + border);
    }

    public static void Main()
    {
        Action onHiddenInternal = () => { };
        Func<bool> shouldHide = () => true;

        // Skips onClickOutside; provides later optionals by name (like Popover.ShowFor).
        Show(1, 2, out var hide,
             arrow: true, onHidden: onHiddenInternal, onHide: shouldHide,
             manualTrigger: true, border: 20);
        // Expect: arrow=True onHidden=fn onHide=fn onClickOutside=null manual=True border=20
    }
}
                ");
        }
    }
}
