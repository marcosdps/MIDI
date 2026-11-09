using System.Collections.Generic;
using System.Globalization;



using System;



public class Parser {
	public const int _EOF = 0;
	public const int _numero = 1;
	public const int _real = 2;
	public const int _ident = 3;
	public const int maxT = 63;

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

  // Busca un nombre y controla que este declarado y sea del tipo esperado
  Simbolo Usar(string nombre, Tipo esperado) {
    Simbolo s = tabla.Buscar(nombre);
    if (s == null) {
      SemErr(nombre + " no esta declarado");
      return null;
    }
    if (s.Tipo != esperado) {
      SemErr(nombre + " es de tipo " + Simbolo.NombreTipo(s.Tipo)
             + "; se esperaba " + Simbolo.NombreTipo(esperado));
      return null;
    }
    return s;
  }



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
		Expect(4);
		Expect(1);
		midi.Tempo = int.Parse(t.val);
		if (midi.Tempo <= 0) SemErr("el tempo debe ser mayor que 0"); 
		while (StartOf(1)) {
			Declaracion();
		}
		Bloque();
		while (la.kind == 45 || la.kind == 46) {
			Bloque();
		}
	}

	void Declaracion() {
		string nombre; int linea; double n;
		List<double> ac; List<Elemento> pasos;
		Simbolo s = null; 
		if (la.kind == 5) {
			Get();
			Nombre(out nombre, out linea);
			Expect(6);
			NotaValor(out n);
			s = new Simbolo(nombre, Tipo.Nota, linea);
			s.Nota = n; 
		} else if (la.kind == 7) {
			Get();
			Nombre(out nombre, out linea);
			Expect(6);
			AcordeValor(out ac);
			s = new Simbolo(nombre, Tipo.Acorde, linea);
			s.Acorde = ac; 
		} else if (la.kind == 8) {
			Get();
			Nombre(out nombre, out linea);
			Expect(6);
			Secuencia(out pasos);
			s = new Simbolo(nombre, Tipo.Secuencia, linea);
			s.Pasos = pasos; 
		} else if (la.kind == 9) {
			Get();
			Nombre(out nombre, out linea);
			Expect(6);
			SecuenciaGolpes(out pasos);
			s = new Simbolo(nombre, Tipo.Golpes, linea);
			s.Pasos = pasos; 
		} else if (la.kind == 10) {
			Get();
			Nombre(out nombre, out linea);
			Expect(6);
			Expect(1);
			s = new Simbolo(nombre, Tipo.Entero, linea);
			s.Entero = int.Parse(t.val); 
		} else SynErr(64);
		Expect(11);
		tabla.Insertar(s); 
	}

	void Bloque() {
		if (la.kind == 45) {
			Get();
			if (!midi.NuevaPista(false))
			 SemErr("no quedan canales para mas pistas"); 
			if (la.kind == 49) {
				Instrumento();
			}
			Expect(25);
			if (StartOf(2)) {
				ItemMelodico();
				while (la.kind == 23) {
					Get();
					ItemMelodico();
				}
			}
			Expect(26);
		} else if (la.kind == 46) {
			Get();
			midi.NuevaPista(true); 
			if (la.kind == 57) {
				Kit();
			}
			Expect(25);
			if (StartOf(3)) {
				ItemPercusion();
				while (la.kind == 23) {
					Get();
					ItemPercusion();
				}
			}
			Expect(26);
		} else SynErr(65);
	}

	void Nombre(out string nombre, out int linea) {
		Expect(3);
		nombre = t.val; linea = t.line;
		if (tabla.Buscar(nombre) != null)
		 SemErr(nombre + " ya fue declarado"); 
	}

	void NotaValor(out double n) {
		Simbolo s; n = 0; 
		if (StartOf(4)) {
			Tono(out n);
		} else if (la.kind == 1 || la.kind == 2) {
			Frecuencia(out n);
		} else if (la.kind == 3) {
			Get();
			s = Usar(t.val, Tipo.Nota);
			if (s != null) n = s.Nota; 
		} else SynErr(66);
	}

	void AcordeValor(out List<double> ac) {
		Simbolo s; ac = new List<double>(); 
		if (la.kind == 22) {
			AcordeLit(out ac);
		} else if (la.kind == 3) {
			Get();
			s = Usar(t.val, Tipo.Acorde);
			if (s != null) ac = s.Acorde; 
		} else SynErr(67);
	}

	void Secuencia(out List<Elemento> pasos) {
		Elemento e; pasos = new List<Elemento>(); 
		Expect(25);
		if (StartOf(5)) {
			Paso(out e);
			pasos.Add(e); 
			while (la.kind == 23) {
				Get();
				Paso(out e);
				pasos.Add(e); 
			}
		}
		Expect(26);
	}

	void SecuenciaGolpes(out List<Elemento> pasos) {
		Elemento e; pasos = new List<Elemento>(); 
		Expect(25);
		if (StartOf(6)) {
			PasoGolpe(out e);
			pasos.Add(e); 
			while (la.kind == 23) {
				Get();
				PasoGolpe(out e);
				pasos.Add(e); 
			}
		}
		Expect(26);
	}

	void Tono(out double n) {
		int altura, alteracion, octava; n = 0; 
		Altura(out altura);
		Alteracion(out alteracion);
		Expect(1);
		octava = int.Parse(t.val);
		n = 12 * (octava + 1) + altura + alteracion;
		if (n < 0 || n > 127)
		 SemErr("nota fuera del rango MIDI"); 
	}

	void Frecuencia(out double n) {
		double f = 0; n = 0; 
		if (la.kind == 1) {
			Get();
		} else if (la.kind == 2) {
			Get();
		} else SynErr(68);
		f = double.Parse(t.val, CultureInfo.InvariantCulture); 
		Expect(12);
		n = 69 + 12 * Math.Log2(f / 440.0);
		if (f <= 0 || Math.Round(n) < 0 || Math.Round(n) > 127)
		 SemErr("frecuencia fuera del rango MIDI (8.2 a 12543 hz)"); 
	}

	void Altura(out int n) {
		n = 0; 
		switch (la.kind) {
		case 13: {
			Get();
			n = 0; 
			break;
		}
		case 14: {
			Get();
			n = 2; 
			break;
		}
		case 15: {
			Get();
			n = 4; 
			break;
		}
		case 16: {
			Get();
			n = 5; 
			break;
		}
		case 17: {
			Get();
			n = 7; 
			break;
		}
		case 18: {
			Get();
			n = 9; 
			break;
		}
		case 19: {
			Get();
			n = 11; 
			break;
		}
		default: SynErr(69); break;
		}
	}

	void Alteracion(out int a) {
		a = 0; 
		if (la.kind == 20 || la.kind == 21) {
			if (la.kind == 20) {
				Get();
				a = 1; 
			} else {
				Get();
				a = -1; 
			}
		}
	}

	void AcordeLit(out List<double> ac) {
		double n; ac = new List<double>(); 
		Expect(22);
		NotaValor(out n);
		ac.Add(n); 
		while (la.kind == 23) {
			Get();
			NotaValor(out n);
			ac.Add(n); 
		}
		Expect(24);
		if (!Midi.MismaDesafinacion(ac))
		 SemErr("un acorde no puede mezclar notas con distinta desafinacion"); 
	}

	void Paso(out Elemento e) {
		List<double> notas; int dur; e = null; 
		if (StartOf(7)) {
			Sonido(out notas);
			Duracion(out dur);
			e = new Elemento(notas, dur); 
		} else if (la.kind == 27) {
			Get();
			Duracion(out dur);
			e = new Elemento(new List<double>(), dur); 
		} else SynErr(70);
	}

	void Sonido(out List<double> notas) {
		double n; Simbolo s; notas = new List<double>(); 
		if (StartOf(4)) {
			Tono(out n);
			notas.Add(n); 
		} else if (la.kind == 1 || la.kind == 2) {
			Frecuencia(out n);
			notas.Add(n); 
		} else if (la.kind == 22) {
			AcordeLit(out notas);
		} else if (la.kind == 3) {
			Get();
			s = tabla.Buscar(t.val);
			if (s == null)
			 SemErr(t.val + " no esta declarado");
			else if (s.Tipo == Tipo.Nota)
			 notas.Add(s.Nota);
			else if (s.Tipo == Tipo.Acorde)
			 notas.AddRange(s.Acorde);
			else
			 SemErr(t.val + " es de tipo " + Simbolo.NombreTipo(s.Tipo)
			        + "; se esperaba nota o acorde"); 
		} else SynErr(71);
	}

	void Duracion(out int d) {
		d = 0; 
		if (la.kind == 28) {
			Get();
			d = 1920; 
		} else if (la.kind == 29) {
			Get();
			d = 960; 
		} else if (la.kind == 30) {
			Get();
			d = 480; 
		} else if (la.kind == 31) {
			Get();
			d = 240; 
		} else SynErr(72);
	}

	void PasoGolpe(out Elemento e) {
		List<double> sonidos = new List<double>(); int n, dur; e = null; 
		if (StartOf(8)) {
			if (StartOf(9)) {
				Percusion(out n);
				sonidos.Add(n); 
			} else {
				Get();
				Percusion(out n);
				sonidos.Add(n); 
				while (la.kind == 23) {
					Get();
					Percusion(out n);
					sonidos.Add(n); 
				}
				Expect(24);
			}
			Duracion(out dur);
			e = new Elemento(sonidos, dur); 
		} else if (la.kind == 27) {
			Get();
			Duracion(out dur);
			e = new Elemento(new List<double>(), dur); 
		} else SynErr(73);
	}

	void Percusion(out int n) {
		n = 36; 
		switch (la.kind) {
		case 32: {
			Get();
			n = 36; 
			break;
		}
		case 33: {
			Get();
			n = 37; 
			break;
		}
		case 34: {
			Get();
			n = 38; 
			break;
		}
		case 35: {
			Get();
			n = 39; 
			break;
		}
		case 36: {
			Get();
			n = 42; 
			break;
		}
		case 37: {
			Get();
			n = 45; 
			break;
		}
		case 38: {
			Get();
			n = 46; 
			break;
		}
		case 39: {
			Get();
			n = 47; 
			break;
		}
		case 40: {
			Get();
			n = 49; 
			break;
		}
		case 41: {
			Get();
			n = 50; 
			break;
		}
		case 42: {
			Get();
			n = 51; 
			break;
		}
		case 43: {
			Get();
			n = 54; 
			break;
		}
		case 44: {
			Get();
			n = 56; 
			break;
		}
		default: SynErr(74); break;
		}
	}

	void Instrumento() {
		int p = 0; 
		Expect(49);
		if (StartOf(10)) {
			NombreInstrumento(out p);
		} else if (la.kind == 1) {
			Get();
			p = int.Parse(t.val) - 1;
			if (p < 0 || p > 127)
			 SemErr("el instrumento debe estar entre 1 y 128"); 
		} else SynErr(75);
		midi.CambiarInstrumento(p); 
	}

	void ItemMelodico() {
		Elemento e; 
		if (StartOf(5)) {
			Paso(out e);
			midi.Tocar(e); 
		} else if (la.kind == 47) {
			Tocar(Tipo.Secuencia);
		} else SynErr(76);
	}

	void Kit() {
		int k = 0; 
		Expect(57);
		if (la.kind == 58) {
			Get();
			k = 0; 
		} else if (la.kind == 59) {
			Get();
			k = 24; 
		} else if (la.kind == 60) {
			Get();
			k = 25; 
		} else if (la.kind == 61) {
			Get();
			k = 32; 
		} else if (la.kind == 62) {
			Get();
			k = 40; 
		} else SynErr(77);
		midi.CambiarInstrumento(k); 
	}

	void ItemPercusion() {
		Elemento e; 
		if (StartOf(6)) {
			PasoGolpe(out e);
			midi.Tocar(e); 
		} else if (la.kind == 47) {
			Tocar(Tipo.Golpes);
		} else SynErr(78);
	}

	void Tocar(Tipo esperado) {
		Simbolo s; int veces = 1; 
		Expect(47);
		Expect(3);
		s = Usar(t.val, esperado); 
		if (la.kind == 1 || la.kind == 3) {
			Cantidad(out veces);
			Expect(48);
		}
		if (s != null)
		 for (int i = 0; i < veces; i++)
		   foreach (Elemento e in s.Pasos) midi.Tocar(e); 
	}

	void Cantidad(out int n) {
		Simbolo s; n = 1; 
		if (la.kind == 1) {
			Get();
			n = int.Parse(t.val); 
		} else if (la.kind == 3) {
			Get();
			s = Usar(t.val, Tipo.Entero);
			if (s != null) n = s.Entero; 
		} else SynErr(79);
		if (n < 1)
		 SemErr("la cantidad de repeticiones debe ser al menos 1"); 
	}

	void NombreInstrumento(out int p) {
		p = 0; 
		switch (la.kind) {
		case 50: {
			Get();
			p = 0; 
			break;
		}
		case 51: {
			Get();
			p = 19; 
			break;
		}
		case 52: {
			Get();
			p = 24; 
			break;
		}
		case 53: {
			Get();
			p = 33; 
			break;
		}
		case 54: {
			Get();
			p = 40; 
			break;
		}
		case 55: {
			Get();
			p = 56; 
			break;
		}
		case 56: {
			Get();
			p = 73; 
			break;
		}
		default: SynErr(80); break;
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
		{_T,_x,_x,_x, _x,_x,_x,_x, _x,_x,_x,_x, _x,_x,_x,_x, _x,_x,_x,_x, _x,_x,_x,_x, _x,_x,_x,_x, _x,_x,_x,_x, _x,_x,_x,_x, _x,_x,_x,_x, _x,_x,_x,_x, _x,_x,_x,_x, _x,_x,_x,_x, _x,_x,_x,_x, _x,_x,_x,_x, _x,_x,_x,_x, _x},
		{_x,_x,_x,_x, _x,_T,_x,_T, _T,_T,_T,_x, _x,_x,_x,_x, _x,_x,_x,_x, _x,_x,_x,_x, _x,_x,_x,_x, _x,_x,_x,_x, _x,_x,_x,_x, _x,_x,_x,_x, _x,_x,_x,_x, _x,_x,_x,_x, _x,_x,_x,_x, _x,_x,_x,_x, _x,_x,_x,_x, _x,_x,_x,_x, _x},
		{_x,_T,_T,_T, _x,_x,_x,_x, _x,_x,_x,_x, _x,_T,_T,_T, _T,_T,_T,_T, _x,_x,_T,_x, _x,_x,_x,_T, _x,_x,_x,_x, _x,_x,_x,_x, _x,_x,_x,_x, _x,_x,_x,_x, _x,_x,_x,_T, _x,_x,_x,_x, _x,_x,_x,_x, _x,_x,_x,_x, _x,_x,_x,_x, _x},
		{_x,_x,_x,_x, _x,_x,_x,_x, _x,_x,_x,_x, _x,_x,_x,_x, _x,_x,_x,_x, _x,_x,_T,_x, _x,_x,_x,_T, _x,_x,_x,_x, _T,_T,_T,_T, _T,_T,_T,_T, _T,_T,_T,_T, _T,_x,_x,_T, _x,_x,_x,_x, _x,_x,_x,_x, _x,_x,_x,_x, _x,_x,_x,_x, _x},
		{_x,_x,_x,_x, _x,_x,_x,_x, _x,_x,_x,_x, _x,_T,_T,_T, _T,_T,_T,_T, _x,_x,_x,_x, _x,_x,_x,_x, _x,_x,_x,_x, _x,_x,_x,_x, _x,_x,_x,_x, _x,_x,_x,_x, _x,_x,_x,_x, _x,_x,_x,_x, _x,_x,_x,_x, _x,_x,_x,_x, _x,_x,_x,_x, _x},
		{_x,_T,_T,_T, _x,_x,_x,_x, _x,_x,_x,_x, _x,_T,_T,_T, _T,_T,_T,_T, _x,_x,_T,_x, _x,_x,_x,_T, _x,_x,_x,_x, _x,_x,_x,_x, _x,_x,_x,_x, _x,_x,_x,_x, _x,_x,_x,_x, _x,_x,_x,_x, _x,_x,_x,_x, _x,_x,_x,_x, _x,_x,_x,_x, _x},
		{_x,_x,_x,_x, _x,_x,_x,_x, _x,_x,_x,_x, _x,_x,_x,_x, _x,_x,_x,_x, _x,_x,_T,_x, _x,_x,_x,_T, _x,_x,_x,_x, _T,_T,_T,_T, _T,_T,_T,_T, _T,_T,_T,_T, _T,_x,_x,_x, _x,_x,_x,_x, _x,_x,_x,_x, _x,_x,_x,_x, _x,_x,_x,_x, _x},
		{_x,_T,_T,_T, _x,_x,_x,_x, _x,_x,_x,_x, _x,_T,_T,_T, _T,_T,_T,_T, _x,_x,_T,_x, _x,_x,_x,_x, _x,_x,_x,_x, _x,_x,_x,_x, _x,_x,_x,_x, _x,_x,_x,_x, _x,_x,_x,_x, _x,_x,_x,_x, _x,_x,_x,_x, _x,_x,_x,_x, _x,_x,_x,_x, _x},
		{_x,_x,_x,_x, _x,_x,_x,_x, _x,_x,_x,_x, _x,_x,_x,_x, _x,_x,_x,_x, _x,_x,_T,_x, _x,_x,_x,_x, _x,_x,_x,_x, _T,_T,_T,_T, _T,_T,_T,_T, _T,_T,_T,_T, _T,_x,_x,_x, _x,_x,_x,_x, _x,_x,_x,_x, _x,_x,_x,_x, _x,_x,_x,_x, _x},
		{_x,_x,_x,_x, _x,_x,_x,_x, _x,_x,_x,_x, _x,_x,_x,_x, _x,_x,_x,_x, _x,_x,_x,_x, _x,_x,_x,_x, _x,_x,_x,_x, _T,_T,_T,_T, _T,_T,_T,_T, _T,_T,_T,_T, _T,_x,_x,_x, _x,_x,_x,_x, _x,_x,_x,_x, _x,_x,_x,_x, _x,_x,_x,_x, _x},
		{_x,_x,_x,_x, _x,_x,_x,_x, _x,_x,_x,_x, _x,_x,_x,_x, _x,_x,_x,_x, _x,_x,_x,_x, _x,_x,_x,_x, _x,_x,_x,_x, _x,_x,_x,_x, _x,_x,_x,_x, _x,_x,_x,_x, _x,_x,_x,_x, _x,_x,_T,_T, _T,_T,_T,_T, _T,_x,_x,_x, _x,_x,_x,_x, _x}

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
			case 2: s = "real expected"; break;
			case 3: s = "ident expected"; break;
			case 4: s = "\"tempo\" expected"; break;
			case 5: s = "\"nota\" expected"; break;
			case 6: s = "\"=\" expected"; break;
			case 7: s = "\"acorde\" expected"; break;
			case 8: s = "\"secuencia\" expected"; break;
			case 9: s = "\"golpes\" expected"; break;
			case 10: s = "\"entero\" expected"; break;
			case 11: s = "\";\" expected"; break;
			case 12: s = "\"hz\" expected"; break;
			case 13: s = "\"Do\" expected"; break;
			case 14: s = "\"Re\" expected"; break;
			case 15: s = "\"Mi\" expected"; break;
			case 16: s = "\"Fa\" expected"; break;
			case 17: s = "\"Sol\" expected"; break;
			case 18: s = "\"La\" expected"; break;
			case 19: s = "\"Si\" expected"; break;
			case 20: s = "\"#\" expected"; break;
			case 21: s = "\"b\" expected"; break;
			case 22: s = "\"[\" expected"; break;
			case 23: s = "\",\" expected"; break;
			case 24: s = "\"]\" expected"; break;
			case 25: s = "\"{\" expected"; break;
			case 26: s = "\"}\" expected"; break;
			case 27: s = "\"silencio\" expected"; break;
			case 28: s = "\"redonda\" expected"; break;
			case 29: s = "\"blanca\" expected"; break;
			case 30: s = "\"negra\" expected"; break;
			case 31: s = "\"corchea\" expected"; break;
			case 32: s = "\"bombo\" expected"; break;
			case 33: s = "\"aro\" expected"; break;
			case 34: s = "\"caja\" expected"; break;
			case 35: s = "\"palmas\" expected"; break;
			case 36: s = "\"hihat\" expected"; break;
			case 37: s = "\"tomgrave\" expected"; break;
			case 38: s = "\"hihatabierto\" expected"; break;
			case 39: s = "\"tommedio\" expected"; break;
			case 40: s = "\"crash\" expected"; break;
			case 41: s = "\"tomagudo\" expected"; break;
			case 42: s = "\"ride\" expected"; break;
			case 43: s = "\"pandereta\" expected"; break;
			case 44: s = "\"cencerro\" expected"; break;
			case 45: s = "\"pista\" expected"; break;
			case 46: s = "\"bateria\" expected"; break;
			case 47: s = "\"tocar\" expected"; break;
			case 48: s = "\"veces\" expected"; break;
			case 49: s = "\"instrumento\" expected"; break;
			case 50: s = "\"piano\" expected"; break;
			case 51: s = "\"organo\" expected"; break;
			case 52: s = "\"guitarra\" expected"; break;
			case 53: s = "\"bajo\" expected"; break;
			case 54: s = "\"violin\" expected"; break;
			case 55: s = "\"trompeta\" expected"; break;
			case 56: s = "\"flauta\" expected"; break;
			case 57: s = "\"kit\" expected"; break;
			case 58: s = "\"acustica\" expected"; break;
			case 59: s = "\"electronica\" expected"; break;
			case 60: s = "\"ritmos\" expected"; break;
			case 61: s = "\"jazz\" expected"; break;
			case 62: s = "\"escobillas\" expected"; break;
			case 63: s = "??? expected"; break;
			case 64: s = "invalid Declaracion"; break;
			case 65: s = "invalid Bloque"; break;
			case 66: s = "invalid NotaValor"; break;
			case 67: s = "invalid AcordeValor"; break;
			case 68: s = "invalid Frecuencia"; break;
			case 69: s = "invalid Altura"; break;
			case 70: s = "invalid Paso"; break;
			case 71: s = "invalid Sonido"; break;
			case 72: s = "invalid Duracion"; break;
			case 73: s = "invalid PasoGolpe"; break;
			case 74: s = "invalid Percusion"; break;
			case 75: s = "invalid Instrumento"; break;
			case 76: s = "invalid ItemMelodico"; break;
			case 77: s = "invalid Kit"; break;
			case 78: s = "invalid ItemPercusion"; break;
			case 79: s = "invalid Cantidad"; break;
			case 80: s = "invalid NombreInstrumento"; break;

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
