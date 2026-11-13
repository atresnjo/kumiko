using CoreAnimation;
using CoreGraphics;
using UIKit;

namespace Kumiko.iOS;

internal sealed partial class NativeArcMorphView
{
    /// <summary>
    /// A routine card drawn at its 282×234pt design size. The surface resizes into an orb while the copy stays
    /// centred and fades; the orb value travels from its place in the dot-matrix time to the orb's centre.
    /// </summary>
    sealed class ArcCard : UIView
    {
        const float DesignWidth = 282;
        const float DesignHeight = 234;
        const float TimeSize = 44;
        const float OrbDigits = 19;
        readonly UIView surface = new() { UserInteractionEnabled = false };
        readonly CAGradientLayer neutral = new() { LayerType = CAGradientLayerType.Radial };
        readonly CAGradientLayer wash = new();
        readonly CAGradientLayer glow = new() { LayerType = CAGradientLayerType.Radial };
        readonly UIView copy = new() { UserInteractionEnabled = false };
        readonly UIView chip = new() { UserInteractionEnabled = false };
        readonly UIImageView chipSymbol = new() { ContentMode = UIViewContentMode.ScaleAspectFit };
        readonly UILabel chipLabel = new();
        readonly UIView play = new() { UserInteractionEnabled = false };
        readonly UIImageView playSymbol = new() { ContentMode = UIViewContentMode.ScaleAspectFit };
        readonly NativeDotMatrixLayer time = new();
        readonly NativeDotMatrixLayer value = new();
        readonly UILabel title = new();
        readonly UILabel detail = new();
        readonly CAShapeLayer ticks = new();
        readonly CALayer playhead = new();
        readonly UILabel[] marks = [new(), new(), new()];
        readonly CAShapeLayer ring = new();
        NativeArcCard? item;
        CGPoint valueStart;
        bool shared;
        nfloat fit = 1;

        internal ArcCard()
        {
            UserInteractionEnabled = false;
            AddSubview(surface);
            surface.ClipsToBounds = true;
            surface.Layer.CornerCurve = CACornerCurve.Continuous;
            surface.Layer.BorderWidth = 1;
            surface.Layer.AddSublayer(neutral);
            surface.Layer.AddSublayer(wash);
            surface.Layer.AddSublayer(glow);
            surface.AddSubview(copy);
            surface.Layer.AddSublayer(value);
            copy.Bounds = new CGRect(0, 0, DesignWidth, DesignHeight);
            copy.AddSubview(chip);
            chip.AddSubview(chipSymbol);
            chip.AddSubview(chipLabel);
            copy.AddSubview(play);
            play.AddSubview(playSymbol);
            copy.Layer.AddSublayer(time);
            copy.AddSubview(title);
            copy.AddSubview(detail);
            copy.Layer.AddSublayer(ticks);
            copy.Layer.AddSublayer(playhead);
            foreach (var mark in marks)
                copy.AddSubview(mark);
            Layer.InsertSublayer(ring, 0);
            Layer.ShadowColor = UIColor.Black.CGColor;
            Layer.ShadowOpacity = .07f;
            Layer.ShadowRadius = 16;
            Layer.ShadowOffset = new CGSize(0, 10);
            using var edge = UIColor.White.ColorWithAlpha(.7f);
            surface.Layer.BorderColor = edge.CGColor;
            using var white = UIColor.White;
            using var stone = UIColor.FromRGB(0xe4, 0xe1, 0xdc);
            neutral.Colors = [white.CGColor, UIColor.FromRGB(0xf3, 0xf1, 0xed).CGColor, stone.CGColor];
            neutral.Locations = [0, .45, 1];
            neutral.StartPoint = new CGPoint(.36, .3);
            neutral.EndPoint = new CGPoint(1.06, 1.06);
            chip.Layer.CornerRadius = 15;
            chip.Layer.BorderWidth = 1;
            chip.Layer.BorderColor = edge.CGColor;
            chip.BackgroundColor = UIColor.White.ColorWithAlpha(.34f);
            play.Layer.CornerRadius = 15;
            play.BackgroundColor = UIColor.FromRGB(0x1f, 0x1e, 0x1c);
            play.Layer.ShadowColor = UIColor.Black.CGColor;
            play.Layer.ShadowOpacity = .18f;
            play.Layer.ShadowRadius = 5;
            play.Layer.ShadowOffset = new CGSize(0, 3);
            playSymbol.Image = UIImage.GetSystemImage("play.fill")?.ApplyTintColor(UIColor.White, UIImageRenderingMode.AlwaysOriginal);
            using var tick = UIColor.FromRGBA(0x8e, 0x8a, 0x85, 0x99);
            ticks.StrokeColor = tick.CGColor;
            ticks.LineWidth = 1;
            playhead.BackgroundColor = UIColor.FromRGB(0x2a, 0x28, 0x26).CGColor;
            playhead.CornerRadius = 1.5f;
            ring.FillColor = null;
            ring.LineWidth = 2;
            ring.Opacity = 0;
            ring.ShadowOpacity = .55f;
            ring.ShadowRadius = 6;
            ring.ShadowOffset = CGSize.Empty;
            using var digits = UIColor.White;
            time.StrokeColor = digits.CGColor;
            value.StrokeColor = digits.CGColor;
        }

