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
using System.CodeDom.Compiler;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

using JsonFx.Html;
using JsonFx.IO;
using JsonFx.Markup;
using JsonFx.Serialization;
using JsonFx.Utils;

namespace JsonFx.Jbst
{
	/// <summary>
	/// Builds the server-side implementation of a given JBST
	/// </summary>
	internal class JbstControlBuilder
	{
		#region Constants

		private static readonly Regex Regex_MethodName = new Regex("[^0-9a-zA-Z_]+", RegexOptions.Compiled|RegexOptions.CultureInvariant|RegexOptions.ExplicitCapture);

		#endregion Constants

		#region Fields

		private readonly CodeDomProvider Provider;
		private readonly HtmlFormatter Formatter;

		#endregion Fields

		#region Init

		/// <summary>
		/// Ctor
		/// </summary>
		/// <param name="settings"></param>
		public JbstControlBuilder(DataWriterSettings settings, CodeDomProvider provider)
		{
			this.Provider = provider;

			this.Formatter = new HtmlFormatter(settings)
			{
				CanonicalForm = false,
				EncodeNonAscii = true,
				EmptyAttributes = HtmlFormatter.EmptyAttributeType.Html
			};
		}

		#endregion Init

		#region Build Methods

		public void Build(CompilationState state, TextWriter writer)
		{
			if (state == null)
			{
				throw new ArgumentNullException("state");
			}
			if (writer == null)
			{
				throw new ArgumentNullException("writer");
			}

			this.Formatter.ResetScopeChain();

			CodeCompileUnit code = new CodeCompileUnit();

			#region namespace JbstNamespace

			string typeNS, typeName;
			this.SplitTypeName(state.JbstName, out typeNS, out typeName);

			CodeNamespace ns = new CodeNamespace(typeNS);
			code.Namespaces.Add(ns);

			#endregion namespace JbstNamespace

			#region public sealed class JbstTypeName

			CodeTypeDeclaration controlType = new CodeTypeDeclaration();
			controlType.IsClass = true;
			controlType.Name = typeName;
			controlType.Attributes = MemberAttributes.Public|MemberAttributes.Final;

			controlType.BaseTypes.Add(typeof(JbstControl));
			ns.Types.Add(controlType);

			#endregion public sealed class JbstTypeName

			#region [BuildPath(virtualPath)]

			string virtualPath = JbstControlBuilder.QuoteSnippetStringCStyle(PathUtility.EnsureAppRelative(state.FilePath));

			CodeAttributeDeclaration attribute = new CodeAttributeDeclaration(
				new CodeTypeReference(typeof(BuildPathAttribute)),
				new CodeAttributeArgument(new CodeSnippetExpression(virtualPath)));
			controlType.CustomAttributes.Add(attribute);

			#endregion [BuildPath(virtualPath)]

			// build control tree
			string methodName = this.BuildControl(state, controlType);

			#region public override void Bind(TextWriter writer, object data, int index, int count)

			// build binding entry point
			this.BuildEntryPoint(methodName, controlType);

			#endregion public override void Bind(TextWriter writer, object data, int index, int count)

			// emit control code
			this.Provider.GenerateCodeFromCompileUnit(
				code,
				writer,
				new CodeGeneratorOptions
				{
					BlankLinesBetweenMembers = true,
					BracingStyle = "C",
					IndentString = "\t",
					VerbatimOrder = true
				});
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

		private string BuildControl(CompilationState state, CodeTypeDeclaration controlType)
		{
			// each control gets built as a method
			string methodName = this.GetMethodName(state.JbstName);

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
						this.BuildCommand(controlType, method, command);
					}
					stream.Pop();
					continue;
				}

				if (token.TokenType == MarkupTokenType.Primitive &&
					token.Value is JbstCommand)
				{
					markup = this.Formatter.Format(stream.EndChunk());

					this.BuildMarkup(markup, method);

					this.BuildCommand(controlType, method, (JbstCommand)token.Value);

					stream.Pop();
					stream.BeginChunk();
					continue;
				}

				stream.Pop();
			}

			markup = this.Formatter.Format(stream.EndChunk());
			this.BuildMarkup(markup, method);

			controlType.Members.Add(method);

