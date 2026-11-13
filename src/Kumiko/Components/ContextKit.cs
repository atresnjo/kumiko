using Kumiko.Components;

namespace Kumiko.UI;

public static class ContextKitExtensions
{
    extension(Context context)
    {
        /// <summary>shadcn-style components (Kumiko.Components) styled by the active <see cref="KitTheme"/>.</summary>
        public Kit Kit => new(context);
    }
}
