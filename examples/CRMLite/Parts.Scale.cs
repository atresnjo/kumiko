using Kumiko.UI;

namespace CRMLite;

/// <summary>
/// The numbers one platform draws the shared <see cref="Parts"/> at. Shapes, colors and structure are shared; the
/// desktop view (View.Parts.cs) and the phone (CRMLite.iOS Harbor.cs) each supply their own scale, so no part
/// hard-codes either platform's sizes.
/// </summary>
/// <param name="MinText">The smallest letter a logo or avatar draws.</param>
/// <param name="Overflow">How an unwrapped label that does not fit ends.</param>
internal sealed record PartScale(float MinText, TextOverflow Overflow, TagScale Tag, CardScale Card, TimelineScale Timeline);

/// <summary>A stage tag: pill height, gap and horizontal padding, corner radius and label size.</summary>
internal sealed record TagScale(float Height, float Gap, float Padding, float Radius, float Text);

/// <summary>
/// A deal card: padding, row gap and corner radius; logo size; the company name/value and detail text sizes; footer
/// height and gap; owner avatar size; whether the card has a fixed height (a board slot) whose footer is pushed to the
/// bottom, rather than hugging its content.
/// </summary>
internal sealed record CardScale(Edges Padding, float Gap, float Radius, float Logo, float Name, float Detail, float Foot,
    float FootGap, float Avatar, bool FixedHeight);

/// <summary>
/// An activity timeline: day heading, headline, detail and time text sizes; icon rail width, headline line height,
/// kind icon and stage arrow sizes; note card padding and radius; whether headlines and details wrap.
/// </summary>
internal sealed record TimelineScale(float Heading, float Text, float Detail, float Time, float Rail, float Line, float Icon,
    float Arrow, Edges Body, float Radius, bool Wrap);
