#region License
/*---------------------------------------------------------------------------------*\

	Distributed under the terms of an MIT-style license:

	The MIT License

	Copyright (c) 2006-2010 Stephen M. McKamey

	Permission is hereby granted, free of charge, to any person obtaining a copy
	of this software and associated documentation files (the "Software"), to deal
	in the Software without restriction, including without limitation the rights
	to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
	copies of the Software, and to permit persons to whom the Software is
	furnished to do so, subject to the following conditions:

	The above copyright notice and this permission notice shall be included in
	all copies or substantial portions of the Software.

	THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
	IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
	FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
	AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
	LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
	OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
	THE SOFTWARE.

\*---------------------------------------------------------------------------------*/
#endregion License

using System;
using System.CodeDom;
using System.Collections;
using System.Collections.Generic;
using System.IO;

using JsonFx.EcmaScript;
using JsonFx.Html;
using JsonFx.Markup;
using JsonFx.Model;
using JsonFx.Serialization;
using JsonFx.Utils;

namespace JsonFx.Jbst
{
	/// <summary>
	/// Builds the server-side implementation of a given JBST
	/// </summary>
	internal class JbstViewBuilder
	{
		#region Constants

		private const string TemplateMethodFormat = "T_{0:x4}";
		private const string CodeBlockMethodFormat = "B_{0:x4}";
		private const string FieldFormat = "{0}_{1:x}";
		private const string LocalVarFormat = "{0}_{1:x}";

		//private const string JbstVisible = "visible";
		//private const string JbstOnInit = "oninit";
		//private const string JbstOnLoad = "onload";

		private static readonly char[] NameDelim = new[] { ':' };

		private static readonly object Key_VarCount = new object();
		private static readonly object Key_EmptyBody = new object();

		#endregion Constants

		#region Fields

		private readonly ModelAnalyzer Analyzer;
		private readonly HtmlFormatter HtmlFormatter;
		private readonly EcmaScriptFormatter JSFormatter;
		private readonly EcmaScriptBuilder JSBuilder;
		private CodeTypeDeclaration viewType = null;
		private int counter;

		#endregion Fields

		#region Init

		/// <summary>
		/// Ctor
		/// </summary>
		/// <param name="settings"></param>
		public JbstViewBuilder(DataWriterSettings settings)
		{
			this.JSBuilder = new EcmaScriptBuilder(settings);

			this.HtmlFormatter = new HtmlFormatter(settings)
			{
				CanonicalForm = false,
				EncodeNonAscii = true,
				EmptyAttributes = HtmlFormatter.EmptyAttributeType.Html
			};

			this.JSFormatter = new EcmaScriptFormatter(settings)
			{
				EncodeLessThan = true
			};

			this.Analyzer = new ModelAnalyzer(new DataReaderSettings(settings.Resolver, settings.Filters));
		}

		#endregion Init

		#region Build Method

