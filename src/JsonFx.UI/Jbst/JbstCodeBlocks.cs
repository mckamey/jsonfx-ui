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
using System.Text;

using JsonFx.Common;
using JsonFx.Jbst.Extensions;
using JsonFx.Serialization;

namespace JsonFx.Jbst
{
	/// <summary>
	/// Internal representation of JBST commands
	/// </summary>
	internal abstract class JbstCommand : ITextFormattable<CommonTokenType>
	{
		#region Constants

		public const string Prefix = "jbst";
		private const string Noop = "null";

		#endregion Constants

		#region ITextFormattable<CommonTokenType> Members

		public virtual void Format(ITextFormatter<CommonTokenType> formatter, TextWriter writer)
		{
			// emit an innocuous value
			writer.Write(JbstCommand.Noop);
		}

		#endregion ITextFormattable<CommonTokenType> Members
	}

	/// <summary>
	/// Initialization code for a template
	/// </summary>
	internal class JbstDeclarationBlock : JbstCommand
	{
		#region Constants

		private const string DeclarationFormat =
@"// initialize template in the context of ""this""
(function() {{
	{1}
}}).call({0});";

		#endregion Constants

		#region Fields

		private readonly StringBuilder Content = new StringBuilder();
		private string ownerName;

		#endregion Fields

		#region Init

		/// <summary>
		/// Ctor
		/// </summary>
		public JbstDeclarationBlock()
		{
		}

		#endregion Init

		#region Properties

		/// <summary>
		/// Gets the name of the owning JBST
		/// </summary>
		public string OwnerName
		{
			get
			{
				if (String.IsNullOrEmpty(this.ownerName))
				{
					// default to current context
					this.ownerName = "this";
				}
				return this.ownerName;
			}
			set { this.ownerName = value; }
		}

		#endregion Properties

		#region JbstCommand Members

		public override void Format(ITextFormatter<CommonTokenType> formatter, TextWriter writer)
		{
			string content = this.Content.ToString().Trim();
			if (String.IsNullOrEmpty(content))
			{
				base.Format(formatter, writer);
			}
			else
			{
				// render any declarations
				writer.Write(JbstDeclarationBlock.DeclarationFormat, this.OwnerName, content);
			}
		}

		#endregion JbstCommand Members

		#region Methods

		/// <summary>
		/// Append another code block onto declaration block
		/// </summary>
		/// <param name="control"></param>
		public void Append(string code)
		{
			this.Content.Append(code);
		}

		#endregion Methods
	}

	/// <summary>
	/// Internal representation of a JBST code block
	/// </summary>
	internal class JbstCodeBlock : JbstCommand
	{
		#region Fields

		private readonly string code;

		#endregion Fields

		#region Init

		/// <summary>
		/// Ctor
		/// </summary>
		/// <param name="code"></param>
		public JbstCodeBlock(string code)
		{
			this.code = code ?? String.Empty;
		}

		#endregion Init

		#region Properties

		/// <summary>
		/// Gets the code block content
		/// </summary>
		public virtual string Code
		{
			get { return this.code; }
		}

		#endregion Properties

		#region ITextFormattable<CommonTokenType> Members

		public override void Format(ITextFormatter<CommonTokenType> formatter, TextWriter writer)
		{
			if (String.IsNullOrEmpty(this.code))
			{
				base.Format(formatter, writer);
			}
			else
			{
				writer.Write(this.Code);
			}
		}

		#endregion ITextFormattable<CommonTokenType> Members
	}

	/// <summary>
	/// A comment which is 
	/// </summary>
	internal class JbstCommentBlock : JbstCodeBlock
	{
		#region Constants

		private const string CommentFormat = "\"\"/* {0} */";

		#endregion Constants

		#region Init

		/// <summary>
		/// Ctor
		/// </summary>
		/// <param name="code"></param>
		/// <param name="path"></param>
		public JbstCommentBlock(string code)
			: base(code)
		{
		}

		#endregion Init

		#region JbstCodeBlock Members

		public override void Format(ITextFormatter<CommonTokenType> formatter, TextWriter writer)
		{
			string code = this.Code.Trim();
			if (String.IsNullOrEmpty(code))
			{
				base.Format(formatter, writer);
			}
			else
			{
				writer.Write(CommentFormat, code.Replace(@"*/", @"*\/"));
			}
		}

		#endregion JbstCodeBlock Members
	}

	/// <summary>
	/// Code expression which is evaluated and emitted into the resulting content
	/// </summary>
	internal class JbstExpressionBlock : JbstCodeBlock
	{
		#region Constants