        internal void Configure(NativeArcCard next, float scale)
        {
            if (item == next && fit == scale)
                return;
            item = next;
            fit = scale;
            AccessibilityLabel = next.Category;
            var ink = UI.Color.Hex(0x1e1d1b);
            chipSymbol.Image = UIImage.GetSystemImage(next.Symbol, UIImageSymbolConfiguration.Create(13, UIImageSymbolWeight.Medium))?
                .ApplyTintColor(NativePaint.Color(ink), UIImageRenderingMode.AlwaysOriginal);
            NativePaint.Label(chipLabel, next.Category, 15, UIFontWeight.Regular, ink, adjustsFont: false);
            NativePaint.Label(title, next.Title, 21, UIFontWeight.Regular, ink, adjustsFont: false);
            NativePaint.Label(detail, next.Detail, 14, UIFontWeight.Regular, UI.Color.Hex(0x6e6a66), adjustsFont: false);
            for (var i = 0; i < marks.Length; i++)
            {
                using var font = UIFont.SystemFontOfSize(12, UIFontWeight.Medium);
                using var color = NativePaint.Color(UI.Color.Hex(0x34312e));
                marks[i].AttributedText = new Foundation.NSAttributedString(i < next.Marks.Length ? next.Marks[i] : "",
                    new UIStringAttributes { Font = font, ForegroundColor = color, KerningAdjustment = .7f });
            }
            Paint(next.Tint);
            shared = next.Time.StartsWith(next.Value, StringComparison.Ordinal);
            var (width, prefix) = time.Configure(next.Time, TimeSize, next.Value.Length);
            if (shared)
                time.Configure(next.Time[next.Value.Length..], TimeSize);
            value.Configure(next.Value, TimeSize);
            var left = (DesignWidth - width) / 2;
            var capCenter = 82.5f;
            time.AnchorPoint = new CGPoint(0, time.CapCenter / time.Bounds.Height);
            time.Position = new CGPoint(left + (shared ? prefix : 0), capCenter);
            value.AnchorPoint = new CGPoint(.5, value.CapCenter / value.Bounds.Height);
            valueStart = new CGPoint(left + (shared ? prefix : value.Bounds.Width) / 2, capCenter);
            using var accent = NativePaint.Color(next.Accent);
            ring.StrokeColor = accent.CGColor;
            ring.ShadowColor = accent.CGColor;
            ArrangeCopy();
        }

        void Paint(UI.Color tint)
        {
            using var strong = NativePaint.Color(tint);
            using var middle = NativePaint.Color(tint with
            {
                A = .55f
            });
            using var clear = NativePaint.Color(tint with
            {
                A = 0
            });
            glow.Colors = [strong.CGColor, middle.CGColor, clear.CGColor];
            glow.Locations = [0, .38, 1];
            glow.StartPoint = new CGPoint(.5, .5);
            glow.EndPoint = new CGPoint(1, 1);
            using var top = NativePaint.Color(Mix(tint, UI.Color.White, .5));
            using var cream = NativePaint.Color(UI.Color.Hex(0xf6efea));
            using var sky = NativePaint.Color(UI.Color.Hex(0xd9e4f2));
            wash.Colors = [top.CGColor, cream.CGColor, sky.CGColor];
            wash.Locations = [0, .58, 1];
        }

        void ArrangeCopy()
        {
            chipSymbol.Frame = new CGRect(12, 8, 14, 14);
            var label = chipLabel.SizeThatFits(new CGSize(200, 30));
            chipLabel.Frame = new CGRect(32, (30 - label.Height) / 2, label.Width, label.Height);
            chip.Frame = new CGRect(16, 16, 32 + label.Width + 14, 30);
            play.Frame = new CGRect(DesignWidth - 46, 16, 30, 30);
            playSymbol.Frame = new CGRect(10.5, 9, 11, 12);
            title.Frame = new CGRect(17, 124, DesignWidth - 34, 28);
            detail.Frame = new CGRect(17, 154, DesignWidth - 34, 18);
            using var path = new CGPath();
            for (var i = 0; i < 48; i++)
            {
                var x = 17.5 + i * 5.25;
                var tall = i % 6 == 0;
                path.MoveToPoint((nfloat)x, 198);
                path.AddLineToPoint((nfloat)x, tall ? 182 : 189);
            }
            ticks.Path = path;
            ticks.Frame = copy.Bounds;
            playhead.Frame = new CGRect(66, 173, 3, 26);
            var columns = new[] { 17f, 73f, 143f };
            for (var i = 0; i < marks.Length; i++)
                marks[i].Frame = new CGRect(columns[i], 205, 90, 16);
        }

