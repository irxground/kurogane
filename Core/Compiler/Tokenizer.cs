﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Diagnostics.Contracts;

namespace Kurogane.Compiler {

	/// <summary>
	/// 入力をトークンで分割するクラス
	/// </summary>
	public static class Tokenizer {

		/// <summary>
		/// プログラムをトークンで区切る。
		/// </summary>
		/// <param name="code">プログラム</param>
		/// <returns></returns>
		public static Token Tokenize(TextReader code, string filename) {
			Contract.Requires<ArgumentException>(!String.IsNullOrWhiteSpace(filename));
			var lexer = new Lexer(code, filename);
			return lexer.Next();
		}
	}
}
