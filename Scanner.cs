
using System;
using System.IO;
using System.Collections;



public class Token {
	public int kind;    // token kind
	public int pos;     // token position in bytes in the source text (starting at 0)
	public int charPos;  // token position in characters in the source text (starting at 0)
	public int col;     // token column (starting at 1)
	public int line;    // token line (starting at 1)
	public string val;  // token value
	public Token next;  // ML 2005-03-11 Tokens are kept in linked list
}

//-----------------------------------------------------------------------------------
// Buffer
//-----------------------------------------------------------------------------------
public class Buffer {
	// This Buffer supports the following cases:
	// 1) seekable stream (file)
	//    a) whole stream in buffer
	//    b) part of stream in buffer
	// 2) non seekable stream (network, console)

	public const int EOF = char.MaxValue + 1;
	const int MIN_BUFFER_LENGTH = 1024; // 1KB
	const int MAX_BUFFER_LENGTH = MIN_BUFFER_LENGTH * 64; // 64KB
	byte[] buf;         // input buffer
	int bufStart;       // position of first byte in buffer relative to input stream
	int bufLen;         // length of buffer
	int fileLen;        // length of input stream (may change if the stream is no file)
	int bufPos;         // current position in buffer
	Stream stream;      // input stream (seekable)
	bool isUserStream;  // was the stream opened by the user?
	
	public Buffer (Stream s, bool isUserStream) {
		stream = s; this.isUserStream = isUserStream;
		
		if (stream.CanSeek) {
			fileLen = (int) stream.Length;
			bufLen = Math.Min(fileLen, MAX_BUFFER_LENGTH);
			bufStart = Int32.MaxValue; // nothing in the buffer so far
		} else {
			fileLen = bufLen = bufStart = 0;
		}

		buf = new byte[(bufLen>0) ? bufLen : MIN_BUFFER_LENGTH];
		if (fileLen > 0) Pos = 0; // setup buffer to position 0 (start)
		else bufPos = 0; // index 0 is already after the file, thus Pos = 0 is invalid
		if (bufLen == fileLen && stream.CanSeek) Close();
	}
	
	protected Buffer(Buffer b) { // called in UTF8Buffer constructor
		buf = b.buf;
		bufStart = b.bufStart;
		bufLen = b.bufLen;
		fileLen = b.fileLen;
		bufPos = b.bufPos;
		stream = b.stream;
		// keep destructor from closing the stream
		b.stream = null;
		isUserStream = b.isUserStream;
	}

	~Buffer() { Close(); }
	
	protected void Close() {
		if (!isUserStream && stream != null) {
			stream.Close();
			stream = null;
		}
	}
	
	public virtual int Read () {
		if (bufPos < bufLen) {
			return buf[bufPos++];
		} else if (Pos < fileLen) {
			Pos = Pos; // shift buffer start to Pos
			return buf[bufPos++];
		} else if (stream != null && !stream.CanSeek && ReadNextStreamChunk() > 0) {
			return buf[bufPos++];
		} else {
			return EOF;
		}
	}

	public int Peek () {
		int curPos = Pos;
		int ch = Read();
		Pos = curPos;
		return ch;
	}
	
	// beg .. begin, zero-based, inclusive, in byte
	// end .. end, zero-based, exclusive, in byte
	public string GetString (int beg, int end) {
		int len = 0;
		char[] buf = new char[end - beg];
		int oldPos = Pos;
		Pos = beg;
		while (Pos < end) buf[len++] = (char) Read();
		Pos = oldPos;
		return new String(buf, 0, len);
	}

	public int Pos {
		get { return bufPos + bufStart; }
		set {
			if (value >= fileLen && stream != null && !stream.CanSeek) {
				// Wanted position is after buffer and the stream
				// is not seek-able e.g. network or console,
				// thus we have to read the stream manually till
				// the wanted position is in sight.
				while (value >= fileLen && ReadNextStreamChunk() > 0);
			}

			if (value < 0 || value > fileLen) {
				throw new FatalError("buffer out of bounds access, position: " + value);
			}

			if (value >= bufStart && value < bufStart + bufLen) { // already in buffer
				bufPos = value - bufStart;
			} else if (stream != null) { // must be swapped in
				stream.Seek(value, SeekOrigin.Begin);
				bufLen = stream.Read(buf, 0, buf.Length);
				bufStart = value; bufPos = 0;
			} else {
				// set the position to the end of the file, Pos will return fileLen.
				bufPos = fileLen - bufStart;
			}
		}
	}
	
