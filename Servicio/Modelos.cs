// Datos que viajan entre el navegador y el servidor

public record MensajeChat(string Rol, string Contenido);
public record PeticionChat(List<MensajeChat> Mensajes);
public record PeticionComponer(string Cancion);

public record ErrorCompilacion(int Linea, int Columna, string Tipo, string Mensaje);
public record SimboloInfo(string Nombre, string Tipo, int Linea, string Valor);
public record ResultadoCompilacion(bool Ok, List<ErrorCompilacion> Errores, byte[]? Midi,
                                   List<SimboloInfo> Simbolos, int Pistas);
