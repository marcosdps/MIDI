using System.Text;
using System.Text.Json;

// API de Google Gemini (nivel gratuito: la key se crea en https://aistudio.google.com/apikey)
public class ClienteGemini : ClienteIA
{
    readonly string apiKey;
    readonly string modelo;
    readonly string urlBase;

    public override bool Configurado => !string.IsNullOrWhiteSpace(apiKey);
    public override string NombreClave => "GEMINI_API_KEY";

    public ClienteGemini(HttpClient http, IConfiguration cfg) : base(http, cfg)
    {
        apiKey = cfg["GEMINI_API_KEY"] ?? cfg["Gemini:ApiKey"] ?? "";
        modelo = cfg["Gemini:Model"] ?? "gemini-flash-latest";
        urlBase = (cfg["Gemini:BaseUrl"] ?? "https://generativelanguage.googleapis.com").TrimEnd('/');
    }

    public override async Task<string> Responder(IEnumerable<MensajeChat> mensajes, CancellationToken ct)
    {
        // Gemini llama "model" al asistente y espera turnos alternados: se juntan los consecutivos
        var contents = new List<(string rol, StringBuilder texto)>();
        foreach (var m in mensajes)
        {
            string rol = m.Rol == "assistant" ? "model" : "user";
            if (contents.Count > 0 && contents[^1].rol == rol) contents[^1].texto.Append("\n\n").Append(m.Contenido);
            else contents.Add((rol, new StringBuilder(m.Contenido)));
        }

        var cuerpo = new
        {
            systemInstruction = new { parts = new[] { new { text = sistema } } },
            contents = contents.Select(c => new { role = c.rol, parts = new[] { new { text = c.texto.ToString() } } }),
            generationConfig = new { maxOutputTokens = 16000 }
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, $"{urlBase}/v1beta/models/{modelo}:generateContent")
        {
            Content = new StringContent(JsonSerializer.Serialize(cuerpo), Encoding.UTF8, "application/json")
        };
        req.Headers.Add("x-goog-api-key", apiKey);

        using var resp = await http.SendAsync(req, ct);
        string json = await resp.Content.ReadAsStringAsync(ct);

        if ((int)resp.StatusCode == 429)
            throw new InvalidOperationException("Se alcanzó el límite del nivel gratuito de Gemini. Esperá un minuto y volvé a intentar.");

        using var doc = JsonDocument.Parse(json);
        if (!resp.IsSuccessStatusCode)
        {
            string detalle = doc.RootElement.TryGetProperty("error", out var e)
                             && e.TryGetProperty("message", out var m) ? m.GetString() ?? "" : json;
            throw new InvalidOperationException($"La API de Gemini respondió {(int)resp.StatusCode}: {detalle}");
        }

        // Si Gemini bloquea o corta la respuesta, no hay "parts"
        if (!doc.RootElement.TryGetProperty("candidates", out var cands) || cands.GetArrayLength() == 0
            || !cands[0].TryGetProperty("content", out var cont) || !cont.TryGetProperty("parts", out var parts))
        {
            string motivo = cands.ValueKind == JsonValueKind.Array && cands.GetArrayLength() > 0
                            && cands[0].TryGetProperty("finishReason", out var fr) ? fr.GetString() ?? "" : "sin respuesta";
            throw new InvalidOperationException("Gemini no devolvió texto (" + motivo + "). Probá reformular el pedido.");
        }

        return string.Concat(parts.EnumerateArray()
            .Where(p => p.TryGetProperty("text", out _) && !(p.TryGetProperty("thought", out var t) && t.ValueKind == JsonValueKind.True))
            .Select(p => p.GetProperty("text").GetString()));
    }
}
