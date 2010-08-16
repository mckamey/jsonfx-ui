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
using System.IO;

using JsonFx.Common;
using JsonFx.Serialization;

namespace JsonFx.Jbst
{
	/// <summary>
	/// Internal representation of template/control commands
	/// </summary>
	internal abstract class JbstTemplateCall : JbstCommand
	{
		#region Constants

		public static readonly DataName CommandName = new DataName("control", JbstCommand.Prefix, null);

		public const string KeyName = "name";	// id
		public const string KeyData = "data";	// model
		public const string KeyIndex = "index";	// skip
		public const string KeyCount = "count";	// take

		private const string DefaultDataExpression = "this."+KeyData;
		private const string DefaultIndexExpression = "this."+KeyIndex;
		private const string DefaultCountExpression = "this."+KeyCount;

		private const string FunctionEvalExpression = "({0}).call(this)";

		#endregion Constants

		#region Fields

		protected readonly CompilationState State;
		private object nameExpr;
		private object dataExpr;
		private object indexExpr;
		private object countExpr;

		#endregion Fields

		#region Init

		/// <summary>
		/// Ctor
		/// </summary>
		/// <param name="state"></param>
		public JbstTemplateCall(CompilationState state)
		{
			this.State = state;
		}

		#endregion Init

		#region Properties

		public object NameExpr
		{
			get { return this.nameExpr; }
			set { this.nameExpr = value; }
		}

		public object DataExpr
		{
			get { return (this.dataExpr ?? JbstTemplateCall.DefaultDataExpression); }
			set { this.dataExpr = value; }
		}

		public object IndexExpr
		{
			get { return (this.indexExpr ?? JbstTemplateCall.DefaultIndexExpression); }
			set { this.indexExpr = value; }
		}

		public object CountExpr
		{
			get { return (this.countExpr ?? JbstTemplateCall.DefaultCountExpression); }
			set { this.countExpr = value; }
		}

		#endregion Properties
		
		#region Utility Methods

		public static string FormatExpression(ITextFormatter<CommonTokenType> formatter, object argument)
		{
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
					((JbstCommand)argument).Format(formatter, writer);

					// convert to anonymous function call expression
					return String.Format(FunctionEvalExpression, writer.GetStringBuilder().ToString().Trim());
				}
			}

			// convert to token sequence and allow formatter to emit as primitive
			return formatter.Format(new[] { new Token<CommonTokenType>(CommonTokenType.Primitive, argument) });
		}

		#endregion Utility Methods
	}

	/// <summary>
	/// Internal representation of a reference to a named template
	/// </summary>
	internal class JbstTemplateReference : JbstTemplateCall
	{
		#region Constants

		private const string TemplateReferenceFormat =
@"function() {{
	return JsonML.BST({0}).dataBind({1}, {2}, {3});
}}";

		#endregion Constants

		#region Init

		/// <summary>
		/// Ctor
		/// </summary>
		/// <param name="state"></param>
		public JbstTemplateReference(CompilationState state)
			: base(state)
		{
		}

		#endregion Init

		#region JbstCommand Members

		public override void Format(ITextFormatter<CommonTokenType> formatter, TextWriter writer)
		{
			writer.Write(
				JbstTemplateReference.TemplateReferenceFormat,
				FormatExpression(formatter, this.NameExpr),
				FormatExpression(formatter, this.DataExpr),
				FormatExpression(formatter, this.IndexExpr),
				FormatExpression(formatter, this.CountExpr));
		}

		#endregion JbstCommand Members
	}

	/// <summary>
	/// Internal representation of an anonymous inline template
	/// </summary>
	internal class JbstInlineTemplate : JbstTemplateCall
	{
		#region Constants

		private const string InlineTemplateStart =
@"function() {
	return JsonML.BST(";

		private const string InlineTemplateEndFormat =
@").dataBind({0}, {1}, {2});
}}";

		#endregion Constants

		#region Init

		/// <summary>
		/// Ctor
		/// </summary>
		/// <param name="state"></param>
		public JbstInlineTemplate(CompilationState state)
			: base(state)
		{
		}

		#endregion Init

		#region JbstCommand Members

		public override void Format(ITextFormatter<CommonTokenType> formatter, TextWriter writer)
		{
			writer.Write(JbstInlineTemplate.InlineTemplateStart);

			if (this.State == null ||
				this.State.Content == null)
			{
				base.Format(formatter, writer);
			}
			else
			{
				formatter.Format(this.State.Content, writer);
			}

			writer.Write(
				JbstInlineTemplate.InlineTemplateEndFormat,
				FormatExpression(formatter, this.DataExpr),
				FormatExpression(formatter, this.IndexExpr),
				FormatExpression(formatter, this.CountExpr));
		}

		#endregion JbstCommand Members
	}

	/// <summary>
	/// Internal representation of a wrapper control containing named or anonymous inner-templates
	/// </summary>
	internal class JbstWrapperTemplate : JbstTemplateCall
	{
		#region Constants

		private const string WrapperStartFormat =
@"function() {{
	return JsonML.BST({0}).dataBind({1}, {2}, {3}, ";

		private const string WrapperEnd =
@");
}";

		#endregion Constants

		#region Init

		/// <summary>
		/// Ctor
		/// </summary>
		/// <param name="state"></param>
		public JbstWrapperTemplate(CompilationState state)
			: base(state)
		{
		}

		#endregion Init

		#region JbstCommand Members

		public override void Format(ITextFormatter<CommonTokenType> formatter, TextWriter writer)
		{
			writer.Write(JbstWrapperTemplate.WrapperStartFormat,
				FormatExpression(formatter, this.NameExpr),
				FormatExpression(formatter, this.DataExpr),
				FormatExpression(formatter, this.IndexExpr),
				FormatExpression(formatter, this.CountExpr));

			// TODO: emit template object here

			writer.Write(JbstWrapperTemplate.WrapperEnd);
		}

		#endregion JbstCommand Members
	}

	/// <summary>
	/// Internal representation of a placeholder control
	/// </summary>
	internal class JbstPlaceholder : JbstTemplateCall
	{
		#region Constants

		public const string InlinePrefix = "$";

		private const string PlaceholderStatementStart =
@"function() {
	var inline = ";

		private const string PlaceholderStatementEndFormat =
@",
		parts = this.args;

	if (parts && parts[inline]) {{
		return JsonML.BST(parts[inline]).dataBind({0}, {1}, {2}, parts);
	}}
}}";

		#endregion Constants

		#region Init

		/// <summary>
		/// Ctor
		/// </summary>
		/// <param name="state"></param>
		public JbstPlaceholder()
			: base(null)
		{
		}

		#endregion Init

		#region JbstCommand Members

		public override void Format(ITextFormatter<CommonTokenType> formatter, TextWriter writer)
		{
			writer.Write(JbstPlaceholder.PlaceholderStatementStart);

			// escape as a string literal
			formatter.Format(new[] { new Token<CommonTokenType>(CommonTokenType.Primitive, JbstPlaceholder.InlinePrefix+this.NameExpr) });

			writer.Write(
				JbstPlaceholder.PlaceholderStatementEndFormat,
				this.DataExpr,
				this.IndexExpr,
				this.CountExpr);
		}

		#endregion JbstCommand Members
	}
}
