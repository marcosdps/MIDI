using System.Collections.Generic;



using System;



public class Parser {
	public const int _EOF = 0;
	public const int _numero = 1;
	public const int _ident = 2;
	public const int maxT = 53;

	const bool _T = true;
	const bool _x = false;
	const int minErrDist = 2;
	
	public Scanner scanner;
	public Errors  errors;

	public Token t;    // last recognized token
	public Token la;   // lookahead token
	int errDist = minErrDist;

public Midi midi = new Midi();
  public TablaSimbolos tabla = new TablaSimbolos();



	public Parser(Scanner scanner) {
		this.scanner = scanner;
		errors = new Errors();
	}

	void SynErr (int n) {
		if (errDist >= minErrDist) errors.SynErr(la.line, la.col, n);
		errDist = 0;
	}

	public void SemErr (string msg) {
		if (errDist >= minErrDist) errors.SemErr(t.line, t.col, msg);
		errDist = 0;
	}
	
	void Get () {
		for (;;) {
			t = la;
			la = scanner.Scan();
			if (la.kind <= maxT) { ++errDist; break; }

			la = t;
		}
	}
	
	void Expect (int n) {
		if (la.kind==n) Get(); else { SynErr(n); }
	}
	
	bool StartOf (int s) {
		return set[s, la.kind];
	}
	
	void ExpectWeak (int n, int follow) {
		if (la.kind == n) Get();
		else {
			SynErr(n);
			while (!StartOf(follow)) Get();
		}
	}


	bool WeakSeparator(int n, int syFol, int repFol) {
		int kind = la.kind;
		if (kind == n) {Get(); return true;}
		else if (StartOf(repFol)) {return false;}
		else {
			SynErr(n);
			while (!(set[syFol, kind] || set[repFol, kind] || set[0, kind])) {
				Get();
				kind = la.kind;
			}
			return StartOf(syFol);
		}
	}

	
	void Cancion() {
		Expect(3);
		Expect(1);
		midi.Tempo = int.Parse(t.val); 
		while (la.kind == 6 || la.kind == 7) {
			Definicion();
		}
		Contenido();
		while (la.kind == 4 || la.kind == 5) {
			if (la.kind == 4) {
				Get();
				if (!midi.NuevaPista(false))
				 SemErr("no quedan canales para mas pistas"); 
				Contenido();
			} else {
				Get();
				midi.NuevaPista(true); 
				if (la.kind == 25) {
					Kit();
				}
				ContenidoBateria();
			}
		}
	}

	void Definicion() {
		Simbolo s = null; 
		if (la.kind == 6) {
			Get();
			Nombre(TipoMotivo.Melodico, out s);
			Contenido();
		} else if (la.kind == 7) {
			Get();
			Nombre(TipoMotivo.Percusion, out s);
			ContenidoBateria();
		} else SynErr(54);
		Expect(8);
		midi.TerminarGrabacion();
		tabla.Insertar(s); 
	}

	void Contenido() {
		while (StartOf(1)) {
			if (StartOf(2)) {
				Nota();
			} else if (la.kind == 39) {
				Silencio();
			} else if (la.kind == 31) {
				Instrumento();
			} else if (la.kind == 10) {
				Acorde();
			} else {
				Tocar(TipoMotivo.Melodico);
			}
		}
	}

	void Kit() {
		int k = 0; 
		Expect(25);
		if (la.kind == 26) {
			Get();
			k = 0; 
		} else if (la.kind == 27) {
			Get();
			k = 24; 
		} else if (la.kind == 28) {
			Get();
			k = 25; 
		} else if (la.kind == 29) {
			Get();
			k = 32; 
		} else if (la.kind == 30) {
			Get();
			k = 40; 
		} else SynErr(55);
		midi.CambiarInstrumento(k); 
	}

	void ContenidoBateria() {
		while (StartOf(3)) {
			if (StartOf(4)) {
				Golpe();
			} else if (la.kind == 10) {
				Golpes();
			} else if (la.kind == 39) {
				Silencio();
			} else {
				Tocar(TipoMotivo.Percusion);
			}
		}
	}

