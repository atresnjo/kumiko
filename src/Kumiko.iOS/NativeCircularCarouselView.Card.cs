using CoreGraphics;
using UIKit;

namespace Kumiko.iOS;

internal sealed partial class NativeCircularCarouselView
{
    sealed class CarouselCard : UIView
    {
        internal readonly UIView Decoration = new() { UserInteractionEnabled = false };
        readonly UIView square = new() { UserInteractionEnabled = false };
        readonly UIView circle = new() { UserInteractionEnabled = false };
        readonly UIView capsule = new() { UserInteractionEnabled = false };
        readonly UIView surface = new() { BackgroundColor = UIColor.White, UserInteractionEnabled = false };
        readonly UILabel emoji = new() { TextAlignment = UITextAlignment.Center };
        readonly UILabel title = new() { TextAlignment = UITextAlignment.Center, Lines = 2 };
        bool preview;
        int motif;

        readonly Action<Action> guard;

        internal CarouselCard(Action<Action> guard)
        {
            this.guard = guard;
            UserInteractionEnabled = false;
            AddSubview(Decoration);
            Decoration.AddSubview(square);
            Decoration.AddSubview(circle);
            Decoration.AddSubview(capsule);
            AddSubview(surface);
            surface.AddSubview(emoji);
            surface.AddSubview(title);
            surface.Layer.CornerCurve = CoreAnimation.CACornerCurve.Continuous;
            surface.Layer.ShadowColor = UIColor.Black.CGColor;
            surface.Layer.ShadowOpacity = .06f;
            surface.Layer.ShadowRadius = 14;
            surface.Layer.ShadowOffset = new CGSize(0, 8);
        }

        internal void Configure(NativeCarouselItem item, bool small)
        {
            preview = small;
            motif = item.Motif;
            NativePaint.Label(emoji, item.Emoji, small ? 28 : 40, UIFontWeight.Regular, UI.Color.Black);
            NativePaint.Label(title, item.Title, small ? 15 : 24, UIFontWeight.Medium, UI.Color.Hex(0x1b2118));
            title.Lines = 2;
            title.AdjustsFontSizeToFitWidth = true;
            title.MinimumScaleFactor = .7f;
            AccessibilityLabel = item.Title;
            square.BackgroundColor = UIColor.FromRGBA(motif == 1 ? .98f : 1, motif == 1 ? .64f : .78f, .55f, .38f);
            circle.BackgroundColor = UIColor.FromRGBA(motif == 2 ? .98f : .76f, motif == 2 ? .72f : .38f, .16f, .38f);
            capsule.BackgroundColor = UIColor.FromRGBA(.24f, .6f, .25f, .4f);
            SetNeedsLayout();
        }

        public override void LayoutSubviews()
        {
            base.LayoutSubviews();
            guard(ArrangeCard);
        }

        void ArrangeCard()
        {
            var w = Bounds.Width;
            var h = Bounds.Height;
            Decoration.Frame = Bounds;
            surface.Frame = Bounds;
            surface.Layer.CornerRadius = preview ? 22 : 36;
            emoji.Frame = new CGRect(12, h * .17, w - 24, h * .31);
            title.Frame = new CGRect(12, h * .48, w - 24, h * .4);
            square.Transform = CGAffineTransform.MakeIdentity();
            square.Frame = Bounds;
            square.Layer.CornerRadius = 14;
            square.Transform = CGAffineTransform.MakeRotation(motif == 1 ? .78f : -.78f);
            circle.Frame = new CGRect(-w * .08, motif == 1 ? h * .7 : -h * .28, w * .65, w * .65);
            circle.Layer.CornerRadius = w * .325f;
            capsule.Transform = CGAffineTransform.MakeIdentity();
            capsule.Frame = new CGRect(w * .62, motif == 1 ? -h * .35 : h * .15, w * .55, h * 1.2);
            capsule.Layer.CornerRadius = w * .275f;
            capsule.Transform = CGAffineTransform.MakeRotation(.44f);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Layer.RemoveAllAnimations();
                Decoration.Layer.RemoveAllAnimations();
                RemoveFromSuperview();
                title.Dispose();
                emoji.Dispose();
                surface.Dispose();
                capsule.Dispose();
                circle.Dispose();
                square.Dispose();
                Decoration.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