		public CodeCompileUnit Build(CompilationState state)
		{
			if (state == null)
			{
				throw new ArgumentNullException("state");
			}

			this.counter = 0;
			this.HtmlFormatter.ResetScopeChain();

			CodeCompileUnit code = new CodeCompileUnit();

			#region namespace JbstNamespace

			state.EnsureName();

			string typeNS, typeName;
			this.SplitTypeName(state.JbstName, out typeNS, out typeName);

			CodeNamespace ns = new CodeNamespace(typeNS);
			code.Namespaces.Add(ns);

			#endregion namespace JbstNamespace

			#region public partial class JbstTypeName

			CodeTypeDeclaration viewType = new CodeTypeDeclaration();
			viewType.IsClass = true;
			viewType.Name = typeName;
			viewType.IsPartial = true;
			viewType.Attributes = MemberAttributes.Public|MemberAttributes.Final;

			viewType.BaseTypes.Add(typeof(JbstView));
			ns.Types.Add(viewType);

			#endregion public sealed class JbstTypeName

			#region [BuildPath(virtualPath)]

			string virtualPath = PathUtility.EnsureAppRelative(state.FilePath);

			CodeAttributeDeclaration attribute = new CodeAttributeDeclaration(
				new CodeTypeReference(typeof(BuildPathAttribute)),
				new CodeAttributeArgument(new CodePrimitiveExpression(virtualPath)));
			viewType.CustomAttributes.Add(attribute);

			#endregion [BuildPath(virtualPath)]

			#region Init

			CodeConstructor ctor = new CodeConstructor();
			ctor.Attributes = MemberAttributes.Public;
			viewType.Members.Add(ctor);

			ctor = new CodeConstructor();
			ctor.Attributes = MemberAttributes.Public;
			ctor.Parameters.Add(new CodeParameterDeclarationExpression(typeof(JbstView), "view"));
			ctor.BaseConstructorArgs.Add(new CodeArgumentReferenceExpression("view"));
			viewType.Members.Add(ctor);

			ctor = new CodeConstructor();
			ctor.Attributes = MemberAttributes.Public;
			ctor.Parameters.Add(new CodeParameterDeclarationExpression(typeof(DataWriterSettings), "settings"));
			ctor.Parameters.Add(new CodeParameterDeclarationExpression(typeof(IClientIDStrategy), "clientID"));
			ctor.BaseConstructorArgs.Add(new CodeArgumentReferenceExpression("settings"));
			ctor.BaseConstructorArgs.Add(new CodeArgumentReferenceExpression("clientID"));
			viewType.Members.Add(ctor);

			CodeMemberMethod init = new CodeMemberMethod();
			init.Name = "Init";
			init.Attributes = MemberAttributes.Family|MemberAttributes.Override;
			viewType.Members.Add(init);

			#endregion Init

			List<CodeObject> output = new List<CodeObject>();

			this.viewType = viewType;
			try
			{
				// build control tree
				this.ProcessTemplate(state, output, true);
			}
			finally
			{
				this.viewType = null;
			}

			foreach (CodeTypeMember member in output)
			{
				viewType.Members.Add(member);
			}

			return code;
		}

		#endregion Build Method

		#region Process Methods

		private string ProcessTemplate(CompilationState state, List<CodeObject> output, bool isRoot)
		{
			var content = state.TransformContent();
			if (content == null)
			{
				return null;
			}

			// effectively FirstOrDefault
			foreach (var input in this.Analyzer.Analyze(content))
			{
				IList<Token<MarkupTokenType>> buffer = new List<Token<MarkupTokenType>>();

				this.ProcessChild(input, output, buffer);

				if (buffer.Count > 0)
				{
					// emit any trailing buffered content
					string markup = this.HtmlFormatter.Format(buffer);
					output.Add(this.EmitMarkup(markup));
					buffer.Clear();
				}

				#region private void methodName(TextWriter writer, object data, int index, int count)

				// each template gets built as a method as it can be called in a loop
				string methodName = isRoot ? "Root" : String.Format(TemplateMethodFormat, ++this.counter);

				CodeMemberMethod method = new CodeMemberMethod();

				method.Name = methodName;
				method.Attributes = isRoot ? MemberAttributes.Override|MemberAttributes.Family : MemberAttributes.Private;
				method.Parameters.Add(new CodeParameterDeclarationExpression(typeof(TextWriter), "writer"));
				method.Parameters.Add(new CodeParameterDeclarationExpression(typeof(object), "data"));
				method.Parameters.Add(new CodeParameterDeclarationExpression(typeof(int), "index"));
				method.Parameters.Add(new CodeParameterDeclarationExpression(typeof(int), "count"));

				#endregion private void methodName(TextWriter writer, object data, int index, int count)

				// combine all the statements into the method
				for (int i=0, length=output.Count; i<length; i++)
				{
					var code = output[i];
					bool remove = false;

					if (code == null)
					{
						remove = true;
					}
					else if (code is CodeExpression)
					{
						method.Statements.Add((CodeExpression)code);
						remove = true;
					}
					else if (code is CodeStatement)
					{
						method.Statements.Add((CodeStatement)code);
						remove = true;
					}

					if (remove)
					{
						output.RemoveAt(i);
						i--;
						length--;
					}
				}

				output.Add(method);

				return methodName;
			}

			return null;
		}