	void Nota() {
		int nota, dur; 
		Tono(out nota);
		Duracion(out dur);
		midi.AgregarNota(nota, dur); 
	}

	void Silencio() {
		int dur; 
		Expect(39);
		Duracion(out dur);
		midi.AgregarSilencio(dur); 
	}

	void Instrumento() {
		int p = 0; 
		Expect(31);
		if (StartOf(5)) {
			NombreInstrumento(out p);
		} else if (la.kind == 1) {
			Get();
			p = int.Parse(t.val) - 1;
			if (p < 0 || p > 127)
			 SemErr("el instrumento debe estar entre 1 y 128"); 
		} else SynErr(56);
		midi.CambiarInstrumento(p); 
	}

	void Acorde() {
		int nota, dur;
		List<int> notas = new List<int>(); 
		Expect(10);
		Tono(out nota);
		notas.Add(nota); 
		while (StartOf(2)) {
			Tono(out nota);
			notas.Add(nota); 
		}
		Expect(11);
		Duracion(out dur);
		midi.AgregarAcorde(notas, dur); 
	}

	void Tocar(TipoMotivo esperado) {
		Expect(9);
		Expect(2);
		Simbolo s = tabla.Buscar(t.val);
		if (s == null)
		 SemErr("el motivo " + t.val + " no esta definido");
		else if (s.Tipo != esperado)
		 SemErr("el motivo " + t.val + " no es del tipo de esta pista");
		else
		 midi.Tocar(s.Contenido); 
	}

	void Golpe() {
		int n, dur; 
		Percusion(out n);
		Duracion(out dur);
		midi.AgregarNota(n, dur); 
	}

	void Golpes() {
		int n, dur;
		List<int> sonidos = new List<int>(); 
		Expect(10);
		Percusion(out n);
		sonidos.Add(n); 
		while (StartOf(4)) {
			Percusion(out n);
			sonidos.Add(n); 
		}
		Expect(11);
		Duracion(out dur);
		midi.AgregarAcorde(sonidos, dur); 
	}

	void Nombre(TipoMotivo tipo, out Simbolo s) {
		Expect(2);
		if (tabla.Buscar(t.val) != null)
		 SemErr("el motivo " + t.val + " ya fue definido");
		s = new Simbolo(t.val, tipo, t.line);
		midi.EmpezarGrabacion(s.Contenido); 
	}

	void Percusion(out int n) {
		n = 36; 
		switch (la.kind) {
		case 12: {
			Get();
			n = 36; 
			break;
		}
		case 13: {
			Get();
			n = 37; 
			break;
		}
		case 14: {
			Get();
			n = 38; 
			break;
		}
		case 15: {
			Get();
			n = 39; 
			break;
		}
		case 16: {
			Get();
			n = 42; 
			break;
		}
		case 17: {
			Get();
			n = 45; 
			break;
		}
		case 18: {
			Get();
			n = 46; 
			break;
		}
		case 19: {
			Get();
			n = 47; 
			break;
		}
		case 20: {
			Get();
			n = 49; 
			break;
		}
		case 21: {
			Get();
			n = 50; 
			break;
		}
		case 22: {
			Get();
			n = 51; 
			break;
		}
		case 23: {
			Get();
			n = 54; 
			break;
		}
		case 24: {
			Get();
			n = 56; 
			break;
		}
		default: SynErr(57); break;
		}
	}

	void Duracion(out int d) {
		d = 0; 
		if (la.kind == 49) {
			Get();
			d = 1920; 
		} else if (la.kind == 50) {
			Get();
			d = 960; 
		} else if (la.kind == 51) {
			Get();
			d = 480; 
		} else if (la.kind == 52) {
			Get();
			d = 240; 
		} else SynErr(58);
	}

	void Tono(out int nota) {
		int altura, alteracion, octava; nota = 0; 
		Altura(out altura);
		Alteracion(out alteracion);
		Expect(1);
		octava = int.Parse(t.val);
		nota = 12 * (octava + 1) + altura + alteracion; 
	}

