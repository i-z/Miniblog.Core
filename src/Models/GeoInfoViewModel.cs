namespace Miniblog.Core.Models;

public class GeoInfoViewModel
{
    public string IpAddress { get; set; } = string.Empty;

    public string? Country { get; set; }

    public string? City { get; set; }

    public string? ErrorMessage { get; set; }
}
