using Transpose;
using Transpose.Core;

namespace Transpose.Core
{
    public static partial class dom
    {
        // ---------------------------------------------------------------------------------------
        // The Web Animations enumerations. Declared the way a hand-written binding declares one (see
        // dom.WebXR.cs) - a LiteralType<string> named System.String, whose members are [Template]s
        // that emit the bare string literal - rather than through dom.Literals.Types, which is the
        // shape the decompiled dom.cs happens to use.
        // ---------------------------------------------------------------------------------------

        /// <summary>How an effect combines with the underlying value: <c>KeyframeEffect.composite</c>.</summary>
        [Name("System.String")]
        public class CompositeOperation : LiteralType<string>
        {
            [Template("<self>\"replace\"")]
            public static readonly dom.CompositeOperation replace;

            [Template("<self>\"add\"")]
            public static readonly dom.CompositeOperation add;

            [Template("<self>\"accumulate\"")]
            public static readonly dom.CompositeOperation accumulate;

            private extern CompositeOperation();
            public static extern implicit operator dom.CompositeOperation(string value);
        }

        /// <summary>
        /// A single keyframe's composite operation, which may also defer to the effect's:
        /// <c>ComputedKeyframe.composite</c>.
        /// </summary>
        [Name("System.String")]
        public class CompositeOperationOrAuto : LiteralType<string>
        {
            [Template("<self>\"replace\"")]
            public static readonly dom.CompositeOperationOrAuto replace;

            [Template("<self>\"add\"")]
            public static readonly dom.CompositeOperationOrAuto add;

            [Template("<self>\"accumulate\"")]
            public static readonly dom.CompositeOperationOrAuto accumulate;

            [Template("<self>\"auto\"")]
            public static readonly dom.CompositeOperationOrAuto auto;

            private extern CompositeOperationOrAuto();
            public static extern implicit operator dom.CompositeOperationOrAuto(string value);
        }

        /// <summary>How successive iterations combine: <c>KeyframeEffect.iterationComposite</c>.</summary>
        [Name("System.String")]
        public class IterationCompositeOperation : LiteralType<string>
        {
            [Template("<self>\"replace\"")]
            public static readonly dom.IterationCompositeOperation replace;

            [Template("<self>\"accumulate\"")]
            public static readonly dom.IterationCompositeOperation accumulate;

            private extern IterationCompositeOperation();
            public static extern implicit operator dom.IterationCompositeOperation(string value);
        }

        /// <summary>
        /// Whether an animation is still being kept around: <c>Animation.replaceState</c>. A finished
        /// fill-mode animation is automatically <c>"removed"</c> once another animation covers the same
        /// property, unless <see cref="dom.Animation.persist"/> pinned it as <c>"persisted"</c>.
        /// </summary>
        [Name("System.String")]
        public class AnimationReplaceState : LiteralType<string>
        {
            [Template("<self>\"active\"")]
            public static readonly dom.AnimationReplaceState active;

            [Template("<self>\"removed\"")]
            public static readonly dom.AnimationReplaceState removed;

            [Template("<self>\"persisted\"")]
            public static readonly dom.AnimationReplaceState persisted;

            private extern AnimationReplaceState();
            public static extern implicit operator dom.AnimationReplaceState(string value);
        }

        // ---------------------------------------------------------------------------------------
        // The dictionaries.
        // ---------------------------------------------------------------------------------------

        /// <summary>
        /// The option bag of <see cref="dom.Element.getAnimations(dom.GetAnimationsOptions)"/>.
        /// </summary>
        [IgnoreCast]
        [ObjectLiteral]
        [FormerInterface]
        public class GetAnimationsOptions : IObject
        {
            /// <summary>
            /// Also report the animations of this element's descendants (and of their pseudo-elements).
            /// Defaults to false, which reports only the element's own.
            /// </summary>
            public bool? subtree
            {
                get;
                set;
            }
        }

        /// <summary>
        /// An effect's timing, as read back by <see cref="dom.AnimationEffectReadOnly.getTiming"/> and
        /// written by <see cref="dom.KeyframeEffect.updateTiming(dom.EffectTiming)"/>.
        ///
        /// Distinct from <see cref="dom.AnimationOptions"/>, which is what <c>Element.animate()</c>
        /// takes: that one is the whole keyframe-animation bag (this, plus <c>id</c>), so the two are
        /// deliberately separate dictionaries rather than one shared type.
        /// </summary>
        [IgnoreCast]
        [ObjectLiteral]
        [FormerInterface]
        public class EffectTiming : IObject
        {
            public double? delay
            {
                get;
                set;
            }

            public dom.Literals.Options.direction direction
            {
                get;
                set;
            }

            /// <summary>A duration in milliseconds, or the string <c>"auto"</c>.</summary>
            public Union<double, string> duration
            {
                get;
                set;
            }

            public string easing
            {
                get;
                set;
            }

            public double? endDelay
            {
                get;
                set;
            }

            public dom.Literals.Options.fill fill
            {
                get;
                set;
            }

            public double? iterationStart
            {
                get;
                set;
            }

