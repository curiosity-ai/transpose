// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

namespace System.Diagnostics.CodeAnalysis
{
    /// <summary>
    /// Marks a constructor as initializing every required member of its type, so callers need not set
    /// them. Roslyn treats this as a compiler-required member: as soon as a type has a `required`
    /// member it emits the attribute on the constructors that satisfy it — for a record, that is the
    /// synthesized copy constructor `with` uses — so without a definition here any `required` member
    /// failed to compile with "Missing compiler required member
    /// 'System.Diagnostics.CodeAnalysis.SetsRequiredMembersAttribute..ctor'".
    /// </summary>
    [AttributeUsage(AttributeTargets.Constructor, AllowMultiple = false, Inherited = false)]
    public sealed class SetsRequiredMembersAttribute : Attribute
    {
    }
}