	// Read the next chunk of bytes from the stream, increases the buffer
	// if needed and updates the fields fileLen and bufLen.
	// Returns the number of bytes read.
	private int ReadNextStreamChunk() {
		int free = buf.Length - bufLen;
		if (free == 0) {
			// in the case of a growing input stream
			// we can neither seek in the stream, nor can we
			// foresee the maximum length, thus we must adapt
			// the buffer size on demand.
			byte[] newBuf = new byte[bufLen * 2];
			Array.Copy(buf, newBuf, bufLen);
			buf = newBuf;
			free = bufLen;
		}
		int read = stream.Read(buf, bufLen, free);
		if (read > 0) {
			fileLen = bufLen = (bufLen + read);
			return read;
		}
		// end of stream reached
		return 0;
	}
}

//-----------------------------------------------------------------------------------
// UTF8Buffer
//-----------------------------------------------------------------------------------
public class UTF8Buffer: Buffer {
	public UTF8Buffer(Buffer b): base(b) {}

	public override int Read() {
		int ch;
		do {
			ch = base.Read();
			// until we find a utf8 start (0xxxxxxx or 11xxxxxx)
		} while ((ch >= 128) && ((ch & 0xC0) != 0xC0) && (ch != EOF));
		if (ch < 128 || ch == EOF) {
			// nothing to do, first 127 chars are the same in ascii and utf8
			// 0xxxxxxx or end of file character
		} else if ((ch & 0xF0) == 0xF0) {
			// 11110xxx 10xxxxxx 10xxxxxx 10xxxxxx
			int c1 = ch & 0x07; ch = base.Read();
			int c2 = ch & 0x3F; ch = base.Read();
			int c3 = ch & 0x3F; ch = base.Read();
			int c4 = ch & 0x3F;
			ch = (((((c1 << 6) | c2) << 6) | c3) << 6) | c4;
		} else if ((ch & 0xE0) == 0xE0) {
			// 1110xxxx 10xxxxxx 10xxxxxx
			int c1 = ch & 0x0F; ch = base.Read();
			int c2 = ch & 0x3F; ch = base.Read();
			int c3 = ch & 0x3F;
			ch = (((c1 << 6) | c2) << 6) | c3;
		} else if ((ch & 0xC0) == 0xC0) {
			// 110xxxxx 10xxxxxx
			int c1 = ch & 0x1F; ch = base.Read();
			int c2 = ch & 0x3F;
			ch = (c1 << 6) | c2;
		}
		return ch;
	}
}

//-----------------------------------------------------------------------------------
// Scanner
//-----------------------------------------------------------------------------------
public class Scanner {
	const char EOL = '\n';
	const int eofSym = 0; /* pdt */
	const int maxT = 53;
	const int noSym = 53;


	public Buffer buffer; // scanner buffer
	
	Token t;          // current token
	int ch;           // current input character
	int pos;          // byte position of current character
	int charPos;      // position by unicode characters starting with 0
	int col;          // column number of current character
	int line;         // line number of current character
	int oldEols;      // EOLs that appeared in a comment;
	static readonly Hashtable start; // maps first token character to start state

	Token tokens;     // list of tokens already peeked (first token is a dummy)
	Token pt;         // current peek token
	
	char[] tval = new char[128]; // text of current token
	int tlen;         // length of current token
	
	static Scanner() {
		start = new Hashtable(128);
		for (int i = 48; i <= 57; ++i) start[i] = 1;
		start[64] = 2; 
		start[116] = 210; 
		start[112] = 211; 
		start[98] = 212; 
		start[109] = 16; 
		start[114] = 213; 
		start[102] = 214; 
		start[91] = 27; 
		start[93] = 28; 
		start[97] = 215; 
		start[99] = 216; 
		start[104] = 217; 
		start[107] = 84; 
		start[101] = 218; 
		start[106] = 105; 
		start[105] = 118; 
		start[111] = 132; 
		start[103] = 138; 
		start[118] = 148; 
		start[115] = 166; 
		start[35] = 174; 
		start[68] = 175; 
		start[82] = 177; 
		start[77] = 179; 
		start[70] = 181; 
		start[83] = 219; 
		start[76] = 185; 
		start[110] = 199; 
		start[Buffer.EOF] = -1;

	}
	
	public Scanner (string fileName) {
		try {
			Stream stream = new FileStream(fileName, FileMode.Open, FileAccess.Read, FileShare.Read);
			buffer = new Buffer(stream, false);
			Init();
		} catch (IOException) {
			throw new FatalError("Cannot open file " + fileName);
		}
	}
	