            /// <summary>The iteration count; <see cref="double.PositiveInfinity"/> repeats forever.</summary>
            public double? iterations
            {
                get;
                set;
            }
        }

        /// <summary>
        /// The timing of a <see cref="dom.KeyframeEffect"/> plus the three things only a keyframe
        /// effect has: how it composites, how its iterations accumulate, and which pseudo-element of
        /// its target it animates.
        /// </summary>
        [IgnoreCast]
        [ObjectLiteral]
        [FormerInterface]
        public class KeyframeEffectOptions : dom.EffectTiming
        {
            public dom.CompositeOperation composite
            {
                get;
                set;
            }

            public dom.IterationCompositeOperation iterationComposite
            {
                get;
                set;
            }

            /// <summary>e.g. <c>"::before"</c>; null animates the element itself.</summary>
            public string pseudoElement
            {
                get;
                set;
            }
        }

        /// <summary>
        /// A keyframe as the browser resolved it - every offset filled in and every shorthand expanded -
        /// as returned by <see cref="dom.KeyframeEffect.getKeyframes"/>. Derives from
        /// <see cref="dom.AnimationKeyFrame"/>, so the result can be handed straight back to
        /// <see cref="dom.KeyframeEffect.setKeyframes(dom.AnimationKeyFrame[])"/>; the animated
        /// properties themselves are read through the inherited string indexer.
        /// </summary>
        [IgnoreCast]
        [ObjectLiteral]
        [FormerInterface]
        public class ComputedKeyframe : dom.AnimationKeyFrame
        {
            /// <summary>The offset the browser computed for this keyframe, never null.</summary>
            public virtual double computedOffset
            {
                get;
                set;
            }

            public virtual dom.CompositeOperationOrAuto composite
            {
                get;
                set;
            }
        }

        /// <summary>The option bag of the <see cref="dom.DocumentTimeline"/> constructor.</summary>
        [IgnoreCast]
        [ObjectLiteral]
        [FormerInterface]
        public class DocumentTimelineOptions : IObject
        {
            /// <summary>
            /// The zero point of the new timeline, as a time on the document timeline. Defaults to 0,
            /// i.e. the document's own time origin.
            /// </summary>
            public double? originTime
            {
                get;
                set;
            }
        }

        // ---------------------------------------------------------------------------------------
        // The interfaces.
        // ---------------------------------------------------------------------------------------

        /// <summary>
        /// The concrete <see cref="dom.AnimationEffectReadOnly"/> a CSS animation, a CSS transition or a
        /// <c>Element.animate()</c> call produces - the one that names the element being animated.
        ///
        /// <see cref="dom.Animation.effect"/> is typed as the base, which carries only the timing, so a
        /// caller walking <c>getAnimations()</c> to find out *what* is animating casts to this. The cast
        /// is erased: an external type from a binding library is not runtime-checked.
        /// </summary>
        [CombinedClass]
        [FormerInterface]
        public class KeyframeEffect : dom.AnimationEffectReadOnly
        {
            public extern KeyframeEffect(dom.Element target, dom.AnimationKeyFrame[] keyframes);

            public extern KeyframeEffect(dom.Element target, dom.AnimationKeyFrame[] keyframes, double options);

            public extern KeyframeEffect(
              dom.Element target,
              dom.AnimationKeyFrame[] keyframes,
              dom.KeyframeEffectOptions options);

            public extern KeyframeEffect(dom.Element target, dom.AnimationKeyFrame keyframes);

            public extern KeyframeEffect(dom.Element target, dom.AnimationKeyFrame keyframes, double options);

            public extern KeyframeEffect(
              dom.Element target,
              dom.AnimationKeyFrame keyframes,
              dom.KeyframeEffectOptions options);

            /// <summary>Copies an existing effect, timing and keyframes alike.</summary>
            public extern KeyframeEffect(dom.KeyframeEffect source);

            public static dom.KeyframeEffect prototype
            {
                get;
                set;
            }

            /// <summary>
            /// The two members the read-only base leaves abstract. A keyframe effect is the concrete
            /// effect the platform actually hands out, so it is where they become real.
            /// </summary>
            public override dom.EffectTiming timing
            {
                get;
            }

            public override extern dom.ComputedTimingProperties getComputedTiming();

            /// <summary>The element being animated, or null for an effect that targets none.</summary>
            public virtual dom.Element target
            {
                get;
                set;
            }

            /// <summary>The pseudo-element being animated - e.g. <c>"::before"</c> - or null for the element itself.</summary>
            public virtual string pseudoElement
            {
                get;
                set;
            }

            public virtual dom.CompositeOperation composite
            {
                get;
                set;
            }

            public virtual dom.IterationCompositeOperation iterationComposite
            {
                get;
                set;
            }

            /// <summary>
            /// This effect's keyframes, resolved: every offset filled in, every shorthand expanded, and
            /// each animated property carried on the keyframe under its own (camel-cased) name.
            /// </summary>
            public virtual extern dom.ComputedKeyframe[] getKeyframes();

