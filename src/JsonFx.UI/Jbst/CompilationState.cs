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

using JsonFx.EcmaScript;
using JsonFx.Markup;
using JsonFx.Model;
using JsonFx.Serialization;

namespace JsonFx.Jbst
{
	/// <summary>
	/// The result from compiling a template
	/// </summary>
	internal class CompilationState : JbstCommand
	{
		#region Constants

		private static readonly Regex Regex_JbstName = new Regex(@"[^0-9a-zA-Z$_\.]+", RegexOptions.Compiled|RegexOptions.CultureInvariant|RegexOptions.ExplicitCapture);

		#endregion Constants

		#region Fields

		public readonly string FilePath;

		private readonly IDataTransformer<MarkupTokenType, ModelTokenType> Transformer;
		private readonly EcmaScriptIdentifier DefaultNamespace;
		private List<string> imports;
		private JbstDeclarationBlock declarationBlock;
		private IDictionary<string, IEnumerable<Token<MarkupTokenType>>> namedTemplates;
		private IEnumerable<Token<MarkupTokenType>> content;
		private IEnumerable<Token<ModelTokenType>> transformedContent;

		#endregion Fields

		#region Init

		/// <summary>
		/// Ctor
		/// </summary>
		/// <param name="filePath"></param>
		public CompilationState(IDataTransformer<MarkupTokenType, ModelTokenType> transformer, string filePath, EcmaScriptIdentifier defaultNamespace)
		{
			this.Transformer = transformer;
			this.DefaultNamespace = defaultNamespace ?? String.Empty;
			this.FilePath = filePath ?? String.Empty;
		}

		#endregion Init

		#region Properties

		/// <summary>
		/// Gets the command type
		/// </summary>
		public override JbstCommandType CommandType
		{
			get { return JbstCommandType.RootTemplate; }
		}

		public List<string> Imports
		{
			get
			{
				if (this.imports == null)
				{
					this.imports = new List<string>();
				}
				return this.imports;
			}
		}

		public string JbstName
		{
			get;
			set;
		}

		public JbstDeclarationBlock DeclarationBlock
		{
			get
			{
				if (this.declarationBlock == null)
				{
					this.declarationBlock = new JbstDeclarationBlock();
				}
				return this.declarationBlock;
			}
		}

		public IEnumerable<Token<MarkupTokenType>> Content
		{
			get { return this.content; }
			set
			{
				this.content = value;
				this.transformedContent = null;
			}
		}

		public EngineType Engine
		{
			get;
			set;
		}

		#endregion Properties

		#region ITextFormattable<ModelTokenType> Members

		public override void Format(ITextFormatter<ModelTokenType> formatter, TextWriter writer)
		{
			this.EnsureName();

			this.FormatGlobals(writer);

			// emit namespace or variable
			if (!EcmaScriptFormatter.WriteNamespaceDeclaration(writer, this.JbstName, null, true))
			{
				writer.Write("var ");
			}

			// assign to named var
			writer.Write(this.JbstName);

			// emit template body and wrap with ctor
			writer.Write(" = JsonML.BST(");

			if (this.content == null)
			{
				base.Format(formatter, writer);
			}
			else
			{
				var transformed = this.TransformContent();
				formatter.Format(transformed, writer);
			}

			writer.WriteLine(");");

			if (this.declarationBlock != null)
			{
				// emit init block
				this.declarationBlock.OwnerName = this.JbstName;
				this.declarationBlock.Format(formatter, writer);
			}
		}

		public IEnumerable<Token<ModelTokenType>> TransformContent()
		{
			if (this.transformedContent == null &&
				this.content != null)
			{
				this.transformedContent = this.Transformer.Transform(this.content);
			}

			return this.transformedContent;
		}

		#endregion ITextFormattable<ModelTokenType> Members

		#region Named Template Methods

		public void AddNamedTemplate(string name, IEnumerable<Token<MarkupTokenType>> content)
		{
			if (name == null)
			{
				name = String.Empty;
			}

			if (this.namedTemplates == null)
			{
				this.namedTemplates = new Dictionary<string, IEnumerable<Token<MarkupTokenType>>>(StringComparer.OrdinalIgnoreCase);
			}

			this.namedTemplates[name] = content;
		}

		public IEnumerable<Token<ModelTokenType>> NamedTemplates()
		{
			if (this.transformedContent == null &&
				this.content != null)
			{
				this.transformedContent = this.TransformNamedTemplates();
			}

			return this.transformedContent;
		}

		private IEnumerable<Token<ModelTokenType>> TransformNamedTemplates()
		{
			List<Token<ModelTokenType>> templates = new List<Token<ModelTokenType>>();

			templates.Add(new Token<ModelTokenType>(ModelTokenType.ObjectBegin));

			if (this.Content != null)
			{
				templates.Add(new Token<ModelTokenType>(ModelTokenType.Property, new DataName(JbstPlaceholder.InlinePrefix)));

				var output = this.Transformer.Transform(this.Content);
				templates.AddRange(output);
			}

			if (this.namedTemplates != null)
			{
				foreach (var template in this.namedTemplates)
				{
					templates.Add(new Token<ModelTokenType>(ModelTokenType.Property, new DataName(JbstPlaceholder.InlinePrefix+template.Key)));

					if (template.Value != null)
					{
						var output = this.Transformer.Transform(template.Value);
						templates.AddRange(output);
					}
				}
			}

			templates.Add(new Token<ModelTokenType>(ModelTokenType.ObjectEnd));

			return templates;
		}

		#endregion Named Template Methods

		#region Utility Methods

		public void EnsureName()
		{
			if (!String.IsNullOrEmpty(this.JbstName))
			{
				return;
			}

			this.JbstName = CompilationState.GenerateJbstName(this.FilePath, this.DefaultNamespace);
		}

		/// <summary>
		/// Generates a globals list from import directives
		/// </summary>
		private void FormatGlobals(TextWriter writer)
		{
			this.Imports.Insert(0, "JsonML.BST");

			bool hasGlobals = false;
			foreach (string import in this.Imports)
			{
				string ident = EcmaScriptIdentifier.VerifyIdentifier(import, true);

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

		private static string GenerateJbstName(string filePath, EcmaScriptIdentifier defaultNamespace)
		{
			if (String.IsNullOrEmpty(filePath))
			{
				filePath = String.Concat('$', Guid.NewGuid().ToString("n"));
			}
			else
			{
				filePath = Path.GetFileNameWithoutExtension(filePath);
				filePath = Regex_JbstName.Replace(filePath, "_");
			}

			if (String.IsNullOrEmpty(defaultNamespace))
			{
				return filePath;
			}

			return String.Concat(defaultNamespace, '.', filePath);
		}

		#endregion Utility Methods
	}
}
