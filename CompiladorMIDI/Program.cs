using System;

if (args.Length == 0)
{
    Console.WriteLine("Uso: dotnet run -- cancion.txt");
    return;
}

var scanner = new Scanner(args[0]);
var parser = new Parser(scanner);
parser.Parse();

if (parser.errors.count == 0)
{
    parser.midi.Guardar("salida.mid");
    Console.WriteLine("Listo: se generó salida.mid");
}
else
{
    Console.WriteLine($"Se encontraron {parser.errors.count} error(es).");
}