            public virtual extern void setKeyframes(dom.AnimationKeyFrame[] keyframes);

            public virtual extern void setKeyframes(dom.AnimationKeyFrame keyframes);
        }

        public partial class AnimationEffectReadOnly
        {
            /// <summary>
            /// This effect's timing as specified - the delay, duration, iteration count and so on that
            /// were asked for. <see cref="getComputedTiming"/> reports what they resolved to.
            /// </summary>
            public virtual extern dom.EffectTiming getTiming();

            /// <summary>
            /// Changes part of this effect's timing in place: only the members present on
            /// <paramref name="timing"/> are applied, the rest keep their current value.
            /// </summary>
            public virtual extern void updateTiming(dom.EffectTiming timing);
        }

        public partial class Animation
        {
            /// <summary>
            /// Whether this animation is still being kept around. A finished, filling animation is
            /// automatically removed once another animation covers the same properties - see
            /// <see cref="persist"/> and <see cref="commitStyles"/>.
            /// </summary>
            public virtual dom.AnimationReplaceState replaceState
            {
                get;
            }

            /// <summary>
            /// Writes this animation's current computed values into its target's inline style, so they
            /// survive the animation being removed. Throws if the target has no inline style or is not
            /// being rendered.
            /// </summary>
            public virtual extern void commitStyles();

            /// <summary>Exempts this animation from automatic removal.</summary>
            public virtual extern void persist();

            /// <summary>
            /// Changes the playback rate without dropping frames: the rate is applied once the animation
            /// is <see cref="ready"/>, keeping <see cref="currentTime"/> continuous. Setting
            /// <see cref="playbackRate"/> directly jumps instead.
            /// </summary>
            public virtual extern void updatePlaybackRate(double playbackRate);

            /// <summary>Raised when this animation is automatically removed.</summary>
            public virtual dom.Animation.onremoveFn onremove
            {
                get;
                set;
            }

            [Generated]
            public delegate void onremoveFn(dom.AnimationPlaybackEvent ev);
        }

        /// <summary>
        /// A document's own monotonically-increasing timeline: the default timeline of every animation
        /// on the document, and the one <c>Animation.currentTime</c> is measured against.
        /// </summary>
        [CombinedClass]
        [FormerInterface]
        public class DocumentTimeline : dom.AnimationTimeline
        {
            public extern DocumentTimeline();

            public extern DocumentTimeline(dom.DocumentTimelineOptions options);

            public static dom.DocumentTimeline prototype
            {
                get;
                set;
            }
        }

        // ---------------------------------------------------------------------------------------
        // The Animatable mixin, and the getAnimations() extensions to Document / DocumentOrShadowRoot.
        // ---------------------------------------------------------------------------------------

        public partial class Document
        {
            /// <summary>
            /// Every animation in this document whose target is a descendant of it - CSS animations, CSS
            /// transitions and <c>Element.animate()</c> alike, running or not. Read
            /// <see cref="dom.Animation.playState"/> to tell those apart.
            /// </summary>
            public virtual extern dom.Animation[] getAnimations();

            /// <summary>This document's default timeline.</summary>
            public virtual dom.DocumentTimeline timeline
            {
                get;
            }
        }

        public partial class DocumentOrShadowRoot
        {
            /// <summary>
            /// Every animation in this shadow tree whose target is a descendant of it. A shadow tree's
            /// animations are *not* reported by the host document's
            /// <see cref="dom.Document.getAnimations"/>, so this is the only way to reach them.
            /// </summary>
            public abstract dom.Animation[] getAnimations();
        }

        public partial class Element
        {
            /// <summary>
            /// Every animation affecting this element, running or not. Excludes the animations of its
            /// descendants - pass <see cref="dom.GetAnimationsOptions.subtree"/>, or use
            /// <see cref="dom.Document.getAnimations"/>, for those.
            /// </summary>
            public virtual extern dom.Animation[] getAnimations();

            /// <summary>
            /// Every animation affecting this element, and - with
            /// <see cref="dom.GetAnimationsOptions.subtree"/> set - every animation affecting its
            /// descendants and their pseudo-elements.
            /// </summary>
            public virtual extern dom.Animation[] getAnimations(dom.GetAnimationsOptions options);

            /// <summary>
            /// Starts an animation on this element and returns it. The write half of the Animatable
            /// mixin, which belongs on Element rather than HTMLElement: an SVGElement animates too.
            /// </summary>
            public virtual extern dom.Animation animate(
              Union<dom.AnimationKeyFrame, dom.AnimationKeyFrame[]> keyframes,
              Union<double, dom.AnimationOptions> options);

            public virtual extern dom.Animation animate(
              dom.AnimationKeyFrame keyframes,
              double options);

            public virtual extern dom.Animation animate(
              dom.AnimationKeyFrame keyframes,
              dom.AnimationOptions options);

            public virtual extern dom.Animation animate(
              dom.AnimationKeyFrame[] keyframes,
              double options);

            public virtual extern dom.Animation animate(
              dom.AnimationKeyFrame[] keyframes,
              dom.AnimationOptions options);
        }
    }
}
