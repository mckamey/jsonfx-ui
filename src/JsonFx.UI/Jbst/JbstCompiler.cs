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
				this.Translate(path, markup, writer);

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
			this.Translate(path, markup, output);
		}

		#endregion Compile Methods

		#region Translation Methods

		private void Translate(string path, IEnumerable<Token<MarkupTokenType>> markup, TextWriter writer)
		{
			CompilationState state = new CompilationState(path);

			// process markup converting code blocks and normalizing whitespace
			var jbst = this.ProcessMarkup(state, markup);
			if (jbst.Count < 1)
			{
				// empty input results in nothing
				return;
			}

			// convert markup into JsonML object structure
			var tokens = new JsonMLReader.JsonMLInTransformer { PreserveWhitespace = true }.Transform(jbst);

			this.EmitGlobals(state, writer);

			// emit namespace or variable
			if (!EcmaScriptFormatter.WriteNamespaceDeclaration(writer, state.JbstName, null, true))
			{
				writer.Write("var ");
			}

			// wrap with ctor and assign
			writer.Write(state.JbstName);
			writer.WriteLine(" = JsonML.BST(");

			var formatter = new EcmaScriptFormatter(this.Settings);

			// emit template body
			formatter.Format(tokens, writer);

			writer.WriteLine(");");

			// render any declarations
			if (state.DeclarationBlock.HasCode)
			{
				state.DeclarationBlock.OwnerName = state.JbstName;
				formatter.Format(new[] { new Token<CommonTokenType>(CommonTokenType.Primitive, state.DeclarationBlock) }, writer);
			}
		}

		private List<Token<MarkupTokenType>> ProcessMarkup(CompilationState state, IEnumerable<Token<MarkupTokenType>> markup)
		{
			int depth = 0,
				rootCount = 0;
			var result = new List<Token<MarkupTokenType>>();

			var enumerator = markup.GetEnumerator();
			while (enumerator.MoveNext())
			{
				var token = enumerator.Current;
				switch (token.TokenType)
				{
					case MarkupTokenType.ElementBegin:
					case MarkupTokenType.ElementVoid:
					{
						depth++;
						switch (token.Name.Prefix.ToLowerInvariant())
						{
							case "jbst":
							{
								if (depth == 1)
								{
									rootCount++;
								}

								// TODO: process jbst controls
								result.Add(token);
								break;
							}
							case "":
							{
								switch (token.Name.LocalName.ToLowerInvariant())
								{
									case "script":
									{
										// process declaration block
										bool done = false;
										while (!done && enumerator.MoveNext())
										{
											token = enumerator.Current;
											switch (token.TokenType)
											{
												case MarkupTokenType.Primitive:
												{
													state.DeclarationBlock.Append(token.ValueAsString());
													continue;
												}
												case MarkupTokenType.ElementEnd:
												{
													depth--;

													done = true;
													continue;
												}
												case MarkupTokenType.Attribute:
												{
													// skip attribute value
													enumerator.MoveNext();
													continue;
												}
												case MarkupTokenType.ElementBegin:
												case MarkupTokenType.ElementVoid:
												{
													depth++;
													continue;
												}
											}
										}
										break;
									}
									default:
									{
										if (depth == 1)
										{
											rootCount++;
										}

										// other elements pass through unaffected
										result.Add(token);
										break;
									}
								}
								break;
							}
							default:
							{
								if (depth == 1)
								{
									rootCount++;
								}

								// other prefixes pass through unaffected
								result.Add(token);
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
							Token<MarkupTokenType> codeBlock = this.ProcessCodeBlock(state, block);
							if (codeBlock != null)
							{
								if (depth == 0)
								{
									rootCount++;
								}

								result.Add(codeBlock);
							}
						}
						else
						{
							string normalized;
							if (this.NormalizeWhitespace(token.ValueAsString(), out normalized))
							{
								token = new Token<MarkupTokenType>(MarkupTokenType.Primitive, token.Name, normalized);
							}

							if (String.IsNullOrEmpty(normalized))
							{
								// prune empty text nodes
								continue;
							}

							if ((depth == 0) && !StringComparer.Ordinal.Equals(normalized, JbstCompiler.Whitespace))
							{
								rootCount++;
							}

							// text which does not need normalization passes through unaffected
							result.Add(token);
						}
						continue;
					}
					case MarkupTokenType.ElementEnd:
					{
						depth--;

						// pass through unaffected
						result.Add(token);
						continue;
					}
					default:
					{
						// all others pass through unaffected
						result.Add(token);
						continue;
					}
				}
			}

			if (rootCount > 1)
			{
				// wrap content in a single container root

				// an unnamed element will be preserved in JsonML as a document fragment
				result.Insert(0, new Token<MarkupTokenType>(MarkupTokenType.ElementBegin, JbstCompiler.FragmentName));
				result.Add(new Token<MarkupTokenType>(MarkupTokenType.ElementEnd));

				return result;
			}

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

			// should be a single root or empty
			return result;
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
			var enumerator = new HtmlTokenizer().GetTokens(asTag).GetEnumerator();
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
						if (!enumerator.MoveNext())
						{
							return;
						}
						string attrName = token.Name.LocalName;
						token = enumerator.Current;
						if (token.TokenType != MarkupTokenType.Primitive)
						{
							throw new InvalidOperationException("Unexpected directive attribute value: "+token);
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
									string[] packages = package.Split(JbstCompiler.ImportDelim, StringSplitOptions.RemoveEmptyEntries);
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
						if (!enumerator.MoveNext())
						{
							return;
						}
						string attrName = token.Name.LocalName;
						token = enumerator.Current;
						if (token.TokenType != MarkupTokenType.Primitive)
						{
							throw new InvalidOperationException("Unexpected token in directive: "+token);
						}

						switch (attrName.ToLowerInvariant())
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

		#endregion Translation Methods
	}
}