			return methodName;
		}

		private void BuildCommand(CodeTypeDeclaration controlType, CodeMemberMethod method, JbstCommand command)
		{
			switch (command.CommandType)
			{
				case JbstCommandType.InlineTemplate:
				{
					string childMethod = this.BuildControl(((JbstInlineTemplate)command).State, controlType);
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
				default:
				{
					// TODO:
					return;
				}
			}
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

			string escaped = JbstControlBuilder.QuoteSnippetStringVerbatimStyle(markup);

			#region writer.Write("escaped markup");

			CodeExpression methodCall = new CodeMethodInvokeExpression(
				new CodeArgumentReferenceExpression("writer"),
				"Write",
				new CodeSnippetExpression(escaped));

			#endregion writer.Write("escaped markup");

			method.Statements.Add(methodCall);
		}

		#endregion Build Methods

		#region Utility Methods

		private string GetMethodName(string name)
		{
			if (String.IsNullOrEmpty(name))
			{
				name = Guid.NewGuid().ToString("n");
			}
			else
			{
				name = Regex_MethodName.Replace(name, "_");
			}

			// TODO: check for name collisions
			return "Bind_"+name;
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

		/// <summary>
		/// Escapes a C# string using C-style escape sequences.
		/// </summary>
		/// <param name="value"></param>
		/// <returns></returns>
		/// <remarks>
		/// Adapted from Microsoft.CSharp.CSharpCodeGenerator.QuoteSnippetStringCStyle
		/// Primary difference is does not wrap at 80 chars as large strings cause C# compiler to fail.
		/// </remarks>
		private static string QuoteSnippetStringCStyle(string value)
		{
			// CS1647: An expression is too long or complex to compile near '...'
			// happens if line wraps too many times (335440 chars is max for x64, 926240 chars is max for x86)

			// CS1034: Compiler limit exceeded: Line cannot exceed 16777214 characters
			// theoretically every character could be escaped unicode (6 chars), plus quotes, etc.

			const int LineWrapWidth = (16777214/6)-4;
			StringBuilder buffer = new StringBuilder(value.Length+5);

			buffer.Append("\"");
			for (int i=0, length=value.Length; i<length; i++)
			{
				switch (value[i])
				{
					case '\u2028':
					case '\u2029':
					{
						int ch = (int)value[i];
						buffer.Append(@"\u");
						buffer.Append(ch.ToString("X4", CultureInfo.InvariantCulture));
						break;
					}
					case '\\':
					{
						buffer.Append(@"\\");
						break;
					}
					case '\'':
					{
						buffer.Append(@"\'");
						break;
					}
					case '\t':
					{
						buffer.Append(@"\t");
						break;
					}
					case '\n':
					{
						buffer.Append(@"\n");
						break;
					}
					case '\r':
					{
						buffer.Append(@"\r");
						break;
					}
					case '"':
					{
						buffer.Append("\\\"");
						break;
					}
					case '\0':
					{
						buffer.Append(@"\0");
						break;
					}
					default:
					{
						buffer.Append(value[i]);
						break;
					}
				}

				if ((i > 0) && ((i % LineWrapWidth) == 0))
				{
					if ((Char.IsHighSurrogate(value[i]) && (i < (value.Length - 1))) && Char.IsLowSurrogate(value[i + 1]))
					{
						buffer.Append(value[++i]);
					}
					buffer.Append("\"+\r\n");
					buffer.Append('"');
				}
			}
			buffer.Append("\"");
			return buffer.ToString();
		}

		/// <summary>
		/// Escapes a C# string using C-style escape sequences.
		/// </summary>
		/// <param name="value"></param>
		/// <returns></returns>
		/// <remarks>
		/// Adapted from Microsoft.CSharp.CSharpCodeGenerator.QuoteSnippetStringVerbatimStyle
		/// Primary difference is does not wrap at 80 chars as large strings cause C# compiler to fail.
		/// </remarks>
		private static string QuoteSnippetStringVerbatimStyle(string value)
		{
			// CS1647: An expression is too long or complex to compile near '...'
			// happens if line wraps too many times (335440 chars is max for x64, 926240 chars is max for x86)

			// CS1034: Compiler limit exceeded: Line cannot exceed 16777214 characters
			// theoretically every character could be escaped unicode (6 chars), plus quotes, etc.

			const int LineWrapWidth = (16777214/6)-4;
			StringBuilder buffer = new StringBuilder(value.Length+5);

			buffer.AppendLine();
			buffer.Append("@\"");
			for (int i=0, length=value.Length; i<length; i++)
			{
				if (value[i] == '"')
				{
					buffer.Append('"');
				}

				buffer.Append(value[i]);

				if ((i > 0) && ((i % LineWrapWidth) == 0))
				{
					if ((Char.IsHighSurrogate(value[i]) && (i < (value.Length - 1))) && Char.IsLowSurrogate(value[i + 1]))
					{
						buffer.Append(value[++i]);
					}
					buffer.AppendLine("\"+");
					buffer.Append("@\"");
				}
			}
			buffer.Append("\"");
			return buffer.ToString();
		}

		#endregion Utility Methods
	}
}