	public Scanner (Stream s) {
		buffer = new Buffer(s, true);
		Init();
	}
	
	void Init() {
		pos = -1; line = 1; col = 0; charPos = -1;
		oldEols = 0;
		NextCh();
		if (ch == 0xEF) { // check optional byte order mark for UTF-8
			NextCh(); int ch1 = ch;
			NextCh(); int ch2 = ch;
			if (ch1 != 0xBB || ch2 != 0xBF) {
				throw new FatalError(String.Format("illegal byte order mark: EF {0,2:X} {1,2:X}", ch1, ch2));
			}
			buffer = new UTF8Buffer(buffer); col = 0; charPos = -1;
			NextCh();
		}
		pt = tokens = new Token();  // first token is a dummy
	}
	
	void NextCh() {
		if (oldEols > 0) { ch = EOL; oldEols--; } 
		else {
			pos = buffer.Pos;
			// buffer reads unicode chars, if UTF8 has been detected
			ch = buffer.Read(); col++; charPos++;
			// replace isolated '\r' by '\n' in order to make
			// eol handling uniform across Windows, Unix and Mac
			if (ch == '\r' && buffer.Peek() != '\n') ch = EOL;
			if (ch == EOL) { line++; col = 0; }
		}

	}

	void AddCh() {
		if (tlen >= tval.Length) {
			char[] newBuf = new char[2 * tval.Length];
			Array.Copy(tval, 0, newBuf, 0, tval.Length);
			tval = newBuf;
		}
		if (ch != Buffer.EOF) {
			tval[tlen++] = (char) ch;
			NextCh();
		}
	}




	void CheckLiteral() {
		switch (t.val) {
			default: break;
		}
	}

