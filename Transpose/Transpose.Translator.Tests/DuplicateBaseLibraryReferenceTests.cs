using System;
using System.IO;
using Microsoft.CodeAnalysis;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Transpose.Translator.Tests;

/// <summary>
/// A project's resolved references may contain a <em>second</em> copy of the base library — the
/// <c>Transpose.BCL</c> package it pins, when that is not the same file
/// <see cref="TransposeAssemblies.TransposeDllPath"/> discovered (a newer one in the NuGet cache, or
/// a <c>TRANSPOSE_DLL_PATH</c> pointing at a locally built runtime). Only one may reach the
/// compilation, and the reason is not "duplicate assembly" tidiness.
///
/// Overload numbering asks whether a method has an IL body by looking its metadata <b>token</b> up in
/// a set read from <c>TransposeDllPath</c> (<c>TransposeNaming.HasNoBody</c>). A token from a
/// different build of the same assembly names an unrelated method, so members get misread as extern
/// JS-backed ones and emitted under their bare, unsuffixed names. Calls then bind to whichever
/// overload happens to hold that name, and nothing fails until it runs:
/// <c>List&lt;T&gt;.Sort(Comparison&lt;T&gt;)</c> compiled to <c>Sort()</c>, silently sorting with the
/// default comparer and throwing "Cannot compare items" on the first element type that is not
/// <c>IComparable</c> — which is how this was found, in Tesserae's diagram auto-layout.
///
/// The de-duplication used to be by file path, which cannot see the case it exists to prevent.
/// </summary>
[TestClass]
public sealed class DuplicateBaseLibraryReferenceTests
{
    private string _dir = "";

    [TestInitialize]
    public void Setup()
    {
        _dir = Path.Combine(Path.GetTempPath(), "tps-dupbcl-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); } catch { }
    }

    private const string SortsWithAComparison = @"
using System.Collections.Generic;
public class Item { public int N; }
public class P { public static void M(List<Item> xs) { xs.Sort((a, b) => a.N - b.N); } }";

    /// <summary>How many of the compilation's references are the base library, counted by what each
    /// FILE is. Deliberately not by assembly symbol: Roslyn unifies two references of the same
    /// identity down to one symbol, so the symbol view shows one either way while the compilation
    /// still reads its metadata from the wrong file.</summary>
    private static int BaseLibraryReferencesIn(params string[] extraReferencePaths)
    {
        var compilation = CompilationBuilder.Build(
            new[] { ("App.cs", SortsWithAComparison) }, "App",
            extraReferencePaths: extraReferencePaths);

        var count = 0;
        foreach (var reference in compilation.References)
            if (reference is PortableExecutableReference { FilePath: { } file }
                && TransposeAssemblies.AssemblySimpleName(file) == "Transpose") count++;
        return count;
    }

    /// <summary>A build of the base library that is genuinely a different file from the one in use —
    /// which is what a mismatched <c>Transpose.BCL</c> PackageReference resolves to. An identical copy
    /// will not do: Roslyn unifies two references of the same assembly identity, so the damage needs
    /// two builds, and only a machine that has both can show it.</summary>
    private static string? ADifferentBuildOfTheBaseLibrary()
    {
        var inUse = Path.GetFullPath(TransposeAssemblies.TransposeDllPath);
        foreach (var root in new[]
                 {
                     Environment.GetEnvironmentVariable("NUGET_PACKAGES"),
                     Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages"),
                 })
        {
            if (string.IsNullOrEmpty(root)) continue;
            foreach (var id in new[] { "transpose.bcl", "Transpose.BCL" })
            {
                var pkg = Path.Combine(root!, id);
                if (!Directory.Exists(pkg)) continue;
                foreach (var version in Directory.GetDirectories(pkg))
                {
                    var dll = Path.Combine(version, "lib", "netstandard2.0", "Transpose.dll");
                    if (File.Exists(dll) && !string.Equals(Path.GetFullPath(dll), inUse, StringComparison.OrdinalIgnoreCase))
                        return dll;
                }
            }
        }
        return null;
    }

    [TestMethod]
    public void ADifferentBuildOfTheBaseLibraryDoesNotChangeTheEmittedJavaScript()
    {
        var other = ADifferentBuildOfTheBaseLibrary();
        if (other is null) Assert.Inconclusive("needs a second build of Transpose.dll (a Transpose.BCL package in the NuGet cache)");

        var alone = new RoslynTranslator().Translate(
            new[] { ("App.cs", SortsWithAComparison) }, "App", null).Javascript ?? "";
        var withOther = new RoslynTranslator().Translate(
            new[] { ("App.cs", SortsWithAComparison) }, "App", new[] { other! }).Javascript ?? "";

        StringAssert.Contains(alone, "Sort$2(",
            "sanity: Sort(Comparison<T>) is not the first Sort overload, so it carries a suffix");
        Assert.AreEqual(alone, withOther,
            "a second build of the base library must not change a single byte of the emitted JavaScript — "
            + "it used to rename members onto their bare overload names, binding calls to the wrong one");
    }

    [TestMethod]
    public void OnlyOneBaseLibraryReachesTheCompilation()
    {
        var other = ADifferentBuildOfTheBaseLibrary();
        if (other is null) Assert.Inconclusive("needs a second build of Transpose.dll (a Transpose.BCL package in the NuGet cache)");

        Assert.AreEqual(1, BaseLibraryReferencesIn(), "sanity: the base library is injected once on its own");
        Assert.AreEqual(1, BaseLibraryReferencesIn(other!),
            "a second build of the base library must not be referenced alongside the injected one");
    }

    [TestMethod]
    public void AGenuinelyDifferentAssemblyIsNotTakenForTheBaseLibrary()
    {
        // The filter must not swallow a real reference — a project legitimately binds against
        // Transpose.Core, Transpose.Newtonsoft.Json and its own dependencies. Only the assembly
        // literally named `Transpose` is the duplicate, and the package id (Transpose.BCL) is not
        // what it is called.
        Assert.AreEqual("Transpose", TransposeAssemblies.AssemblySimpleName(TransposeAssemblies.TransposeDllPath));

        var other = typeof(DuplicateBaseLibraryReferenceTests).Assembly.Location;
        Assert.AreNotEqual("Transpose", TransposeAssemblies.AssemblySimpleName(other),
            "an ordinary assembly must keep being referenced");
    }

    [TestMethod]
    public void AFileThatIsNotAnAssemblyIsNotMistakenForOne()
    {
        var junk = Path.Combine(_dir, "notadll.dll");
        File.WriteAllText(junk, "this is not a PE file");

        Assert.IsNull(TransposeAssemblies.AssemblySimpleName(junk));
        Assert.IsNull(TransposeAssemblies.AssemblySimpleName(Path.Combine(_dir, "missing.dll")));
    }
}
