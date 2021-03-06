﻿using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using Kurogane.Compiler;
using Kurogane.Expressions;
using Kurogane.RuntimeBinder;
using System.Diagnostics.Contracts;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using System.Reflection.Emit;
using Microsoft.Scripting.Generation;

namespace Kurogane {

	/// <summary>
	/// クロガネのプログラムを実行するエンジン。
	/// </summary>
	public class Engine {

		#region static

		public const string FileExtension = ".krg";

		/// <summary>言語のバージョン</summary>
		public static readonly Version Version;

		/// <summary>アセンブリのGUID</summary>
		public static readonly Guid Guid;

		static Engine() {
			var asm = typeof(Engine).Assembly;
			Version = asm.GetName().Version;
			Guid = new Guid(((GuidAttribute)asm.GetCustomAttributes(typeof(GuidAttribute), true)[0]).Value);

		}

		#endregion

		// ----- ----- ----- ----- ----- fields ----- ----- ----- ----- -----

		protected BinderFactory Factory { get; private set; }

		/// <summary>グローバルスコープ</summary>
		public Scope Global { get; private set; }

		/// <summary>入力先</summary>
		public TextReader In { get; set; }

		/// <summary>出力先</summary>
		public TextWriter Out { get; set; }

		public Encoding DefaultEncoding { get; set; }

		/// <summary>初期化されたかどうか</summary>
		protected bool Initialized { get; set; }

		/// <summary>
		/// コンパイルが終了したときに発生するイベント。
		/// 例外発生時は呼ばれない。
		/// </summary>
		public event EventHandler<EngineEventArgs> OnCompiled;

		/// <summary>
		/// 実行が終了したときに発生するイベント。
		/// 例外発生時は呼ばれない。
		/// </summary>
		public event EventHandler<EngineEventArgs> OnExecuted;

		// ----- ----- ----- ----- ----- ctor ----- ----- ----- ----- -----

		/// <summary>通常のコンストラクタ</summary>
		public Engine() : this(new BinderFactory()) { }

		protected Engine(BinderFactory factory) {
			Contract.Requires<ArgumentNullException>(factory != null);

			Factory = factory;
			Global = new Scope();
			In = Console.In;
			Out = Console.Out;
			DefaultEncoding = Encoding.Default;
		}

		// ----- ----- ----- ----- ----- methods ----- ----- ----- ----- -----

		protected virtual void Initialize() {
			if (Initialized)
				return;
			lock (this) {
				if (Initialized)
					return;
				Initialized = true;
				LoadLibrary(typeof(Engine).Assembly);
			}
		}

		/// <summary>
		/// クロガネのプログラムを実行する。
		/// </summary>
		/// <param name="code">プログラム</param>
		/// <param name="filename">プログラムのファイル名</param>
		/// <returns>実行結果</returns>
		public object Execute(string code, string filename) {
			using (var reader = new StringReader(code)) {
				return ExecuteCore(reader, filename);
			}
		}

		/// <summary>
		/// クロガネのプログラムを実行する。
		/// </summary>
		/// <param name="fileName">プログラムのファイル名</param>
		/// <returns>実行結果</returns>
		public object ExecuteFile(string fileName) {
			string fullPath = null;
			if (File.Exists(fileName)) {
				fullPath = Path.GetFullPath(fileName);
			}
			else if (  File.Exists(fileName + FileExtension)) {
				fullPath = Path.GetFullPath(fileName + FileExtension);
			}
			if (fullPath == null)
				throw new FileNotFoundException("実行するファイルが見つかりません。", fileName);
			using (var stream = new StreamReader(fullPath, DefaultEncoding)) {
				return ExecuteCore(stream, fullPath);
			}
		}

		/// <summary>
		/// クロガネのプログラムを実行する。
		/// </summary>
		/// <param name="stream">プログラム</param>
		/// <param name="filename">プログラムのファイル名</param>
		/// <returns>実行結果</returns>
		private object ExecuteStream(Stream stream, string filename) {
			using (var reader = new StreamReader(stream, DefaultEncoding)) {
				return ExecuteCore(reader, filename);
			}
		}

		/// <summary>
		/// 実際に実行する部分。
		/// </summary>
		protected virtual object ExecuteCore(TextReader stream, string filename) {
			Contract.Requires<ArgumentNullException>(stream != null);
			Contract.Requires<ArgumentNullException>(filename != null);
			Initialize();
			Func<Scope, object> program;
			{
				// Compile
				var sw = Stopwatch.StartNew();
				var token = Tokenizer.Tokenize(stream, filename);
				var ast = Parser.Parse(token, filename);
				var expr = ExpressionGenerator.Generate(ast, this.Factory, filename);
				expr = ExpressionOptimizer.Analyze(expr);
				program = CompilerHelpers.Compile(expr, true);
				//program = expr.Compile();
				sw.Stop();
				var ev = OnCompiled;
				if (ev != null)
					ev(this, new EngineEventArgs(sw.ElapsedMilliseconds));
			}
			{
				// Execute
				var sw = Stopwatch.StartNew();
				var result = program(this.Global);
				sw.Stop();
				var ev = OnExecuted;
				if (ev != null)
					ev(this, new EngineEventArgs(sw.ElapsedMilliseconds));
				return result;
			}
		}

		#region LoadLibrary

