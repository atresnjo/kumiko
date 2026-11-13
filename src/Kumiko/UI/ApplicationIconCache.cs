namespace Kumiko.UI;

public class ApplicationIconCache(Func<string, Kumiko.Rendering.Image?> source)
{
    readonly Dictionary<string, Kumiko.Rendering.Image?> images = new(StringComparer.Ordinal);
    int budget;
    object? owner;
    long frame = -1;
    public int Count
    {
        get
        {
            Runtime.KumikoExecution.CheckUI();
            return images.Count;
        }
    }
    bool pending;
    public bool Pending
    {
        get
        {
            Runtime.KumikoExecution.CheckUI();
            return pending;
        }
        private set => pending = value;
    }

    public void BeginFrame(object context, long sequence)
    {
        Runtime.KumikoExecution.CheckUI();
        if (ReferenceEquals(owner, context) && frame == sequence)
            return;
        owner = context;
        frame = sequence;
        budget = 6;
        Pending = false;
    }

    public Kumiko.Rendering.Image? Get(string bundle)
    {
        Runtime.KumikoExecution.CheckUI();
        if (bundle.Length == 0)
            return null;
        if (images.TryGetValue(bundle, out var image))
            return image;
        // Admission bounded for the entire window lifetime: eviction would leave
        // old images in the renderer's packed atlas and cause repeated uploads.
        if (images.Count >= 256)
            return null;
        if (budget == 0)
        {
            Pending = true;
            return null;
        }

        budget--;
        try
        {
            image = source(bundle);
        }
        catch (Exception e) when (e is IOException or InvalidOperationException or ArgumentException)
        {
            image = null;
        }

        images[bundle] = image;
        return image;
    }
}