	Token NextToken() {
		while (ch == ' ' ||
			ch >= 9 && ch <= 10 || ch == 13
		) NextCh();

		int recKind = noSym;
		int recEnd = pos;
		t = new Token();
		t.pos = pos; t.col = col; t.line = line; t.charPos = charPos;
		int state;
		if (start.ContainsKey(ch)) { state = (int) start[ch]; }
		else { state = 0; }
		tlen = 0; AddCh();
		
		switch (state) {
			case -1: { t.kind = eofSym; break; } // NextCh already done
			case 0: {
				if (recKind != noSym) {
					tlen = recEnd - t.pos;
					SetScannerBehindT();
				}
				t.kind = recKind; break;
			} // NextCh already done
			case 1:
				recEnd = pos; recKind = 1;
				if (ch >= '0' && ch <= '9') {AddCh(); goto case 1;}
				else {t.kind = 1; break;}
			case 2:
				if (ch >= 'A' && ch <= 'Z' || ch >= 'a' && ch <= 'z') {AddCh(); goto case 3;}
				else {goto case 0;}
			case 3:
				recEnd = pos; recKind = 2;
				if (ch >= '0' && ch <= '9' || ch >= 'A' && ch <= 'Z' || ch >= 'a' && ch <= 'z') {AddCh(); goto case 3;}
				else {t.kind = 2; break;}
			case 4:
				if (ch == 'm') {AddCh(); goto case 5;}
				else {goto case 0;}
			case 5:
				if (ch == 'p') {AddCh(); goto case 6;}
				else {goto case 0;}
			case 6:
				if (ch == 'o') {AddCh(); goto case 7;}
				else {goto case 0;}
			case 7:
				{t.kind = 3; break;}
			case 8:
				if (ch == 't') {AddCh(); goto case 9;}
				else {goto case 0;}
			case 9:
				if (ch == 'a') {AddCh(); goto case 10;}
				else {goto case 0;}
			case 10:
				{t.kind = 4; break;}
			case 11:
				if (ch == 'e') {AddCh(); goto case 12;}
				else {goto case 0;}
			case 12:
				if (ch == 'r') {AddCh(); goto case 13;}
				else {goto case 0;}
			case 13:
				if (ch == 'i') {AddCh(); goto case 14;}
				else {goto case 0;}
			case 14:
				if (ch == 'a') {AddCh(); goto case 15;}
				else {goto case 0;}
			case 15:
				{t.kind = 5; break;}
			case 16:
				if (ch == 'o') {AddCh(); goto case 17;}
				else {goto case 0;}
			case 17:
				if (ch == 't') {AddCh(); goto case 18;}
				else {goto case 0;}
			case 18:
				if (ch == 'i') {AddCh(); goto case 19;}
				else {goto case 0;}
			case 19:
				if (ch == 'v') {AddCh(); goto case 20;}
				else {goto case 0;}
			case 20:
				if (ch == 'o') {AddCh(); goto case 21;}
				else {goto case 0;}
			case 21:
				{t.kind = 6; break;}
			case 22:
				if (ch == 'n') {AddCh(); goto case 23;}
				else {goto case 0;}
			case 23:
				{t.kind = 8; break;}
			case 24:
				if (ch == 'a') {AddCh(); goto case 25;}
				else {goto case 0;}
			case 25:
				if (ch == 'r') {AddCh(); goto case 26;}
				else {goto case 0;}
			case 26:
				{t.kind = 9; break;}
			case 27:
				{t.kind = 10; break;}
			case 28:
				{t.kind = 11; break;}
			case 29:
				if (ch == 'm') {AddCh(); goto case 30;}
				else {goto case 0;}
			case 30:
				if (ch == 'b') {AddCh(); goto case 31;}
				else {goto case 0;}
			case 31:
				if (ch == 'o') {AddCh(); goto case 32;}
				else {goto case 0;}
			case 32:
				{t.kind = 12; break;}
			case 33:
				if (ch == 'o') {AddCh(); goto case 34;}
				else {goto case 0;}
			case 34:
				{t.kind = 13; break;}
			case 35:
				if (ch == 'j') {AddCh(); goto case 36;}
				else {goto case 0;}
			case 36:
				if (ch == 'a') {AddCh(); goto case 37;}
				else {goto case 0;}
			case 37:
				{t.kind = 14; break;}
			case 38:
				if (ch == 'm') {AddCh(); goto case 39;}
				else {goto case 0;}
			case 39:
				if (ch == 'a') {AddCh(); goto case 40;}
				else {goto case 0;}
			case 40:
				if (ch == 's') {AddCh(); goto case 41;}
				else {goto case 0;}
			case 41:
				{t.kind = 15; break;}
			case 42:
				if (ch == 'r') {AddCh(); goto case 43;}
				else {goto case 0;}
			case 43:
				if (ch == 'a') {AddCh(); goto case 44;}
				else {goto case 0;}
			case 44:
				if (ch == 'v') {AddCh(); goto case 45;}
				else {goto case 0;}
			case 45:
				if (ch == 'e') {AddCh(); goto case 46;}
				else {goto case 0;}
			case 46:
				{t.kind = 17; break;}
			case 47:
				if (ch == 'b') {AddCh(); goto case 48;}
				else {goto case 0;}
			case 48:
				if (ch == 'i') {AddCh(); goto case 49;}
				else {goto case 0;}
			case 49:
				if (ch == 'e') {AddCh(); goto case 50;}
				else {goto case 0;}
			case 50:
				if (ch == 'r') {AddCh(); goto case 51;}
				else {goto case 0;}
			case 51:
				if (ch == 't') {AddCh(); goto case 52;}
				else {goto case 0;}
			case 52:
				if (ch == 'o') {AddCh(); goto case 53;}
				else {goto case 0;}
			case 53:
				{t.kind = 18; break;}
			case 54:
				if (ch == 'e') {AddCh(); goto case 55;}
				else {goto case 0;}
			case 55:
				if (ch == 'd') {AddCh(); goto case 56;}
				else {goto case 0;}
			case 56:
				if (ch == 'i') {AddCh(); goto case 57;}
				else {goto case 0;}
			case 57:
				if (ch == 'o') {AddCh(); goto case 58;}
				else {goto case 0;}
			case 58:
				{t.kind = 19; break;}
			case 59:
				if (ch == 'a') {AddCh(); goto case 60;}
				else {goto case 0;}
			case 60:
				if (ch == 's') {AddCh(); goto case 61;}
				else {goto case 0;}
			case 61:
				if (ch == 'h') {AddCh(); goto case 62;}
				else {goto case 0;}
			case 62:
				{t.kind = 20; break;}
			case 63:
				if (ch == 'g') {AddCh(); goto case 64;}
				else {goto case 0;}
			case 64:
				if (ch == 'u') {AddCh(); goto case 65;}
				else {goto case 0;}
			case 65:
				if (ch == 'd') {AddCh(); goto case 66;}
				else {goto case 0;}
			case 66:
				if (ch == 'o') {AddCh(); goto case 67;}
				else {goto case 0;}
			case 67:
				{t.kind = 21; break;}
			case 68:
				if (ch == 'e') {AddCh(); goto case 69;}
				else {goto case 0;}
			case 69:
				{t.kind = 22; break;}
			case 70:
				if (ch == 'd') {AddCh(); goto case 71;}
				else {goto case 0;}
			case 71:
				if (ch == 'e') {AddCh(); goto case 72;}
				else {goto case 0;}
			case 72:
				if (ch == 'r') {AddCh(); goto case 73;}
				else {goto case 0;}
			case 73:
				if (ch == 'e') {AddCh(); goto case 74;}
				else {goto case 0;}
			case 74:
				if (ch == 't') {AddCh(); goto case 75;}
				else {goto case 0;}
			case 75:
				if (ch == 'a') {AddCh(); goto case 76;}
				else {goto case 0;}
			case 76:
				{t.kind = 23; break;}
			case 77:
				if (ch == 'n') {AddCh(); goto case 78;}
				else {goto case 0;}
			case 78:
				if (ch == 'c') {AddCh(); goto case 79;}
				else {goto case 0;}
			case 79:
				if (ch == 'e') {AddCh(); goto case 80;}
				else {goto case 0;}
			case 80:
				if (ch == 'r') {AddCh(); goto case 81;}
				else {goto case 0;}
			case 81:
				if (ch == 'r') {AddCh(); goto case 82;}
				else {goto case 0;}
			case 82:
				if (ch == 'o') {AddCh(); goto case 83;}
				else {goto case 0;}
			case 83:
				{t.kind = 24; break;}
			case 84:
				if (ch == 'i') {AddCh(); goto case 85;}
				else {goto case 0;}
			case 85:
				if (ch == 't') {AddCh(); goto case 86;}
				else {goto case 0;}
			case 86:
				{t.kind = 25; break;}
			case 87:
				if (ch == 'u') {AddCh(); goto case 88;}
				else {goto case 0;}
			case 88:
				if (ch == 's') {AddCh(); goto case 89;}
				else {goto case 0;}
			case 89:
				if (ch == 't') {AddCh(); goto case 90;}
				else {goto case 0;}
			case 90:
				if (ch == 'i') {AddCh(); goto case 91;}
				else {goto case 0;}
			case 91:
				if (ch == 'c') {AddCh(); goto case 92;}
				else {goto case 0;}
			case 92:
				if (ch == 'a') {AddCh(); goto case 93;}
				else {goto case 0;}
			case 93:
				{t.kind = 26; break;}
			case 94:
				if (ch == 'e') {AddCh(); goto case 95;}
				else {goto case 0;}
			case 95:
				if (ch == 'c') {AddCh(); goto case 96;}
				else {goto case 0;}
			case 96:
				if (ch == 't') {AddCh(); goto case 97;}
				else {goto case 0;}
			case 97:
				if (ch == 'r') {AddCh(); goto case 98;}
				else {goto case 0;}
			case 98:
				if (ch == 'o') {AddCh(); goto case 99;}
				else {goto case 0;}
			case 99:
				if (ch == 'n') {AddCh(); goto case 100;}
				else {goto case 0;}
			case 100:
				if (ch == 'i') {AddCh(); goto case 101;}
				else {goto case 0;}
			case 101:
				if (ch == 'c') {AddCh(); goto case 102;}
				else {goto case 0;}
			case 102:
				if (ch == 'a') {AddCh(); goto case 103;}
				else {goto case 0;}
			case 103:
				{t.kind = 27; break;}
			case 104:
				{t.kind = 28; break;}
			case 105:
				if (ch == 'a') {AddCh(); goto case 106;}
				else {goto case 0;}
			case 106:
				if (ch == 'z') {AddCh(); goto case 107;}
				else {goto case 0;}
			case 107:
				if (ch == 'z') {AddCh(); goto case 108;}
				else {goto case 0;}
			case 108:
				{t.kind = 29; break;}
			case 109:
				if (ch == 'c') {AddCh(); goto case 110;}
				else {goto case 0;}
			case 110:
				if (ch == 'o') {AddCh(); goto case 111;}
				else {goto case 0;}
			case 111:
				if (ch == 'b') {AddCh(); goto case 112;}
				else {goto case 0;}
			case 112:
				if (ch == 'i') {AddCh(); goto case 113;}
				else {goto case 0;}
			case 113:
				if (ch == 'l') {AddCh(); goto case 114;}
				else {goto case 0;}
			case 114:
				if (ch == 'l') {AddCh(); goto case 115;}
				else {goto case 0;}
			case 115:
				if (ch == 'a') {AddCh(); goto case 116;}
				else {goto case 0;}
			case 116:
				if (ch == 's') {AddCh(); goto case 117;}
				else {goto case 0;}
			case 117:
				{t.kind = 30; break;}
			case 118:
				if (ch == 'n') {AddCh(); goto case 119;}
				else {goto case 0;}
			case 119:
				if (ch == 's') {AddCh(); goto case 120;}
				else {goto case 0;}
			case 120:
				if (ch == 't') {AddCh(); goto case 121;}
				else {goto case 0;}
			case 121:
				if (ch == 'r') {AddCh(); goto case 122;}
				else {goto case 0;}
			case 122:
				if (ch == 'u') {AddCh(); goto case 123;}
				else {goto case 0;}
			case 123:
				if (ch == 'm') {AddCh(); goto case 124;}
				else {goto case 0;}
			case 124:
				if (ch == 'e') {AddCh(); goto case 125;}
				else {goto case 0;}
			case 125:
				if (ch == 'n') {AddCh(); goto case 126;}
				else {goto case 0;}
			case 126:
				if (ch == 't') {AddCh(); goto case 127;}
				else {goto case 0;}
			case 127:
				if (ch == 'o') {AddCh(); goto case 128;}
				else {goto case 0;}
			case 128:
				{t.kind = 31; break;}
			case 129:
				if (ch == 'n') {AddCh(); goto case 130;}
				else {goto case 0;}
			case 130:
				if (ch == 'o') {AddCh(); goto case 131;}
				else {goto case 0;}
			case 131:
				{t.kind = 32; break;}
			case 132:
				if (ch == 'r') {AddCh(); goto case 133;}
				else {goto case 0;}
			case 133:
				if (ch == 'g') {AddCh(); goto case 134;}
				else {goto case 0;}
			case 134:
				if (ch == 'a') {AddCh(); goto case 135;}
				else {goto case 0;}
			case 135:
				if (ch == 'n') {AddCh(); goto case 136;}
				else {goto case 0;}
			case 136:
				if (ch == 'o') {AddCh(); goto case 137;}
				else {goto case 0;}
			case 137:
				{t.kind = 33; break;}
			case 138:
				if (ch == 'u') {AddCh(); goto case 139;}
				else {goto case 0;}
			case 139:
				if (ch == 'i') {AddCh(); goto case 140;}
				else {goto case 0;}
			case 140:
				if (ch == 't') {AddCh(); goto case 141;}
				else {goto case 0;}
			case 141:
				if (ch == 'a') {AddCh(); goto case 142;}
				else {goto case 0;}
			case 142:
				if (ch == 'r') {AddCh(); goto case 143;}
				else {goto case 0;}
			case 143:
				if (ch == 'r') {AddCh(); goto case 144;}
				else {goto case 0;}
			case 144:
				if (ch == 'a') {AddCh(); goto case 145;}
				else {goto case 0;}
			case 145:
				{t.kind = 34; break;}
			case 146:
				if (ch == 'o') {AddCh(); goto case 147;}
				else {goto case 0;}
			case 147:
				{t.kind = 35; break;}
			case 148:
				if (ch == 'i') {AddCh(); goto case 149;}
				else {goto case 0;}
			case 149:
				if (ch == 'o') {AddCh(); goto case 150;}
				else {goto case 0;}
			case 150:
				if (ch == 'l') {AddCh(); goto case 151;}
				else {goto case 0;}
			case 151:
				if (ch == 'i') {AddCh(); goto case 152;}
				else {goto case 0;}
			case 152:
				if (ch == 'n') {AddCh(); goto case 153;}
				else {goto case 0;}
			case 153:
				{t.kind = 36; break;}
			case 154:
				if (ch == 'o') {AddCh(); goto case 155;}
				else {goto case 0;}
			case 155:
				if (ch == 'm') {AddCh(); goto case 156;}
				else {goto case 0;}
			case 156:
				if (ch == 'p') {AddCh(); goto case 157;}
				else {goto case 0;}
			case 157:
				if (ch == 'e') {AddCh(); goto case 158;}
				else {goto case 0;}
			case 158:
				if (ch == 't') {AddCh(); goto case 159;}
				else {goto case 0;}
			case 159:
				if (ch == 'a') {AddCh(); goto case 160;}
				else {goto case 0;}
			case 160:
				{t.kind = 37; break;}
			case 161:
				if (ch == 'a') {AddCh(); goto case 162;}
				else {goto case 0;}
			case 162:
				if (ch == 'u') {AddCh(); goto case 163;}
				else {goto case 0;}
			case 163:
				if (ch == 't') {AddCh(); goto case 164;}
				else {goto case 0;}
			case 164:
				if (ch == 'a') {AddCh(); goto case 165;}
				else {goto case 0;}
			case 165:
				{t.kind = 38; break;}
			case 166:
				if (ch == 'i') {AddCh(); goto case 167;}
				else {goto case 0;}
			case 167:
				if (ch == 'l') {AddCh(); goto case 168;}
				else {goto case 0;}
			case 168:
				if (ch == 'e') {AddCh(); goto case 169;}
				else {goto case 0;}
			case 169:
				if (ch == 'n') {AddCh(); goto case 170;}
				else {goto case 0;}
			case 170:
				if (ch == 'c') {AddCh(); goto case 171;}
				else {goto case 0;}
			case 171:
				if (ch == 'i') {AddCh(); goto case 172;}
				else {goto case 0;}
			case 172:
				if (ch == 'o') {AddCh(); goto case 173;}
				else {goto case 0;}
			case 173:
				{t.kind = 39; break;}
			case 174:
				{t.kind = 40; break;}
			case 175:
				if (ch == 'o') {AddCh(); goto case 176;}
				else {goto case 0;}
			case 176:
				{t.kind = 42; break;}
			case 177:
				if (ch == 'e') {AddCh(); goto case 178;}
				else {goto case 0;}
			case 178:
				{t.kind = 43; break;}
			case 179:
				if (ch == 'i') {AddCh(); goto case 180;}
				else {goto case 0;}
			case 180:
				{t.kind = 44; break;}
			case 181:
				if (ch == 'a') {AddCh(); goto case 182;}
				else {goto case 0;}
			case 182:
				{t.kind = 45; break;}
			case 183:
				if (ch == 'l') {AddCh(); goto case 184;}
				else {goto case 0;}
			case 184:
				{t.kind = 46; break;}
			case 185:
				if (ch == 'a') {AddCh(); goto case 186;}
				else {goto case 0;}
			case 186:
				{t.kind = 47; break;}
			case 187:
				{t.kind = 48; break;}
			case 188:
				if (ch == 'd') {AddCh(); goto case 189;}
				else {goto case 0;}
			case 189:
				if (ch == 'o') {AddCh(); goto case 190;}
				else {goto case 0;}
			case 190:
				if (ch == 'n') {AddCh(); goto case 191;}
				else {goto case 0;}
			case 191:
				if (ch == 'd') {AddCh(); goto case 192;}
				else {goto case 0;}
			case 192:
				if (ch == 'a') {AddCh(); goto case 193;}
				else {goto case 0;}
			case 193:
				{t.kind = 49; break;}
			case 194:
				if (ch == 'a') {AddCh(); goto case 195;}
				else {goto case 0;}
			case 195:
				if (ch == 'n') {AddCh(); goto case 196;}
				else {goto case 0;}
			case 196:
				if (ch == 'c') {AddCh(); goto case 197;}
				else {goto case 0;}
			case 197:
				if (ch == 'a') {AddCh(); goto case 198;}
				else {goto case 0;}
			case 198:
				{t.kind = 50; break;}
			case 199:
				if (ch == 'e') {AddCh(); goto case 200;}
				else {goto case 0;}
			case 200:
				if (ch == 'g') {AddCh(); goto case 201;}
				else {goto case 0;}
			case 201:
				if (ch == 'r') {AddCh(); goto case 202;}
				else {goto case 0;}
			case 202:
				if (ch == 'a') {AddCh(); goto case 203;}
				else {goto case 0;}
			case 203:
				{t.kind = 51; break;}
			case 204:
				if (ch == 'r') {AddCh(); goto case 205;}
				else {goto case 0;}
			case 205:
				if (ch == 'c') {AddCh(); goto case 206;}
				else {goto case 0;}
			case 206:
				if (ch == 'h') {AddCh(); goto case 207;}
				else {goto case 0;}
			case 207:
				if (ch == 'e') {AddCh(); goto case 208;}
				else {goto case 0;}
			case 208:
				if (ch == 'a') {AddCh(); goto case 209;}
				else {goto case 0;}
			case 209:
				{t.kind = 52; break;}
			case 210:
				if (ch == 'e') {AddCh(); goto case 4;}
				else if (ch == 'o') {AddCh(); goto case 220;}
				else if (ch == 'r') {AddCh(); goto case 154;}
				else {goto case 0;}
			case 211:
				if (ch == 'i') {AddCh(); goto case 221;}
				else if (ch == 'a') {AddCh(); goto case 222;}
				else {goto case 0;}
			case 212:
				recEnd = pos; recKind = 41;
				if (ch == 'a') {AddCh(); goto case 223;}
				else if (ch == 'o') {AddCh(); goto case 29;}
				else if (ch == 'l') {AddCh(); goto case 194;}
				else {t.kind = 41; break;}
			case 213:
				if (ch == 'i') {AddCh(); goto case 224;}
				else if (ch == 'e') {AddCh(); goto case 188;}
				else {goto case 0;}
			case 214:
				if (ch == 'i') {AddCh(); goto case 22;}
				else if (ch == 'l') {AddCh(); goto case 161;}
				else {goto case 0;}
			case 215:
				if (ch == 'r') {AddCh(); goto case 33;}
				else if (ch == 'c') {AddCh(); goto case 87;}
				else {goto case 0;}
			case 216:
				if (ch == 'a') {AddCh(); goto case 35;}
				else if (ch == 'r') {AddCh(); goto case 59;}
				else if (ch == 'e') {AddCh(); goto case 77;}
				else if (ch == 'o') {AddCh(); goto case 204;}
				else {goto case 0;}
			case 217:
				if (ch == 'i') {AddCh(); goto case 225;}
				else {goto case 0;}
			case 218:
				if (ch == 'l') {AddCh(); goto case 94;}
				else if (ch == 's') {AddCh(); goto case 109;}
				else {goto case 0;}
			case 219:
				if (ch == 'o') {AddCh(); goto case 183;}
				else if (ch == 'i') {AddCh(); goto case 187;}
				else {goto case 0;}
			case 220:
				if (ch == 'c') {AddCh(); goto case 24;}
				else if (ch == 'm') {AddCh(); goto case 226;}
				else {goto case 0;}
			case 221:
				if (ch == 's') {AddCh(); goto case 8;}
				else if (ch == 'a') {AddCh(); goto case 129;}
				else {goto case 0;}
			case 222:
				if (ch == 'l') {AddCh(); goto case 38;}
				else if (ch == 'n') {AddCh(); goto case 70;}
				else {goto case 0;}
			case 223:
				if (ch == 't') {AddCh(); goto case 11;}
				else if (ch == 'j') {AddCh(); goto case 146;}
				else {goto case 0;}
			case 224:
				if (ch == 't') {AddCh(); goto case 227;}
				else if (ch == 'd') {AddCh(); goto case 68;}
				else {goto case 0;}
			case 225:
				if (ch == 'h') {AddCh(); goto case 228;}
				else {goto case 0;}
			case 226:
				if (ch == 'g') {AddCh(); goto case 42;}
				else if (ch == 'm') {AddCh(); goto case 54;}
				else if (ch == 'a') {AddCh(); goto case 63;}
				else {goto case 0;}
			case 227:
				if (ch == 'm') {AddCh(); goto case 229;}
				else {goto case 0;}
			case 228:
				if (ch == 'a') {AddCh(); goto case 230;}
				else {goto case 0;}
			case 229:
				if (ch == 'o') {AddCh(); goto case 231;}
				else {goto case 0;}
			case 230:
				if (ch == 't') {AddCh(); goto case 232;}
				else {goto case 0;}
			case 231:
				recEnd = pos; recKind = 7;
				if (ch == 's') {AddCh(); goto case 104;}
				else {t.kind = 7; break;}
			case 232:
				recEnd = pos; recKind = 16;
				if (ch == 'a') {AddCh(); goto case 47;}
				else {t.kind = 16; break;}

		}
		t.val = new String(tval, 0, tlen);
		return t;
	}
	
	private void SetScannerBehindT() {
		buffer.Pos = t.pos;
		NextCh();
		line = t.line; col = t.col; charPos = t.charPos;
		for (int i = 0; i < tlen; i++) NextCh();
	}
	
	// get the next token (possibly a token already seen during peeking)
	public Token Scan () {
		if (tokens.next == null) {
			return NextToken();
		} else {
			pt = tokens = tokens.next;
			return tokens;
		}
	}

	// peek for the next token, ignore pragmas
	public Token Peek () {
		do {
			if (pt.next == null) {
				pt.next = NextToken();
			}
			pt = pt.next;
		} while (pt.kind > maxT); // skip pragmas
	
		return pt;
	}

	// make sure that peeking starts at the current scan position
	public void ResetPeek () { pt = tokens; }

} // end Scanner
