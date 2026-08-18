namespace Transpose
{
    /// <summary>
    /// Keeps a type out of the module chunker's reference graph.
    ///
    /// A chunk is a strongly-connected component of that graph, so a "hub" type — a static facade
    /// whose members construct half the library — fuses everything it touches into one chunk: the
    /// facade reaches every component, every component reaches the facade, and the cycle makes them
    /// one unit. Marking the facade with this attribute drops the edges *out of* it and instead
    /// attributes each member's dependencies to the code that CALLS that member, which is where they
    /// are actually needed: a static method body only runs when someone invokes it.
    ///
    /// The type itself still becomes a chunk and is still imported by its callers — only its
    /// outgoing edges move. Nothing changes for a non-module build.
    ///
    /// Apply it to a static class of factory/helper methods. It is not meaningful on a type that is
    /// instantiated, inherited from, or whose static state initialises eagerly: those references
    /// really do have to be resolved when the type is defined, not when a member is called.
    /// </summary>
    [NonScriptable]
    [System.AttributeUsage(System.AttributeTargets.Class, Inherited = false)]
    public sealed class SkipTypeClusteringAttribute : System.Attribute
    {
    }
}