	void Altura(out int n) {
		n = 0; 
		switch (la.kind) {
		case 42: {
			Get();
			n = 0; 
			break;
		}
		case 43: {
			Get();
			n = 2; 
			break;
		}
		case 44: {
			Get();
			n = 4; 
			break;
		}
		case 45: {
			Get();
			n = 5; 
			break;
		}
		case 46: {
			Get();
			n = 7; 
			break;
		}
		case 47: {
			Get();
			n = 9; 
			break;
		}
		case 48: {
			Get();
			n = 11; 
			break;
		}
		default: SynErr(59); break;
		}
	}

	void Alteracion(out int a) {
		a = 0; 
		if (la.kind == 40 || la.kind == 41) {
			if (la.kind == 40) {
				Get();
				a = 1; 
			} else {
				Get();
				a = -1; 
			}
		}
	}

	void NombreInstrumento(out int p) {
		p = 0; 
		switch (la.kind) {
		case 32: {
			Get();
			p = 0; 
			break;
		}
		case 33: {
			Get();
			p = 19; 
			break;
		}
		case 34: {
			Get();
			p = 24; 
			break;
		}
		case 35: {
			Get();
			p = 33; 
			break;
		}
		case 36: {
			Get();
			p = 40; 
			break;
		}
		case 37: {
			Get();
			p = 56; 
			break;
		}
		case 38: {
			Get();
			p = 73; 
			break;
		}
		default: SynErr(60); break;
		}
	}



	public void Parse() {
		la = new Token();
		la.val = "";		
		Get();
		Cancion();
		Expect(0);

	}
	
	static readonly bool[,] set = {
		{_T,_x,_x,_x, _x,_x,_x,_x, _x,_x,_x,_x, _x,_x,_x,_x, _x,_x,_x,_x, _x,_x,_x,_x, _x,_x,_x,_x, _x,_x,_x,_x, _x,_x,_x,_x, _x,_x,_x,_x, _x,_x,_x,_x, _x,_x,_x,_x, _x,_x,_x,_x, _x,_x,_x},
		{_x,_x,_x,_x, _x,_x,_x,_x, _x,_T,_T,_x, _x,_x,_x,_x, _x,_x,_x,_x, _x,_x,_x,_x, _x,_x,_x,_x, _x,_x,_x,_T, _x,_x,_x,_x, _x,_x,_x,_T, _x,_x,_T,_T, _T,_T,_T,_T, _T,_x,_x,_x, _x,_x,_x},
		{_x,_x,_x,_x, _x,_x,_x,_x, _x,_x,_x,_x, _x,_x,_x,_x, _x,_x,_x,_x, _x,_x,_x,_x, _x,_x,_x,_x, _x,_x,_x,_x, _x,_x,_x,_x, _x,_x,_x,_x, _x,_x,_T,_T, _T,_T,_T,_T, _T,_x,_x,_x, _x,_x,_x},
		{_x,_x,_x,_x, _x,_x,_x,_x, _x,_T,_T,_x, _T,_T,_T,_T, _T,_T,_T,_T, _T,_T,_T,_T, _T,_x,_x,_x, _x,_x,_x,_x, _x,_x,_x,_x, _x,_x,_x,_T, _x,_x,_x,_x, _x,_x,_x,_x, _x,_x,_x,_x, _x,_x,_x},
		{_x,_x,_x,_x, _x,_x,_x,_x, _x,_x,_x,_x, _T,_T,_T,_T, _T,_T,_T,_T, _T,_T,_T,_T, _T,_x,_x,_x, _x,_x,_x,_x, _x,_x,_x,_x, _x,_x,_x,_x, _x,_x,_x,_x, _x,_x,_x,_x, _x,_x,_x,_x, _x,_x,_x},
		{_x,_x,_x,_x, _x,_x,_x,_x, _x,_x,_x,_x, _x,_x,_x,_x, _x,_x,_x,_x, _x,_x,_x,_x, _x,_x,_x,_x, _x,_x,_x,_x, _T,_T,_T,_T, _T,_T,_T,_x, _x,_x,_x,_x, _x,_x,_x,_x, _x,_x,_x,_x, _x,_x,_x}

	};
} // end Parser


public class Errors {
	public int count = 0;                                    // number of errors detected
	public System.IO.TextWriter errorStream = Console.Out;   // error messages go to this stream
	public string errMsgFormat = "-- line {0} col {1}: {2}"; // 0=line, 1=column, 2=text