		private void ProcessChild(object child, List<CodeObject> output, IList<Token<MarkupTokenType>> buffer)
		{
			if (child is IList)
			{
				this.ProcessElement((IList)child, output, buffer);
			}

			else if (child is JbstCommand)
			{
				if (buffer.Count > 0)
				{
					string markup = this.HtmlFormatter.Format(buffer);
					output.Add(this.EmitMarkup(markup));
					buffer.Clear();
				}

				IList<CodeObject> code = this.ProcessCommand((JbstCommand)child, false);
				if (code != null)
				{
					for (int j=0, lines=code.Count; j<lines; j++)
					{
						var line = code[j];
						if (line == null)
						{
							code.RemoveAt(j);
							j--;
							lines--;
						}

						if (line is CodeExpression)
						{
							code[j] = this.EmitExpression((CodeExpression)line);
						}
					}
					output.AddRange(code);
				}
			}

			else
			{
				// process as literal
				buffer.Add(new Token<MarkupTokenType>(MarkupTokenType.Primitive, child));
			}
		}

		private void ProcessElement(IList input, List<CodeObject> output, IList<Token<MarkupTokenType>> buffer)
		{
			if (input == null || input.Count < 1)
			{
				return;
			}

			DataName tagName = this.SplitDataName((string)input[0], false);
			buffer.Add(new Token<MarkupTokenType>(MarkupTokenType.ElementBegin, tagName));

			int i=1, length=input.Count;

			IDictionary<DataName, object> attrs = null;
			if (length > 1 && input[i] is IDictionary<string, object>)
			{
				IDictionary<string, object> allAttr = (IDictionary<string, object>)input[i];
				attrs = this.ProcessAttributes((IDictionary<string, object>)input[i], buffer);
				i++;

				if (attrs != null && !allAttr.ContainsKey("id"))
				{
					allAttr["id"] = "[TODO]";
					buffer.Add(new Token<MarkupTokenType>(MarkupTokenType.Attribute, new DataName("id")));
					buffer.Add(new Token<MarkupTokenType>(MarkupTokenType.Primitive, allAttr["id"]));
				}
			}

			for (; i<length; i++)
			{
				var item = input[i];

				this.ProcessChild(item, output, buffer);
			}

			buffer.Add(new Token<MarkupTokenType>(MarkupTokenType.ElementEnd));
		}

		private IDictionary<DataName, object> ProcessAttributes(IDictionary<string, object> attributes, IList<Token<MarkupTokenType>> buffer)
		{
			IDictionary<DataName, object> attrs = null;

			foreach (var attr in attributes)
			{
				DataName name = this.SplitDataName(attr.Key, true);

				object value = attr.Value;
				bool isCommand = value is JbstCommand;

				if (!isCommand && name.Prefix == "jbst")
				{
					value = new JbstExpressionBlock(value as string);
					isCommand = true;
				}

				if (isCommand)
				{
					if (attrs == null)
					{
						attrs = new Dictionary<DataName, object>(attributes.Count);
					}

					attrs[name] = value;
				}
				else
				{
					buffer.Add(new Token<MarkupTokenType>(MarkupTokenType.Attribute, name));
					buffer.Add(new Token<MarkupTokenType>(MarkupTokenType.Primitive, value));
				}
			}

			return attrs;
		}

