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
using System.Collections.Generic;
using System.IO;

using JsonFx.EcmaScript;
using JsonFx.Html;
using JsonFx.IO;
using JsonFx.Markup;
using JsonFx.Model;
using JsonFx.Serialization;
using JsonFx.Utils;

using TokenSequence=System.Collections.Generic.IEnumerable<JsonFx.Serialization.Token<JsonFx.Model.ModelTokenType>>;

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
		private const string LocalVarFormat = "{0}_{1:x}";
		private static readonly object Key_VarCount = new object();

		#endregion Constants

		#region Fields

		private readonly HtmlFormatter HtmlFormatter;
		private readonly EcmaScriptFormatter JSFormatter;
		private readonly EcmaScriptBuilder JSBuilder = new EcmaScriptBuilder();
		private readonly Dictionary<string, string> MethodCache = new Dictionary<string, string>();
		private int counter;

		#endregion Fields

		#region Init

		/// <summary>
		/// Ctor
		/// </summary>
		/// <param name="settings"></param>
		public JbstViewBuilder(DataWriterSettings settings)
		{
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
		}

		#endregion Init

		#region Build Methods

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

			#region Constructors

			CodeConstructor ctor = new CodeConstructor();
			ctor.Attributes = MemberAttributes.Public;
			viewType.Members.Add(ctor);

			ctor = new CodeConstructor();
			ctor.Attributes = MemberAttributes.Public;
			ctor.Parameters.Add(new CodeParameterDeclarationExpression(typeof(DataWriterSettings), "settings"));

			ctor.BaseConstructorArgs.Add(new CodeArgumentReferenceExpression("settings"));

			viewType.Members.Add(ctor);

			#endregion Constructors

			// build control tree
			string methodName = this.BuildTemplate(state, viewType);

			#region public override void Bind(TextWriter writer, object data, int index, int count)

			// build binding entry point
			this.BuildEntryPoint(methodName, viewType);

			#endregion public override void Bind(TextWriter writer, object data, int index, int count)

			return code;
		}

		private void BuildEntryPoint(string methodName, CodeTypeDeclaration viewType)
		{
			#region public override void Bind(TextWriter writer, object data, int index, int count)

			CodeMemberMethod method = new CodeMemberMethod();

			method.Name = "Bind";
			method.Attributes = MemberAttributes.Public|MemberAttributes.Override;
			method.Parameters.Add(new CodeParameterDeclarationExpression(typeof(TextWriter), "writer"));
			method.Parameters.Add(new CodeParameterDeclarationExpression(typeof(object), "data"));
			method.Parameters.Add(new CodeParameterDeclarationExpression(typeof(int), "index"));
			method.Parameters.Add(new CodeParameterDeclarationExpression(typeof(int), "count"));

			#endregion public override void Bind(TextWriter writer, object data, int index, int count)

			#region this.Bind(this.methodName, writer, data, index, count);

			this.BuildBindAdapterCall(
				viewType,
				methodName,
				null,
				null,
				null,
				method);

			#endregion this.Bind(this.methodName, writer, data, index, count);

			viewType.Members.Add(method);
		}

		private string BuildTemplate(CompilationState state, CodeTypeDeclaration viewType)
		{
			// each template gets built as a method as it can be called in a loop
			string methodName = String.Format(TemplateMethodFormat, this.counter++);

			#region private void methodName(TextWriter writer, object data, int index, int count)

			CodeMemberMethod method = new CodeMemberMethod();

			method.Name = methodName;
			method.Attributes = MemberAttributes.Private;
			method.Parameters.Add(new CodeParameterDeclarationExpression(typeof(TextWriter), "writer"));
			method.Parameters.Add(new CodeParameterDeclarationExpression(typeof(TokenSequence), "data"));
			method.Parameters.Add(new CodeParameterDeclarationExpression(typeof(int), "index"));
			method.Parameters.Add(new CodeParameterDeclarationExpression(typeof(int), "count"));

			#endregion private void methodName(TextWriter writer, object data, int index, int count)

			string markup;
			var stream = Stream<Token<MarkupTokenType>>.Create(state.Content);

			stream.BeginChunk();
			while (!stream.IsCompleted)
			{
				var token = stream.Peek();
				if (token.TokenType == MarkupTokenType.Attribute)
				{
					DataName attrName = stream.Pop().Name;
					token = stream.Peek();

					JbstCommand command = token.Value as JbstCommand;
					if (command != null)
					{
						markup = this.HtmlFormatter.Format(stream.EndChunk());
						this.EmitMarkup(markup, method);

						// TODO: emit code reference
						this.BuildCommand(viewType, method, command, true);

						stream.Pop();
						stream.BeginChunk();
					}
					else
					{
						stream.Pop();
					}
					continue;
				}
				else if (token.TokenType == MarkupTokenType.Primitive &&
					token.Value is JbstCommand)
				{
					markup = this.HtmlFormatter.Format(stream.EndChunk());
					this.EmitMarkup(markup, method);

					this.BuildCommand(viewType, method, (JbstCommand)token.Value, false);

					stream.Pop();
					stream.BeginChunk();
					continue;
				}

				stream.Pop();
			}

			markup = this.HtmlFormatter.Format(stream.EndChunk());
			this.EmitMarkup(markup, method);

			viewType.Members.Add(method);

			return methodName;
		}

		private void BuildCommand(CodeTypeDeclaration viewType, CodeMemberMethod method, JbstCommand command, bool isAttribute)
		{
			switch (command.CommandType)
			{
				case JbstCommandType.DeclarationBlock:
				{
					// nothing emitted on the server
					return;
				}
				case JbstCommandType.TemplateReference:
				{
					JbstTemplateReference reference = (JbstTemplateReference)command;
					this.BuildBindReferenceCall(viewType, reference.NameExpr, reference.DataExpr, reference.IndexExpr, reference.CountExpr, method);
					return;
				}
				case JbstCommandType.InlineTemplate:
				{
					JbstInlineTemplate inline = (JbstInlineTemplate)command;
					string childMethod = this.BuildTemplate(inline.State, viewType);
					this.BuildBindAdapterCall(viewType, childMethod, inline.DataExpr, inline.IndexExpr, inline.CountExpr, method);
					return;
				}
				case JbstCommandType.CommentBlock:
				{
					JbstCommentBlock comment = (JbstCommentBlock)command;
					this.EmitMarkup(
						String.Concat("<!--", comment.Code, "-->"),
						method);
					return;
				}
				case JbstCommandType.ExpressionBlock:
				case JbstCommandType.UnparsedBlock:
				case JbstCommandType.StatementBlock:
				{
					CodeExpression expr = this.Translate(viewType, command);
					if (expr == null)
					{
						this.BuildClientExecution(command, method);
					}
					else
					{
						this.EmitExpression(expr, method);
					}
					return;
				}
				default:
				{
					// TODO:
					return;
				}
			}
		}

		private void BuildBindReferenceCall(CodeTypeDeclaration viewType, object nameExpr, object dataExpr, object indexExpr, object countExpr, CodeMemberMethod method)
		{
			#region new ExternalTemplate().Bind(writer, dataExpr, indexExpr, countExpr);

			CodeExpression nameCode;
			if (nameExpr is string)
			{
				string name = (string)nameExpr;
				if (EcmaScriptIdentifier.IsValidIdentifier(name, true))
				{
					nameCode = new CodeObjectCreateExpression(name, new CodeFieldReferenceExpression(new CodeThisReferenceExpression(), "Settings"));
				}
				else
				{
					nameCode = this.Translate(viewType, nameExpr);
				}
			}
			else
			{
				nameCode = this.Translate(viewType, nameExpr);
			}

			CodeExpression dataCode, indexCode, countCode;
			this.ProcessArgs(viewType, dataExpr, indexExpr, countExpr, out dataCode, out indexCode, out countCode);

			if (dataCode == null || indexCode == null || countCode == null)
			{
				// force into client mode
				this.BuildClientReference(nameExpr, dataExpr, indexExpr, countExpr, method);
				return;
			}

			CodeMethodInvokeExpression methodCall = new CodeMethodInvokeExpression(
				nameCode,
				"Bind",
				new CodeArgumentReferenceExpression("writer"),
				dataCode,
				indexCode,
				countCode);

			#endregion new ExternalTemplate().Bind(writer, dataExpr, indexExpr, countExpr);

			method.Statements.Add(methodCall);
		}

		private void ProcessArgs(CodeTypeDeclaration viewType, object dataExpr, object indexExpr, object countExpr, out CodeExpression dataCode, out CodeExpression indexCode, out CodeExpression countCode)
		{
			dataCode = this.Translate(viewType, dataExpr);
			indexCode = (dataCode != null) ? this.Translate<int>(viewType, indexExpr) : null;
			countCode = (indexCode != null) ? this.Translate<int>(viewType, countExpr) : null;

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

		private void BuildClientReference(object nameExpr, object dataExpr, object indexExpr, object countExpr, CodeMemberMethod method)
		{
			string varID = this.GenerateNewIDVar(method, "id");

			this.EmitMarkup("<noscript id=\"", method);
			this.EmitVarValue(varID, method);
			this.EmitMarkup("\">", method);

			this.EmitMarkup("</noscript><script type=\"text/javascript\">", method);
			this.EmitMarkup(this.FormatExpression(nameExpr), method);
			this.EmitMarkup(".replace(\"", method);
			this.EmitVarValue(varID, method);
			this.EmitMarkup("\",", method);
			this.EmitMarkup(this.FormatExpression(dataExpr), method);
			this.EmitMarkup(",", method);
			this.EmitMarkup(this.FormatExpression(indexExpr), method);
			this.EmitMarkup(",", method);
			this.EmitMarkup(this.FormatExpression(countExpr), method);
			this.EmitMarkup(");</script>", method);
		}

		private void BuildWrapperReference(object nameExpr, object dataExpr, object indexExpr, object countExpr, CodeMemberMethod method)
		{
			string varID = this.GenerateNewIDVar(method, "id");

			this.EmitMarkup("<div id=\"", method);
			this.EmitVarValue(varID, method);
			this.EmitMarkup("\">", method);

			// TODO: inline content goes here
			this.EmitMarkup("[ inline content goes here ]", method);

			this.EmitMarkup("</div><script type=\"text/javascript\">", method);
			this.EmitMarkup(this.FormatExpression(nameExpr), method);
			this.EmitMarkup(".replace(\"", method);
			this.EmitVarValue(varID, method);
			this.EmitMarkup("\",", method);
			this.EmitMarkup(this.FormatExpression(dataExpr), method);
			this.EmitMarkup(",", method);
			this.EmitMarkup(this.FormatExpression(indexExpr), method);
			this.EmitMarkup(",", method);
			this.EmitMarkup(this.FormatExpression(countExpr), method);
			this.EmitMarkup(");</script>", method);
		}

		public CodeExpression Translate(CodeTypeDeclaration viewType, object expr)
		{
			return this.Translate<object>(viewType, expr);
		}

		private CodeExpression Translate<TResult>(CodeTypeDeclaration viewType, object expr)
		{
			string script = this.FormatExpression(expr);

			CodeMemberMethod method = this.JSBuilder.Translate<TResult>(String.Format(CodeBlockMethodFormat, this.counter), script);
			if (method == null)
			{
				// not yet supported
				return null;
			}

			if (method.Statements.Count < 1)
			{
				// no method body, return default value of expected type
				return new CodeDefaultValueExpression(new CodeTypeReference(typeof(TResult)));
			}

			if (method.Statements.Count == 1)
			{
				if (method.Statements[0] is CodeMethodReturnStatement)
				{
					// unwrap return expression
					return ((CodeMethodReturnStatement)method.Statements[0]).Expression;
				}

				if (method.Statements[0] is CodeExpressionStatement)
				{
					// unwrap expression statement
					return ((CodeExpressionStatement)method.Statements[0]).Expression;
				}

				if (method.Statements[0] is CodeSnippetStatement)
				{
					// unwrap snippet statement
					return new CodeSnippetExpression(((CodeSnippetStatement)method.Statements[0]).Value);
				}

				// for now, others remain as a method
			}

			this.counter++;
			viewType.Members.Add(method);

			// return an invokable expression
			return new CodeMethodInvokeExpression(
				new CodeThisReferenceExpression(),
				method.Name,
				new CodeArgumentReferenceExpression("data"),
				new CodeArgumentReferenceExpression("index"),
				new CodeArgumentReferenceExpression("count"));
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
				return ((string)argument);
			}

			JbstCodeBlock block = argument as JbstCodeBlock;
			if (block != null)
			{
				return block.Code;
			}

			// convert to token sequence and allow formatter to emit as primitive
			return this.JSFormatter.Format(new[] { new Token<ModelTokenType>(ModelTokenType.Primitive, argument) });
		}

		private void BuildClientExecution(JbstCommand command, CodeMemberMethod method)
		{
			string code;
			using (var writer = new StringWriter())
			{
				command.Format(null, writer);
				code = writer.GetStringBuilder().ToString();
			}

			this.EmitMarkup(
				String.Concat("<script type=\"text/javascript\">", Environment.NewLine, code, Environment.NewLine, "</script>"),
				method);
		}

		private void BuildBindAdapterCall(CodeTypeDeclaration viewType, string methodName, object dataExpr, object indexExpr, object countExpr, CodeMemberMethod method)
		{
			CodeExpression dataCode, indexCode, countCode;
			this.ProcessArgs(viewType, dataExpr, indexExpr, countExpr, out dataCode, out indexCode, out countCode);

			if (dataCode == null || indexCode == null || countCode == null)
			{
				// TODO
				return;
			}

			#region this.Bind(this.methodName, writer, data, index, count);

			CodeMethodInvokeExpression methodCall = new CodeMethodInvokeExpression(
				new CodeThisReferenceExpression(),
				"Bind",
				new CodeMethodReferenceExpression(new CodeThisReferenceExpression(), methodName),
				new CodeArgumentReferenceExpression("writer"),
				dataCode,
				indexCode,
				countCode);

			#endregion this.Bind(this.methodName, writer, data, index, count);

			method.Statements.Add(methodCall);
		}

		#endregion Build Methods

		#region Methods

		private void EmitVarValue(string varName, CodeMemberMethod method)
		{
			if (String.IsNullOrEmpty(varName))
			{
				return;
			}

			#region writer.Write(varName);

			CodeExpression methodCall = new CodeMethodInvokeExpression(
				new CodeArgumentReferenceExpression("writer"),
				"Write",
				new CodeVariableReferenceExpression(varName));

			#endregion writer.Write(varName);

			method.Statements.Add(methodCall);
		}

		private void EmitMarkup(string markup, CodeMemberMethod method)
		{
			if (String.IsNullOrEmpty(markup))
			{
				return;
			}

			#region writer.Write("escaped markup");

			CodeExpression methodCall = new CodeMethodInvokeExpression(
				new CodeArgumentReferenceExpression("writer"),
				"Write",
				new CodePrimitiveExpression(markup));

			method.Statements.Add(methodCall);

			#endregion writer.Write("escaped markup");
		}

		private void EmitExpression(CodeExpression expr, CodeMemberMethod method)
		{
			if (expr == null)
			{
				return;
			}

			#region base.Write(writer, expr);

			// let base class clean it up
			CodeMethodInvokeExpression methodCall = new CodeMethodInvokeExpression(
				new CodeThisReferenceExpression(),
				"Write",
				new CodeArgumentReferenceExpression("writer"),
				expr);

			method.Statements.Add(methodCall);

			#endregion base.Write(writer, expr);
		}

		private CodeVariableDeclarationStatement AllocateLocalVar<TVar>(CodeMemberMethod method, string prefix)
		{
			#region TVar prefix_X;

			int varCount;
			method.UserData[Key_VarCount] = varCount = method.UserData[Key_VarCount] is Int32 ? ((Int32)method.UserData[Key_VarCount])+1 : 0;
			string locID = String.Format(LocalVarFormat, prefix, varCount);

			var newLoc = new CodeVariableDeclarationStatement(typeof(TVar), locID);

			method.Statements.Add(newLoc);

			return newLoc;

			#endregion TVar prefix_X;
		}

		private string GenerateNewIDVar(CodeMemberMethod method, string prefix)
		{
			#region string prefix_XXXX = String.Format("_{0:n}", Guid.NewGuid());

			var newLoc = this.AllocateLocalVar<string>(method, prefix);

			newLoc.InitExpression = new CodeMethodInvokeExpression(
				new CodeTypeReferenceExpression(typeof(String)), "Format",
				new CodePrimitiveExpression("_{0:n}"),
				new CodeMethodInvokeExpression(new CodeTypeReferenceExpression(typeof(Guid)), "NewGuid"));

			return newLoc.Name;

			#endregion string prefix_XXXX = String.Format("_{0:n}", Guid.NewGuid());
		}

		#endregion Methods

		#region Utility Methods

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
