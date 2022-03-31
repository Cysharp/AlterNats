namespace AlterNats;

internal sealed class NatsUri
{
    const string DefaultScheme = "nats://";

    readonly Uri uri;
    public bool IsSecure { get; }
    public string Host => uri.Host;
    public int Port => uri.Port;

    public NatsUri(string urlString)
    {
        if (!urlString.Contains("://"))
        {
            urlString = DefaultScheme + urlString;
            IsSecure = false;
        }
        else
        {
            IsSecure = urlString.Contains("tls://");
        }

        this.uri = new Uri(urlString);
    }

    public override string ToString()
    {
        return uri.ToString();
    }
}
