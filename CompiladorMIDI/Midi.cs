using System.Collections.Generic;
using System.IO;

public class Midi
{
    public int Tempo = 120;
    const int TicksPorNegra = 480;
    readonly List<byte> eventos = new List<byte>();

    public void AgregarNota(int nota, int duracion)
    {
        // Note-On: empieza ya, canal 1, velocidad 100
        EscribirDelta(0);
        eventos.Add(0x90); eventos.Add((byte)nota); eventos.Add(100);

        // Note-Off: termina después de 'duracion' ticks
        EscribirDelta(duracion);
        eventos.Add(0x80); eventos.Add((byte)nota); eventos.Add(0);
    }

    public void Guardar(string ruta)
    {
        var pista = new List<byte>();

        // Evento de tempo: microsegundos por negra
        int microseg = 60000000 / Tempo;
        pista.AddRange(new byte[] { 0x00, 0xFF, 0x51, 0x03,
            (byte)(microseg >> 16), (byte)(microseg >> 8), (byte)microseg });

        pista.AddRange(eventos);

        // Fin de pista
        pista.AddRange(new byte[] { 0x00, 0xFF, 0x2F, 0x00 });

        using var f = new BinaryWriter(File.Create(ruta));

        // Cabecera del archivo
        f.Write(new byte[] { (byte)'M', (byte)'T', (byte)'h', (byte)'d' });
        Escribir32(f, 6);
        Escribir16(f, 0); // formato 0: una sola pista
        Escribir16(f, 1); // cantidad de pistas
        Escribir16(f, TicksPorNegra);

        // Pista
        f.Write(new byte[] { (byte)'M', (byte)'T', (byte)'r', (byte)'k' });
        Escribir32(f, pista.Count);
        f.Write(pista.ToArray());
    }

    // Los tiempos MIDI usan 7 bits por byte; el bit alto indica que sigue otro byte
    void EscribirDelta(int valor)
    {
        var bytes = new Stack<byte>();
        bytes.Push((byte)(valor & 0x7F));
        valor >>= 7;
        while (valor > 0)
        {
            bytes.Push((byte)((valor & 0x7F) | 0x80));
            valor >>= 7;
        }
        eventos.AddRange(bytes);
    }

    // MIDI guarda los números con el byte más significativo primero
    static void Escribir32(BinaryWriter f, int v)
    {
        f.Write((byte)(v >> 24)); f.Write((byte)(v >> 16));
        f.Write((byte)(v >> 8)); f.Write((byte)v);
    }

    static void Escribir16(BinaryWriter f, int v)
    {
        f.Write((byte)(v >> 8)); f.Write((byte)v);
    }
}