		private IList<CodeObject> ProcessCommand(JbstCommand command, bool isAttribute)
		{
			switch (command.CommandType)
			{
				case JbstCommandType.ExpressionBlock:
				case JbstCommandType.UnparsedBlock:
				case JbstCommandType.StatementBlock:
				{
					return this.Translate<object>(command);
				}
				case JbstCommandType.TemplateReference:
				{
					JbstTemplateReference reference = (JbstTemplateReference)command;
					return this.BuildBindReferenceCall(reference.NameExpr, reference.DataExpr, reference.IndexExpr, reference.CountExpr);
				}
				case JbstCommandType.InlineTemplate:
				{
					JbstInlineTemplate inline = (JbstInlineTemplate)command;

					List<CodeObject> output = new List<CodeObject>();
					string childMethod = this.ProcessTemplate(inline.State, output, false);
					if (!String.IsNullOrEmpty(childMethod))
					{
						var call = this.BuildBindAdapterCall(childMethod, inline.DataExpr, inline.IndexExpr, inline.CountExpr, false);
						output.Add(call);
					}
					return output;
				}
				case JbstCommandType.Placeholder:
				{
					// TODO
					return null;
				}
				case JbstCommandType.CommentBlock:
				{
					JbstCommentBlock comment = (JbstCommentBlock)command;
					return new CodeObject[] { this.EmitMarkup(String.Concat("<!--", comment.Code, "-->")) };
				}
				case JbstCommandType.DeclarationBlock:
				{
					// these have been compiled away
					return null;
				}
				default:
				{
					// TODO:
					return null;
				}
			}
		}

		#endregion Process Methods

		#region Build Methods

		private IList<CodeObject> BuildBindReferenceCall(object nameExpr, object dataExpr, object indexExpr, object countExpr)
		{
			#region this.Bind(new ExternalTemplate(), writer, dataExpr, indexExpr, countExpr);

			CodeExpression nameCode;
			if (nameExpr is string)
			{
				string name = (string)nameExpr;
				if (EcmaScriptIdentifier.IsValidIdentifier(name, true))
				{
					nameCode = this.GenerateExternalTemplateField(name);
				}
				else
				{
					nameCode = this.TranslateExpression<object>(nameExpr);
				}
			}
			else
			{
				nameCode = this.TranslateExpression<object>(nameExpr);
			}

			CodeExpression dataCode, indexCode, countCode;
			this.ProcessArgs(dataExpr, indexExpr, countExpr, out dataCode, out indexCode, out countCode);

			if (nameCode == null || dataCode == null || indexCode == null || countCode == null)
			{
				// force into client mode
				return this.BuildClientReference(nameExpr, dataExpr, indexExpr, countExpr);
			}

			CodeMethodInvokeExpression methodCall = new CodeMethodInvokeExpression(
				new CodeThisReferenceExpression(),
				"Bind",
				nameCode,
				new CodeArgumentReferenceExpression("writer"),
				dataCode,
				indexCode,
				countCode);

			#endregion this.Bind(new ExternalTemplate(), writer, dataExpr, indexExpr, countExpr);

			return new CodeObject[] { new CodeExpressionStatement(methodCall) };
		}

		private CodeExpression GenerateExternalTemplateField(string externalType)
		{
			CodeMemberMethod init = null;
			CodeMemberField field;

			foreach (CodeTypeMember member in this.viewType.Members)
			{
				field = member as CodeMemberField;
				if (field == null)
				{
					CodeMemberMethod temp = member as CodeMemberMethod;
					if (temp != null &&
						temp.Name == "Init")
					{
						// save Init in case need to generate initialization
						init = temp;
					}
					continue;
				}

				if (StringComparer.Ordinal.Equals(field.Type.BaseType, externalType))
				{
					// reuse a previously created instance
					return new CodeFieldReferenceExpression(new CodeThisReferenceExpression(), field.Name);
				}
			}

			if (init == null)
			{
				throw new InvalidOperationException("Constructor not found.");
			}

			#region private externalType t_XXXX;

			field = new CodeMemberField(externalType, String.Format(FieldFormat, "t", ++this.counter));
			field.Attributes = MemberAttributes.Private;
			this.viewType.Members.Add(field);

			#endregion private externalType t_XXXX;

			#region this.t_XXXX

			var fieldRef = new CodeFieldReferenceExpression(new CodeThisReferenceExpression(), field.Name);

			#endregion this.t_XXXX

			#region this.t_XXXX = new externalType(this);
			
			var fieldInit = new CodeAssignStatement(
				fieldRef,
				new CodeObjectCreateExpression(
					externalType,
					new CodeThisReferenceExpression()));

			init.Statements.Add(fieldInit);

			#endregion this.t_XXXX = new externalType(this);

			return fieldRef;
		}

