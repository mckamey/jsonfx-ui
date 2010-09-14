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
using System.Collections.Generic;

using JsonFx.Markup;
using JsonFx.Serialization;

namespace JsonFx.Jbst
{
	/// <summary>
	/// The intermediate results while translating a template
	/// </summary>
	internal class TranslationState
	{
		#region Fields

		private readonly ITextFormatter<MarkupTokenType> Formatter;

		public readonly List<CodeObject> Code = new List<CodeObject>();
		public readonly List<Token<MarkupTokenType>> Buffer;
		public readonly List<KeyValuePair<string, CodeObject>> Replacements;

		#endregion Fields

		#region Init

		/// <summary>
		/// Ctor
		/// </summary>
		/// <param name="parent"></param>
		public TranslationState(TranslationState parent)
		{
			this.Formatter = parent.Formatter;
			this.Buffer = parent.Buffer;
			this.Replacements = parent.Replacements;
		}

		/// <summary>
		/// Ctor
		/// </summary>
		/// <param name="formatter"></param>
		public TranslationState(ITextFormatter<MarkupTokenType> formatter)
		{
			this.Formatter = formatter;
			this.Buffer = new List<Token<MarkupTokenType>>();
			this.Replacements = new List<KeyValuePair<string, CodeObject>>();
		}

		#endregion Init

		#region Flush Methods

		public void Flush()
		{
			if (this.Buffer.Count < 1)
			{
				return;
			}

			// flush the buffer
			string markup = this.Formatter.Format(this.Buffer);
			this.Buffer.Clear();

			var code = this.EmitMarkup(markup);
			this.Replacements.Clear();

			if (code != null)
			{
				this.Code.AddRange(code);
			}
		}

		public void Flush(int start)
		{
			if (this.Buffer.Count <= start)
			{
				this.Flush();
				return;
			}

			if (start < 1)
			{
				return;
			}

			// flush buffer before start
			string markup = this.Formatter.Format(this.Buffer.GetRange(0, start));
			this.Buffer.RemoveRange(0, start);

			var code = this.EmitMarkup(markup);
			if (code != null)
			{
				this.Code.AddRange(code);
			}
		}

		#endregion Flush Methods

		#region Emit Methods

		private IList<CodeObject> EmitMarkup(string markup)
		{
			if (String.IsNullOrEmpty(markup))
			{
				return null;
			}

			List<CodeObject> code = new List<CodeObject>();

			int start = 0;
			foreach (var replace in this.Replacements)
			{
				// split value and emit replacement code
				int end = markup.IndexOf(replace.Key, start);
				if (end < 0)
				{
					continue;
				}

				code.Add(this.EmitLiteral(markup.Substring(start, end-start)));
				code.Add(replace.Value);

				start = end + replace.Key.Length;
			}

			if (start < markup.Length)
			{
				code.Add(this.EmitLiteral(markup.Substring(start)));
			}

			return code;
		}

		private CodeExpression EmitLiteral(string text)
		{
			if (String.IsNullOrEmpty(text))
			{
				return null;
			}

			#region writer.Write("literal markup");

			return new CodeMethodInvokeExpression(
				new CodeArgumentReferenceExpression("writer"),
				"Write",
				new CodePrimitiveExpression(text));

			#endregion writer.Write("literal markup");
		}

		#endregion Emit Methods
	}
}
