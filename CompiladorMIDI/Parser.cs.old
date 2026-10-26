
using System;



public class Parser {
	public const int _EOF = 0;
	public const int _numero = 1;
	public const int maxT = 14;

	const bool _T = true;
	const bool _x = false;
	const int minErrDist = 2;
	
	public Scanner scanner;
	public Errors  errors;

	public Token t;    // last recognized token
	public Token la;   // lookahead token
	int errDist = minErrDist;

public Midi midi = new Midi();



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
		Expect(2);
		Expect(1);
		midi.Tempo = int.Parse(t.val); 
		while (StartOf(1)) {
			Nota();
		}
	}

	void Nota() {
		int altura, octava, dur; 
		Altura(out altura);
		Expect(1);
		octava = int.Parse(t.val); 
		Duracion(out dur);
		midi.AgregarNota(12 * (octava + 1) + altura, dur); 
	}

	void Altura(out int n) {
		n = 0; 
		switch (la.kind) {
		case 3: {
			Get();
			n = 0; 
			break;
		}
		case 4: {
			Get();
			n = 2; 
			break;
		}
		case 5: {
			Get();
			n = 4; 
			break;
		}
		case 6: {
			Get();
			n = 5; 
			break;
		}
		case 7: {
			Get();
			n = 7; 
			break;
		}
		case 8: {
			Get();
			n = 9; 
			break;
		}
		case 9: {
			Get();
			n = 11; 
			break;
		}
		default: SynErr(15); break;
		}
	}

	void Duracion(out int d) {
		d = 0; 
		if (la.kind == 10) {
			Get();
			d = 1920; 
		} else if (la.kind == 11) {
			Get();
			d = 960; 
		} else if (la.kind == 12) {
			Get();
			d = 480; 
		} else if (la.kind == 13) {
			Get();
			d = 240; 
		} else SynErr(16);
	}



	public void Parse() {
		la = new Token();
		la.val = "";		
		Get();
		Cancion();
		Expect(0);

	}
	
	static readonly bool[,] set = {
		{_T,_x,_x,_x, _x,_x,_x,_x, _x,_x,_x,_x, _x,_x,_x,_x},
		{_x,_x,_x,_T, _T,_T,_T,_T, _T,_T,_x,_x, _x,_x,_x,_x}

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
			case 2: s = "\"tempo\" expected"; break;
			case 3: s = "\"Do\" expected"; break;
			case 4: s = "\"Re\" expected"; break;
			case 5: s = "\"Mi\" expected"; break;
			case 6: s = "\"Fa\" expected"; break;
			case 7: s = "\"Sol\" expected"; break;
			case 8: s = "\"La\" expected"; break;
			case 9: s = "\"Si\" expected"; break;
			case 10: s = "\"redonda\" expected"; break;
			case 11: s = "\"blanca\" expected"; break;
			case 12: s = "\"negra\" expected"; break;
			case 13: s = "\"corchea\" expected"; break;
			case 14: s = "??? expected"; break;
			case 15: s = "invalid Altura"; break;
			case 16: s = "invalid Duracion"; break;

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