        internal bool PlayContains(CGPoint point)
        {
            var local = copy.ConvertPointFromView(point, this);
            return copy.Alpha > .5 && play.Frame.Inset(-10, -10).Contains(local);
        }

        internal void Apply(ArcPose pose)
        {
            Bounds = new CGRect(0, 0, pose.Width, pose.Height);
            Center = pose.Center;
            Transform = CGAffineTransform.MakeRotation(pose.Rotation);
            Alpha = pose.Alpha;
            surface.Frame = Bounds;
            surface.Layer.CornerRadius = pose.Radius;
            using var outline = UIBezierPath.FromRoundedRect(Bounds, pose.Radius);
            Layer.ShadowPath = outline.CGPath;
            neutral.Frame = Bounds;
            wash.Frame = Bounds;
            wash.Opacity = (float)pose.Tint;
            glow.Opacity = (float)pose.Tint;
            var glowCenter = new CGPoint(pose.Width / 2, Lerp(.18, .46, pose.Morph) * pose.Height);
            var glowSize = new CGSize(Lerp(1.3, 1.15, pose.Morph) * pose.Width, Lerp(1.25, 1.15, pose.Morph) * pose.Height);
            glow.Frame = new CGRect(glowCenter.X - glowSize.Width / 2, glowCenter.Y - glowSize.Height / 2, glowSize.Width, glowSize.Height);
            copy.Transform = CGAffineTransform.MakeScale(pose.Fit, pose.Fit);
            copy.Center = new CGPoint(pose.Width / 2, pose.Height / 2);
            copy.Alpha = pose.ContentAlpha;
            var travel = Smooth(0, .85, pose.Morph);
            var start = new CGPoint(pose.Width / 2 + (valueStart.X - DesignWidth / 2) * pose.Fit,
                pose.Height / 2 + (valueStart.Y - DesignHeight / 2) * pose.Fit);
            value.Position = new CGPoint(Lerp(start.X, pose.Width / 2, travel), Lerp(start.Y, pose.Height / 2, travel));
            var scale = (nfloat)Lerp(pose.Fit, OrbDigits / TimeSize, travel);
            value.Transform = CATransform3D.MakeScale(scale, scale, 1);
            value.Opacity = shared ? 1 : (float)Smooth(.04, .24, pose.Morph);
            using var digits = NativePaint.Color(Mix(UI.Color.Hex(0x3b3936), UI.Color.White, pose.Tint));
            value.StrokeColor = digits.CGColor;
            var halo = Math.Max(pose.Width, pose.Height) + 12;
            using var circle = UIBezierPath.FromOval(new CGRect((pose.Width - halo) / 2, (pose.Height - halo) / 2, halo, halo));
            ring.Path = circle.CGPath;
            ring.Frame = Bounds;
        }

        internal void ShowRing(bool visible, bool animate)
        {
            var opacity = visible ? 1f : 0f;
            if (Math.Abs(ring.Opacity - opacity) < .01)
                return;
            ring.RemoveAllAnimations();
            if (animate && visible)
            {
                using var spring = new CASpringAnimation { KeyPath = "transform.scale", Mass = 1, Stiffness = 260, Damping = 18 };
                spring.From = Foundation.NSNumber.FromDouble(.86);
                spring.To = Foundation.NSNumber.FromDouble(1);
                spring.Duration = .7;
                ring.AddAnimation(spring, "kumiko-arc-ring-scale");
                using var fade = CABasicAnimation.FromKeyPath("opacity");
                fade.From = Foundation.NSNumber.FromFloat(0);
                fade.To = Foundation.NSNumber.FromFloat(1);
                fade.Duration = .22;
                ring.AddAnimation(fade, "kumiko-arc-ring-opacity");
            }
            CATransaction.Begin();
            CATransaction.DisableActions = true;
            ring.Opacity = opacity;
            CATransaction.Commit();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Layer.RemoveAllAnimations();
                ring.RemoveAllAnimations();
                RemoveFromSuperview();
                foreach (var mark in marks)
                    mark.Dispose();
                foreach (var layer in new CALayer[] { neutral, wash, glow, time, value, ticks, playhead, ring })
                {
                    layer.RemoveFromSuperLayer();
                    layer.Dispose();
                }
                chipSymbol.Dispose();
                chipLabel.Dispose();
                chip.Dispose();
                playSymbol.Dispose();
                play.Dispose();
                title.Dispose();
                detail.Dispose();
                copy.Dispose();
                surface.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
