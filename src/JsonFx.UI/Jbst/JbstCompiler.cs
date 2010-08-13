﻿#region License
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

using JsonFx.EcmaScript;
using JsonFx.Html;
using JsonFx.JsonML;
using JsonFx.Markup;
using JsonFx.Serialization;

namespace JsonFx.Jbst
{
	/// <summary>
	/// Compiles JBST templates into executable JavaScript
	/// </summary>
	public class JbstCompiler
	{
		#region Constants

		private static readonly char[] ImportDelim = { ' ', ',' };
		private const string Whitespace = " ";
		private static readonly Regex RegexWhitespace = new Regex(@"\s+", RegexOptions.Compiled|RegexOptions.CultureInvariant);

		#endregion Constants

		#region Fields

		private readonly DataWriterSettings Settings;

		#endregion Fields

		#region Init

		/// <summary>
		/// Ctor
		/// </summary>
		public JbstCompiler()
			: this(new DataWriterSettings())
		{
		}

		/// <summary>
		/// Ctor
		/// </summary>
		/// <param name="settings"></param>
		public JbstCompiler(DataWriterSettings settings)
		{
			this.Settings = settings;
		}

		#endregion Init

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
				this.Translate(markup, new CompilationState(path), writer);

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
			this.Translate(markup, new CompilationState(path), output);
		}

		#endregion Compile Methods

		#region Translation Methods

		private void Translate(IEnumerable<Token<MarkupTokenType>> markup, CompilationState state, TextWriter writer)
		{
			// process markup converting code blocks and normalizing whitespace
			var jbst = this.ProcessMarkup(markup, state);

			// convert markup into JsonML object structure
			var tokens = new JsonMLReader.JsonMLInTransformer { PreserveWhitespace = true }.Transform(jbst);

			// emit script
			new EcmaScriptFormatter(this.Settings, true).Format(tokens, writer);
		}

		private IEnumerable<Token<MarkupTokenType>> ProcessMarkup(IEnumerable<Token<MarkupTokenType>> markup, CompilationState state)
		{
			var enumerator = markup.GetEnumerator();
			while (enumerator.MoveNext())
			{
				var token = enumerator.Current;
				switch (token.TokenType)
				{
					case MarkupTokenType.ElementBegin:
					case MarkupTokenType.ElementVoid:
					{
						switch (token.Name.Prefix)
						{
							case "jbst":
							{
								// TODO: process jbst controls
								yield return token;
								break;
							}
							default:
							{
								yield return token;
								break;
							}
						}
						continue;
					}
					case MarkupTokenType.Primitive:
					{
						var block = token.Value as UnparsedBlock;
						if (block != null)
						{
							Token<MarkupTokenType> codeBlock = this.ProcessCodeBlock(block, state);
							if (codeBlock != null)
							{
								yield return codeBlock;
							}
						}
						else
						{
							string normalized;
							if (this.NormalizeWhitespace(token.ValueAsString(), out normalized))
							{
								yield return new Token<MarkupTokenType>(MarkupTokenType.Primitive, token.Name, normalized);
							}
							else
							{
								yield return token;
							}
						}
						continue;
					}
					default:
					{
						yield return token;
						continue;
					}
				}
			}
		}

		private Token<MarkupTokenType> ProcessCodeBlock(UnparsedBlock block, CompilationState state)
		{
			switch (block.Begin)
			{
				case "%@":
				{
					// store directive for specialized parsing
					this.ProcessDirective(block, state);
					return null;
				}
				case "%!":
				{
					// analogous to static code, or JSP declarations
					// executed only on initialization of template
					// output from declarations are appended after the template
					state.Declarations.Append(block.Value);
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

		private void ProcessDirective(UnparsedBlock block, CompilationState state)
		{
			string asTag = String.Concat('<', (block.Value ?? "").TrimStart(), '>');

			var tokens = new HtmlTokenizer().GetTokens(asTag);
			if (tokens == null)
			{
				return;
			}

			var enumerator = tokens.GetEnumerator();
			if (!enumerator.MoveNext())
			{
				return;
			}

			var token = enumerator.Current;
			var tokenType = token.TokenType;
			if (tokenType != MarkupTokenType.ElementBegin &&
				tokenType != MarkupTokenType.ElementVoid)
			{
				throw new InvalidOperationException("Unexpected directive element start: "+token);
			}

			switch (token.Name.LocalName.ToLowerInvariant())
			{
				case "page":
				case "control":
				{
					while (enumerator.MoveNext())
					{
						token = enumerator.Current;
						if (token.TokenType != MarkupTokenType.Attribute)
						{
							throw new InvalidOperationException("Unexpected directive attribute name: "+token);
						}
						string attrName = token.Name.LocalName.ToLowerInvariant();
						if (!enumerator.MoveNext())
						{
							return;
						}
						token = enumerator.Current;
						if (token.TokenType != MarkupTokenType.Primitive)
						{
							throw new InvalidOperationException("Unexpected directive attribute value: "+token);
						}

						switch (attrName)
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
								catch
								{
									throw new ArgumentException("\""+token.ValueAsString()+"\" is an invalid value for AutoMarkup.");
								}
								break;
							}
							case "import":
							{
								string package = token.ValueAsString();
								if (!String.IsNullOrEmpty(package))
								{
									string[] packages = package.Split(ImportDelim, StringSplitOptions.RemoveEmptyEntries);
									state.Imports.AddRange(packages);
								}
								break;
							}
						}
					}
					break;
				}
				case "import":
				{
					while (enumerator.MoveNext())
					{
						token = enumerator.Current;
						if (token.TokenType != MarkupTokenType.Attribute)
						{
							throw new InvalidOperationException("Unexpected token in directive: "+token);
						}
						string attrName = token.Name.LocalName.ToLowerInvariant();
						if (!enumerator.MoveNext())
						{
							return;
						}
						token = enumerator.Current;
						if (token.TokenType != MarkupTokenType.Primitive)
						{
							throw new InvalidOperationException("Unexpected token in directive: "+token);
						}

						switch (attrName)
						{
							case "namespace":
							{
								state.Imports.Add(token.ValueAsString());
								break;
							}
						}
					}
					break;
				}
			}
		}

		private bool NormalizeWhitespace(string text, out string normalized)
		{
			if (String.IsNullOrEmpty(text))
			{
				normalized = text;
				return false;
			}

			// replace whitespace chunks with single space (HTML-style normalization)
			normalized = JbstCompiler.RegexWhitespace.Replace(text, JbstCompiler.Whitespace);

			return !StringComparer.Ordinal.Equals(normalized, text);
		}

		#endregion Translation Methods
	}
}