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
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.IO;
using System.Text;

using JsonFx.EcmaScript;
using JsonFx.Html;
using JsonFx.IO;
using JsonFx.JsonML;
using JsonFx.Markup;
using JsonFx.Model;
using JsonFx.Serialization;
using JsonFx.Utils;

namespace JsonFx.Jbst
{
	/// <summary>
	/// Compiles JBST templates into executable JavaScript controls.
	/// </summary>
	public class JbstCompiler
	{
		#region Constants

		private static readonly char[] ImportDelim = { ' ', ',' };
		private static readonly DataName FragmentName = new DataName(String.Empty);
		private static readonly DataName ScriptName = new DataName("script");
		private static readonly DataName ScriptSrcName = new DataName("src");

		#endregion Constants

		#region Fields

		private readonly CodeDomProvider Provider;
		private readonly EcmaScriptIdentifier DefaultNamespace;
		private readonly DataWriterSettings Settings = new DataWriterSettings { PrettyPrint=true };
		private readonly IDataTransformer<MarkupTokenType, ModelTokenType> Transformer = new JsonMLReader.JsonMLInTransformer { Whitespace = WhitespaceType.Normalize };

		#endregion Fields

		#region Init

		/// <summary>
		/// Ctor
		/// </summary>
		public JbstCompiler()
			: this(null, null)
		{
		}

		/// <summary>
		/// Ctor
		/// </summary>
		/// <param name="defaultNamespace"></param>
		public JbstCompiler(EcmaScriptIdentifier defaultNamespace)
			: this(defaultNamespace, null)
		{
		}

		/// <summary>
		/// Ctor
		/// </summary>
		/// <param name="defaultNamespace"></param>
		/// <param name="provider"></param>
		public JbstCompiler(EcmaScriptIdentifier defaultNamespace, CodeDomProvider provider)
		{
			this.DefaultNamespace = defaultNamespace;
			this.Provider = provider ?? new Microsoft.CSharp.CSharpCodeProvider();
		}

		#endregion Init

		#region Compile Methods

		/// <summary>
		/// Compiles the JBST template into executable JavaScript
		/// </summary>
		/// <param name="virtualPath"></param>
		/// <param name="input"></param>
		/// <param name="clientOutput"></param>
		/// <param name="serverOutput"></param>
		public void Compile(string virtualPath, string input, out string clientOutput, out string serverOutput)
		{
			if (input == null)
			{
				throw new ArgumentNullException("input");
			}

			// parse the markup
			var markup = this.GetTokenizer().GetTokens(input);

			// translate to script
			using (StringWriter clientWriter = new StringWriter())
			using (StringWriter serverWriter = new StringWriter())
			{
				this.Compile(virtualPath, markup, clientWriter, serverWriter);

				clientOutput = clientWriter.GetStringBuilder().ToString();
				serverOutput = serverWriter.GetStringBuilder().ToString();
			}
		}

		/// <summary>
		/// Compiles the JBST template into executable JavaScript
		/// </summary>
		/// <param name="virtualPath"></param>
		/// <param name="input"></param>
		/// <param name="clientOutput"></param>
		/// <param name="serverOutput"></param>
		public void Compile(string virtualPath, TextReader input, TextWriter clientOutput, TextWriter serverOutput)
		{
			if (input == null)
			{
				throw new ArgumentNullException("input");
			}
			if (clientOutput == null)
			{
				throw new ArgumentNullException("clientOutput");
			}
			if (serverOutput == null)
			{
				throw new ArgumentNullException("serverOutput");
			}

			// parse the markup
			var markup = this.GetTokenizer().GetTokens(input);

			// translate to script
			this.Compile(virtualPath, markup, clientOutput, serverOutput);
		}

		/// <summary>
		/// Compiles the JBST template into executable JavaScript
		/// </summary>
		/// <param name="virtualPath"></param>
		/// <param name="markup"></param>
		/// <param name="clientOutput"></param>
		private void Compile(string virtualPath, IEnumerable<Token<MarkupTokenType>> markup, TextWriter clientOutput, TextWriter serverOutput)
		{
			var stream = Stream<Token<MarkupTokenType>>.Create(markup, true);

			// process markup converting code blocks and normalizing whitespace
			CompilationState state = this.ProcessTemplate(virtualPath, stream);

			// convert markup into JsonML object structure
			state.Format(new EcmaScriptFormatter(this.Settings), clientOutput);

			var code = new JbstViewBuilder(this.Settings).Build(state);

			// emit view code
			this.Provider.GenerateCodeFromCompileUnit(
				code,
				serverOutput,
				new CodeGeneratorOptions
				{
					BlankLinesBetweenMembers = true,
					BracingStyle = "C",
					IndentString = "\t",
					VerbatimOrder = true
				});
		}

