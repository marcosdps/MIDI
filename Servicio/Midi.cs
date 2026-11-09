using System;
using System.Collections.Generic;
using System.IO;

// Se lanza cuando la cancion pide mas trabajo del permitido (ej: "tocar x 2000000000 veces")
public class LimiteExcedido : Exception
{
    public LimiteExcedido(string mensaje) : base(mensaje) { }
}

public class Midi
{
    const int MaxOperaciones = 100_000;      // pasos tocados en total
    const int MaxBytesEventos = 4_000_000;   // tamano maximo de los eventos
    int operaciones = 0;
    int bytesEstimados = 0;

    // Lo que guarda cada pista: sus eventos, su espera acumulada y su canal
    class Pista
    {
        public List<byte> eventos = new List<byte>();
        public int esperaPendiente = 0;
        public int canal;
        public Pista(int canal) { this.canal = canal; }
    }

    public int Tempo = 120;
    const int TicksPorNegra = 480;
    readonly List<Pista> pistas = new List<Pista>();
    Pista actual;               // la pista donde se agregan los eventos ahora
    int siguienteCanal = 0;     // primer canal libre para pistas melódicas

    // Empieza una pista nueva; devuelve false si ya no quedan canales
    public bool NuevaPista(bool percusion)
    {
        int canal;
        if (percusion)
        {
            canal = 9;                                  // canal 10: batería
        }
        else
        {
            if (siguienteCanal == 9) siguienteCanal++;  // saltea el de batería
            if (siguienteCanal > 15) return false;
            canal = siguienteCanal++;
        }
        actual = new Pista(canal);
        pistas.Add(actual);
        return true;
    }

    // Cuánto se aparta una nota del semitono más cercano (0 si es menos de 1 cent)
    public static double Desvio(double nota)
    {
        double d = nota - Math.Round(nota);
        return Math.Abs(d) < 0.01 ? 0 : d;
    }

    // Todas las notas de un acorde tienen que tener la misma desafinación,
    // porque el Pitch Bend afecta a todo el canal
    public static bool MismaDesafinacion(List<double> notas)
    {
        foreach (double n in notas)
            if (Math.Abs(Desvio(n) - Desvio(notas[0])) >= 0.01) return false;
        return true;
    }

    // Toca un paso de una secuencia: un silencio, una nota o un acorde
    public void Tocar(Elemento e)
    {
        if (e == null || actual == null) return;   // hubo un error antes
        if (++operaciones > MaxOperaciones)
            throw new LimiteExcedido("la cancion es demasiado larga (mas de " + MaxOperaciones + " pasos)");
        bytesEstimados += 8 * e.Notas.Count;
        if (bytesEstimados > MaxBytesEventos)
            throw new LimiteExcedido("la cancion es demasiado grande");
        if (e.Notas.Count == 0)
            actual.esperaPendiente += e.Duracion;  // silencio: solo suma espera
        else
            AgregarSonido(e.Notas, e.Duracion);
    }

    public void CambiarInstrumento(int programa)
    {
        if (actual == null) return;
        EscribirDelta(actual.esperaPendiente);
        actual.esperaPendiente = 0;
        actual.eventos.Add((byte)(0xC0 | actual.canal));   // Program Change
        actual.eventos.Add((byte)programa);
    }

    void AgregarSonido(List<double> notas, int duracion)
    {
        double desvio = Desvio(notas[0]);

        // Esperar los silencios acumulados
        EscribirDelta(actual.esperaPendiente);
        actual.esperaPendiente = 0;

        // Si la nota está desafinada, primero se desafina el canal
        if (desvio != 0)
        {
            PitchBend(desvio);
            EscribirDelta(0);
        }

        // Prender todas las notas a la vez
        for (int i = 0; i < notas.Count; i++)
        {
            if (i > 0) EscribirDelta(0);
            Evento(0x90, (int)Math.Round(notas[i]), 100);
        }

        // Apagarlas: la primera después de la duración, el resto a la vez
        for (int i = 0; i < notas.Count; i++)
        {
            EscribirDelta(i == 0 ? duracion : 0);
            Evento(0x80, (int)Math.Round(notas[i]), 0);
        }

        // Volver el canal a la afinación normal
        if (desvio != 0)
        {
            EscribirDelta(0);
            PitchBend(0);
        }
    }

