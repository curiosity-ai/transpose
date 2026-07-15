using System;

#if TRANSPOSE_COMPILER
namespace Transpose.Contract
#else
namespace Transpose
#endif
{
    /// <summary>
    /// Allow to control some aspects of generated code
    /// </summary>
#if TRANSPOSE_COMPILER
    public class CompilerRule
    {
        public static CompilerRule DefaultIfNotH5()
        {
            return new CompilerRule()
            {
                AnonymousType = AnonymousTypeRule.Plain,
                ArrayIndex = ArrayIndexRule.Managed,
                AutoProperty = AutoPropertyRule.Plain,
                Boxing = BoxingRule.Managed,
                ExternalCast = ExternalCastRule.Plain,
                InlineComment = InlineCommentRule.Plain,
                Integer = IntegerRule.Managed,
                Lambda = LambdaRule.Plain,
                UseShortForms = false
            };
        }

#else
    [NonScriptable]
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Property | AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Constructor | AttributeTargets.Interface | AttributeTargets.Assembly, AllowMultiple = false)]
    public class RulesAttribute : Attribute
    {
#endif
        public
#if  TRANSPOSE_COMPILER
            LambdaRule?
#else
            LambdaRule
#endif
        Lambda
        { get; set; }

        public
#if TRANSPOSE_COMPILER
            BoxingRule?
#else
            BoxingRule
#endif
        Boxing
        { get; set; }

        public
#if TRANSPOSE_COMPILER
            ArrayIndexRule?
#else
            ArrayIndexRule
#endif
        ArrayIndex
        { get; set; }

        public
#if TRANSPOSE_COMPILER
            IntegerRule?
#else
            IntegerRule
#endif
        Integer
        { get; set; }

        public
#if TRANSPOSE_COMPILER
            AnonymousTypeRule?
#else
            AnonymousTypeRule
#endif
        AnonymousType
        { get; set; }

        public
#if TRANSPOSE_COMPILER
            bool?
#else
            bool
#endif
        UseShortForms
        { get; set; }

        public
#if TRANSPOSE_COMPILER
            AutoPropertyRule?
#else
            AutoPropertyRule
#endif
        AutoProperty
        { get; set; }

        public
#if TRANSPOSE_COMPILER
            InlineCommentRule?
#else
            InlineCommentRule
#endif
        InlineComment
        { get; set; }

        public
#if TRANSPOSE_COMPILER
            ExternalCastRule?
#else
            ExternalCastRule
#endif
        ExternalCast
        { get; set; }

#if TRANSPOSE_COMPILER
        public CompilerRuleLevel Level { get; set; }
#endif
    }

#if !TRANSPOSE_COMPILER
    [NonScriptable]
#endif
    public enum LambdaRule
    {
        Managed = 0,
        Plain = 1
    }

#if !TRANSPOSE_COMPILER
    [NonScriptable]
#endif
    public enum BoxingRule
    {
        Managed = 0,
        Plain = 1
    }

#if !TRANSPOSE_COMPILER
    [NonScriptable]
#endif
    public enum ArrayIndexRule
    {
        Managed = 0,
        Plain = 1
    }

#if !TRANSPOSE_COMPILER
    [NonScriptable]
#endif
    public enum IntegerRule
    {
        Managed = 0,
        Plain = 1
    }

#if !TRANSPOSE_COMPILER
    [NonScriptable]
#endif
    public enum AnonymousTypeRule
    {
        Managed = 0,
        Plain = 1
    }

#if !TRANSPOSE_COMPILER
    [NonScriptable]
#endif
    public enum AutoPropertyRule
    {
        Managed = 0,
        Plain = 1
    }

#if !TRANSPOSE_COMPILER
    [NonScriptable]
#endif
    public enum InlineCommentRule
    {
        Managed = 0,
        Plain = 1
    }

#if !TRANSPOSE_COMPILER
    [NonScriptable]
#endif
    public enum ExternalCastRule
    {
        Managed = 0,
        Plain = 1
    }
}