		private HtmlTokenizer GetTokenizer()
		{
			return new HtmlTokenizer
			{
				AutoBalanceTags = true,
				UnparsedTags = new[] { "script", "style" },
				UnwrapUnparsedComments = true
			};
		}

		#endregion Compile Methods

		#region Processing Methods

		/// <summary>
		/// Translates the template from markup to a JsonML+BST structure
		/// </summary>
		/// <param name="state"></param>
		/// <param name="markup"></param>
		/// <returns></returns>
		private CompilationState ProcessTemplate(string virtualPath, IStream<Token<MarkupTokenType>> stream)
		{
			CompilationState state = new CompilationState(this.Transformer, virtualPath, this.DefaultNamespace);
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
						if (StringComparer.OrdinalIgnoreCase.Equals(token.Name.Prefix, JbstCommand.Prefix))
						{
							if (depth == 0)
							{
								rootCount++;
							}

							// process declarative template markup
							this.ProcessCommand(state, stream, output);
						}
						else if (token.Name == JbstCompiler.ScriptName)
						{
							// process declaration block
							this.ProcessScriptBlock(state, stream, output);
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
						else if (token.Value != null)
						{
							if ((depth == 0) && (!(token.Value is string) || !CharUtility.IsNullOrWhiteSpace(token.ValueAsString())))
							{
								rootCount++;
							}
							output.Add(token);
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

			if (output.Count > 0)
			{
				state.Content = output;
			}
			else
			{
				state.Content = null;
			}

			return state;
		}

		private void ProcessCommand(CompilationState state, IStream<Token<MarkupTokenType>> stream, List<Token<MarkupTokenType>> output)
		{
			var token = stream.Pop();
			string commandName = token.Name.ToPrefixedName();
			bool isVoid = (token.TokenType == MarkupTokenType.ElementVoid);

			object	name = null,
					data = null,
					index = null,
					count = null;

			while (!stream.IsCompleted)
			{
				token = stream.Peek();
				if (token.TokenType != MarkupTokenType.Attribute)
				{
					break;
				}
				stream.Pop();
				if (stream.IsCompleted)
				{
					throw new TokenException<MarkupTokenType>(token, "Unexpected end of stream while processing JBST command");
				}

				DataName attrName = token.Name;
				if (!String.IsNullOrEmpty(attrName.Prefix) && !StringComparer.OrdinalIgnoreCase.Equals(attrName.Prefix, JbstCommand.Prefix))
				{
					throw new TokenException<MarkupTokenType>(token, String.Format("Unsupported JBST command arg ({0})", attrName));
				}

				token = stream.Pop();
				if (token.TokenType != MarkupTokenType.Primitive)
				{
					throw new TokenException<MarkupTokenType>(token, "Unexpected value for JBST command arg: "+token.TokenType);
				}

				object attrValue = null;
				var block = token.Value as UnparsedBlock;
				if (block != null)
				{
					// interpret an unparsed block
					Token<MarkupTokenType> codeBlock = this.ProcessCodeBlock(state, block);
					if (codeBlock != null && codeBlock.Value != null)
					{
						attrValue = codeBlock.Value;
					}
				}
				else
				{
					attrValue = token.Value;
				}

				switch (attrName.LocalName.ToLowerInvariant())
				{
					case JbstTemplateCall.ArgName:
					{
						name = attrValue;
						break;
					}
					case JbstTemplateCall.ArgData:
					{
						data = attrValue;
						break;
					}
					case JbstTemplateCall.ArgIndex:
					{
						index = attrValue;
						break;
					}
					case JbstTemplateCall.ArgCount:
					{
						count = attrValue;
						break;
					}
					case JbstTemplateCall.ArgVisible:
					case JbstTemplateCall.ArgOnInit:
					case JbstTemplateCall.ArgOnLoad:
					//{
					//    break;
					//}
					default:
					{
						throw new TokenException<MarkupTokenType>(token, String.Format("Unsupported JBST command arg ({0})", attrName));
					}
				}
			}

			CompilationState innerState = null;
			if (!isVoid)
			{
				// new state created for inner context
				innerState = this.ProcessTemplate(state.FilePath, stream);

				// consume closing command tag
				if (stream.IsCompleted)
				{
					throw new TokenException<MarkupTokenType>(token, "Unexpected end of stream while processing JBST command");
				}
				token = stream.Pop();
				if (token.TokenType != MarkupTokenType.ElementEnd)
				{
					throw new TokenException<MarkupTokenType>(token, "Unexpected token while processing JBST command");
				}
			}

			JbstTemplateCall command = null;
			switch ((commandName??"").ToLowerInvariant())
			{
				case JbstTemplateCall.ControlCommand:
				{
					if (name == null)
					{
						// anonymous inline template
						command = new JbstInlineTemplate(innerState)
						{
							DataExpr = data,
							IndexExpr = index,
							CountExpr = count
						};
					}
					else if (innerState == null || innerState.Content == null)
					{
						// simple template reference
						command = new JbstTemplateReference(innerState)
						{
							NameExpr = name,
							DataExpr = data,
							IndexExpr = index,
							CountExpr = count
						};
					}
					else
					{
						// wrapper control containing named or anonymous inner-templates
						command = new JbstWrapperTemplate(innerState)
						{
							NameExpr = name,
							DataExpr = data,
							IndexExpr = index,
							CountExpr = count
						};
					}
					break;
				}
				case JbstPlaceholder.PlaceholderCommand:
				{
					// wrapper control containing named or anonymous inner-templates
					command = new JbstPlaceholder()
					{
						NameExpr = name,
						DataExpr = data,
						IndexExpr = index,
						CountExpr = count
					};
					break;
				}
				case JbstWrapperTemplate.InlineCommand:
				{
					state.AddNamedTemplate(name as String, innerState.Content);
					break;
				}
			}

			if (command != null)
			{
				output.Add(new Token<MarkupTokenType>(MarkupTokenType.Primitive, command));
			}
		}

		/// <summary>
		/// Processes a script block as a declaration
		/// </summary>
		/// <param name="state"></param>
		/// <param name="stream"></param>
		private void ProcessScriptBlock(CompilationState state, IStream<Token<MarkupTokenType>> stream, List<Token<MarkupTokenType>> output)
		{
			stream.BeginChunk();
			stream.Pop();

			bool done = false, 
				isExternal = false;

			StringBuilder content = new StringBuilder();
			while (!done && !stream.IsCompleted)
			{
				var token = stream.Pop();
				switch (token.TokenType)
				{
					case MarkupTokenType.Primitive:
					{
						if (!isExternal)
						{
							content.Append(token.ValueAsString());
						}
						continue;
					}
					case MarkupTokenType.ElementEnd:
					{
						done = true;
						continue;
					}
					case MarkupTokenType.Attribute:
					{
						if (token.Name == JbstCompiler.ScriptSrcName)
						{
							isExternal = true;
						}

						// attribute value
						stream.Pop();
						continue;
					}
				}
			}

			if (isExternal)
			{
				output.AddRange(stream.EndChunk());
			}
			else
			{
				state.DeclarationBlock.Append(content.ToString());
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
					return new Token<MarkupTokenType>(MarkupTokenType.Primitive, new JbstExtensionBlock(block.Value, state.FilePath));
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
					return new Token<MarkupTokenType>(MarkupTokenType.Primitive, block);
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
				case "view":
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
						state.JbstName = EcmaScriptIdentifier.VerifyIdentifier(token.ValueAsString(), true);
						break;
					}
					case "engine":
					{
						try
						{
							state.Engine = (EngineType)Enum.Parse(typeof(EngineType), token.ValueAsString(), true);
						}
						catch (Exception ex)
						{
							throw new TokenException<MarkupTokenType>(token, "\""+token.ValueAsString()+"\" is an invalid value for EngineType", ex);
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
					!CharUtility.IsNullOrWhiteSpace(root.ValueAsString()))
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

		#endregion Processing Methods
	}
}
