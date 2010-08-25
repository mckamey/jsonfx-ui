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
using System.IO;

using JsonFx.EcmaScript;
using JsonFx.Html;
using JsonFx.IO;
using JsonFx.Markup;
using JsonFx.Model;
using JsonFx.Serialization;
using JsonFx.Utils;

namespace JsonFx.Jbst
{
	/// <summary>
	/// Builds the server-side implementation of a given JBST
	/// </summary>
	internal class JbstControlBuilder
	{
		#region Fields

		private readonly HtmlFormatter HtmlFormatter;
		private readonly EcmaScriptFormatter JSFormatter;
		private int counter;

		#endregion Fields

		#region Init

		/// <summary>
		/// Ctor
		/// </summary>
		/// <param name="settings"></param>
		public JbstControlBuilder(DataWriterSettings settings)
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

			CodeTypeDeclaration controlType = new CodeTypeDeclaration();
			controlType.IsClass = true;
			controlType.Name = typeName;
			controlType.IsPartial = true;
			controlType.Attributes = MemberAttributes.Public|MemberAttributes.Final;

			controlType.BaseTypes.Add(typeof(JbstControl));
			ns.Types.Add(controlType);

			#endregion public sealed class JbstTypeName

			#region [BuildPath(virtualPath)]

			string virtualPath = PathUtility.EnsureAppRelative(state.FilePath);

			CodeAttributeDeclaration attribute = new CodeAttributeDeclaration(
				new CodeTypeReference(typeof(BuildPathAttribute)),
				new CodeAttributeArgument(new CodePrimitiveExpression(virtualPath)));
			controlType.CustomAttributes.Add(attribute);

			#endregion [BuildPath(virtualPath)]

			// build control tree
			string methodName = this.BuildTemplate(state, controlType, true);

			#region public override void Bind(TextWriter writer, object data, int index, int count)

			// build binding entry point
			this.BuildEntryPoint(methodName, controlType);

			#endregion public override void Bind(TextWriter writer, object data, int index, int count)

			return code;
		}

		private void BuildEntryPoint(string methodName, CodeTypeDeclaration controlType)
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

			#region this.BindInternal(this.methodName, writer, data, index, count);

			this.BuildBindInternalCall(methodName, method);

			#endregion this.BindInternal(this.methodName, writer, data, index, count);