		private void ProcessArgs(object dataExpr, object indexExpr, object countExpr, out CodeExpression dataCode, out CodeExpression indexCode, out CodeExpression countCode)
		{
			dataCode = this.TranslateExpression<object>(dataExpr);
			indexCode = (dataCode != null) ? this.TranslateExpression<int>(indexExpr) as CodeExpression : null;
			countCode = (indexCode != null) ? this.TranslateExpression<int>(countExpr) as CodeExpression : null;

			if (dataCode is CodeDefaultValueExpression)
			{
				// expression evaluated to an empty method so pass surrounding scope through
				dataCode = new CodeArgumentReferenceExpression("data");
			}
			if (indexCode is CodeDefaultValueExpression)
			{
				// expression evaluated to an empty method so pass surrounding scope through
				indexCode = new CodeArgumentReferenceExpression("index");
			}
			if (countCode is CodeDefaultValueExpression)
			{
				// expression evaluated to an empty method so pass surrounding scope through
				countCode = new CodeArgumentReferenceExpression("count");
			}
		}

		private IList<CodeObject> BuildClientReference(object nameExpr, object dataExpr, object indexExpr, object countExpr)
		{
			List<CodeObject> output = new List<CodeObject>(20);

			string varID;
			output.Add(this.GenerateClientIDVar(out varID));

			output.Add(this.EmitMarkup("<noscript id=\""));
			output.Add(this.EmitVarValue(varID));
			output.Add(this.EmitMarkup("\">"));

			output.Add(this.EmitMarkup("</noscript><script type=\"text/javascript\">"));
			output.Add(this.EmitMarkup(this.FormatExpression(nameExpr)));
			output.Add(this.EmitMarkup(".replace(\""));
			output.Add(this.EmitVarValue(varID));
			output.Add(this.EmitMarkup("\","));
			output.Add(this.EmitMarkup(this.FormatExpression(dataExpr)));
			output.Add(this.EmitMarkup(","));
			output.Add(this.EmitMarkup(this.FormatExpression(indexExpr)));
			output.Add(this.EmitMarkup(","));
			output.Add(this.EmitMarkup(this.FormatExpression(countExpr)));
			output.Add(this.EmitMarkup(");</script>"));

			return output;
		}

		private IList<CodeObject> BuildWrapperReference(object nameExpr, object dataExpr, object indexExpr, object countExpr)
		{
			List<CodeObject> output = new List<CodeObject>(20);

			string varID;
			output.Add(this.GenerateClientIDVar(out varID));

			output.Add(this.EmitMarkup("<div id=\""));
			output.Add(this.EmitVarValue(varID));
			output.Add(this.EmitMarkup("\">"));

			// TODO: inline content goes here
			output.Add(this.EmitMarkup("[ inline content goes here ]"));

			output.Add(this.EmitMarkup("</div><script type=\"text/javascript\">"));
			output.Add(this.EmitMarkup(this.FormatExpression(nameExpr)));
			output.Add(this.EmitMarkup(".replace(\""));
			output.Add(this.EmitVarValue(varID));
			output.Add(this.EmitMarkup("\","));
			output.Add(this.EmitMarkup(this.FormatExpression(dataExpr)));
			output.Add(this.EmitMarkup(","));
			output.Add(this.EmitMarkup(this.FormatExpression(indexExpr)));
			output.Add(this.EmitMarkup(","));
			output.Add(this.EmitMarkup(this.FormatExpression(countExpr)));
			output.Add(this.EmitMarkup(");</script>"));

			return output;
		}

