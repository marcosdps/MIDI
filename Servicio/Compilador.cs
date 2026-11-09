using System.Text;
using System.Text.RegularExpressions;

// Recolecta los errores del parser en una lista en vez de imprimirlos por consola
class RecolectorErrores : Errors
{
    public readonly List<ErrorCompilacion> Lista = new();

    public RecolectorErrores()
    {
        errorStream = new StringWriter();
        errMsgFormat = "{0}\t{1}\t{2}";      // linea, columna, texto
    }

    void Guardar(string tipo)
    {
        var sw = (StringWriter)errorStream;
        var partes = sw.ToString().TrimEnd('\r', '\n').Split('\t', 3);
        sw.GetStringBuilder().Clear();
        if (partes.Length == 3)
            Lista.Add(new ErrorCompilacion(int.Parse(partes[0]), int.Parse(partes[1]), tipo,
                                           Compilador.Traducir(partes[2])));
    }

    public override void SynErr(int line, int col, int n)
    {
        base.SynErr(line, col, n);
        Guardar("sintactico");
    }

    public override void SemErr(int line, int col, string s)
    {
        base.SemErr(line, col, s);
        Guardar("semantico");
    }

    public void Agregar(int linea, int col, string tipo, string mensaje)
    {
        Lista.Add(new ErrorCompilacion(linea, col, tipo, mensaje));
        count++;
    }
}

public static class Compilador
{
    public const int MaxCaracteres = 60_000;

    public static ResultadoCompilacion Compilar(string fuente)
    {
        var errores = new RecolectorErrores();
        var simbolos = new List<SimboloInfo>();
        Parser? parser = null;
        try
        {
            // Con BOM el Scanner decodifica UTF-8 (acentos en los comentarios, etc.)
            var bytes = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes(fuente)).ToArray();
            using var ms = new MemoryStream(bytes);
            parser = new Parser(new Scanner(ms));
            parser.errors = errores;
            parser.Parse();

            if (errores.count == 0 && (parser.midi.Tempo < 20 || parser.midi.Tempo > 300))
                errores.Agregar(1, 1, "semantico", "el tempo debe estar entre 20 y 300");
        }
        catch (LimiteExcedido ex)
        {
            errores.Agregar(1, 1, "semantico", ex.Message);
        }
        catch (Exception ex) when (ex is FormatException or OverflowException)
        {
            // Si ya hubo errores de sintaxis, esto es solo consecuencia de ellos: no se agrega ruido
            if (errores.count == 0)
                errores.Agregar(1, 1, "semantico", "hay un numero demasiado grande");
        }
        catch (FatalError ex)
        {
            errores.Agregar(1, 1, "sintactico", ex.Message);
        }
        catch (Exception ex)
        {
            // un fallo inesperado del compilador no debe tirar el servicio
            Console.Error.WriteLine(ex);
            errores.Agregar(1, 1, "interno", "error interno del compilador: " + ex.Message);
        }

        if (parser != null)
            simbolos = parser.tabla.Lista()
                .Select(s => new SimboloInfo(s.Nombre, Simbolo.NombreTipo(s.Tipo), s.Linea, s.Valor()))
                .ToList();

        if (errores.count > 0)
            return new ResultadoCompilacion(false, AgregarPistas(errores.Lista, fuente), null, simbolos, 0);

