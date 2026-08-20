using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Transpose.Translator.Tests
{
    /// <summary>
    /// <c>Transpose.Require</c> (Resources/Require.js): the run-time loader for a script, an ES module
    /// or a stylesheet the page fetches after it has started.
    ///
    /// Every test runs with <c>skipRoslyn: true</c> — there is no native .NET counterpart of injecting
    /// a &lt;script&gt; — against a tiny DOM stub installed from C#, which records what was asked for
    /// and can be told which URLs 404. That is what lets the two behaviours worth guarding be asserted
    /// without a browser: which element a URL is loaded with, and the <c>.js</c> ⇄ <c>.min.js</c>
    /// fallback that lets a library published once work in a site built either way.
    /// </summary>
    [TestClass]
    public class RequireLoaderTests : TranslatorTestBase
    {
        /// <summary>A DOM just real enough for Require.js: elements that report load or error a tick
        /// after they are appended, a document whose baseURI relative URLs resolve against, and a log
        /// of every request in the order it was made.</summary>
        private const string Preamble = @"
using System;
using System.Threading.Tasks;
using Transpose;

public static class Dom
{
    public static void Install()
    {
        Script.Write(@""
            var byTag = { script: [], link: [] };
            globalThis.__requested = [];
            globalThis.__missing = {};
            globalThis.window = { addEventListener: function () { } };
            globalThis.document = {
                baseURI: 'https://site.test/app/',
                readyState: 'loading',
                createElement: function (tag) {
                    return {
                        tagName: tag,
                        $l: {},
                        addEventListener: function (type, handler) {
                            (this.$l[type] = this.$l[type] || []).push(handler);
                        }
                    };
                },
                getElementsByTagName: function (tag) { return byTag[tag] || []; }
            };
            document.head = {
                appendChild: function (element) {
                    var url = element.src || element.href;
                    var kind = element.tagName === 'link' ? 'style' : (element.type === 'module' ? 'module' : 'script');
                    __requested.push(kind + ' ' + url);
                    var list = byTag[element.tagName];
                    list.push(element);
                    element.parentNode = { removeChild: function (e) { list.splice(list.indexOf(e), 1); } };
                    setTimeout(function () {
                        var type = __missing[url] ? 'error' : 'load';
                        var handlers = element.$l[type] || [];
                        for (var i = 0; i < handlers.length; i++) handlers[i]({ type: type });
                    }, 0);
                }
            };
        "");
    }

    public static void Missing(string url) => Script.Write(""__missing[{0}] = true;"", url);

    public static string Requested => Script.Write<string>(""__requested.join('\\n')"");
}
";

        [TestMethod]
        public async Task EachUrlIsLoadedWithTheElementItsExtensionCallsForAsync()
        {
            var output = await RunTest(Preamble + @"
public class Program
{
    public static async Task Main()
    {
        Dom.Install();

        // One call, several files: they load in order, because a plugin has to arrive after the
        // library it extends.
        await Require.RequireAsync(""assets/css/site.css"", ""assets/js/d3.js"", ""./chunks/c0.mjs"");

        // A module entry keeps the bundle's .js name, so nothing about the URL says what it is.
        await Require.RequireAsync(RequireKind.Module, ""Admin.js?v=7"");

        Console.WriteLine(Dom.Requested);
    }
}", skipRoslyn: true);

            Assert.AreEqual(
                "style https://site.test/app/assets/css/site.css\n"
                + "script https://site.test/app/assets/js/d3.js\n"
                + "module https://site.test/app/chunks/c0.mjs\n"
                + "module https://site.test/app/Admin.js?v=7",
                output,
                "a .css is a stylesheet link, a .mjs a module, anything else a classic script — and an "
                + "explicit kind wins over the extension");
        }

        [TestMethod]
        public async Task AMissingMinifiedVariantFallsBackToTheFormattedOneAsync()
        {
            // The case this exists for: a library asks for its bundle by the minified name, and the
            // site it is running in was built formatted, so only the other spelling is there.
            var output = await RunTest(Preamble + @"
public class Program
{
    public static async Task Main()
    {
        Dom.Install();
        Dom.Missing(""https://site.test/app/assets/js/graph-kit.min.js"");
        Dom.Missing(""https://site.test/app/assets/js/only-minified.js"");

        await Require.RequireAsync(""assets/js/graph-kit.min.js"");
        await Require.RequireAsync(""assets/js/only-minified.js"");

        Console.WriteLine(Dom.Requested);
    }
}", skipRoslyn: true);

            Assert.AreEqual(
                "script https://site.test/app/assets/js/graph-kit.min.js\n"
                + "script https://site.test/app/assets/js/graph-kit.js\n"
                + "script https://site.test/app/assets/js/only-minified.js\n"
                + "script https://site.test/app/assets/js/only-minified.min.js",
                output,
                "the fallback goes both ways: a site keeps whichever variant its own build called for");
        }

        [TestMethod]
        public async Task OneFetchIsSharedAndAFailedOneIsForgottenAsync()
        {
            var output = await RunTest(Preamble + @"
public class Program
{
    public static async Task Main()
    {
        Dom.Install();

        // The same file asked for three ways: relative, dot-relative, absolute. Every spelling
        // resolves against the document base first, so it is one entry and one fetch.
        await Require.RequireAsync(""assets/js/lib.js"");
        await Require.RequireAsync(""./assets/js/lib.js"");
        await Require.RequireAsync(""https://site.test/app/assets/js/lib.js"");
        Console.WriteLine(""loaded: "" + Require.IsLoaded(""assets/js/lib.js""));

        Dom.Missing(""https://site.test/app/gone.js"");
        Dom.Missing(""https://site.test/app/gone.min.js"");
        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                await Require.RequireAsync(""gone.js"");
                Console.WriteLine(""unexpected success"");
            }
            catch (Exception e)
            {
                Console.WriteLine(""failed: "" + e.Message);
            }
        }

        Console.WriteLine(Dom.Requested);
    }
}", skipRoslyn: true);

            Assert.AreEqual(
                "loaded: True\n"
                + "failed: Transpose.Require: failed to load 'https://site.test/app/gone.js'.\n"
                + "failed: Transpose.Require: failed to load 'https://site.test/app/gone.js'.\n"
                + "script https://site.test/app/assets/js/lib.js\n"
                + "script https://site.test/app/gone.js\n"
                + "script https://site.test/app/gone.min.js\n"
                + "script https://site.test/app/gone.js\n"
                + "script https://site.test/app/gone.min.js",
                output,
                "a successful load is shared by every later caller; a failed one is forgotten, so the "
                + "second attempt really tries again — and it reports the URL that was asked for, not "
                + "the counterpart it also tried");
        }

        [TestMethod]
        public async Task AFileTheDocumentAlreadyCarriesIsNotFetchedAgainAsync()
        {
            var output = await RunTest(Preamble + @"
public class Program
{
    public static async Task Main()
    {
        Dom.Install();

        // index.html scripted it before anything asked: the document is complete, so there is
        // nothing left to wait for and nothing to add.
        Script.Write(@""
            document.readyState = 'complete';
            document.getElementsByTagName('script').push({ src: 'https://site.test/app/tss.js' });
        "");

        Console.WriteLine(""loaded before: "" + Require.IsLoaded(""tss.js""));
        await Require.RequireAsync(""tss.js"");
        Console.WriteLine(""requests: ["" + Dom.Requested + ""]"");
    }
}", skipRoslyn: true);

            Assert.AreEqual("loaded before: True\nrequests: []", output,
                "a file the page already carries is waited on, never fetched a second time");
        }
    }
}
