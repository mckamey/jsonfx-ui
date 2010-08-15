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
	internal abstract class JbstTemplate : JbstCommand
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

		private object nameExpr;
		private object dataExpr;
		private object indexExpr;
		private object countExpr;

		#endregion Fields

		#region Properties

		public object NameExpr
		{
			get { return this.nameExpr; }
			set { this.nameExpr = value; }
		}

		public object DataExpr
		{
			get { return (this.dataExpr ?? JbstTemplate.DefaultDataExpression); }
			set { this.dataExpr = value; }
		}

		public object IndexExpr
		{
			get { return (this.indexExpr ?? JbstTemplate.DefaultIndexExpression); }
			set { this.indexExpr = value; }
		}

		public object CountExpr
		{
			get { return (this.countExpr ?? JbstTemplate.DefaultCountExpression); }
			set { this.countExpr = value; }
		}

		#endregion Properties

		#region Methods

		protected string RenderExpression(ITextFormatter<CommonTokenType> formatter, object argument)
		{
			if (argument is string)
			{
				// directly use as inline expression
				return ((string)argument??"").Trim();
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
					return String.Format(JbstTemplate.FunctionEvalExpression, writer.GetStringBuilder().ToString().Trim());
				}
			}

			// convert to token sequence and allow formatter to emit as primitive
			return formatter.Format(new[] { new Token<CommonTokenType>(CommonTokenType.Primitive, argument) });
		}

		#endregion Methods
	}

	/// <summary>
	/// Internal representation of a reference to a named template
	/// </summary>
	internal class JbstTemplateReference : JbstTemplate
	{
		#region Constants

		private const string TemplateReferenceFormat =
@"function() {{
	return JsonML.BST({0}).dataBind({1}, {2}, {3});
}}";

		#endregion Constants

		#region JbstCommand Members

		public override void Format(ITextFormatter<CommonTokenType> formatter, TextWriter writer)
		{
			writer.Write(
				JbstTemplateReference.TemplateReferenceFormat,
				this.RenderExpression(formatter, this.NameExpr),
				this.RenderExpression(formatter, this.DataExpr),
				this.RenderExpression(formatter, this.IndexExpr),
				this.RenderExpression(formatter, this.CountExpr));
		}

		#endregion JbstCommand Members
	}

	/// <summary>
	/// Internal representation of an anonymous inline template
	/// </summary>
	internal class JbstInlineTemplate : JbstTemplate
	{
		#region Constants

		private const string InlineTemplateStart =
@"function() {
	return JsonML.BST(";

		private const string InlineTemplateEndFormat =
@").dataBind({0}, {1}, {2});
}}";

		#endregion Constants

		#region Fields

		private bool isEnd;

		#endregion Fields

		#region Methods

		/// <summary>
		/// Gets the matching end token
		/// </summary>
		/// <returns></returns>
		public JbstInlineTemplate GetInlineEnd()
		{
			var end = (JbstInlineTemplate)this.MemberwiseClone();
			end.isEnd = true;
			return end;
		}

		#endregion Methods

		#region JbstCommand Members

		public override void Format(ITextFormatter<CommonTokenType> formatter, TextWriter writer)
		{
			if (this.isEnd)
			{
				writer.Write(
					JbstInlineTemplate.InlineTemplateEndFormat,
					this.RenderExpression(formatter, this.DataExpr),
					this.RenderExpression(formatter, this.IndexExpr),
					this.RenderExpression(formatter, this.CountExpr));
			}
			else
			{
				writer.Write(JbstInlineTemplate.InlineTemplateStart);
			}
		}

		#endregion JbstCommand Members
	}

	/// <summary>
	/// Internal representation of a wrapper control containing named or anonymous inner-templates
	/// </summary>
	internal class JbstWrapperTemplate : JbstTemplate
	{
		#region Constants

		private const string WrapperStartFormat =
@"function() {{
	return JsonML.BST({0}).dataBind({1}, {2}, {3}, ";

		private const string WrapperEnd =
@");
}";

		#endregion Constants

		#region Fields

		private bool isEnd;

		#endregion Fields

		#region Methods

		/// <summary>
		/// Gets the matching end token
		/// </summary>
		/// <returns></returns>
		public JbstWrapperTemplate GetInlineEnd()
		{
			var end = (JbstWrapperTemplate)this.MemberwiseClone();
			end.isEnd = true;
			return end;
		}

		#endregion Methods

		#region JbstCommand Members

		public override void Format(ITextFormatter<CommonTokenType> formatter, TextWriter writer)
		{
			if (this.isEnd)
			{
				writer.Write(JbstWrapperTemplate.WrapperEnd);
			}
			else
			{
				writer.Write(JbstWrapperTemplate.WrapperStartFormat,
					this.RenderExpression(formatter, this.NameExpr),
					this.RenderExpression(formatter, this.DataExpr),
					this.RenderExpression(formatter, this.IndexExpr),
					this.RenderExpression(formatter, this.CountExpr));
			}
		}

		#endregion JbstCommand Members
	}

	/// <summary>
	/// Internal representation of a placeholder control
	/// </summary>
	internal class JbstPlaceholder : JbstTemplate
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

		#region JbstCommand Members

		public override void Format(ITextFormatter<CommonTokenType> formatter, TextWriter writer)
		{
			writer.Write(JbstPlaceholder.PlaceholderStatementStart);

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