        return new ResultadoCompilacion(true, new(), parser!.midi.ObtenerBytes(), simbolos,
                                        parser.midi.CantidadPistas);
    }

    static readonly Regex Percusion = new(@"\b(bombo|aro|caja|palmas|hihat|hihatabierto|tomgrave|tommedio|tomagudo|crash|ride|pandereta|cencerro)\b");
    static readonly Regex NotaMusical = new(@"\b(Do|Re|Mi|Fa|Sol|La|Si)\b");

    // Errores tipicos de la IA: mezclar percusion con notas. Se aclara en el mensaje.
    static List<ErrorCompilacion> AgregarPistas(List<ErrorCompilacion> lista, string fuente)
    {
        var lineas = fuente.Replace("\r", "").Split('\n');
        return lista.Select(e =>
        {
            if (e.Linea < 1 || e.Linea > lineas.Length) return e;
            string l = lineas[e.Linea - 1].TrimStart();
            if (l.StartsWith("secuencia") && Percusion.IsMatch(l))
                return e with { Mensaje = e.Mensaje + ". CAUSA PROBABLE: esta linea declara una 'secuencia' pero usa instrumentos de percusion (bombo, hihat, caja...). Los ritmos de bateria se declaran con 'golpes', no con 'secuencia': cambiar la palabra 'secuencia' por 'golpes' en esta declaracion" };
            if (l.StartsWith("golpes") && NotaMusical.IsMatch(l))
                return e with { Mensaje = e.Mensaje + ". CAUSA PROBABLE: esta linea declara 'golpes' pero usa notas musicales. Las notas van en una 'secuencia'; 'golpes' solo admite percusion" };
            return e;
        }).ToList();
    }

    // Pistas para los "invalid X" de Coco/R, segun la produccion de la gramatica donde fallo
    static readonly Dictionary<string, string> Ayudas = new()
    {
        ["Duracion"] = "Falta la duracion o es invalida: usar redonda, blanca, negra o corchea",
        ["Paso"] = "Se esperaba un paso (nota, acorde o silencio seguido de una duracion); revisar si sobra una coma antes de la llave de cierre",
        ["PasoGolpe"] = "Se esperaba un golpe de percusion con su duracion, o silencio con su duracion; revisar si sobra una coma al final",
        ["Altura"] = "Se esperaba una nota: Do, Re, Mi, Fa, Sol, La o Si (con mayuscula inicial)",
        ["Sonido"] = "Se esperaba una nota, un acorde, una variable o una frecuencia en hz",
        ["Percusion"] = "Se esperaba un instrumento de percusion (bombo, aro, caja, palmas, hihat, hihatabierto, tomgrave, tommedio, tomagudo, crash, ride, pandereta, cencerro)",
        ["Declaracion"] = "Se esperaba una declaracion (nota, acorde, secuencia, golpes, entero) o un bloque pista/bateria",
        ["Bloque"] = "Se esperaba pista o bateria",
        ["ItemMelodico"] = "Se esperaba un paso (nota + duracion) o tocar una secuencia",
        ["ItemPercusion"] = "Se esperaba un golpe (percusion + duracion) o tocar una variable de golpes",
        ["Instrumento"] = "Se esperaba un nombre de instrumento (piano, organo, guitarra, bajo, violin, trompeta, flauta) o un numero de 1 a 128",
        ["NombreInstrumento"] = "Instrumento desconocido: usar piano, organo, guitarra, bajo, violin, trompeta, flauta o un numero de 1 a 128",
        ["Kit"] = "Kit desconocido: usar acustica, electronica, ritmos, jazz o escobillas",
        ["Cantidad"] = "Se esperaba un numero o una variable entero antes de veces",
        ["NotaValor"] = "Se esperaba una nota (ej. Do 4), una frecuencia (ej. 440 hz) o una variable de tipo nota",
        ["AcordeValor"] = "Se esperaba un acorde entre corchetes o una variable de tipo acorde",
        ["Frecuencia"] = "Se esperaba un numero seguido de hz",
    };

    // Mensajes de Coco/R (en ingles) a algo mas claro
    public static string Traducir(string m)
    {
        if (m == "EOF expected") return "Se esperaba el fin del archivo: sobra texto despues de la ultima pista";
        if (m == "??? expected") return "Simbolo inesperado";
        var r = Regex.Match(m, "^(.+) expected$");
        if (r.Success)
        {
            string x = r.Groups[1].Value;
            if (x == "numero") x = "un numero";
            else if (x == "ident") x = "un nombre de variable (si ya escribiste uno, puede ser una palabra reservada)";
            else if (x == "real") x = "un numero con decimales";
            return "Se esperaba " + x;
        }
        r = Regex.Match(m, "^invalid (.+)$");
        if (r.Success)
            return Ayudas.TryGetValue(r.Groups[1].Value, out var ayuda)
                ? ayuda
                : "Lo escrito no es valido en este punto (se estaba leyendo: " + r.Groups[1].Value + ")";
        r = Regex.Match(m, "^this symbol not expected in (.+)$");
        if (r.Success) return "Simbolo inesperado en " + r.Groups[1].Value;
        return m;
    }

    // Texto que se le manda a la IA para que corrija la cancion
    public static string DescribirErrores(IEnumerable<ErrorCompilacion> errores)
    {
        var sb = new StringBuilder();
        foreach (var e in errores.Take(15))
            sb.AppendLine($"- linea {e.Linea}, columna {e.Columna} ({e.Tipo}): {e.Mensaje}");
        return sb.ToString();
    }
}
