using System.Text;
using System.Text.Json;

// API de Anthropic (de pago). Se usa con "Proveedor": "anthropic".
public class ClienteClaude : ClienteIA
{
    readonly string apiKey;
    readonly string modelo;
    readonly string urlBase;

    public override bool Configurado => !string.IsNullOrWhiteSpace(apiKey);
    public override string NombreClave => "ANTHROPIC_API_KEY";

    public ClienteClaude(HttpClient http, IConfiguration cfg) : base(http, cfg)
    {
        apiKey = cfg["ANTHROPIC_API_KEY"] ?? cfg["Anthropic:ApiKey"] ?? "";
        modelo = cfg["Anthropic:Model"] ?? "claude-sonnet-5-5";
        urlBase = (cfg["Anthropic:BaseUrl"] ?? "https://api.anthropic.com").TrimEnd('/');
    }

    public override async Task<string> Responder(IEnumerable<MensajeChat> mensajes, CancellationToken ct)
    {
        var cuerpo = new
        {
            model = modelo,
            max_tokens = 8000,
            system = sistema,
            messages = mensajes.Select(m => new { role = m.Rol, content = m.Contenido })
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, urlBase + "/v1/messages")
        {
            Content = new StringContent(JsonSerializer.Serialize(cuerpo), Encoding.UTF8, "application/json")
        };
        req.Headers.Add("x-api-key", apiKey);
        req.Headers.Add("anthropic-version", "2023-06-01");

        using var resp = await http.SendAsync(req, ct);
        string json = await resp.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);

        if (!resp.IsSuccessStatusCode)
        {
            string detalle = doc.RootElement.TryGetProperty("error", out var e)
                             && e.TryGetProperty("message", out var m) ? m.GetString() ?? "" : json;
            throw new InvalidOperationException($"La API de Claude respondió {(int)resp.StatusCode}: {detalle}");
        }

        return string.Concat(doc.RootElement.GetProperty("content").EnumerateArray()
            .Where(b => b.GetProperty("type").GetString() == "text")
            .Select(b => b.GetProperty("text").GetString()));
    }
}