			controlType.Members.Add(method);
		}

		private string BuildTemplate(CompilationState state, CodeTypeDeclaration controlType, bool isRoot)
		{
			// each template gets built as a method as it can be called in a loop
			string methodName = isRoot ? "Bind_Root" : String.Concat("Bind_", (++this.counter).ToString("000"));

			#region private void methodName(TextWriter writer, object data, int index, int count)

			CodeMemberMethod method = new CodeMemberMethod();

			method.Name = methodName;
			method.Attributes = MemberAttributes.Private;
			method.Parameters.Add(new CodeParameterDeclarationExpression(typeof(TextWriter), "writer"));
			method.Parameters.Add(new CodeParameterDeclarationExpression(typeof(object), "data"));
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
						// TODO: emit code reference
						this.BuildCommand(controlType, method, command, true);
					}
					stream.Pop();
					continue;
				}

				if (token.TokenType == MarkupTokenType.Primitive &&
					token.Value is JbstCommand)
				{
					markup = this.HtmlFormatter.Format(stream.EndChunk());

					this.BuildMarkup(markup, method);

					this.BuildCommand(controlType, method, (JbstCommand)token.Value, false);

					stream.Pop();
					stream.BeginChunk();
					continue;
				}

				stream.Pop();
			}

			markup = this.HtmlFormatter.Format(stream.EndChunk());
			this.BuildMarkup(markup, method);

			controlType.Members.Add(method);

			return methodName;
		}

		private void BuildCommand(CodeTypeDeclaration controlType, CodeMemberMethod method, JbstCommand command, bool isAttribute)
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
					this.BuildBindReferenceCall(reference.NameExpr, reference.DataExpr, reference.IndexExpr, reference.CountExpr, method);
					return;
				}
				case JbstCommandType.InlineTemplate:
				{
					string childMethod = this.BuildTemplate(((JbstInlineTemplate)command).State, controlType, false);
					this.BuildBindInternalCall(childMethod, method);
					return;
				}
				case JbstCommandType.CommentBlock:
				{
					JbstCommentBlock comment = (JbstCommentBlock)command;
					this.BuildMarkup(
						String.Concat("<!--", comment.Code, "-->"),
						method);
					return;
				}
				case JbstCommandType.ExpressionBlock:
				case JbstCommandType.UnparsedBlock:
				{
					JbstCodeBlock codeBlock = (JbstCodeBlock)command;
					if (!this.Transcode(codeBlock.Code, method))
					{
						this.BuildClientExecution(command, method);
					}
					return;
				}
				case JbstCommandType.StatementBlock:
				{
					JbstCodeBlock codeBlock = (JbstCodeBlock)command;
					if (!this.Transcode(codeBlock.Code, method))
					{
						this.BuildClientExecution(command, method);
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

		private void BuildBindReferenceCall(object nameExpr, object dataExpr, object indexExpr, object countExpr, CodeMemberMethod method)
		{
			try
			{
				string name = nameExpr as string;

				#region new ExternalTemplate().Bind(writer, dataExpr, indexExpr, countExpr);

				CodeMethodInvokeExpression methodCall = new CodeMethodInvokeExpression(
					new CodeObjectCreateExpression(name),
					"Bind",
					new CodeArgumentReferenceExpression("writer"),
					this.EvaluateExpression(dataExpr),
					this.EvaluateExpression(indexExpr),
					this.EvaluateExpression(countExpr));

				#endregion new ExternalTemplate().Bind(writer, dataExpr, indexExpr, countExpr);

				method.Statements.Add(methodCall);
			}
			catch
			{
				this.BuildClientReference(nameExpr, dataExpr, indexExpr, countExpr, method);
			}
		}

		private void BuildClientReference(object nameExpr, object dataExpr, object indexExpr, object countExpr, CodeMemberMethod method)
		{
			string id = String.Concat("_", Guid.NewGuid().ToString("n"));

			string markup = String.Concat(
				"<noscript id=\"",
				id,
				"\"></noscript><script type=\"text/javascript\">",
				this.FormatExpression(nameExpr),
				".replace(\"",
				id,
				"\",",
				this.FormatExpression(dataExpr),
				",",
				this.FormatExpression(indexExpr),
				",",
				this.FormatExpression(countExpr),
				");</script>");

			this.BuildMarkup(markup, method);
		}

		private void BuildWrapperReference(object nameExpr, object dataExpr, object indexExpr, object countExpr, CodeMemberMethod method)
		{
			string id = String.Concat("_", Guid.NewGuid().ToString("n"));

			string markup = String.Concat(
				"<div id=\"",
				id,
				"\">",
				// TODO: inline content goes here
				"</div><script type=\"text/javascript\">",
				this.FormatExpression(nameExpr),
				".replace(\"",
				id,
				"\",",
				this.FormatExpression(dataExpr),
				",",
				this.FormatExpression(indexExpr),
				",",
				this.FormatExpression(countExpr),
				");</script>");

			this.BuildMarkup(markup, method);
		}

		private CodeExpression EvaluateExpression(object expr)
		{
			if (expr == null)
			{
				return new CodePrimitiveExpression(null);
			}

			// TODO: convert expression to CodeDom or throw
			switch (expr.ToString().Trim())
			{
				case "this.data":
				{
					return new CodeArgumentReferenceExpression("data");
				}
				case "this.index":
				{
					return new CodeArgumentReferenceExpression("index");
				}
				case "this.count":
				{
					return new CodeArgumentReferenceExpression("count");
				}
			}

			throw new InvalidOperationException("Cannot translate expression");
		}

		private bool Transcode(string script, CodeMemberMethod method)
		{
			return false;
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
				return ((string)argument).Trim();
			}

			if (argument is JbstExpressionBlock)
			{
				// convert to inline expression
				return ((JbstExpressionBlock)argument).Code.Trim();
			}

			if (argument is JbstCommand)
			{
				using (StringWriter writer = new StringWriter())
				{
					// render code block as function
					((JbstCommand)argument).Format(null, writer);

					// convert to anonymous function call expression
					return writer.GetStringBuilder().ToString().Trim();
				}
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

			this.BuildMarkup(
				String.Concat("<script type=\"text/javascript\">", Environment.NewLine, code, Environment.NewLine, "</script>"),
				method);
		}

		private void BuildBindInternalCall(string methodName, CodeMemberMethod method)
		{
			#region this.BindInternal(this.methodName, writer, data, index, count);

			CodeMethodInvokeExpression methodCall = new CodeMethodInvokeExpression(
				new CodeThisReferenceExpression(),
				"BindInternal",
				new CodeMethodReferenceExpression(new CodeThisReferenceExpression(), methodName),
				new CodeArgumentReferenceExpression("writer"),
				new CodeArgumentReferenceExpression("data"),
				new CodeArgumentReferenceExpression("index"),
				new CodeArgumentReferenceExpression("count"));

			#endregion this.BindInternal(this.methodName, writer, data, index, count);

			method.Statements.Add(methodCall);
		}

		private void BuildMarkup(string markup, CodeMemberMethod method)
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

			#endregion writer.Write("escaped markup");

			method.Statements.Add(methodCall);
		}

		#endregion Build Methods

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
