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
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

using JsonFx.Common;
using JsonFx.EcmaScript;
using JsonFx.Html;
using JsonFx.IO;
using JsonFx.JsonML;
using JsonFx.Markup;
using JsonFx.Serialization;

namespace JsonFx.Jbst
{
	/// <summary>
	/// Compiles JBST templates into executable JavaScript controls.
	/// </summary>
	public class JbstCompiler
	{
		#region Constants

		private static readonly char[] ImportDelim = { ' ', ',' };
		private const string Whitespace = " ";
		private static readonly Regex RegexWhitespace = new Regex(@"\s+", RegexOptions.Compiled|RegexOptions.CultureInvariant);
		private static readonly DataName FragmentName = new DataName(String.Empty);
		private static readonly DataName ScriptName = new DataName("script");

		#endregion Constants

		#region Fields

		private readonly DataWriterSettings Settings = new DataWriterSettings { PrettyPrint=true };

		#endregion Fields

		#region Compile Methods

		/// <summary>
		/// Compiles the JBST template into executable JavaScript
		/// </summary>
		/// <param name="input"></param>
		/// <returns></returns>
		public string Compile(string path, string input)
		{
			if (input == null)
			{
				throw new ArgumentNullException("input");
			}

			// parse the markup
			var markup = new HtmlTokenizer { AutoBalanceTags = true }.GetTokens(input);

			// translate to script
			using (StringWriter writer = new StringWriter())
			{
				this.Compile(path, markup, writer);

				return writer.GetStringBuilder().ToString();
			}
		}

		/// <summary>
		/// Compiles the JBST template into executable JavaScript
		/// </summary>
		/// <param name="input"></param>
		/// <param name="output"></param>
		public void Compile(string path, TextReader input, TextWriter output)
		{
			if (input == null)
			{
				throw new ArgumentNullException("input");
			}

			if (output == null)
			{
				throw new ArgumentNullException("output");
			}

			// parse the markup
			var markup = new HtmlTokenizer { AutoBalanceTags = true }.GetTokens(input);

			// translate to script
			this.Compile(path, markup, output);
		}

		/// <summary>
		/// Compiles the JBST template into executable JavaScript
		/// </summary>
		/// <param name="path"></param>
		/// <param name="markup"></param>
		/// <param name="output"></param>
		private void Compile(string path, IEnumerable<Token<MarkupTokenType>> markup, TextWriter output)
		{
			CompilationState state = new CompilationState(path);

			var stream = Stream<Token<MarkupTokenType>>.Create(markup, true);

			// process markup converting code blocks and normalizing whitespace
			var jbst = this.ProcessTemplate(state, stream);
			if (jbst.Count < 1)
			{
				// empty input results in nothing
				return;
			}

			// convert markup into JsonML object structure
			var tokens = new JsonMLReader.JsonMLInTransformer { PreserveWhitespace = true }.Transform(jbst);

			this.EmitGlobals(state, output);

			// emit namespace or variable
			if (!EcmaScriptFormatter.WriteNamespaceDeclaration(output, state.JbstName, null, true))
			{
				output.Write("var ");
			}

			// wrap with ctor and assign
			output.Write(state.JbstName);
			output.WriteLine(" = JsonML.BST(");

			var formatter = new EcmaScriptFormatter(this.Settings);

			// emit template body
			formatter.Format(tokens, output);

			output.WriteLine(");");

			// render any declarations
			if (state.DeclarationBlock.HasCode)
			{
				state.DeclarationBlock.OwnerName = state.JbstName;
				formatter.Format(new[] { new Token<CommonTokenType>(CommonTokenType.Primitive, state.DeclarationBlock) }, output);
			}
		}

		#endregion Compile Methods

		#region Processing Methods

