using System;
using System.Collections.Generic;

public enum TipoMotivo { Melodico, Percusion }

// Una entrada de la tabla: un nombre con sus atributos
public class Simbolo
{
    public string Nombre;
    public TipoMotivo Tipo;
    public int Linea;                                      // dónde se definió
    public List<Action> Contenido = new List<Action>();   // lo que hay que tocar

    public Simbolo(string nombre, TipoMotivo tipo, int linea)
    {
        Nombre = nombre; Tipo = tipo; Linea = linea;
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

    // Devuelve el símbolo, o null si no está definido
    public Simbolo Buscar(string nombre)
    {
        return tabla.TryGetValue(nombre, out var s) ? s : null;
    }

    public void Mostrar()
    {
        Console.WriteLine("Tabla de simbolos:");
        Console.WriteLine("  Nombre          Tipo        Linea  Elementos");
        foreach (var s in tabla.Values)
            Console.WriteLine($"  {s.Nombre,-14}  {s.Tipo,-10}  {s.Linea,5}  {s.Contenido.Count,9}");
    }
}