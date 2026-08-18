using Transpose;
using Transpose.Core;

namespace Transpose.Core
{
    public static partial class dom
    {
        /// <summary>
        /// The concrete <see cref="dom.AnimationEffectReadOnly"/> a CSS animation, a CSS transition or a
        /// <c>Element.animate()</c> call produces - the one that names the element being animated.
        ///
        /// <see cref="dom.Animation.effect"/> is typed as the base, which carries only the timing, so a
        /// caller walking <c>getAnimations()</c> to find out *what* is animating casts to this. The cast
        /// is erased: an external type from a binding library is not runtime-checked.
        /// </summary>
        [IgnoreCast]
        [Virtual]
        [FormerInterface]
        public abstract class KeyframeEffect : dom.AnimationEffectReadOnly
        {
            /// <summary>The element being animated, or null for an effect that targets none.</summary>
            public abstract dom.Element target { get; }

            /// <summary>The pseudo-element being animated - e.g. <c>"::before"</c> - or null for the element itself.</summary>
            public abstract string pseudoElement { get; }
        }

        public partial class Document
        {
            /// <summary>
            /// Every animation in this document whose target is a descendant of it - CSS animations, CSS
            /// transitions and <c>Element.animate()</c> alike, running or not. Read
            /// <see cref="dom.Animation.playState"/> to tell those apart.
            /// </summary>
            public virtual extern dom.Animation[] getAnimations();
        }

        public partial class Element
        {
            /// <summary>
            /// Every animation affecting this element, running or not. Excludes the animations of its
            /// descendants - use <see cref="dom.Document.getAnimations"/> for those.
            /// </summary>
            public virtual extern dom.Animation[] getAnimations();
        }
    }
}
