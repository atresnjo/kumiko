using System.Net;
using System.Security.Cryptography;
using System.Text;
using Kumiko.Desktop;

static class UpdateFlows
{
    internal static async Task Run(string output, Action<bool, string> check)
    {
        byte[] package = "A verified package fixture, never installed"u8.ToArray();
        string digest = Convert.ToHexString(SHA256.HashData(package));
        string payload = "{\"version\":1,\"releases\":[{\"version\":\"1.0.1\",\"runtime\":\"osx-arm64\",\"url\":\"https://fixture.invalid/package\",\"sha256\":\"" + digest + "\"}]}";
        using var rsa = RSA.Create(2048);
        byte[] bytes = Encoding.UTF8.GetBytes(payload);
        byte[] signature = rsa.SignData(bytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pss);
        string envelope = "{\"payload\":\"" + Convert.ToBase64String(bytes) + "\",\"signature\":\"" + Convert.ToBase64String(signature) + "\"}";
        var handler = new FixtureHandler(Encoding.UTF8.GetBytes(envelope), package);
        using var http = new HttpClient(handler);
        var feed = new UpdateFeed(http, new("https://fixture.invalid/feed"), rsa.ExportSubjectPublicKeyInfoPem());
        var catalog = await feed.ReadAsync();
        check(catalog is [{ Version: "1.0.1" }], "custom HTTPS feed verifies detached catalog signature before accepting releases");
        var staged = await feed.StageAsync(catalog[0], Path.Combine(output, "staged"));
        check(File.ReadAllBytes(staged.Path).SequenceEqual(package), "verified package is staged without installation or launch");
        handler.Tamper = true;
        try
        {
            await feed.StageAsync(catalog[0], Path.Combine(output, "staged"));
            check(false, "tampered package rejected");
        }
        catch (InvalidDataException)
        {
            check(true, "tampered package rejected before staging");
        }
        handler.Catalog = Encoding.UTF8.GetBytes(envelope.Replace(Convert.ToBase64String(signature), Convert.ToBase64String(new byte[signature.Length]), StringComparison.Ordinal));
        try
        {
            await feed.ReadAsync();
            check(false, "tampered catalog rejected");
        }
        catch (InvalidDataException)
        {
            check(true, "tampered catalog rejected before release selection");
        }
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        try
        {
            await feed.ReadAsync(cancellation.Token);
            check(false, "cancelled feed rejected");
        }
        catch (OperationCanceledException)
        {
            check(true, "feed cancellation is observable");
        }
    }
    sealed class FixtureHandler(byte[] catalog, byte[] package) : HttpMessageHandler
    {
        internal byte[] Catalog = catalog;
        internal bool Tamper;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = new ByteArrayContent(request.RequestUri!.AbsolutePath == "/feed" ? Catalog : Tamper ? [0, 1, 2] : package)
            });
        }
    }
}