	public virtual void SynErr (int line, int col, int n) {
		string s;
		switch (n) {
			case 0: s = "EOF expected"; break;
			case 1: s = "numero expected"; break;
			case 2: s = "ident expected"; break;
			case 3: s = "\"tempo\" expected"; break;
			case 4: s = "\"pista\" expected"; break;
			case 5: s = "\"bateria\" expected"; break;
			case 6: s = "\"motivo\" expected"; break;
			case 7: s = "\"ritmo\" expected"; break;
			case 8: s = "\"fin\" expected"; break;
			case 9: s = "\"tocar\" expected"; break;
			case 10: s = "\"[\" expected"; break;
			case 11: s = "\"]\" expected"; break;
			case 12: s = "\"bombo\" expected"; break;
			case 13: s = "\"aro\" expected"; break;
			case 14: s = "\"caja\" expected"; break;
			case 15: s = "\"palmas\" expected"; break;
			case 16: s = "\"hihat\" expected"; break;
			case 17: s = "\"tomgrave\" expected"; break;
			case 18: s = "\"hihatabierto\" expected"; break;
			case 19: s = "\"tommedio\" expected"; break;
			case 20: s = "\"crash\" expected"; break;
			case 21: s = "\"tomagudo\" expected"; break;
			case 22: s = "\"ride\" expected"; break;
			case 23: s = "\"pandereta\" expected"; break;
			case 24: s = "\"cencerro\" expected"; break;
			case 25: s = "\"kit\" expected"; break;
			case 26: s = "\"acustica\" expected"; break;
			case 27: s = "\"electronica\" expected"; break;
			case 28: s = "\"ritmos\" expected"; break;
			case 29: s = "\"jazz\" expected"; break;
			case 30: s = "\"escobillas\" expected"; break;
			case 31: s = "\"instrumento\" expected"; break;
			case 32: s = "\"piano\" expected"; break;
			case 33: s = "\"organo\" expected"; break;
			case 34: s = "\"guitarra\" expected"; break;
			case 35: s = "\"bajo\" expected"; break;
			case 36: s = "\"violin\" expected"; break;
			case 37: s = "\"trompeta\" expected"; break;
			case 38: s = "\"flauta\" expected"; break;
			case 39: s = "\"silencio\" expected"; break;
			case 40: s = "\"#\" expected"; break;
			case 41: s = "\"b\" expected"; break;
			case 42: s = "\"Do\" expected"; break;
			case 43: s = "\"Re\" expected"; break;
			case 44: s = "\"Mi\" expected"; break;
			case 45: s = "\"Fa\" expected"; break;
			case 46: s = "\"Sol\" expected"; break;
			case 47: s = "\"La\" expected"; break;
			case 48: s = "\"Si\" expected"; break;
			case 49: s = "\"redonda\" expected"; break;
			case 50: s = "\"blanca\" expected"; break;
			case 51: s = "\"negra\" expected"; break;
			case 52: s = "\"corchea\" expected"; break;
			case 53: s = "??? expected"; break;
			case 54: s = "invalid Definicion"; break;
			case 55: s = "invalid Kit"; break;
			case 56: s = "invalid Instrumento"; break;
			case 57: s = "invalid Percusion"; break;
			case 58: s = "invalid Duracion"; break;
			case 59: s = "invalid Altura"; break;
			case 60: s = "invalid NombreInstrumento"; break;

			default: s = "error " + n; break;
		}
		errorStream.WriteLine(errMsgFormat, line, col, s);
		count++;
	}

	public virtual void SemErr (int line, int col, string s) {
		errorStream.WriteLine(errMsgFormat, line, col, s);
		count++;
	}
	
	public virtual void SemErr (string s) {
		errorStream.WriteLine(s);
		count++;
	}
	
	public virtual void Warning (int line, int col, string s) {
		errorStream.WriteLine(errMsgFormat, line, col, s);
	}
	
	public virtual void Warning(string s) {
		errorStream.WriteLine(s);
	}
} // Errors


public class FatalError: Exception {
	public FatalError(string m): base(m) {}
}