		private const string ExpressionFormat =
@"function() {{
	return {0};
}}";

		#endregion Constants

		#region Init

		/// <summary>
		/// Ctor
		/// </summary>
		/// <param name="code"></param>
		public JbstExpressionBlock(string code)
			: base(code)
		{
		}

		#endregion Init

		#region JbstCodeBlock Members

		public override void Format(ITextFormatter<CommonTokenType> formatter, TextWriter writer)
		{
			string code = this.Code.Trim();
			if (String.IsNullOrEmpty(code))
			{
				base.Format(formatter, writer);
			}
			else
			{
				// output expressions are the core of the syntax
				writer.Write(ExpressionFormat, code);
			}
		}

		#endregion JbstCodeBlock Members
	}

	/// <summary>
	/// Bind-time code which may or may not emit resulting content
	/// </summary>
	/// <remarks>
	/// The return value is the
	/// </remarks>
	internal class JbstStatementBlock : JbstCodeBlock
	{
		#region Constants

		private const string StatementFormat =
			@"function() {{
				{0}
			}}";

		#endregion Constants

		#region Init

		/// <summary>
		/// Ctor
		/// </summary>
		/// <param name="code"></param>
		/// <param name="path"></param>
		public JbstStatementBlock(string code)
			: base(code)
		{
		}

		#endregion Init

		#region JbstCodeBlock Members

		public override void Format(ITextFormatter<CommonTokenType> formatter, TextWriter writer)
		{
			string code = this.Code.Trim();
			if (String.IsNullOrEmpty(code))
			{
				base.Format(formatter, writer);
			}
			else
			{
				// analogous to instance code, or JSP scriptlets
				// executed each time template is bound
				writer.Write(StatementFormat, code);
			}
		}

		#endregion JbstCodeBlock Members
	}

	internal class JbstExtensionBlock : JbstCodeBlock
	{
		#region Constants

		private static readonly char[] PrefixDelim = { ':' };

		#endregion Constants

		#region Fields

		private readonly JbstExtension extension = null;

		#endregion Fields

		#region Init

		/// <summary>
		/// Ctor
		/// </summary>
		/// <param name="code"></param>
		/// <param name="path"></param>
		public JbstExtensionBlock(string code, string path)
			: base(code)
		{
			KeyValuePair<string, string> expr = JbstExtensionBlock.ParseExpression(code);

			// TODO: expose ability to extend evaluators
			switch ((expr.Key??"").ToLowerInvariant())
			{
				case "appsettings":
				{
					this.extension = new AppSettingsJbstExtension(expr.Value, path);
					break;
				}
				case "resources":
				{
					this.extension = new ResourceJbstExtension(expr.Value, path);
					break;
				}
				default:
				{
					this.extension = new JbstExtension(code, path);
					break;
				}
			}
		}

		#endregion Init

		#region Properties

		/// <summary>
		/// Gets the extension represented by this block
		/// </summary>
		public JbstExtension Extension
		{
			get { return this.extension; }
		}

		#endregion Properties

		#region JbstCodeBlock Members

		public override void Format(ITextFormatter<CommonTokenType> formatter, TextWriter writer)
		{
			// execute the corresponding extension evaluator
			this.Extension.Format(formatter, writer);
		}

		#endregion JbstCodeBlock Members

		#region Utility Methods

		private static KeyValuePair<string, string> ParseExpression(string extension)
		{
			string key = String.Empty;
			string value = String.Empty;

			if (!String.IsNullOrEmpty(extension))
			{
				// split on first ':'
				string[] parts = extension.Split(JbstExtensionBlock.PrefixDelim, 2, StringSplitOptions.RemoveEmptyEntries);
				if (parts.Length > 1)
				{
					value = parts[1].Trim();
				}

				key = parts[0].Trim();
			}

			return new KeyValuePair<string, string>(key, value);
		}

		#endregion Utility Methods
	}

	/// <summary>
	/// Raw content
	/// </summary>
	internal class JbstUnparsedBlock : JbstCodeBlock
	{
		#region Constants

		private const string UnparsedFormat =
@"function() {{
	return JsonML.raw({0});
}}";

		#endregion Constants

		#region Init

		/// <summary>
		/// Ctor
		/// </summary>
		/// <param name="code"></param>
		public JbstUnparsedBlock(string code)
			: base(code)
		{
		}

		#endregion Init

		#region JbstCodeBlock Members

		public override void Format(ITextFormatter<CommonTokenType> formatter, TextWriter writer)
		{
			string code = this.Code;
			if (String.IsNullOrEmpty(code))
			{
				base.Format(formatter, writer);
			}
			else
			{
				writer.Write(UnparsedFormat, code);
			}
		}

		#endregion JbstCodeBlock Members
	}
}
