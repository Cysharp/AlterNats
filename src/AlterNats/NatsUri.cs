namespace AlterNats;

internal sealed class NatsUri : IEquatable<NatsUri>
{
    const string DefaultScheme = "nats://";
    public static readonly NatsUri Default = new NatsUri("nats://localhost:4222");
    public const int DefaultPort = 4222;

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
        return uri.ToString().Trim('/');
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(IsSecure, Host, Port);
    }

    public bool Equals(NatsUri? other)
    {
        if (other == null) return false;
        if (ReferenceEquals(this, other)) return true;

        return this.IsSecure == other.IsSecure
            && this.Host == other.Host
            && this.Port == other.Port;
    }
}