		/// <summary>
		/// Translates the template from markup to a JsonML+BST structure
		/// </summary>
		/// <param name="state"></param>
		/// <param name="markup"></param>
		/// <returns></returns>
		private List<Token<MarkupTokenType>> ProcessTemplate(CompilationState state, IStream<Token<MarkupTokenType>> stream)
		{
			int rootCount = 0,
				depth = 0;

			var output = new List<Token<MarkupTokenType>>();

			bool done = false;
			while (!done && !stream.IsCompleted)
			{
				var token = stream.Peek();
				switch (token.TokenType)
				{
					case MarkupTokenType.ElementBegin:
					case MarkupTokenType.ElementVoid:
					{
						if (StringComparer.OrdinalIgnoreCase.Equals(token.Name.Prefix, JbstCommand.JbstPrefix))
						{
							if (depth == 0)
							{
								rootCount++;
							}

							// process declarative template markup
							this.ProcessJbstCommand(stream, output);
						}
						else if (token.Name == JbstCompiler.ScriptName)
						{
							// process declaration block
							this.ProcessScriptBlock(state, stream);
						}
						else
						{
							if (depth == 0)
							{
								rootCount++;
							}

							if (token.TokenType == MarkupTokenType.ElementBegin)
							{
								depth++;
							}

							// other elements pass through to the output unaffected
							output.Add(stream.Pop());
						}
						continue;
					}
					case MarkupTokenType.Primitive:
					{
						stream.Pop();
						var block = token.Value as UnparsedBlock;
						if (block != null)
						{
							// interpret an unparsed block
							Token<MarkupTokenType> codeBlock = this.ProcessCodeBlock(state, block);
							if (codeBlock != null)
							{
								if (depth == 0)
								{
									rootCount++;
								}

								output.Add(codeBlock);
							}
						}
						else
						{
							if (this.ProcessLiteralText(output, token) &&
								(depth == 0))
							{
								rootCount++;
							}
						}
						continue;
					}
					case MarkupTokenType.ElementEnd:
					{
						if (depth == 0)
						{
							// this has been auto-balaced so an extra ElementEnd
							// means we've reached end of parent container
							done = true;
							continue;
						}

						depth--;
						goto default;
					}
					default:
					{
						// all others pass through unaffected
						output.Add(stream.Pop());
						continue;
					}
				}
			}

			if (rootCount > 1)
			{
				this.WrapTemplateRoot(output);
			}
			else
			{
				this.TrimTemplateRoot(output);
			}

			return output;
		}

		private void ProcessJbstCommand(IStream<Token<MarkupTokenType>> stream, List<Token<MarkupTokenType>> output)
		{
			var token = stream.Pop();
			DataName commandName = token.Name;
			bool isVoid = (token.TokenType == MarkupTokenType.ElementVoid);

			IDictionary<string, object> attributes = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
			while (!stream.IsCompleted)
			{
				token = stream.Pop();
				if (token.TokenType != MarkupTokenType.Attribute)
				{
					break;
				}
				if (stream.IsCompleted)
				{
					throw new TokenException<MarkupTokenType>(token, "Unexpected end of stream while processing JBST command");
				}

				string attrName = token.Name.LocalName;
				token = stream.Pop();
				if (token.TokenType != MarkupTokenType.Primitive)
				{
					throw new TokenException<MarkupTokenType>(token, "Unexpected value for JBST command arg: "+token.TokenType);
				}

				attributes[attrName] = token.Value;
			}

			if (commandName == JbstTemplateCommand.CommandName)
			{
				object name, data, index, count;

				attributes.TryGetValue(JbstTemplateCommand.KeyName, out name);
				attributes.TryGetValue(JbstTemplateCommand.KeyData, out data);
				attributes.TryGetValue(JbstTemplateCommand.KeyIndex, out index);
				attributes.TryGetValue(JbstTemplateCommand.KeyCount, out count);

				JbstTemplateCommand command;
				if (isVoid && name != null)
				{
					command = new JbstTemplateReference
						{
							NameExpr = name,
							DataExpr = data,
							IndexExpr = index,
							CountExpr = count
						};
				}
				else
				{
					command = new JbstInlineTemplate
					{
						NameExpr = name,
						DataExpr = data,
						IndexExpr = index,
						CountExpr = count
					};
				}

				output.Add(new Token<MarkupTokenType>(MarkupTokenType.Primitive, command));
			}
		}

		/// <summary>
		/// Processes a script block as a declaration
		/// </summary>
		/// <param name="state"></param>
		/// <param name="stream"></param>
		private void ProcessScriptBlock(CompilationState state, IStream<Token<MarkupTokenType>> stream)
		{
			stream.Pop();
			while (!stream.IsCompleted)
			{
				var token = stream.Pop();
				switch (token.TokenType)
				{
					case MarkupTokenType.Primitive:
					{
						state.DeclarationBlock.Append(token.ValueAsString());
						continue;
					}
					case MarkupTokenType.ElementEnd:
					{
						return;
					}
					case MarkupTokenType.Attribute:
					{
						// skip attribute value
						stream.Pop();
						continue;
					}
				}
			}
		}