		public string FormatExpression(object argument)
		{
			if (argument == null)
			{
				return null;
			}

			if (argument is string)
			{
				// directly use as inline expression
				argument = new JbstExpressionBlock((string)argument);
			}

			JbstCommand command = argument as JbstCommand;
			if (command != null)
			{
				//return block.Code;

				string code;
				using (var writer = new StringWriter())
				{
					command.Format(null, writer);
					code = writer.GetStringBuilder().ToString();
				}
			}

			// convert to token sequence and allow formatter to emit as JS primitive
			return this.JSFormatter.Format(new[] { new Token<ModelTokenType>(ModelTokenType.Primitive, argument) });
		}

		private CodeObject BuildClientExecution(TranslationResult result)
		{
			return new CodePrimitiveExpression(result.Script);
		}

		private CodeStatement BuildBindAdapterCall(string methodName, object dataExpr, object indexExpr, object countExpr, bool normalize)
		{
			CodeExpression dataCode, indexCode, countCode;
			this.ProcessArgs(dataExpr, indexExpr, countExpr, out dataCode, out indexCode, out countCode);

			if (dataCode == null || indexCode == null || countCode == null)
			{
				// TODO
				return null;
			}

			#region this.Bind(this.methodName, writer, data, index, count, normalize);

			CodeMethodInvokeExpression methodCall = new CodeMethodInvokeExpression(
				new CodeThisReferenceExpression(),
				"Bind",
				new CodeMethodReferenceExpression(new CodeThisReferenceExpression(), methodName),
				new CodeArgumentReferenceExpression("writer"),
				dataCode,
				indexCode,
				countCode,
				new CodePrimitiveExpression(normalize));

			#endregion this.Bind(this.methodName, writer, data, index, count, normalize);

			return new CodeExpressionStatement(methodCall);
		}

		#endregion Build Methods

		#region Code Translation Methods

		private CodeExpression TranslateExpression<TResult>(object expr)
		{
			IList<CodeObject> code = this.Translate<TResult>(expr);
			return (code.Count == 1) ? code[0] as CodeExpression : null;
		}

		private IList<CodeObject> Translate<TResult>(object expr)
		{
			List<CodeObject> code = new List<CodeObject>();

			TranslationResult result = new TranslationResult(
				typeof(TResult),
				String.Format(CodeBlockMethodFormat, this.counter),
				new TranslationResult.ParamDefn(typeof(object), "data"),
				new TranslationResult.ParamDefn(typeof(int), "index"),
				new TranslationResult.ParamDefn(typeof(int), "count"))
			{
				Script = this.FormatExpression(expr)
			};

			this.JSBuilder.Translate(result);
			if (result.IsClientOnly)
			{
				// not yet supported on server-side
				code.Add(this.BuildClientExecution(result));
				return code;
			}

			int lineCount = result.LineCount;
			if (lineCount < 1)
			{
				// no method body, return default value of expected type
				var defValue = new CodeDefaultValueExpression(new CodeTypeReference(typeof(TResult)));
				defValue.UserData[Key_EmptyBody] = result.Script;

				code.Add(defValue);
				return code;
			}

			if (lineCount == 1)
			{
				var onlyLine = result.Methods[0].Statements[0];

				CodeMethodReturnStatement rs = onlyLine as CodeMethodReturnStatement;
				if (rs != null)
				{
					if (rs.Expression == null)
					{
						// no method body, return default value of expected type
						var defValue = new CodeDefaultValueExpression(new CodeTypeReference(typeof(TResult)));
						defValue.UserData[Key_EmptyBody] = result.Script;
						code.Add(defValue);
						return code;
					}

					// unwrap return expression
					code.Add(rs.Expression);
					return code;
				}

				if (onlyLine is CodeExpressionStatement)
				{
					// unwrap expression statement
					code.Add(((CodeExpressionStatement)onlyLine).Expression);
					return code;
				}

				if (onlyLine is CodeSnippetStatement)
				{
					// unwrap snippet statement
					code.Add(new CodeSnippetExpression(((CodeSnippetStatement)onlyLine).Value));
					return code;
				}

				// others will remain as a complete method
			}

			this.counter++;

			foreach (var method in result.Methods)
			{
				code.Add(method);
			}

			// return an invokable expression
			code.Add(new CodeMethodInvokeExpression(
				new CodeThisReferenceExpression(),
				result.Methods[0].Name,
				new CodeArgumentReferenceExpression("data"),
				new CodeArgumentReferenceExpression("index"),
				new CodeArgumentReferenceExpression("count")));

			return code;
		}