		private void LoadLibrary(Assembly asm) {
			// 静的ライブラリのロード
			Array.ForEach(asm.GetTypes(), delegate(Type type) {
				// 関数と変数
				var typeAttrs = type.GetCustomAttributes(typeof(LibraryAttribute), false);
				if (typeAttrs.Length > 0) {
					foreach (var fInfo in type.GetFields(BindingFlags.Public | BindingFlags.Static))
						SetField(fInfo);
					foreach (var mInfo in type.GetMethods(BindingFlags.Public | BindingFlags.Static))
						SetMethod(mInfo);
				}
				// クラス
				var aliasAttr = type.GetCustomAttributes(typeof(AliasForAttribute), false);
				if (aliasAttr.Length > 0) {
					foreach (AliasForAttribute attr in aliasAttr) {
						MetaObjectLoader.RegisterAlias(attr.Type, type);
					}
				}
				else {
					var nameAttr = type.GetCustomAttributes(typeof(JpNameAttribute), false);
					if (nameAttr.Length > 0) {
						MetaObjectLoader.RegisterAlias(type, type);
					}
				}
			});
			// 動的ライブラリのロード
			foreach (var name in asm.GetManifestResourceNames())
				if (name.EndsWith(FileExtension))
					this.ExecuteStream(asm.GetManifestResourceStream(name), name);

			//var pairs =
			//    from resName in asm.GetManifestResourceNames()
			//    where resName.EndsWith(".krg")
			//    let stream = asm.GetManifestResourceStream(resName)
			//    let reader = new StreamReader(stream, DefaultEncoding)
			//    let token = Tokenizer.Tokenize(reader, resName)
			//    let ast = Parser.Parse(token, resName)
			//    select Tuple.Create(ast, resName);
			//var loadExpr = ExpressionGenerator.GenerateAll(this.Factory, pairs);
			//var loadFunc = ExpressionOptimizer.Analyze(loadExpr).Compile();
			//loadFunc(this.Global);
		}

		private void SetField(FieldInfo fInfo) {
			var nameAttrs = fInfo.GetCustomAttributes(typeof(JpNameAttribute), false);
			if (nameAttrs.Length == 0) return;
			var value = fInfo.GetValue(null);
			foreach (var attr in nameAttrs) {
				var nameAttr = attr as JpNameAttribute;
				Global.SetVariable(nameAttr.Name, value);
			}
		}

		private void SetMethod(MethodInfo mInfo) {
			var nameAttrs = mInfo.GetCustomAttributes(typeof(JpNameAttribute), false);
			if (nameAttrs.Length == 0)
				return;
			var paramInfos = mInfo.GetParameters();
			int ext = mInfo.GetCustomAttributes(typeof(ExtensionAttribute), false).Length > 0 ? 1 : 0;
			var sfxs = new String[paramInfos.Length - ext];
			for (int i = 0; i < sfxs.Length; i++) {
				var sfxAttrs = paramInfos[i + ext].GetCustomAttributes(typeof(SuffixAttribute), false);
				if (sfxAttrs.Length == 0)
					return;
				var sfxAttr = sfxAttrs[0] as SuffixAttribute;
				sfxs[i] = sfxAttr.Name;
			}
			var types = new Type[paramInfos.Length - ext + 1];
			for (int i = 0; i < types.Length - 1; i++)
				types[i] = paramInfos[i + ext].ParameterType;
			types[types.Length - 1] = mInfo.ReturnType;
			var funcType = Expression.GetFuncType(types);

			var outerParam = Expression.Parameter(paramInfos[0].ParameterType);
			var innerParams = new ParameterExpression[types.Length - 1];
			for (int i = 0; i < innerParams.Length; i++)
				innerParams[i] = Expression.Parameter(types[i]);
			var lambda = Expression.Lambda(Expression.Call(mInfo,
				(ext == 0 ? innerParams : Enumerable.Concat(new[] { outerParam }, innerParams))),
				innerParams);
			var ctorInfo = typeof(SuffixFunc<>).MakeGenericType(funcType).GetConstructor(new[] { funcType, typeof(string[]) });
			var body = Expression.New(ctorInfo, lambda, Expression.Constant(sfxs));
			if (ext > 0) {
				// this パラメタが存在する
				if (paramInfos[0].ParameterType == typeof(Engine)) {
					var func = Expression.Lambda<Func<Engine, object>>(Expression.Convert(body, typeof(object)), outerParam);
					var exec = func.Compile();
					foreach (var nAttr in nameAttrs) {
						var a = nAttr as JpNameAttribute;
						Global.SetVariable(a.Name, exec(this));
					}
				}
				else if (paramInfos[0].ParameterType == typeof(Scope)) {
					var func = Expression.Lambda<Func<Scope, object>>(Expression.Convert(body, typeof(object)), outerParam);
					var exec = func.Compile();
					foreach (var nAttr in nameAttrs) {
						var a = nAttr as JpNameAttribute;
						Global.SetVariable(a.Name, exec(Global));
					}
				}
			}
			else {
				// this パラメタが存在しない。
				var func = Expression.Lambda<Func<object>>(Expression.Convert(body, typeof(object)));
				var exec = func.Compile();
				foreach (var nAttr in nameAttrs) {
					var a = nAttr as JpNameAttribute;
					Global.SetVariable(a.Name, exec());
				}
			}
		}

		#endregion

		#region InternalClass

		/// <summary>エンジン内で発生したイベント</summary>
		public class EngineEventArgs : EventArgs {
			/// <summary>処理にかかった時間</summary>
			public long Milliseconds { get; private set; }

			internal EngineEventArgs(long millis) {
				this.Milliseconds = millis;
			}
		}

		#endregion

	}
}