		/// <summary>
		/// Interprets unparsed blocks
		/// </summary>
		/// <param name="state"></param>
		/// <param name="block"></param>
		/// <returns></returns>
		/// <remarks>
		/// Unrecognized UnparsedBlocks are emitted as plaintext
		/// </remarks>
		private Token<MarkupTokenType> ProcessCodeBlock(CompilationState state, UnparsedBlock block)
		{
			switch (block.Begin)
			{
				case "%@":
				{
					// extract directive settings
					this.ProcessDirective(state, block);
					return null;
				}
				case "%!":
				{
					// equivalent syntax to an inline script tag
					// analogous to static code, or JSP declarations
					// executed only on initialization of template
					// output from declarations are applied to the template
					state.DeclarationBlock.Append(block.Value);
					return null;
				}
				case "%#": // databinding expression
				{
					// unparsed expressions are emitted directly into JBST
					return new Token<MarkupTokenType>(MarkupTokenType.Primitive, new JbstUnparsedBlock(block.Value));
				}
				case "%=": // inline expression
				{
					// expressions are emitted directly into JBST
					return new Token<MarkupTokenType>(MarkupTokenType.Primitive, new JbstExpressionBlock(block.Value));
				}
				case "%$":
				{
					// expressions are emitted directly into JBST
					return new Token<MarkupTokenType>(MarkupTokenType.Primitive, new JbstExtensionBlock(block.Value, state.Path));
				}
				case "%":
				{
					// statements are emitted directly into JBST
					return new Token<MarkupTokenType>(MarkupTokenType.Primitive, new JbstStatementBlock(block.Value));
				}
				case "%--":
				{
					// server-side comments are omitted even for debug
					return null;
				}
				case "!--":
				{
					// HTML Comments are emitted directly into JBST
					return new Token<MarkupTokenType>(MarkupTokenType.Primitive, new JbstCommentBlock(block.Value));
				}
				default:
				{
					// unrecognized sequences get emitted as encoded text
					return new Token<MarkupTokenType>(MarkupTokenType.Primitive, block.ToString());
				}
			}
		}

		/// <summary>
		/// Parses directives and applies their settings to the compilation state
		/// </summary>
		/// <param name="state"></param>
		/// <param name="block"></param>
		/// <remarks>
		/// Unrecognized directives and properties are ignored
		/// </remarks>
		private void ProcessDirective(CompilationState state, UnparsedBlock block)
		{
			if (String.IsNullOrEmpty(block.Value))
			{
				return;
			}

			string asTag = String.Concat('<', block.Value.TrimStart(), '>');

			var stream = Stream<Token<MarkupTokenType>>.Create(new HtmlTokenizer().GetTokens(asTag));
			if (stream.IsCompleted)
			{
				return;
			}

			var token = stream.Pop();
			var tokenType = token.TokenType;
			if (tokenType != MarkupTokenType.ElementBegin &&
				tokenType != MarkupTokenType.ElementVoid)
			{
				throw new TokenException<MarkupTokenType>(token, "Unexpected directive element start: "+token.TokenType);
			}

			switch (token.Name.LocalName.ToLowerInvariant())
			{
				case "page":
				case "control":
				{
					this.ProcessTemplateDirective(state, stream);
					return;
				}
				case "import":
				{
					while (!stream.IsCompleted)
					{
						token = stream.Pop();
						if (token.TokenType != MarkupTokenType.Attribute)
						{
							throw new TokenException<MarkupTokenType>(token, "Unexpected token in directive: "+token.TokenType);
						}
						if (stream.IsCompleted)
						{
							return;
						}
						string attrName = token.Name.LocalName;
						token = stream.Pop();
						if (token.TokenType != MarkupTokenType.Primitive)
						{
							throw new TokenException<MarkupTokenType>(token, "Unexpected token in directive: "+token.TokenType);
						}

						if (StringComparer.OrdinalIgnoreCase.Equals(attrName, "namespace"))
						{
							state.Imports.Add(token.ValueAsString());
						}
					}
					break;
				}
			}
		}

		/// <summary>
		/// 
		/// </summary>
		/// <param name="state"></param>
		/// <param name="stream"></param>
		private void ProcessTemplateDirective(CompilationState state, IStream<Token<MarkupTokenType>> stream)
		{
			while (!stream.IsCompleted)
			{
				var token = stream.Pop();
				if (token.TokenType != MarkupTokenType.Attribute)
				{
					throw new TokenException<MarkupTokenType>(token, "Unexpected directive attribute name: "+token.TokenType);
				}
				if (stream.IsCompleted)
				{
					return;
				}
				string attrName = token.Name.LocalName;
				token = stream.Pop();
				if (token.TokenType != MarkupTokenType.Primitive)
				{
					throw new TokenException<MarkupTokenType>(token, "Unexpected directive attribute value: "+token.TokenType);
				}

				switch (attrName.ToLowerInvariant())
				{
					case "name":
					{
						state.JbstName = EcmaScriptIdentifier.EnsureValidIdentifier(token.ValueAsString(), true);
						break;
					}
					case "automarkup":
					{
						try
						{
							state.AutoMarkup = (AutoMarkupType)Enum.Parse(typeof(AutoMarkupType), token.ValueAsString(), true);
						}
						catch (Exception ex)
						{
							throw new TokenException<MarkupTokenType>(token, "\""+token.ValueAsString()+"\" is an invalid value for AutoMarkup.", ex);
						}
						break;
					}
					case "import":
					{
						string package = token.ValueAsString();
						if (!String.IsNullOrEmpty(package))
						{
							string[] packages = package.Split(JbstCompiler.ImportDelim, StringSplitOptions.RemoveEmptyEntries);
							state.Imports.AddRange(packages);
						}
						break;
					}
				}
			}
		}