		#endregion Code Translation Methods

		#region Methods

		private CodeStatement EmitVarValue(string varName)
		{
			if (String.IsNullOrEmpty(varName))
			{
				return null;
			}

			#region writer.Write(varName);

			CodeExpression methodCall = new CodeMethodInvokeExpression(
				new CodeArgumentReferenceExpression("writer"),
				"Write",
				new CodeVariableReferenceExpression(varName));

			return new CodeExpressionStatement(methodCall);

			#endregion writer.Write(varName);
		}

		private CodeStatement EmitMarkup(string markup)
		{
			if (String.IsNullOrEmpty(markup))
			{
				return null;
			}

			#region writer.Write("escaped markup");

			return this.EmitExpression(new CodePrimitiveExpression(markup));

			#endregion writer.Write("escaped markup");
		}

		private CodeStatement EmitExpression(CodeExpression expr)
		{
			if (expr == null)
			{
				return null;
			}

			CodeDefaultValueExpression defValue = expr as CodeDefaultValueExpression;
			if (defValue != null &&
				defValue.UserData[Key_EmptyBody] != null)
			{
				// empty method body, emit comment
				return new CodeCommentStatement(String.Concat("Empty block: ", defValue.UserData[Key_EmptyBody] as string));
			}

			#region writer.Write(expr);

			CodeExpression methodCall = new CodeMethodInvokeExpression(
				new CodeArgumentReferenceExpression("writer"),
				"Write",
				expr);

			return new CodeExpressionStatement(methodCall);

			#endregion writer.Write(expr);
		}

		private CodeVariableDeclarationStatement AllocateLocalVar<TVar>(string prefix)
		{
			#region TVar prefix_X;

			string locID = String.Format(LocalVarFormat, prefix, ++this.counter);

			return new CodeVariableDeclarationStatement(typeof(TVar), locID);

			#endregion TVar prefix_X;
		}

		private CodeStatement GenerateClientIDVar(out string varName)
		{
			#region string id_XXXX = this.NewID();

			var newLoc = this.AllocateLocalVar<string>("id");

			newLoc.InitExpression = new CodeMethodInvokeExpression(
				new CodeThisReferenceExpression(),
				"NewID");

			varName = newLoc.Name;

			return newLoc;

			#endregion string id_XXXX = this.NewID();
		}

		#endregion Methods

		#region Utility Methods

		private DataName SplitDataName(string name, bool isAttrib)
		{
			string[] parts = (name??"").Split(NameDelim, 2, StringSplitOptions.RemoveEmptyEntries);
			switch (parts.Length)
			{
				case 1:
				{
					return new DataName(parts[0], null, null, isAttrib);
				}
				case 2:
				{
					return new DataName(parts[1], parts[0], null, isAttrib);
				}
				default:
				{
					return new DataName(String.Empty);
				}
			}
		}

		private void SplitTypeName(string fullName, out string typeNS, out string typeName)
		{
			int split = fullName.LastIndexOf('.');
			if (split < 0)
			{
				typeNS = String.Empty;
				typeName = fullName;
				return;
			}

			typeNS = fullName.Substring(0, split);
			typeName = fullName.Substring(split+1);
		}

		#endregion Utility Methods
	}
}