    // Pitch Bend: 8192 es el centro; con el rango estándar de ±2 semitonos,
    // un semitono equivale a 4096
    void PitchBend(double semitonos)
    {
        int v = 8192 + (int)Math.Round(semitonos * 4096);
        if (v < 0) v = 0;
        if (v > 16383) v = 16383;
        actual.eventos.Add((byte)(0xE0 | actual.canal));
        actual.eventos.Add((byte)(v & 0x7F));          // 7 bits bajos
        actual.eventos.Add((byte)((v >> 7) & 0x7F));   // 7 bits altos
    }

    public int CantidadPistas => pistas.Count;

    public void Guardar(string ruta)
    {
        File.WriteAllBytes(ruta, ObtenerBytes());
    }

    // Igual que Guardar, pero devuelve el archivo .mid en memoria (lo usa el servicio web)
    public byte[] ObtenerBytes()
    {
        var ms = new MemoryStream();
        var f = new BinaryWriter(ms);

        // Cabecera: formato 1 = varias pistas que suenan a la vez
        f.Write(new byte[] { (byte)'M', (byte)'T', (byte)'h', (byte)'d' });
        Escribir32(f, 6);
        Escribir16(f, 1);
        Escribir16(f, pistas.Count);
        Escribir16(f, TicksPorNegra);

        for (int i = 0; i < pistas.Count; i++)
        {
            var p = pistas[i];
            var datos = new List<byte>();

            // El tempo va solo en la primera pista
            if (i == 0)
            {
                int microseg = 60000000 / Tempo;
                datos.AddRange(new byte[] { 0x00, 0xFF, 0x51, 0x03,
                    (byte)(microseg >> 16), (byte)(microseg >> 8), (byte)microseg });
            }

            datos.AddRange(p.eventos);

            // Fin de pista, después del silencio final si lo hay
            datos.AddRange(Delta(p.esperaPendiente));
            datos.AddRange(new byte[] { 0xFF, 0x2F, 0x00 });

            f.Write(new byte[] { (byte)'M', (byte)'T', (byte)'r', (byte)'k' });
            Escribir32(f, datos.Count);
            f.Write(datos.ToArray());
        }

        f.Flush();
        return ms.ToArray();
    }

    // Escribe un evento de nota en el canal de la pista actual
    void Evento(int tipo, int nota, int velocidad)
    {
        actual.eventos.Add((byte)(tipo | actual.canal));
        actual.eventos.Add((byte)nota);
        actual.eventos.Add((byte)velocidad);
    }

    void EscribirDelta(int valor)
    {
        actual.eventos.AddRange(Delta(valor));
    }

    // Los tiempos MIDI usan 7 bits por byte; el bit alto indica que sigue otro byte
    static List<byte> Delta(int valor)
    {
        var bytes = new Stack<byte>();
        bytes.Push((byte)(valor & 0x7F));
        valor >>= 7;
        while (valor > 0)
        {
            bytes.Push((byte)((valor & 0x7F) | 0x80));
            valor >>= 7;
        }
        return new List<byte>(bytes);
    }

    // MIDI guarda los números con el byte más significativo primero
    static void Escribir32(BinaryWriter f, int v)
    {
        f.Write((byte)(v >> 24)); f.Write((byte)(v >> 16));
        f.Write((byte)(v >> 8));  f.Write((byte)v);
    }

    static void Escribir16(BinaryWriter f, int v)
    {
        f.Write((byte)(v >> 8)); f.Write((byte)v);
    }
}
