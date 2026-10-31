using System.Collections.Generic;
using System.IO;

public class Midi
{
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
    Pista actual;   // la pista donde se agregan los eventos ahora

    List<System.Action> grabando = null;   // si no es null, estamos definiendo un motivo

    public void EmpezarGrabacion(List<System.Action> destino) { grabando = destino; }

    public void TerminarGrabacion() { grabando = null; }

    public void Tocar(List<System.Action> contenido)
    {
        foreach (var accion in contenido) accion();
    }

    public Midi()
    {
        actual = new Pista(0);
        pistas.Add(actual);
    }

    int siguienteCanal = 1;   // el canal 0 lo usa la primera pista

    // Empieza una pista nueva; devuelve false si ya no quedan canales
    public bool NuevaPista(bool percusion)
    {
        int canal;
        if (percusion)
        {
            canal = 9;                              // canal 10: batería
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

    // Una nota sola es un acorde de una nota
    public void AgregarNota(int nota, int duracion)
    {
        AgregarAcorde(new List<int> { nota }, duracion);
    }

    public void AgregarSilencio(int duracion)
    {
        if (grabando != null) { grabando.Add(() => AgregarSilencio(duracion)); return; }
        actual.esperaPendiente += duracion;
    }

    public void CambiarInstrumento(int programa)
    {
        if (grabando != null) { grabando.Add(() => CambiarInstrumento(programa)); return; }
        EscribirDelta(actual.esperaPendiente);
        actual.esperaPendiente = 0;
        actual.eventos.Add((byte)(0xC0 | actual.canal));   // Program Change en el canal de la pista
        actual.eventos.Add((byte)programa);
    }

    public void AgregarAcorde(List<int> notas, int duracion)
    {

        if (grabando != null) { grabando.Add(() => AgregarAcorde(notas, duracion)); return; }
        // Prender todas: la primera espera los silencios, el resto delta 0
        EscribirDelta(actual.esperaPendiente);
        actual.esperaPendiente = 0;
        for (int i = 0; i < notas.Count; i++)
        {
            if (i > 0) EscribirDelta(0);
            Evento(0x90, notas[i], 100);
        }

        // Apagar todas: la primera después de la duración, el resto delta 0
        for (int i = 0; i < notas.Count; i++)
        {
            EscribirDelta(i == 0 ? duracion : 0);
            Evento(0x80, notas[i], 0);
        }
    }

    public void Guardar(string ruta)
    {
        using var f = new BinaryWriter(File.Create(ruta));

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