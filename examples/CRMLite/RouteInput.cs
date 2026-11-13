namespace CRMLite;

/// <summary>Legacy command-line options feed the same generated route parser as deep links.</summary>
public static class RouteInput
{
    public static Screen? Parse(string? path, string? page = null, string? company = null)
    {
        if (path is not null)
            return ScreenRoutes.Parse(path) ?? throw new ArgumentException("Unknown or malformed --route.", nameof(path));
        if (company is not null)
            return ScreenRoutes.Parse(ScreenRoutes.Format(new Screen.CompanyDetails(company)))
                ?? throw new ArgumentException("Invalid --company.", nameof(company));
        if (page is null)
            return null;
        if (!Enum.TryParse<ScreenKind>(page, true, out var kind) || !Enum.IsDefined(kind) || kind == ScreenKind.Company)
            throw new ArgumentException("Unknown --page or missing company ID.", nameof(page));
        return ScreenRoutes.Parse(ScreenRoutes.Format(ScreenRoutes.Root(kind)));
    }
}