		/// <summary>
		/// Generates a globals list from import directives
		/// </summary>
		private void EmitGlobals(CompilationState state, TextWriter writer)
		{
			bool hasGlobals = false;

			state.Imports.Insert(0, "JsonML.BST");
			foreach (string import in state.Imports)
			{
				string ident = EcmaScriptIdentifier.EnsureValidIdentifier(import, true);

				if (String.IsNullOrEmpty(ident))
				{
					continue;
				}

				if (hasGlobals)
				{
					writer.Write(", ");
				}
				else
				{
					hasGlobals = true;
					writer.Write("/*global ");
				}

				int dot = ident.IndexOf('.');
				writer.Write((dot < 0) ? ident : ident.Substring(0, dot));
			}

			if (hasGlobals)
			{
				writer.WriteLine(" */");
			}
		}

		/// <summary>
		/// Wrap content in a single container root
		/// </summary>
		/// <param name="result"></param>
		/// <returns></returns>
		private void WrapTemplateRoot(List<Token<MarkupTokenType>> result)
		{
			// an unnamed element will be preserved in JsonML as a document fragment
			result.Insert(0, new Token<MarkupTokenType>(MarkupTokenType.ElementBegin, JbstCompiler.FragmentName));
			result.Add(new Token<MarkupTokenType>(MarkupTokenType.ElementEnd));
		}

		/// <summary>
		/// Trims empty or whitespace nodes leaving a single root container
		/// </summary>
		/// <param name="result"></param>
		/// <returns></returns>
		private void TrimTemplateRoot(List<Token<MarkupTokenType>> result)
		{
			// trim trailing then leading whitespace
			bool trailing = true;
			int last = result.Count-1;

			while (last >= 0)
			{
				var root = (last >= 0) ? (result[trailing ? last : 0]) : null;
				if (root.TokenType != MarkupTokenType.Primitive ||
					!StringComparer.Ordinal.Equals(JbstCompiler.Whitespace, root.Value))
				{
					if (trailing)
					{
						// switch to checking leading
						trailing = false;
						continue;
					}

					// done checking both
					break;
				}

				result.RemoveAt(trailing ? last : 0);
				last--;
			}

			// should be a single root or empty list
		}

		/// <summary>
		/// Normalized literal text whitespace since HTML will do this anyway
		/// </summary>
		/// <param name="result"></param>
		/// <param name="token"></param>
		/// <param name="isRoot"></param>
		/// <returns>if resulted in non-whitespace text</returns>
		private bool ProcessLiteralText(List<Token<MarkupTokenType>> result, Token<MarkupTokenType> token)
		{
			string normalized;
			if (this.NormalizeWhitespace(token.ValueAsString(), out normalized))
			{
				// extraneous whitespace was normalized
				token = new Token<MarkupTokenType>(MarkupTokenType.Primitive, token.Name, normalized);
			}

			if (String.IsNullOrEmpty(normalized))
			{
				// prune empty text nodes
				return false;
			}

			// text which does not need normalization passes through unaffected
			result.Add(token);

			// non-whitespace text node was at root
			return !StringComparer.Ordinal.Equals(normalized, JbstCompiler.Whitespace);
		}

		/// <summary>
		/// Replaces string of whitespace with a single space
		/// </summary>
		/// <param name="text"></param>
		/// <param name="normalized"></param>
		/// <returns></returns>
		private bool NormalizeWhitespace(string text, out string normalized)
		{
			if (String.IsNullOrEmpty(text))
			{
				normalized = String.Empty;
				return false;
			}

			// replace whitespace chunks with single space (HTML-style normalization)
			normalized = JbstCompiler.RegexWhitespace.Replace(text, JbstCompiler.Whitespace);

			return !StringComparer.Ordinal.Equals(normalized, text);
		}

		#endregion Processing Methods
	}
}
