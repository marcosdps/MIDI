using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

public enum Tipo { Nota, Acorde, Secuencia, Golpes, Entero }

// Un paso de una secuencia: uno o varios sonidos con una duración.
// Si no tiene sonidos, es un silencio.
public class Elemento
{
    public List<double> Notas;   // números MIDI (pueden tener decimales: desafinación)
    public int Duracion;         // en ticks

    public Elemento(List<double> notas, int duracion)
    {
        Notas = notas; Duracion = duracion;
    }
}

// Una entrada de la tabla: una variable con su tipo y su valor
public class Simbolo
{
    public string Nombre;
    public Tipo Tipo;
    public int Linea;

    // El valor: se usa solo el campo que corresponde al tipo
    public double Nota;               // tipo nota
    public List<double> Acorde;       // tipo acorde
    public List<Elemento> Pasos;      // tipos secuencia y golpes
    public int Entero;                // tipo entero

    public Simbolo(string nombre, Tipo tipo, int linea)
    {
        Nombre = nombre; Tipo = tipo; Linea = linea;
    }

    public static string NombreTipo(Tipo t)
    {
        return t.ToString().ToLower();
    }

    public string Valor()
    {
        switch (Tipo)
        {
            case Tipo.Nota:      return Numero(Nota);
            case Tipo.Acorde:    return "[" + string.Join(", ", Acorde.Select(Numero)) + "]";
            case Tipo.Secuencia:
            case Tipo.Golpes:    return Pasos.Count + " pasos";
            default:             return Entero.ToString();
        }
    }

    static string Numero(double n)
    {
        return n.ToString("0.##", CultureInfo.InvariantCulture);
    }
}

public class TablaSimbolos
{
    readonly Dictionary<string, Simbolo> tabla = new Dictionary<string, Simbolo>();

    // Agrega el símbolo; si el nombre ya existe, se queda con el primero
    public void Insertar(Simbolo s)
    {
        if (s != null && !tabla.ContainsKey(s.Nombre))
            tabla.Add(s.Nombre, s);
    }

    // Devuelve el símbolo, o null si no está declarado
    public Simbolo Buscar(string nombre)
    {
        return tabla.TryGetValue(nombre, out var s) ? s : null;
    }

    public IEnumerable<Simbolo> Lista()
    {
        return tabla.Values;
    }

    public void Mostrar()
    {
        Console.WriteLine("Tabla de simbolos:");
        Console.WriteLine("  Nombre        Tipo        Linea  Valor");
        foreach (var s in tabla.Values)
            Console.WriteLine($"  {s.Nombre,-12}  {Simbolo.NombreTipo(s.Tipo),-10}  {s.Linea,5}  {s.Valor()}");
    }
}
