# Compilador MIDI como servicio

Chat con una IA -> `cancion.txt` en un panel -> botón **Componer** -> `salida.mid` descargable.

## Cómo correrlo (con Gemini, gratis)

1. Entrá a https://aistudio.google.com/apikey con tu cuenta de Google y creá una API key (no pide tarjeta).
2. En PowerShell, dentro de esta carpeta:

```powershell
$env:GEMINI_API_KEY = "tu-key"
dotnet run
```

3. Abrí http://localhost:5000 (o la URL que muestre la consola; se cambia con `--urls http://localhost:8080`).

Requiere el .NET 10 SDK. La key vive solo en el servidor (variable de entorno, o `dotnet user-secrets set Gemini:ApiKey "tu-key"`), nunca en el navegador.

El modo consola de antes sigue andando: `dotnet run -- cancion.txt` genera `salida.mid`.

## Cambiar de IA (appsettings.json)

| Ajuste | Valor |
|---|---|
| `"Proveedor"` | `"gemini"` (gratis, por defecto) o `"anthropic"` (Claude, de pago: usa `ANTHROPIC_API_KEY`) |
| `"Gemini": { "Model" }` | Modelo de Gemini. Por defecto `gemini-flash-latest`. Si da error de modelo o de cuota, elegí en AI Studio un modelo Flash disponible en el nivel gratuito y poné su nombre acá |
| `"MaxReintentos"` | Cuántas veces se le pide a la IA que corrija una canción con errores (2 por defecto) |

El nivel gratuito de Gemini tiene límites por minuto y por día (se ven en tu consola de AI Studio). Si se superan, el chat avisa y hay que esperar.

## Archivos

| Archivo | Qué hace |
|---|---|
| `Program.cs` | Servidor web: `POST /api/chat`, `POST /api/componer`, archivos estáticos y modo consola |
| `Compilador.cs` | Envuelve Scanner/Parser: compila un texto en memoria y devuelve errores estructurados (línea, columna, mensaje) o los bytes del .mid |
| `ClienteIA.cs` | Base común de los proveedores de IA |
| `ClienteGemini.cs` / `ClienteClaude.cs` | Llamadas a cada API |
| `musico.md` | System prompt: el lenguaje Cancion explicado a la IA + criterio musical + ejemplo. Se edita sin recompilar |
| `wwwroot/index.html` | Interfaz: chat, panel con números de línea, Componer, errores, descarga y reproductor |
| `Midi.cs` | Cambiado: + `ObtenerBytes()` (el .mid en memoria) y límites de tamaño (`LimiteExcedido`) |
| `TablaSimbolos.cs` | Cambiado: + `Lista()` |
| `Cancion.ATG`, `Parser.cs`, `Scanner.cs` | **Sin cambios**: no hace falta volver a correr Coco |

## Qué pasa si la canción tiene errores

1. **Antes de mostrarla**: el servidor compila lo que escribió la IA; si falla, le devuelve los errores (línea, columna, mensaje) y le pide corregir, hasta `MaxReintentos` veces.
2. **Si igual queda con errores**: el panel marca las líneas en rojo, lista los errores (clic = salta a la línea) y no genera el .mid. Se puede editar a mano o usar **Pedirle a la IA que lo corrija**.
3. **Si el usuario edita a mano** y después pide cambios por el chat, la IA recibe la versión editada.

## Límites de seguridad

Chat limitado a 12 pedidos por minuto por IP, canción de hasta 60.000 caracteres, historial de 20 mensajes, y tope de pasos/bytes en `Midi.cs` para que `tocar x 2000000000 veces` no agote la memoria.
