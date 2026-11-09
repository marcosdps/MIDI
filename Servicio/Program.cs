using System.Text.RegularExpressions;
using System.Threading.RateLimiting;

// Modo consola (como antes): dotnet run -- cancion.txt
if (args.Length >= 1 && args[0].EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
{
    var r = Compilador.Compilar(File.ReadAllText(args[0]));
    foreach (var s in r.Simbolos) Console.WriteLine($"  {s.Nombre,-12} {s.Tipo,-10} {s.Linea,5}  {s.Valor}");
    if (r.Ok)
    {
        File.WriteAllBytes("salida.mid", r.Midi!);
        Console.WriteLine("Listo: se genero salida.mid");
    }
    else
    {
        foreach (var e in r.Errores) Console.WriteLine($"-- linea {e.Linea} col {e.Columna}: {e.Mensaje}");
        Console.WriteLine($"Se encontraron {r.Errores.Count} error(es).");
    }
    return;
}

// Modo servicio web
var builder = WebApplication.CreateBuilder(args);
// Proveedor de IA: "gemini" (gratis, por defecto) o "anthropic" (de pago)
builder.Services.AddHttpClient<ClienteGemini>();
builder.Services.AddHttpClient<ClienteClaude>();
builder.Services.AddTransient<ClienteIA>(sp =>
    string.Equals(builder.Configuration["Proveedor"], "anthropic", StringComparison.OrdinalIgnoreCase)
        ? sp.GetRequiredService<ClienteClaude>()
        : sp.GetRequiredService<ClienteGemini>());
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = 429;
    // el chat cuesta plata: limite por IP
    o.AddPolicy("chat", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "local",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 12, Window = TimeSpan.FromMinutes(1) }));
});

var app = builder.Build();
app.UseRateLimiter();
app.UseDefaultFiles();
app.UseStaticFiles();

// ---- Chat: el usuario pide una cancion, la IA la escribe, el servidor la valida ----
app.MapPost("/api/chat", async (PeticionChat p, ClienteIA ia, CancellationToken ct) =>
{
    if (!ia.Configurado)
        return Results.Json(new { error = $"Falta configurar {ia.NombreClave} en el servidor." }, statusCode: 503);

    // Solo se aceptan turnos user/assistant, con limite de tamano y de historial
    var msgs = (p.Mensajes ?? new())
        .Where(m => (m.Rol == "user" || m.Rol == "assistant") && !string.IsNullOrWhiteSpace(m.Contenido))
        .Select(m => new MensajeChat(m.Rol, m.Contenido.Length > 40_000 ? m.Contenido[..40_000] : m.Contenido))
        .TakeLast(20).ToList();
    while (msgs.Count > 0 && msgs[0].Rol != "user") msgs.RemoveAt(0);
    if (msgs.Count == 0 || msgs[^1].Rol != "user")
        return Results.BadRequest(new { error = "El ultimo mensaje tiene que ser del usuario." });

    string texto = "";
    string? cancion = null;
    var errores = new List<ErrorCompilacion>();
    int correcciones = 0;

    try
    {
        // Si la cancion no compila, se le devuelven los errores a la IA para que la repare
        for (int intento = 0; ; intento++)
        {
            texto = await ia.Responder(msgs, ct);
            cancion = ExtraerCancion(texto);
            if (cancion == null) { errores.Clear(); break; }   // solo conversacion, sin cancion

            var r = Compilador.Compilar(cancion);
            errores = r.Errores;
            if (r.Ok || intento >= ia.MaxReintentos) break;

            msgs.Add(new MensajeChat("assistant", texto));
            msgs.Add(new MensajeChat("user",
                "El compilador rechazo la cancion con estos errores:\n" + Compilador.DescribirErrores(r.Errores) +
                "\nCorregila y devolve la cancion completa en un unico bloque ```cancion."));
            correcciones++;
        }
    }
    catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException or TaskCanceledException)
    {
        return Results.Json(new { error = ex.Message }, statusCode: 502);
    }

    return Results.Ok(new { texto, mensaje = QuitarBloques(texto), cancion, errores, correcciones });
}).RequireRateLimiting("chat");

// ---- Componer: ejecuta el compilador sobre el texto del panel ----
app.MapPost("/api/componer", (PeticionComponer p) =>
{
    if (string.IsNullOrWhiteSpace(p.Cancion))
        return Results.Ok(new { ok = false, errores = new[] { new ErrorCompilacion(1, 1, "sintactico", "La cancion esta vacia") } });
    if (p.Cancion.Length > Compilador.MaxCaracteres)
        return Results.Ok(new { ok = false, errores = new[] { new ErrorCompilacion(1, 1, "semantico", "La cancion es demasiado larga") } });

    var r = Compilador.Compilar(p.Cancion);
    return Results.Ok(new
    {
        ok = r.Ok,
        errores = r.Errores,
        simbolos = r.Simbolos,
        pistas = r.Pistas,
        midi = r.Midi == null ? null : Convert.ToBase64String(r.Midi)
    });
});

app.Run();

// Saca el texto de la cancion del bloque ```cancion ... ``` de la respuesta
static string? ExtraerCancion(string texto)
{
    string? ultimo = null;
    foreach (Match m in Regex.Matches(texto, @"```[a-zA-Z]*[ \t]*\r?\n(.*?)```", RegexOptions.Singleline))
    {
        string cuerpo = m.Groups[1].Value.Trim();
        if (cuerpo.StartsWith("tempo") || Regex.IsMatch(cuerpo, @"^(//[^\n]*\n\s*)+tempo")) ultimo = cuerpo;
    }
    return ultimo;
}

// El chat muestra solo la explicacion; la cancion se ve en el panel
static string QuitarBloques(string texto) =>
    Regex.Replace(texto, @"```[a-zA-Z]*[ \t]*\r?\n.*?```", "", RegexOptions.Singleline).Trim();
