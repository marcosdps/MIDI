// Base común de los proveedores de IA (Gemini, Claude). El proveedor se elige en appsettings.json ("Proveedor").
public abstract class ClienteIA
{
    protected readonly HttpClient http;
    protected readonly string sistema;     // el system prompt: musico.md

    public int MaxReintentos { get; }
    public abstract bool Configurado { get; }
    public abstract string NombreClave { get; }     // para el mensaje "falta configurar ..."

    protected ClienteIA(HttpClient http, IConfiguration cfg)
    {
        this.http = http;
        http.Timeout = TimeSpan.FromMinutes(3);
        MaxReintentos = int.TryParse(cfg["MaxReintentos"], out var n) ? n : 2;
        sistema = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "musico.md"));
    }

    public abstract Task<string> Responder(IEnumerable<MensajeChat> mensajes, CancellationToken ct);
}
